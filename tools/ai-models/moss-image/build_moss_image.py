"""Build MOSS-Image as two ONNX graphs for Nexaflow's AI host: the denoiser and the decoder. Its prompt encoder,
Qwen3.5-9B unchanged, is read by the host on llama.cpp (compare_moss_encoder.py chooses the build), so no graph is made
of it here.

The graphs are traced from the published PyTorch model with only these changes, each exact or the model's own:
  * the residual streams — the joint image-and-text stream, and the text refiner's — are kept in fp32 and every branch
    runs in fp16; a branch whose values would leave fp16's range is scaled down by a power of two where its last
    projection makes them, and back up, in fp32, as it rejoins the stream (calibrated on bf16 runs, branch-scales.json);
  * rotary angles and the timestep's sinusoid are laid out by the host, as the reference computes them — the angles in
    fp32, the sinusoid in fp64 — and come in as fp32, so no Python integer decides a shape inside the graph;
  * the negative prompt's pass skips block 22, as MOSSImagePipeline does by default: an input, `skip`, chooses it;
  * attention is ONNX Runtime's fused MultiHeadAttention, which never holds every token-by-token score;
  * the decoder takes the denoiser's latents, undoes the VAE's batch norm and its 2x2 patching, and gives pixels in
    [0, 1]; it runs in fp16 with fused attention.

usage: python -I build_moss_image.py <moss-image checkpoint> <out-dir> [--only denoiser|decoder] [--denoiser-bits 8|4]

The calibration peaks are kept in <out-dir>/denoiser/branch-peaks.json and reused when present.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import torch
import torch.nn.functional as F

HEADROOM = 4096.0     # the largest value a scaled branch may reach in calibration: 16x under fp16's limit
FP16_LIMIT = 65504.0
SKIPPED_BLOCK = 22    # MOSSImagePipeline's uncond_skip_layers default

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
CALIBRATION_SIZES = [(1024, 1024), (1456, 1456), (1344, 768)]


def moss(checkpoint):
    sys.path.insert(0, str(checkpoint))
    import pipeline_moss_image
    return pipeline_moss_image


# ── attention ────────────────────────────────────────────────────────────────────────────────────────────────────
# Exported attention otherwise becomes MatMul -> Softmax -> MatMul, which holds every token-by-token score at once.

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
    """ONNX Runtime's contrib ops are not registered with onnx: declare the part of com.microsoft.MultiHeadAttention
    these graphs use — query, key and value in, the attended values out."""
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


# ── what the host lays out ───────────────────────────────────────────────────────────────────────────────────────

def rope_rows(transformer, height, width, text_length, device):
    """The rotary rows the reference computes for a [height, width] latent and a prompt of text_length tokens:
    image rows first, then text, [tokens, head_dim] fp32."""
    return transformer.rope(height, width, text_length, device)[0].float()


def time_rows(transformer, timestep, device):
    """The reference's timestep sinusoid, computed in fp64 and given as fp32: [1, freq_dim]."""
    half = transformer.config.freq_dim // 2
    freq = 10000 ** (-torch.arange(half, device=device, dtype=torch.float64) / half)
    phase = torch.as_tensor([float(timestep)], device=device, dtype=torch.float64)[:, None] * freq
    return torch.cat((phase.cos(), phase.sin()), -1).float()


# ── the denoiser ─────────────────────────────────────────────────────────────────────────────────────────────────

def rms(x, weight):
    """_RMSNorm in fp32, its weight applied in fp32: on an fp32 stream it is the reference's norm without its cast."""
    y = x.float() * torch.rsqrt(x.float().square().mean(-1, keepdim=True) + 1e-6)
    return y * weight.float()


def rotate(x, rope):
    """The reference's _rotate, in fp32: rope rows [tokens, head_dim] are sin then cos, a half each."""
    sin, cos = rope.chunk(2, -1)
    sin = torch.cat((sin, sin), -1)[None, :, None]
    cos = torch.cat((cos, cos), -1)[None, :, None]
    a, b = x.chunk(2, -1)
    return x * cos + torch.cat((-b, a), -1) * sin


def joint_attention(q, k, v, rope, heads):
    """The reference's _attention: rotate q and k in fp32, attend in v's precision."""
    shape = (*q.shape[:2], heads, q.shape[-1] // heads)
    q = rotate(q.float().reshape(shape), rope).flatten(2).to(v.dtype)
    k = rotate(k.float().reshape(shape), rope).flatten(2).to(v.dtype)
    return attention(q, k, v, heads)


def situ(hidden, gate):
    return (4.0 * torch.tanh(gate / 4.0) * torch.sigmoid(gate)) * (25.0 * torch.tanh(hidden / 25.0))


class Denoiser(torch.nn.Module):
    """
    One denoising pass of MOSSImageTransformer for one picture, on rows the host has laid out:

      latents  [1, 128, h, w]   fp32  the picture's latents, h and w its size over 16
      caption  [1, L, 4096]     fp32  the prompt encoder's reading of the prompt, every row a real token
      rope     [h*w + L, 128]   fp32  rotary rows, image first (rope_rows)
      time     [1, 256]         fp32  the timestep's sinusoid (time_rows)
      skip     []               bool  leave out block 22, as the negative prompt's pass does
    -> velocity [1, 128, h, w]  fp32  the flow the sampler steps along

    Weights are fp16, the residual streams fp32. `scales` maps a branch's name to the power of two its last projection's
    weights were divided by; its output is multiplied back in fp32.
    """

    def __init__(self, transformer, scales):
        super().__init__()
        self.t = transformer
        self.scales = scales
        self.heads = transformer.config.num_heads

    def back(self, name, value):
        return value.float() * self.scales.get(name, 1.0)

    def text(self, caption, rope):
        projection = self.t.text_embedder
        x = projection.proj(caption.to(torch.float16)).float()
        for i, block in enumerate(projection.blocks):
            q, k, v = block.qkv(rms(x, block.norm1.weight).to(torch.float16)).chunk(3, -1)
            q = rms(q.unflatten(-1, (self.heads, -1)), block.q_norm.weight).flatten(2)
            k = rms(k.unflatten(-1, (self.heads, -1)), block.k_norm.weight).flatten(2)
            x = x + self.back(f"text.{i}.attention", block.proj(joint_attention(q, k, v, rope, self.heads)))
            y = rms(x, block.norm2.weight).to(torch.float16)
            x = x + self.back(f"text.{i}.feed_forward", block.ffn_down(situ(block.ffn_up(y), block.ffn_gate(y))))
        return x

    def block(self, i, layer, x, rope, modulation):
        shift_a, scale_a, gate_a, shift_f, scale_f, gate_f = modulation.unbind(1)
        y = (rms(x, layer.norm1.weight) * (1 + scale_a[:, None]) + shift_a[:, None]).to(torch.float16)
        q = rms(layer.proj["q"](y).unflatten(-1, (self.heads, -1)), layer.q_norm.weight).flatten(2)
        k = rms(layer.proj["k"](y).unflatten(-1, (self.heads, -1)), layer.k_norm.weight).flatten(2)
        out = layer.proj["o"](joint_attention(q, k, layer.proj["v"](y), rope, self.heads))
        x = x + self.back(f"layers.{i}.attention", out) * gate_a[:, None]
        y = (rms(x, layer.norm2.weight) * (1 + scale_f[:, None]) + shift_f[:, None]).to(torch.float16)
        hidden, gate = layer.proj["ffn_up_gate"](y).chunk(2, -1)
        return x + self.back(f"layers.{i}.feed_forward", layer.proj["ffn_down"](situ(hidden, gate))) * gate_f[:, None]

    def forward(self, latents, caption, rope, time, skip):
        t = self.t
        _, channels, h, w = latents.shape
        time = t.time_embedding(time)
        modulation = t.shared_block_modulation(time).view(1, 6, -1)
        head = t.video_head_modulation(time).view(1, 2, -1)

        video = t.video_embedder(latents.flatten(2).transpose(1, 2).to(torch.float16)).float()
        text = self.text(caption, rope[h * w:])
        x = torch.cat((video, text), 1)
        for i, layer in enumerate(t.layers):
            if i == SKIPPED_BLOCK:
                x = torch.cond(skip, lambda x: x.clone(), lambda x: self.block(i, layer, x, rope, modulation), (x,))
            else:
                x = self.block(i, layer, x, rope, modulation)
        x = rms(x[:, : h * w], t.final_norm_video.weight)
        x = x * (1 + head[:, 1, None]) + head[:, 0, None]
        x = t.final_linear_video(x.to(torch.float16)).float()
        return x.transpose(1, 2).reshape(1, channels, h, w)


BRANCHES = ("proj.o", "proj.ffn_down", "blocks.0.proj", "blocks.0.ffn_down", "blocks.1.proj", "blocks.1.ffn_down")


def branch_name(module_name):
    """layers.N.proj.o -> layers.N.attention, text_embedder.blocks.N.ffn_down -> text.N.feed_forward."""
    if module_name.startswith("text_embedder.blocks."):
        index, part = module_name.split(".")[2], module_name.split(".")[3]
        return f"text.{index}." + ("attention" if part == "proj" else "feed_forward")
    index, part = module_name.split(".")[1], module_name.split(".")[3]
    return f"layers.{index}." + ("attention" if part == "o" else "feed_forward")


def measure_peaks(pipe, prompts):
    """The largest value every linear projection makes across calibration runs in bf16, by module name."""
    peaks = {}
    hooks = []
    for name, module in pipe.transformer.named_modules():
        if isinstance(module, torch.nn.Linear) and not name.startswith(("time_embedding", "shared_block_modulation", "video_head_modulation")):
            def record(_m, _i, out, name=name):
                peaks[name] = max(peaks.get(name, 0.0), float(out.detach().abs().float().max()))
            hooks.append(module.register_forward_hook(record))
    for i, prompt in enumerate(prompts):
        width, height = CALIBRATION_SIZES[i % len(CALIBRATION_SIZES)]
        pipe(prompt, width=width, height=height, num_inference_steps=50, generator=torch.Generator("cuda").manual_seed(i))
    for hook in hooks:
        hook.remove()
    return peaks


def scale_branches(transformer, peaks):
    """Divide each branch's last projection by a power of two where its values would leave fp16's range; every other
    projection must already stay inside it."""
    scales = {}
    for name, peak in sorted(peaks.items()):
        is_branch = name.endswith(("proj.o", "proj.ffn_down")) or (name.startswith("text_embedder.blocks.") and name.endswith((".proj", ".ffn_down")))
        if not is_branch:
            if peak > FP16_LIMIT / 4:
                raise SystemExit(f"{name} reaches {peak:.4g}, too near fp16's limit, and is not a branch's end to scale")
            continue
        if peak <= HEADROOM:
            continue
        factor = 2.0 ** math.ceil(math.log2(peak / HEADROOM))
        linear = transformer.get_submodule(name)
        linear.weight.data /= factor
        if linear.bias is not None:
            linear.bias.data /= factor
        scales[branch_name(name)] = factor
    return scales


def fp32_timing(transformer):
    """The timestep's MLP and the modulations it makes stay fp32: they decide every block's scale and shift."""
    for name in ("time_embedding", "shared_block_modulation", "video_head_modulation"):
        transformer.get_submodule(name).float()


def check_against_original(transformer, denoiser, device):
    """The rebuilt pass gives the original's answer, with and without block 22, for a prompt and a size that are not
    round numbers."""
    latents = torch.randn(1, 128, 48, 80, device=device)
    caption = torch.randn(1, 45, 4096, device=device)
    timestep = 640.0
    rope = rope_rows(transformer, 48, 80, 45, device)
    time = time_rows(transformer, timestep, device)
    for skip in (False, True):
        with torch.no_grad():
            original = denoiser.reference(latents, timestep, caption, skip_layers=[SKIPPED_BLOCK] if skip else None).sample.float()
            rebuilt = denoiser(latents, caption, rope, time, torch.tensor(skip, device=device))
        error = (original - rebuilt).abs().max().item()
        scale = original.abs().max().item()
        print(f"rebuilt denoiser against the original (skip={skip}): max difference {error:.4g} on values up to {scale:.4g}", flush=True)
        if error > 2e-2 * scale:
            raise SystemExit("the rebuilt denoiser does not match the original")


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
    """latents [1,128,h,w] (fp32, the denoiser's) -> pixels [1,3,16h,16w] in [0, 1] (fp32), as MOSSImagePipeline decodes."""

    def __init__(self, vae):
        super().__init__()
        for module in vae.decoder.modules():
            if type(module).__name__ == "Attention":
                module.set_processor(FusedVaeAttnProcessor())
        self.vae = vae
        eps = vae.config.batch_norm_eps
        self.register_buffer("std", (vae.bn.running_var.float() + eps).sqrt().view(1, -1, 1, 1))
        self.register_buffer("mean", vae.bn.running_mean.float().view(1, -1, 1, 1))

    def forward(self, latents):
        z = latents * self.std + self.mean
        b, c, h, w = z.shape
        z = z.reshape(b, c // 4, 2, 2, h, w).permute(0, 1, 4, 2, 5, 3).reshape(b, c // 4, 2 * h, 2 * w)
        pixels = self.vae.decode(z.to(self.vae.dtype), return_dict=False)[0].float().clamp(-1, 1)
        return pixels / 2 + 0.5


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


def quantize(src, dst, block_size, bits):
    import onnx
    from onnxruntime.quantization.matmul_nbits_quantizer import MatMulNBitsQuantizer, DefaultWeightOnlyQuantConfig

    model = onnx.load(str(src), load_external_data=True)
    config = DefaultWeightOnlyQuantConfig(block_size=block_size, is_symmetric=True, accuracy_level=0, bits=bits,
                                          op_types_to_quantize=("MatMul",))
    quantizer = MatMulNBitsQuantizer(model, algo_config=config)
    quantizer.process()
    graph = quantizer.model.model.graph
    used = {name for node in graph.node for name in node.input}
    for subgraph_node in (n for n in graph.node if n.op_type == "If"):
        for attribute in subgraph_node.attribute:
            for node in attribute.g.node:
                used.update(node.input)
    unused = [init for init in graph.initializer if init.name not in used]
    for init in unused:
        graph.initializer.remove(init)
    print(f"dropped {len(unused)} weight(s) the quantised graph no longer uses", flush=True)
    dst.parent.mkdir(parents=True, exist_ok=True)
    clear(dst)
    onnx.save_model(quantizer.model.model, str(dst), save_as_external_data=True, all_tensors_to_one_file=True,
                    location=dst.name + ".data")
    print(f"quantized {dst.name}", flush=True)


def reference_pipeline(module, checkpoint):
    """MOSSImagePipeline as its makers run it: transformer and text encoder in bf16, the VAE in fp32."""
    from diffusers import AutoencoderKLFlux2
    from transformers import AutoModel, AutoTokenizer
    return module.MOSSImagePipeline(
        module.MOSSImageTransformer.from_pretrained(checkpoint / "transformer", torch_dtype=torch.bfloat16).eval(),
        module.MOSSImageScheduler.from_pretrained(checkpoint / "scheduler"),
        AutoModel.from_pretrained(checkpoint / "text_encoder", dtype=torch.bfloat16).eval(),
        AutoTokenizer.from_pretrained(checkpoint / "tokenizer"),
        AutoencoderKLFlux2.from_pretrained(checkpoint / "vae", torch_dtype=torch.float32).eval())


def build_denoiser(checkpoint, out, block_size, bits):
    module = moss(checkpoint)
    device = "cuda"
    cached = out / "denoiser" / "branch-peaks.json"
    if cached.exists():
        peaks = json.loads(cached.read_text())
    else:
        peaks = measure_peaks(reference_pipeline(module, checkpoint).to(device), CALIBRATION)
        cached.parent.mkdir(parents=True, exist_ok=True)
        cached.write_text(json.dumps(peaks, indent=1))
        torch.cuda.empty_cache()

    target = "denoiser" if bits == 8 else f"denoiser-{bits}bit"
    full = out / "denoiser-fp16" / "denoiser.onnx"
    if full.exists() and (out / "denoiser" / "branch-scales.json").exists():
        # The fp16 graph is the same for every weight width: made once from the cached peaks, then quantised again.
        quantize(full, out / target / "denoiser.onnx", block_size, bits)
        (out / target / "branch-scales.json").write_text((out / "denoiser" / "branch-scales.json").read_text())
        return

    reference = module.MOSSImageTransformer.from_pretrained(checkpoint / "transformer", torch_dtype=torch.float32).to(device).eval()
    transformer = module.MOSSImageTransformer.from_pretrained(checkpoint / "transformer", torch_dtype=torch.float16).to(device).eval()
    scales = scale_branches(transformer, peaks)
    (out / "denoiser" / "branch-scales.json").write_text(json.dumps(scales, indent=1))
    fp32_timing(transformer)
    denoiser = Denoiser(transformer, scales).eval()
    denoiser.reference = reference
    check_against_original(transformer, denoiser, device)
    del denoiser.reference, reference
    torch.cuda.empty_cache()

    h = torch.export.Dim("h", min=16, max=128)
    w = torch.export.Dim("w", min=16, max=128)
    rows = torch.export.Dim("caption_rows", min=1, max=4096)
    tokens = torch.export.Dim("rope_rows", min=257, max=20480)
    sample = (torch.randn(1, 128, 64, 64, device=device), torch.randn(1, 77, 4096, device=device),
              rope_rows(transformer, 64, 64, 77, device), time_rows(transformer, 500.0, device),
              torch.tensor(False, device=device))
    with torch.no_grad():
        export(denoiser, sample, full,
               ["latents", "caption", "rope", "time", "skip"], ["velocity"],
               {"latents": {2: h, 3: w}, "caption": {1: rows}, "rope": {0: tokens}, "time": None, "skip": None})
    del transformer, denoiser
    torch.cuda.empty_cache()
    quantize(full, out / target / "denoiser.onnx", block_size, bits)
    if target != "denoiser":
        (out / target / "branch-scales.json").write_text(json.dumps(scales, indent=1))


def build_decoder(checkpoint, out):
    from diffusers import AutoencoderKLFlux2
    device = "cuda"
    vae = AutoencoderKLFlux2.from_pretrained(checkpoint / "vae", torch_dtype=torch.float16).to(device).eval()
    decoder = Decoder(vae).to(device).eval()
    h = torch.export.Dim("h", min=16, max=128)
    w = torch.export.Dim("w", min=16, max=128)
    with torch.no_grad():
        export(decoder, (torch.randn(1, 128, 64, 64, device=device),), out / "decoder" / "decoder.onnx",
               ["latents"], ["pixels"], {"latents": {2: h, 3: w}})


def main():
    p = argparse.ArgumentParser()
    p.add_argument("checkpoint")
    p.add_argument("out")
    p.add_argument("--block-size", type=int, default=32)
    p.add_argument("--denoiser-bits", type=int, choices=[4, 8], default=8)
    p.add_argument("--only", choices=["denoiser", "decoder"], default=None)
    args = p.parse_args()
    checkpoint, out = Path(args.checkpoint), Path(args.out)
    if args.only in (None, "denoiser"):
        build_denoiser(checkpoint, out, args.block_size, args.denoiser_bits)
    if args.only in (None, "decoder"):
        build_decoder(checkpoint, out)


if __name__ == "__main__":
    main()
