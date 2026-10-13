"""Build Z-Image-Turbo as three ONNX graphs for Nexaflow's AI host: the prompt encoder, the denoiser and the decoder.

The graphs are traced from the published PyTorch model with only these changes, each exact or the model's own:
  * the denoiser runs in fp16 with its attention and feed-forward branches scaled down by powers of two, which every
    block's post-branch RMSNorm cancels — without it the branches overflow fp16's range and the image comes out black;
  * rotary embeddings are computed as real cos/sin pairs in fp32 instead of complex numbers, which ONNX has no type for;
  * the timestep embedder stays fp32, as the timestep's precision decides fine detail;
  * the prompt encoder is Qwen3-4B cut after layer 35, so its output is the pipeline's hidden_states[-2]; its
    weights are 8-bit and its arithmetic fp32: fp16 arithmetic, or 4-bit weights, changed the fine text drawn;
  * attention is ONNX Runtime's fused MultiHeadAttention, which never holds every token-by-token score;
  * the decoder takes the denoiser's latents and applies the VAE's scale and shift itself, giving pixels in [0, 1];
    it runs in fp16 — exported unfused, its attention scores overflowed fp16; fused, it matches fp32 to 77 dB.

usage: python -I build_z_image.py <z-image-turbo checkpoint> <out-dir> [--only encoder|denoiser|decoder]

The denoiser's calibration peaks are kept in <out-dir>/denoiser/branch-peaks.json and reused when present; the
factors they give are written beside them, branch-scales.json.
"""
import argparse
import json
import math
from pathlib import Path

import torch
import torch.nn.functional as F

HEADROOM = 4096.0  # the largest value a scaled branch may reach in calibration: 16x under fp16's limit
SEQ_MULTI_OF = 32

CALIBRATION = [
    "A photorealistic portrait of an elderly fisherman with a weathered face, wearing a yellow raincoat, a harbour at dawn behind him.",
    "A minimalist flat app icon of a paper plane inside a rounded square, gradient from teal to blue, plain white background.",
    "A café chalkboard menu that reads 'Plat du jour : Ratatouille 12 €' in neat handwritten chalk lettering.",
    "A German street sign that reads 'Bahnhofstraße', a tram passing behind it on an overcast afternoon.",
    "A conference poster titled 'SUMMIT 2026' with bold modern typography and geometric shapes.",
    "A watercolour illustration of a fox reading a book under a tree in autumn.",
    "An isometric 3D illustration of a small home office, soft pastel colours.",
    "A cyberpunk city street at night in the rain, neon signs, reflections on wet asphalt.",
    "An educational diagram of the water cycle with labelled arrows.",
    "A product photo of a matte black coffee mug with the word 'Bonjour' printed on it, on a marble countertop.",
]


# ── attention ────────────────────────────────────────────────────────────────────────────────────────────────────
# Exported attention otherwise becomes MatMul -> Softmax -> MatMul, which holds every token-by-token score at once:
# 9 GB for the denoiser at 1024x1024. This op runs as PyTorch's attention when traced and checked, and is written into
# the graph as ONNX Runtime's fused com.microsoft.MultiHeadAttention, which never builds that matrix.

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
    """ONNX Runtime's contrib ops are not registered with onnx, which the exporter looks every op up in: declare the
    part of com.microsoft.MultiHeadAttention this graph uses — query, key and value in, the attended values out."""
    from onnx import defs
    try:
        defs.get_schema("MultiHeadAttention", 1, "com.microsoft")
        return
    except defs.SchemaError:
        pass
    p = defs.OpSchema.FormalParameter
    defs.register_schema(defs.OpSchema(
        "MultiHeadAttention", "com.microsoft", 1,
        doc="Multi-head self or cross attention (ONNX Runtime contrib op), as used here.",
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

class RealRope(torch.nn.Module):
    """Stands in for Z-Image's RopeEmbedder: the same angles, given as (cos, sin) pairs in fp32, [tokens, dim/2, 2]."""

    def __init__(self, theta, axes_dims, axes_lens):
        super().__init__()
        self.axes = len(axes_dims)
        for i, (d, e) in enumerate(zip(axes_dims, axes_lens)):
            freqs = 1.0 / (theta ** (torch.arange(0, d, 2, dtype=torch.float64) / d))
            angles = torch.outer(torch.arange(e, dtype=torch.float64), freqs).float()
            self.register_buffer(f"table_{i}", torch.stack([angles.cos(), angles.sin()], dim=-1), persistent=False)

    def forward(self, ids):
        return torch.cat([getattr(self, f"table_{i}")[ids[:, i]] for i in range(self.axes)], dim=1)


class RealRopeAttnProcessor:
    """Z-Image's single-stream attention with the rotation done on real (cos, sin) pairs in fp32."""

    def __call__(self, attn, hidden_states, encoder_hidden_states=None, attention_mask=None, freqs_cis=None):
        query = attn.to_q(hidden_states).unflatten(-1, (attn.heads, -1))
        key = attn.to_k(hidden_states).unflatten(-1, (attn.heads, -1))
        value = attn.to_v(hidden_states).unflatten(-1, (attn.heads, -1))
        if attn.norm_q is not None:
            query = attn.norm_q(query)
        if attn.norm_k is not None:
            key = attn.norm_k(key)

        def rotate(x):
            pairs = x.float().reshape(*x.shape[:-1], -1, 2)
            cos, sin = freqs_cis[..., 0].unsqueeze(2), freqs_cis[..., 1].unsqueeze(2)
            a, b = pairs[..., 0], pairs[..., 1]
            return torch.stack([a * cos - b * sin, a * sin + b * cos], dim=-1).flatten(3).type_as(x)

        if freqs_cis is not None:
            query, key = rotate(query), rotate(key)
        if attention_mask is not None:
            raise ValueError("one picture at a time: the denoiser takes no attention mask")
        out = attention(query.flatten(2, 3), key.flatten(2, 3), value.flatten(2, 3), attn.heads).to(query.dtype)
        return attn.to_out[0](out)


def measure_branch_peaks(pipe, prompts):
    """The largest value each block's attention and feed-forward branch reaches across a calibration run (bf16)."""
    peaks = {}
    hooks = []
    for name, module in pipe.transformer.named_modules():
        if name.endswith(("feed_forward.w2", "feed_forward.w3", "attention.to_out.0", "attention.to_v")):
            def record(_m, _i, out, name=name):
                peaks[name] = max(peaks.get(name, 0.0), float(out.detach().abs().float().max()))
            hooks.append(module.register_forward_hook(record))
    for i, prompt in enumerate(prompts):
        pipe(prompt=prompt, width=1024, height=1024, num_inference_steps=9, guidance_scale=0.0,
             generator=torch.Generator(pipe.device).manual_seed(i))
    for h in hooks:
        h.remove()
    return peaks


def scale_branches(transformer, peaks):
    """Scale each branch that would leave fp16's range down where its values are made, by a power of two."""
    blocks = {name.rsplit(".feed_forward", 1)[0].rsplit(".attention", 1)[0] for name in peaks}
    factors = {}
    for block in sorted(blocks):
        module = transformer.get_submodule(block)
        for branch, source, out in (("attention", "to_v", "to_out.0"), ("feed_forward", "w3", "w2")):
            peak = max(peaks.get(f"{block}.{branch}.{source}", 0.0), peaks.get(f"{block}.{branch}.{out}", 0.0))
            if peak <= HEADROOM:
                continue
            factor = 2.0 ** math.ceil(math.log2(peak / HEADROOM))
            for linear in (getattr(module, branch).get_submodule(source),):
                linear.weight.data /= factor
                if linear.bias is not None:
                    linear.bias.data /= factor
            tail = getattr(module, branch).get_submodule(out)
            if tail.bias is not None:
                tail.bias.data /= factor
            factors[f"{block}.{branch}"] = factor
    return factors


class Denoiser(torch.nn.Module):
    """
    One denoising step on sequences the host has already laid out, so no Python decides a shape inside the graph:

      image_tokens      [1, N, 64]   fp32  the latents cut into 2x2 patches, (row, column, channel) within a patch
      image_positions   [N, 3]       int64 (caption rows + 1, patch row, patch column)
      caption           [1, L, 2560] fp16  the prompt encoder's output, its last row repeated up to a multiple of 32
      caption_padding   [L]          bool  true on the repeated rows
      caption_positions [L, 3]       int64 (1 + i, 0, 0) for every row, repeated ones included
      timestep          [1]          fp32  1 - sigma
    -> velocity         [1, N, 64]   fp32  in the image tokens' layout, the sign the sampler steps along

    N must be a multiple of 32 (every size the host draws at is). This is ZImageTransformer2DModel.forward for one
    picture, without the padding and position code that runs on Python integers.
    """

    def __init__(self, transformer):
        super().__init__()
        self.t = transformer

    def forward(self, image_tokens, image_positions, caption, caption_padding, caption_positions, timestep):
        t = self.t
        adaln = t.t_embedder(timestep * t.t_scale)

        x = t.all_x_embedder["2-1"](image_tokens[0].to(torch.float16))
        x_freqs = t.rope_embedder(image_positions)
        x = x.unsqueeze(0)
        for layer in t.noise_refiner:
            x = layer(x, None, x_freqs.unsqueeze(0), adaln, None, None, None)

        cap = t.cap_embedder(caption[0])
        cap = torch.where(caption_padding.unsqueeze(-1), t.cap_pad_token.to(cap.dtype), cap)
        cap_freqs = t.rope_embedder(caption_positions)
        cap = cap.unsqueeze(0)
        for layer in t.context_refiner:
            cap = layer(cap, None, cap_freqs.unsqueeze(0))

        unified = torch.cat([x, cap], dim=1)
        freqs = torch.cat([x_freqs, cap_freqs], dim=0).unsqueeze(0)
        for layer in t.layers:
            unified = layer(unified, None, freqs, adaln, None, None, None)
        out = t.all_final_layer["2-1"](unified, c=adaln)
        return -out[:, : image_tokens.shape[1]].float()


def sequences(latents, caption):
    """What the host does around the denoiser: patchify the latents and pad and number the prompt, as Z-Image does."""
    _, c, h, w = latents.shape
    tokens = latents[0].view(c, h // 2, 2, w // 2, 2).permute(1, 3, 2, 4, 0).reshape(1, (h // 2) * (w // 2), 4 * c)
    length = caption.shape[1]
    padded = length + (-length) % SEQ_MULTI_OF
    caption = torch.cat([caption, caption[:, -1:].repeat(1, padded - length, 1)], dim=1)
    padding = torch.arange(padded, device=latents.device) >= length
    caption_positions = torch.stack([torch.arange(1, padded + 1), torch.zeros(padded, dtype=torch.long),
                                     torch.zeros(padded, dtype=torch.long)], dim=1).to(latents.device)
    rows, cols = torch.meshgrid(torch.arange(h // 2), torch.arange(w // 2), indexing="ij")
    image_positions = torch.stack([torch.full_like(rows, padded + 1), rows, cols], dim=-1).reshape(-1, 3).to(latents.device)
    return tokens, image_positions, caption, padding, caption_positions


def unpatchify(tokens, h, w, c=16):
    return tokens[0].view(h // 2, w // 2, 2, 2, c).permute(4, 0, 2, 1, 3).reshape(1, c, h, w)


def check_against_original(transformer, device):
    """The rebuilt forward gives the original's answer for a prompt whose length is not a multiple of 32."""
    latents = torch.randn(1, 16, 128, 96, device=device)
    caption = torch.randn(1, 45, 2560, device=device, dtype=torch.float16)
    step = torch.tensor([0.4], device=device)
    with torch.no_grad():
        original = -transformer([latents.to(torch.float16).unsqueeze(2)[0]], step, [caption[0]], return_dict=False)[0][0]
        rebuilt = unpatchify(Denoiser(transformer)(*sequences(latents, caption), step), 128, 96)
    error = (original.squeeze(1).float() - rebuilt[0]).abs().max().item()
    scale = original.abs().max().item()
    print(f"rebuilt denoiser against the original: max difference {error:.4g} on values up to {scale:.4g}", flush=True)
    if error > 1e-2 * scale:
        raise SystemExit("the rebuilt denoiser does not match the original")


def fp32_timestep_embedder(transformer):
    """Keep the timestep embedder in fp32 and hand its output to the fp16 blocks."""
    embedder = transformer.t_embedder.float()
    original = embedder.forward

    def forward(t):
        return original(t.float()).to(torch.float16)

    embedder.forward = forward


# ── the prompt encoder ───────────────────────────────────────────────────────────────────────────────────────────

class PromptEncoder(torch.nn.Module):
    """input_ids [1,L] -> the output of Qwen3's 35th layer [1,L,2560]: the pipeline's hidden_states[-2]."""

    def __init__(self, text_encoder):
        super().__init__()
        model = text_encoder.model
        model.layers = model.layers[: len(model.layers) - 1]
        model.norm = torch.nn.Identity()
        # The 151,936-row embedding table is a quarter of the weights; fp16 holds it exactly enough, read back as fp32.
        embed = model.embed_tokens.half()
        lookup = embed.forward
        embed.forward = lambda ids: lookup(ids).float()
        self.model = model

    def forward(self, input_ids):
        return self.model(input_ids=input_ids, use_cache=False).last_hidden_state


# ── the decoder ──────────────────────────────────────────────────────────────────────────────────────────────────

class FusedVaeAttnProcessor:
    """The VAE's single-head self-attention block (diffusers' AttnProcessor2_0 for this case) on the fused op."""

    def __call__(self, attn, hidden_states, encoder_hidden_states=None, attention_mask=None, temb=None, *args, **kwargs):
        residual = hidden_states
        b, c, h, w = hidden_states.shape
        x = hidden_states.view(b, c, h * w).transpose(1, 2)
        x = attn.group_norm(x.transpose(1, 2)).transpose(1, 2)
        out = attention(attn.to_q(x), attn.to_k(x), attn.to_v(x), attn.heads)
        out = attn.to_out[0](out)
        out = out.transpose(-1, -2).reshape(b, c, h, w)
        return (out + residual) / attn.rescale_output_factor


class Decoder(torch.nn.Module):
    """latents [1,16,h,w] (fp32) -> pixels [1,3,8h,8w] in [0, 1] (fp32)."""

    def __init__(self, vae):
        super().__init__()
        for module in vae.decoder.modules():
            if type(module).__name__ == "Attention":
                module.set_processor(FusedVaeAttnProcessor())
        self.vae = vae
        self.scale, self.shift = vae.config.scaling_factor, vae.config.shift_factor

    def forward(self, latents):
        z = (latents / self.scale + self.shift).to(self.vae.dtype)
        return (self.vae.decode(z, return_dict=False)[0].float() / 2 + 0.5).clamp(0, 1)


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


def quantize(src, dst, block_size, bits=4):
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
    p.add_argument("--denoiser-bits", type=int, choices=[4, 8], default=8)
    p.add_argument("--encoder-bits", type=int, choices=[4, 8], default=8)
    p.add_argument("--only", choices=["encoder", "denoiser", "decoder"], default=None)
    args = p.parse_args()
    checkpoint, out = Path(args.checkpoint), Path(args.out)
    from diffusers import ZImagePipeline

    device = "cuda"
    if args.only in (None, "denoiser"):
        pipe = ZImagePipeline.from_pretrained(checkpoint, torch_dtype=torch.bfloat16).to(device)
        cached = out / "denoiser" / "branch-peaks.json"
        if cached.exists():
            peaks = json.loads(cached.read_text())
        else:
            peaks = measure_branch_peaks(pipe, CALIBRATION)
            cached.parent.mkdir(parents=True, exist_ok=True)
            cached.write_text(json.dumps(peaks, indent=1))
        transformer = pipe.transformer
        transformer.to(torch.float16)
        factors = scale_branches(transformer, peaks)
        (out / "denoiser").mkdir(parents=True, exist_ok=True)
        (out / "denoiser" / "branch-scales.json").write_text(json.dumps(factors, indent=1))
        rope = transformer.rope_embedder
        transformer.rope_embedder = RealRope(rope.theta, rope.axes_dims, rope.axes_lens).to(device)
        for module in transformer.modules():
            if hasattr(module, "set_processor") and hasattr(module, "to_q"):
                module.set_processor(RealRopeAttnProcessor())
        fp32_timestep_embedder(transformer)
        transformer.eval()
        check_against_original(transformer, device)
        denoiser = Denoiser(transformer).eval()
        tokens = torch.export.Dim("image_tokens", min=32, max=36864)
        rows = torch.export.Dim("caption_rows", min=32, max=544)
        sample = sequences(torch.randn(1, 16, 128, 128, device=device),
                           torch.randn(1, 77, 2560, device=device, dtype=torch.float16))
        with torch.no_grad():
            export(denoiser, (*sample, torch.tensor([0.5], device=device)), out / "denoiser-fp16" / "denoiser.onnx",
                   ["image_tokens", "image_positions", "caption", "caption_padding", "caption_positions", "timestep"],
                   ["velocity"],
                   {"image_tokens": {1: tokens}, "image_positions": {0: tokens}, "caption": {1: rows},
                    "caption_padding": {0: rows}, "caption_positions": {0: rows}, "timestep": None})
        del pipe, transformer, denoiser
        torch.cuda.empty_cache()
        quantize(out / "denoiser-fp16" / "denoiser.onnx", out / "denoiser" / "denoiser.onnx", args.block_size, args.denoiser_bits)

    if args.only in (None, "encoder"):
        from transformers import AutoModelForCausalLM
        text_encoder = AutoModelForCausalLM.from_pretrained(checkpoint / "text_encoder", torch_dtype=torch.float32).to(device)
        encoder = PromptEncoder(text_encoder).eval()
        length = torch.export.Dim("tokens", min=2, max=512)
        with torch.no_grad():
            export(encoder, (torch.ones(1, 40, dtype=torch.int64, device=device),), out / "encoder-fp32" / "encoder.onnx",
                   ["input_ids"], ["caption"], {"input_ids": {1: length}})
        del text_encoder, encoder
        torch.cuda.empty_cache()
        quantize(out / "encoder-fp32" / "encoder.onnx", out / "encoder" / "encoder.onnx", args.block_size, args.encoder_bits)

    if args.only in (None, "decoder"):
        from diffusers import AutoencoderKL
        vae = AutoencoderKL.from_pretrained(checkpoint / "vae", torch_dtype=torch.float16).to(device).eval()
        h = torch.export.Dim("h", min=16, max=192) * 2
        w = torch.export.Dim("w", min=16, max=192) * 2
        with torch.no_grad():
            export(Decoder(vae).eval(), (torch.randn(1, 16, 128, 128, device=device),), out / "decoder" / "decoder.onnx",
                   ["latents"], ["pixels"], {"latents": {2: h, 3: w}})


if __name__ == "__main__":
    main()
