"""Build FLUX.2 [klein] 4B as four ONNX graphs for Nexaflow's AI host: the prompt encoder, the denoiser, the picture
encoder and the decoder.

The graphs are traced from Black Forest Labs' published PyTorch model with only these changes, each exact or the
model's own:
  * the denoiser's residual streams are fp32 and every branch runs in fp16: the single-stream blocks' stream reaches
    1.03x10^5, past fp16's range (diffusers clips it there), while no branch passes 3.5x10^3;
  * its modulation, timestep embedder, norms, rotary positions and output projection are fp32;
  * rotary positions come from tables of the angles each whole position has, computed in fp64 as diffusers computes
    them, where ONNX would compute them in fp32;
  * the prompt encoder is Qwen3-4B cut after layer 27, giving the outputs of layers 9, 18 and 27 side by side, as the
    pipeline's hidden_states[9, 18, 27]; its weights are 8-bit and its arithmetic fp32, as Z-Image's must be;
  * attention is ONNX Runtime's fused MultiHeadAttention, which never holds every token-by-token score;
  * the picture encoder and the decoder carry the VAE's 2x2 patching and its latent batch norm, so the host deals only
    in the denoiser's 128-channel latents; the picture encoder takes 8-bit RGB as the host has it, the decoder gives
    pixels in [0, 1].

usage: python -I build_flux2_klein.py <FLUX.2-klein-4B checkpoint> <out-dir> [--only encoder|denoiser|vae]
"""
import argparse
from pathlib import Path

import torch
import torch.nn.functional as F

PROMPT_TOKENS = 512          # the pipeline pads every prompt to this many tokens
PROMPT_LAYERS = (9, 18, 27)  # the hidden states the denoiser reads
REFERENCE_SPACING = 10       # the first rotary coordinate of the i-th picture to work from is 10 + 10 i
ROPE_TABLE = 1024            # whole positions with a precomputed angle, on every axis


# ── attention ────────────────────────────────────────────────────────────────────────────────────────────────────
# Traced and checked as PyTorch's attention, written into the graph as ONNX Runtime's fused MultiHeadAttention.

@torch.library.custom_op("nexaflow::attention", mutates_args=())
def attention(query: torch.Tensor, key: torch.Tensor, value: torch.Tensor, heads: int) -> torch.Tensor:
    """query, key, value [batch, tokens, heads * head_dim] -> [batch, tokens, heads * head_dim]."""
    b, s, e = query.shape
    split = lambda x: x.view(b, -1, heads, e // heads).transpose(1, 2)
    out = F.scaled_dot_product_attention(split(query), split(key), split(value))
    return out.transpose(1, 2).reshape(b, s, e)


@attention.register_fake
def _(query, key, value, heads):
    return torch.empty_like(query)


def declare_multi_head_attention():
    """ONNX Runtime's contrib ops are not registered with onnx, which the exporter looks every op up in."""
    from onnx import defs
    try:
        defs.get_schema("MultiHeadAttention", 1, "com.microsoft")
        return
    except defs.SchemaError:
        pass
    p = defs.OpSchema.FormalParameter
    defs.register_schema(defs.OpSchema(
        "MultiHeadAttention", "com.microsoft", 1,
        doc="Multi-head self attention (ONNX Runtime contrib op), as used here.",
        inputs=[p("query", "T"), p("key", "T"), p("value", "T")],
        outputs=[p("output", "T")],
        type_constraints=[("T", ["tensor(float)", "tensor(float16)"], "query, key, value and output")],
        attributes=[defs.OpSchema.Attribute("num_heads", defs.OpSchema.AttrType.INT, "attention heads")],
    ))


def attention_onnx(query, key, value, heads: int):
    import onnxscript
    declare_multi_head_attention()
    msft = onnxscript.values.Opset("com.microsoft", 1)
    return msft.MultiHeadAttention(query, key, value, num_heads=heads)


# ── the denoiser ─────────────────────────────────────────────────────────────────────────────────────────────────

class RopeTables(torch.nn.Module):
    """Flux2PosEmbed's angles for every whole position below ROPE_TABLE, in fp64 then fp32 as diffusers computes them:
    positions [S, 4] int64 -> (cos, sin) [S, 128] each, every frequency repeated for its pair."""

    def __init__(self, theta, axes_dims):
        super().__init__()
        self.axes = len(axes_dims)
        for i, d in enumerate(axes_dims):
            freqs = 1.0 / (theta ** (torch.arange(0, d, 2, dtype=torch.float64) / d))
            angles = torch.outer(torch.arange(ROPE_TABLE, dtype=torch.float64), freqs)
            self.register_buffer(f"cos_{i}", angles.cos().float().repeat_interleave(2, dim=1), persistent=False)
            self.register_buffer(f"sin_{i}", angles.sin().float().repeat_interleave(2, dim=1), persistent=False)

    def forward(self, positions):
        cos = torch.cat([getattr(self, f"cos_{i}")[positions[:, i]] for i in range(self.axes)], dim=1)
        sin = torch.cat([getattr(self, f"sin_{i}")[positions[:, i]] for i in range(self.axes)], dim=1)
        return cos, sin


def rotate(x, cos, sin):
    """diffusers' apply_rotary_emb (use_real, unbind on the last axis) for x [1, S, heads, 128], in fp32."""
    real, imag = x.float().reshape(*x.shape[:-1], -1, 2).unbind(-1)
    rotated = torch.stack([-imag, real], dim=-1).flatten(3)
    return (x.float() * cos[None, :, None, :] + rotated * sin[None, :, None, :]).to(x.dtype)


def split_modulation(mod, sets):
    chunks = torch.chunk(mod.unsqueeze(1), 3 * sets, dim=-1)
    return [chunks[3 * i: 3 * (i + 1)] for i in range(sets)]


def modulate(stream, norm, scale, shift, dtype):
    """A block's norm on the fp32 stream, modulated in fp32, handed to its branch in the branches' type (fp16)."""
    return ((1 + scale) * norm(stream) + shift).to(dtype)


def heads(x, norm, head_dim):
    """x [1, S, heads * head_dim] cut into heads, each RMS-normed in fp32 — torch.nn.RMSNorm's arithmetic, written out
    because its fused kernel has no ONNX translation."""
    x = x.unflatten(-1, (-1, head_dim))
    f = x.float()
    return (f * torch.rsqrt(f.pow(2).mean(-1, keepdim=True) + norm.eps) * norm.weight.float()).to(x.dtype)


def attend(attn, query, key, value, cos, sin):
    """query and key [1, S, heads, 128], already normed; value [1, S, heads * 128]."""
    return attention(rotate(query, cos, sin).flatten(2), rotate(key, cos, sin).flatten(2), value, attn.heads)


def double_block(block, image, text, mod_image, mod_text, cos, sin, dtype):
    """Flux2TransformerBlock.forward with its two streams in fp32."""
    (shift_a, scale_a, gate_a), (shift_m, scale_m, gate_m) = mod_image
    (c_shift_a, c_scale_a, c_gate_a), (c_shift_m, c_scale_m, c_gate_m) = mod_text
    attn, d = block.attn, block.attn.head_dim
    x = modulate(image, block.norm1, scale_a, shift_a, dtype)
    c = modulate(text, block.norm1_context, c_scale_a, c_shift_a, dtype)
    query = torch.cat([heads(attn.add_q_proj(c), attn.norm_added_q, d), heads(attn.to_q(x), attn.norm_q, d)], dim=1)
    key = torch.cat([heads(attn.add_k_proj(c), attn.norm_added_k, d), heads(attn.to_k(x), attn.norm_k, d)], dim=1)
    value = torch.cat([attn.add_v_proj(c), attn.to_v(x)], dim=1)
    out = attend(attn, query, key, value, cos, sin)
    t = text.shape[1]
    image = image + gate_a * attn.to_out[0](out[:, t:]).float()
    text = text + c_gate_a * attn.to_add_out(out[:, :t]).float()
    image = image + gate_m * block.ff(modulate(image, block.norm2, scale_m, shift_m, dtype)).float()
    text = text + c_gate_m * block.ff_context(modulate(text, block.norm2_context, c_scale_m, c_shift_m, dtype)).float()
    return image, text


def single_block(block, stream, mod, cos, sin, dtype):
    """Flux2SingleTransformerBlock.forward with its stream in fp32."""
    (shift, scale, gate), = mod
    attn = block.attn
    x = attn.to_qkv_mlp_proj(modulate(stream, block.norm, scale, shift, dtype))
    qkv, mlp = torch.split(x, [3 * attn.inner_dim, x.shape[-1] - 3 * attn.inner_dim], dim=-1)
    query, key, value = qkv.chunk(3, dim=-1)
    out = attend(attn, heads(query, attn.norm_q, attn.head_dim), heads(key, attn.norm_k, attn.head_dim), value, cos, sin)
    return stream + gate * attn.to_out(torch.cat([out, attn.mlp_act_fn(mlp)], dim=-1)).float()


class Denoiser(torch.nn.Module):
    """
    One step, on sequences the host lays out:

      image_tokens     [1, N, 128] fp32  the noisy latents' tokens, row by row, then each picture to work from
      image_positions  [N, 4]      int64 (0, row, column, 0) for the latents; (10 + 10 i, row, column, 0) for the i-th picture
      prompt           [1, T, 7680] fp32 the prompt encoder's output
      prompt_positions [T, 4]      int64 (0, 0, 0, i)
      timestep         [1]         fp32  the step's noise level, sigma
    -> velocity        [1, N, 128] fp32  for every image token; the host steps the latents' along it

    This is Flux2Transformer2DModel.forward for one picture, its streams in fp32.
    """

    def __init__(self, transformer):
        super().__init__()
        self.t = transformer
        self.rope = RopeTables(transformer.config.rope_theta, transformer.config.axes_dims_rope).to(transformer.device)

    def forward(self, image_tokens, image_positions, prompt, prompt_positions, timestep):
        t = self.t
        temb = t.time_guidance_embed(timestep * 1000, None)
        mod_image = split_modulation(t.double_stream_modulation_img(temb), 2)
        mod_text = split_modulation(t.double_stream_modulation_txt(temb), 2)
        mod_single = split_modulation(t.single_stream_modulation(temb), 1)

        dtype = t.x_embedder.weight.dtype
        image = t.x_embedder(image_tokens.to(dtype)).float()
        text = t.context_embedder(prompt.to(dtype)).float()
        cos, sin = self.rope(torch.cat([prompt_positions, image_positions], dim=0))

        for block in t.transformer_blocks:
            image, text = double_block(block, image, text, mod_image, mod_text, cos, sin, dtype)
        stream = torch.cat([text, image], dim=1)
        for block in t.single_transformer_blocks:
            stream = single_block(block, stream, mod_single, cos, sin, dtype)
        image = stream[:, prompt.shape[1]:]
        return t.proj_out(t.norm_out(image, temb))


def fp32_conditioning(transformer):
    """The timestep embedder, the modulations, the final norm and the output projection in fp32; the rest fp16."""
    transformer.to(torch.float16)
    for module in (transformer.time_guidance_embed, transformer.double_stream_modulation_img,
                   transformer.double_stream_modulation_txt, transformer.single_stream_modulation,
                   transformer.norm_out, transformer.proj_out):
        module.float()


def check_against_original(original, rebuilt, device):
    """The rebuilt forward gives the original's answer, in fp32, with a picture to work from beside the latents."""
    latents = torch.randn(1, 24 * 32, 128, device=device)
    reference = torch.randn(1, 16 * 16, 128, device=device)
    prompt = torch.randn(1, PROMPT_TOKENS, 7680, device=device)
    image_ids = torch.cat([grid(0, 24, 32), grid(REFERENCE_SPACING, 16, 16)]).to(device)
    text_ids = text_positions(PROMPT_TOKENS).to(device)
    step = torch.tensor([0.6], device=device)
    with torch.no_grad():
        expected = original(hidden_states=torch.cat([latents, reference], dim=1), encoder_hidden_states=prompt,
                            timestep=step, img_ids=image_ids, txt_ids=text_ids, guidance=None, return_dict=False)[0]
        got = rebuilt(torch.cat([latents, reference], dim=1), image_ids, prompt, text_ids, step)
    error = (expected - got).abs().max().item()
    scale = expected.abs().max().item()
    print(f"rebuilt denoiser against the original in fp32: max difference {error:.4g} on values up to {scale:.4g}", flush=True)
    if error > 1e-3 * scale:
        raise SystemExit("the rebuilt denoiser does not match the original")


def grid(t, h, w):
    rows, cols = torch.meshgrid(torch.arange(h), torch.arange(w), indexing="ij")
    return torch.stack([torch.full_like(rows, t), rows, cols, torch.zeros_like(rows)], dim=-1).reshape(-1, 4)


def text_positions(length):
    zeros = torch.zeros(length, dtype=torch.long)
    return torch.stack([zeros, zeros, zeros, torch.arange(length)], dim=1)


# ── the prompt encoder ───────────────────────────────────────────────────────────────────────────────────────────

class PromptEncoder(torch.nn.Module):
    """
    input_ids [1, 512] int64, attention_mask [1, 512] int64 (1 on the prompt, 0 on its padding)
    -> prompt [1, 512, 7680] fp32: Qwen3's hidden states after layers 9, 18 and 27, side by side on each row.
    """

    def __init__(self, text_encoder):
        super().__init__()
        model = text_encoder.model
        model.layers = model.layers[: max(PROMPT_LAYERS)]
        model.norm = torch.nn.Identity()
        embed = model.embed_tokens.half()
        lookup = embed.forward
        embed.forward = lambda ids: lookup(ids).float()
        self.model = model

    def forward(self, input_ids, attention_mask):
        out = self.model(input_ids=input_ids, attention_mask=attention_mask, output_hidden_states=True, use_cache=False)
        return torch.cat([out.hidden_states[k] for k in PROMPT_LAYERS], dim=-1)


# ── the picture encoder and the decoder ──────────────────────────────────────────────────────────────────────────

class FusedVaeAttnProcessor:
    """The VAE's single-head self-attention block on the fused op."""

    def __call__(self, attn, hidden_states, encoder_hidden_states=None, attention_mask=None, temb=None, *args, **kwargs):
        residual = hidden_states
        b, c, h, w = hidden_states.shape
        x = hidden_states.view(b, c, h * w).transpose(1, 2)
        x = attn.group_norm(x.transpose(1, 2)).transpose(1, 2)
        out = attention(attn.to_q(x), attn.to_k(x), attn.to_v(x), attn.heads)
        out = attn.to_out[0](out)
        out = out.transpose(-1, -2).reshape(b, c, h, w)
        return (out + residual) / attn.rescale_output_factor


def fused_vae(vae):
    for module in vae.modules():
        if type(module).__name__ == "Attention":
            module.set_processor(FusedVaeAttnProcessor())
    return vae


def latent_norm(vae):
    mean = vae.bn.running_mean.view(1, -1, 1, 1).float()
    std = torch.sqrt(vae.bn.running_var.view(1, -1, 1, 1).float() + vae.config.batch_norm_eps)
    return mean, std


class PictureEncoder(torch.nn.Module):
    """rgb [H, W, 3] uint8, H and W multiples of 16 -> latents [1, 128, H/16, W/16] fp32, normalised as the denoiser
    reads them: the VAE's mean (argmax), 2x2 patches, the latent batch norm."""

    def __init__(self, vae):
        super().__init__()
        self.vae = fused_vae(vae)
        mean, std = latent_norm(vae)
        self.register_buffer("mean", mean, persistent=False)
        self.register_buffer("std", std, persistent=False)

    def forward(self, rgb):
        pixels = rgb.permute(2, 0, 1).unsqueeze(0).float() / 255.0
        moments = self.vae._encode((pixels * 2 - 1).to(self.vae.dtype))
        z = moments[:, : moments.shape[1] // 2].float()
        b, c, h, w = z.shape
        z = z.view(b, c, h // 2, 2, w // 2, 2).permute(0, 1, 3, 5, 2, 4).reshape(b, c * 4, h // 2, w // 2)
        return (z - self.mean) / self.std


class Decoder(torch.nn.Module):
    """latents [1, 128, h, w] fp32, as the denoiser makes them -> pixels [1, 3, 16h, 16w] fp32 in [0, 1]."""

    def __init__(self, vae):
        super().__init__()
        self.vae = fused_vae(vae)
        mean, std = latent_norm(vae)
        self.register_buffer("mean", mean, persistent=False)
        self.register_buffer("std", std, persistent=False)

    def forward(self, latents):
        z = latents * self.std + self.mean
        b, c, h, w = z.shape
        z = z.reshape(b, c // 4, 2, 2, h, w).permute(0, 1, 4, 2, 5, 3).reshape(b, c // 4, h * 2, w * 2)
        return (self.vae._decode(z.to(self.vae.dtype), return_dict=False)[0].float() / 2 + 0.5).clamp(0, 1)


# ── export ───────────────────────────────────────────────────────────────────────────────────────────────────────

def clear(path):
    """onnx appends external data to a file already there, so a graph and its data are removed before writing."""
    for stale in path.parent.glob(path.name + "*"):
        stale.unlink()


def export(module, args, path, input_names, output_names, dynamic_shapes):
    path.parent.mkdir(parents=True, exist_ok=True)
    clear(path)
    program = torch.onnx.export(module, args, dynamo=True, input_names=input_names, output_names=output_names,
                                dynamic_shapes=dynamic_shapes, optimize=True,
                                custom_translation_table={torch.ops.nexaflow.attention.default: attention_onnx})
    program.save(str(path), external_data=True)
    print(f"exported {path.name}", flush=True)


def quantize(src, dst, block_size, bits=8):
    import onnx
    from onnxruntime.quantization.matmul_nbits_quantizer import MatMulNBitsQuantizer, DefaultWeightOnlyQuantConfig

    model = onnx.load(str(src), load_external_data=True)
    config = DefaultWeightOnlyQuantConfig(block_size=block_size, is_symmetric=True, accuracy_level=0, bits=bits,
                                          op_types_to_quantize=("MatMul",))
    quantizer = MatMulNBitsQuantizer(model, algo_config=config)
    quantizer.process()
    graph = quantizer.model.model.graph
    used = {name for node in graph.node for name in node.input}
    unused = [init for init in graph.initializer if init.name not in used]
    for init in unused:
        graph.initializer.remove(init)
    print(f"dropped {len(unused)} weight(s) the quantised graph no longer uses", flush=True)
    dst.parent.mkdir(parents=True, exist_ok=True)
    clear(dst)
    onnx.save_model(quantizer.model.model, str(dst), save_as_external_data=True, all_tensors_to_one_file=True,
                    location=dst.name + ".data")
    print(f"quantized {dst.name}", flush=True)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("checkpoint")
    p.add_argument("out")
    p.add_argument("--block-size", type=int, default=32)
    p.add_argument("--only", choices=["encoder", "denoiser", "vae"], default=None)
    args = p.parse_args()
    checkpoint, out = Path(args.checkpoint), Path(args.out)
    device = "cuda"

    if args.only in (None, "denoiser"):
        from diffusers import Flux2Transformer2DModel
        original = Flux2Transformer2DModel.from_pretrained(checkpoint / "transformer", torch_dtype=torch.float32).to(device).eval()
        check_against_original(original, Denoiser(original).eval(), device)
        fp32_conditioning(original)
        denoiser = Denoiser(original).eval()
        tokens = torch.export.Dim("image_tokens", min=16, max=24576)
        rows = torch.export.Dim("prompt_rows", min=1, max=PROMPT_TOKENS)
        sample = (torch.randn(1, 64 * 64 + 32 * 32, 128, device=device),
                  torch.cat([grid(0, 64, 64), grid(REFERENCE_SPACING, 32, 32)]).to(device),
                  torch.randn(1, PROMPT_TOKENS, 7680, device=device), text_positions(PROMPT_TOKENS).to(device),
                  torch.tensor([0.5], device=device))
        with torch.no_grad():
            export(denoiser, sample, out / "denoiser-fp16" / "denoiser.onnx",
                   ["image_tokens", "image_positions", "prompt", "prompt_positions", "timestep"], ["velocity"],
                   {"image_tokens": {1: tokens}, "image_positions": {0: tokens}, "prompt": {1: rows},
                    "prompt_positions": {0: rows}, "timestep": None})
        del original, denoiser
        torch.cuda.empty_cache()
        quantize(out / "denoiser-fp16" / "denoiser.onnx", out / "denoiser" / "denoiser.onnx", args.block_size)

    if args.only in (None, "encoder"):
        from transformers import AutoModelForCausalLM
        text_encoder = AutoModelForCausalLM.from_pretrained(checkpoint / "text_encoder", dtype=torch.float32,
                                                            attn_implementation="sdpa").to(device)
        encoder = PromptEncoder(text_encoder).eval()
        ids = torch.ones(1, PROMPT_TOKENS, dtype=torch.int64, device=device)
        mask = (torch.arange(PROMPT_TOKENS, device=device) < 40).long().unsqueeze(0)
        with torch.no_grad():
            export(encoder, (ids, mask), out / "encoder-fp32" / "encoder.onnx",
                   ["input_ids", "attention_mask"], ["prompt"], None)
        del text_encoder, encoder
        torch.cuda.empty_cache()
        quantize(out / "encoder-fp32" / "encoder.onnx", out / "encoder" / "encoder.onnx", args.block_size)

    if args.only in (None, "vae"):
        from diffusers import AutoencoderKLFlux2
        vae = AutoencoderKLFlux2.from_pretrained(checkpoint / "vae", torch_dtype=torch.float16).to(device).eval()
        h = torch.export.Dim("h", min=4, max=160)
        w = torch.export.Dim("w", min=4, max=160)
        with torch.no_grad():
            export(PictureEncoder(vae).eval(), (torch.randint(0, 256, (1024, 768, 3), dtype=torch.uint8, device=device),),
                   out / "picture-encoder" / "picture-encoder.onnx", ["rgb"], ["latents"],
                   {"rgb": {0: h * 16, 1: w * 16}})
            export(Decoder(vae).eval(), (torch.randn(1, 128, 64, 48, device=device),), out / "decoder" / "decoder.onnx",
                   ["latents"], ["pixels"], {"latents": {2: h, 3: w}})


if __name__ == "__main__":
    main()
