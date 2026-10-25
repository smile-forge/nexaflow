"""Build MOSS-Image as two ONNX graphs for Nexaflow's AI host: the denoiser and the decoder. Its prompt encoder,
Qwen3.5-9B unchanged, is read by the host on llama.cpp (compare_moss_encoder.py chooses the build), so no graph is made
of it here.

The graphs are traced from the published PyTorch model with only these changes, each exact or the model's own:
  * the residual streams — the joint image-and-text stream, and the text refiner's — are kept in fp32 and every branch
    runs in fp16; a branch whose values would leave fp16's range is scaled down by a power of two where its last
    projection makes them, and back up, in fp32, as it rejoins the stream (calibrated on bf16 runs, branch-scales.json);
  * every norm is one RMSNormalization and every rotation one RotaryEmbedding (ONNX opset 23), in place of the
    reference's operations one at a time; the rotation is done in fp16, where the reference does it in bf16;
  * rotary angles and the timestep's sinusoid are laid out by the host, as the reference computes them — the angles in
    fp32, the sinusoid in fp64 — and come in as fp32, so no Python integer decides a shape inside the graph;
  * the negative prompt's pass skips block 22, as MOSSImagePipeline does by default: an input, `skip`, chooses it;
  * attention is ONNX Runtime's fused MultiHeadAttention, which never holds every token-by-token score;
  * every block's q, k, v, o, ffn_up_gate and ffn_down projection has a LoRA branch beside it — (x down) up, times
    `lora_strength`, added in fp16 where the projection stays fp16 and in fp32 where it rejoins the stream — run only
    while `lora_strength` is not 0, the blocks being an `If` of the two paths over the same weights; its matrices are
    inputs of any rank defaulting to zeros, and the timestep's MLP and
    the modulations' biases are inputs defaulting to the model's own; an ONNX Runtime LoRA adapter (convert_moss_loras.py)
    sets them for a picture, unmerged, as the LoRAs' makers ask;
  * the decoder takes the denoiser's latents, undoes the VAE's batch norm and its 2x2 patching, and gives pixels in
    [0, 1]; it runs in fp16 with fused attention.

usage: python -I build_moss_image.py <moss-image checkpoint> <out-dir> [--only denoiser|decoder]

The calibration peaks are kept in <out-dir>/denoiser/branch-peaks.json and reused when present.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F

HEADROOM = 4096.0     # the largest value a scaled branch may reach in calibration: 16x under fp16's limit
FP16_LIMIT = 65504.0
SKIPPED_BLOCK = 22    # MOSSImagePipeline's uncond_skip_layers default
EPS = 1e-6            # _RMSNorm's
OPSET = 23            # the denoiser's: RMSNormalization and RotaryEmbedding
# The decoder's. At 21 and later the exporter writes the VAE's group norms as GroupNormalization, which ONNX Runtime 1.31
# runs on CUDA with 2.1 GiB more at 1024x1024, 4.2 GiB more at 1456x1456, and a fifth slower than the InstanceNormalization
# it writes before 21.
DECODER_OPSET = 20

LAYERS = 42
PROJECTIONS = ("q", "k", "v", "o", "ffn_up_gate", "ffn_down")
LORA_DOWN = [f"lora.{i}.{p}.down" for i in range(LAYERS) for p in PROJECTIONS]
LORA_UP = [f"lora.{i}.{p}.up" for i in range(LAYERS) for p in PROJECTIONS]

# The timestep's MLP and the modulations' biases, which a distilled sampler (the 8-step LoRA) replaces: graph input
# names, and the transformer's parameter each defaults to.
TIME_PARAMETERS = {
    "time.embedding.0.weight": "time_embedding.0.weight",
    "time.embedding.0.bias": "time_embedding.0.bias",
    "time.embedding.2.weight": "time_embedding.2.weight",
    "time.embedding.2.bias": "time_embedding.2.bias",
    "time.block_modulation.bias": "shared_block_modulation.bias",
    "time.head_modulation.bias": "video_head_modulation.bias",
}

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


# ── fused operations ─────────────────────────────────────────────────────────────────────────────────────────────
# Each runs as the reference's arithmetic when traced and checked, and is written into the graph as one ONNX operation.

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


@torch.library.custom_op("nexaflow::rms_norm", mutates_args=())
def rms_norm(x: torch.Tensor, weight: torch.Tensor) -> torch.Tensor:
    """_RMSNorm over the last axis, computed in fp32, in x's precision; weight in x's precision too."""
    y = x.float() * torch.rsqrt(x.float().square().mean(-1, keepdim=True) + EPS)
    return (y * weight.float()).to(x.dtype)


@rms_norm.register_fake
def _(x, weight):
    return torch.empty_like(x)


def rms_norm_onnx(x, weight):
    from onnxscript import opset23 as op
    return op.RMSNormalization(x, weight, axis=-1, epsilon=EPS)


@torch.library.custom_op("nexaflow::rotary", mutates_args=())
def rotary(x: torch.Tensor, cos: torch.Tensor, sin: torch.Tensor, positions: torch.Tensor, heads: int) -> torch.Tensor:
    """The reference's _rotate, rotating each head's halves: x [1, tokens, heads * head_dim], cos and sin
    [rows, head_dim / 2] in x's precision, positions [1, tokens] the row each token takes."""
    b, t, e = x.shape
    half = e // heads // 2
    xh = x.view(b, t, heads, 2 * half).float()
    a, rest = xh[..., :half], xh[..., half:]
    c, s = cos[positions[0]].float()[None, :, None], sin[positions[0]].float()[None, :, None]
    return torch.cat((a * c - rest * s, rest * c + a * s), -1).reshape(b, t, e).to(x.dtype)


@rotary.register_fake
def _(x, cos, sin, positions, heads):
    return torch.empty_like(x)


def rotary_onnx(x, cos, sin, positions, heads: int):
    from onnxscript import opset23 as op
    return op.RotaryEmbedding(x, cos, sin, positions, interleaved=0, num_heads=heads)


TRANSLATIONS = {
    torch.ops.nexaflow.attention.default: attention_onnx,
    torch.ops.nexaflow.rms_norm.default: rms_norm_onnx,
    torch.ops.nexaflow.rotary.default: rotary_onnx,
}


# ── what the host lays out ───────────────────────────────────────────────────────────────────────────────────────

def rope_rows(transformer, height, width, text_length, device):
    """The rotary rows the reference computes for a [height, width] latent and a prompt of text_length tokens:
    image rows first, then text, [tokens, head_dim] fp32 — sines of every angle, then cosines."""
    return transformer.rope(height, width, text_length, device)[0].float()


def time_rows(transformer, timestep, device):
    """The reference's timestep sinusoid, computed in fp64 and given as fp32: [1, freq_dim]."""
    half = transformer.config.freq_dim // 2
    freq = 10000 ** (-torch.arange(half, device=device, dtype=torch.float64) / half)
    phase = torch.as_tensor([float(timestep)], device=device, dtype=torch.float64)[:, None] * freq
    return torch.cat((phase.cos(), phase.sin()), -1).float()


def time_defaults(transformer):
    """The timestep path's own parameters, fp32, in TIME_PARAMETERS' order."""
    return [transformer.get_parameter(name).detach().float() for name in TIME_PARAMETERS.values()]


def no_lora(transformer, device):
    """Rank-1 zeros for every LoRA input: a pass with no LoRA."""
    downs, ups = [], []
    for i in range(LAYERS):
        for p in PROJECTIONS:
            linear = transformer.layers[i].proj[p]
            downs.append(torch.zeros(linear.in_features, 1, device=device, dtype=torch.float16))
            ups.append(torch.zeros(1, linear.out_features, device=device, dtype=torch.float16))
    return downs, ups


# ── the denoiser ─────────────────────────────────────────────────────────────────────────────────────────────────

def situ(hidden, gate):
    return (4.0 * torch.tanh(gate / 4.0) * torch.sigmoid(gate)) * (25.0 * torch.tanh(hidden / 25.0))


class Denoiser(torch.nn.Module):
    """
    One denoising pass of MOSSImageTransformer for one picture, on rows the host has laid out:

      latents        [1, 128, h, w]   fp32  the picture's latents, h and w its size over 16
      caption        [1, L, 4096]     fp32  the prompt encoder's reading of the prompt, every row a real token
      rope           [h*w + L, 128]   fp32  rotary rows, image first: sines then cosines (rope_rows)
      time           [1, 256]         fp32  the timestep's sinusoid (time_rows)
      skip           []               bool  leave out block 22, as the negative prompt's pass does
      lora_strength  []               fp32  how much of the LoRA branches is added
      lora.{i}.{p}.down [in, r], .up [r, out]  fp16  each projection's LoRA, (x down) up — zeros when none
      time.*         fp32             the timestep's MLP and the modulations' biases (TIME_PARAMETERS)
    -> velocity [1, 128, h, w]  fp32  the flow the sampler steps along

    Weights are fp16, the residual streams fp32. `scales` maps a branch's name to the power of two its last projection's
    weights were divided by; its output is multiplied back in fp32, and a LoRA's change to it is added after.
    """

    def __init__(self, transformer, scales):
        super().__init__()
        self.t = transformer
        self.scales = scales
        self.heads = transformer.config.num_heads

    def project(self, linear, x16, lora, strength, scale=1.0, keep16=False):
        """A projection and, where a LoRA is active, its branch (x down) up times `lora_strength`. Where `keep16` asks —
        the q, k, v and feed-forward inputs, never scaled — it is given back in fp16 and the branch is added in fp16:
        the sum would be rounded to fp16 anyway, and fp32 copies of these projections would hold a gigabyte at 1024².
        Else it is given back in fp32, a branch's end scaled back up, the LoRA's change added after."""
        out = linear(x16)
        branch = None if lora is None else torch.matmul(torch.matmul(x16, lora[0]), lora[1])
        if keep16:
            if scale != 1.0:
                raise ValueError("a projection given back in fp16 is never scaled")
            return out if branch is None else out + branch * strength.to(torch.float16)
        out = out.float() if scale == 1.0 else out.float() * scale
        return out if branch is None else out + branch.float() * strength

    def text(self, caption, cos, sin, positions):
        projection = self.t.text_embedder
        x = projection.proj(caption.to(torch.float16)).float()
        for i, block in enumerate(projection.blocks):
            q, k, v = block.qkv(rms_norm(x, block.norm1.weight.float()).to(torch.float16)).chunk(3, -1)
            q = rotary(rms_norm(q.unflatten(-1, (self.heads, -1)), block.q_norm.weight).flatten(2), cos, sin, positions, self.heads)
            k = rotary(rms_norm(k.unflatten(-1, (self.heads, -1)), block.k_norm.weight).flatten(2), cos, sin, positions, self.heads)
            x = x + block.proj(attention(q, k, v, self.heads)).float() * self.scales.get(f"text.{i}.attention", 1.0)
            y = rms_norm(x, block.norm2.weight.float()).to(torch.float16)
            x = x + block.ffn_down(situ(block.ffn_up(y), block.ffn_gate(y))).float() * self.scales.get(f"text.{i}.feed_forward", 1.0)
        return x

    def block(self, i, layer, x, cos, sin, positions, modulation, lora, strength):
        """One of the 42 blocks; `lora` maps each projection to its (down, up), or is None where no LoRA is active."""
        shift_a, scale_a, gate_a, shift_f, scale_f, gate_f = modulation.unbind(1)
        proj = layer.proj
        of = lambda p: None if lora is None else lora[p]
        y = (rms_norm(x, layer.norm1.weight.float()) * (1 + scale_a[:, None]) + shift_a[:, None]).to(torch.float16)
        q = self.project(proj["q"], y, of("q"), strength, keep16=True)
        k = self.project(proj["k"], y, of("k"), strength, keep16=True)
        v = self.project(proj["v"], y, of("v"), strength, keep16=True)
        q = rotary(rms_norm(q.unflatten(-1, (self.heads, -1)), layer.q_norm.weight).flatten(2), cos, sin, positions, self.heads)
        k = rotary(rms_norm(k.unflatten(-1, (self.heads, -1)), layer.k_norm.weight).flatten(2), cos, sin, positions, self.heads)
        out = self.project(proj["o"], attention(q, k, v, self.heads), of("o"), strength,
                           self.scales.get(f"layers.{i}.attention", 1.0))
        x = x + out * gate_a[:, None]
        y = (rms_norm(x, layer.norm2.weight.float()) * (1 + scale_f[:, None]) + shift_f[:, None]).to(torch.float16)
        hidden, gate = self.project(proj["ffn_up_gate"], y, of("ffn_up_gate"), strength, keep16=True).chunk(2, -1)
        out = self.project(proj["ffn_down"], situ(hidden, gate), of("ffn_down"), strength,
                           self.scales.get(f"layers.{i}.feed_forward", 1.0))
        return x + out * gate_f[:, None]

    def blocks(self, x, cos, sin, positions, modulation, lora_down, lora_up, strength, skip, with_lora):
        """The 42 blocks, with or without the LoRA branches; block 22 left out where `skip` asks."""
        for i, layer in enumerate(self.t.layers):
            lora = None
            if with_lora:
                lora = {p: (lora_down[i * len(PROJECTIONS) + j], lora_up[i * len(PROJECTIONS) + j]) for j, p in enumerate(PROJECTIONS)}
            if i == SKIPPED_BLOCK:
                x = torch.cond(skip, lambda x: x.clone(),
                               lambda x: self.block(i, layer, x, cos, sin, positions, modulation, lora, strength), (x,))
            else:
                x = self.block(i, layer, x, cos, sin, positions, modulation, lora, strength)
        return x

    def forward(self, latents, caption, rope, time, skip, lora_strength, lora_down, lora_up, time_parameters):
        t = self.t
        _, channels, h, w = latents.shape
        half = rope.shape[1] // 2
        sin, cos = rope[:, :half].to(torch.float16), rope[:, half:].to(torch.float16)
        w0, b0, w2, b2, block_bias, head_bias = time_parameters
        emb = F.linear(F.silu(F.linear(time, w0, b0)), w2, b2)
        modulation = F.linear(emb, t.shared_block_modulation.weight, block_bias).view(1, 6, -1)
        head = F.linear(emb, t.video_head_modulation.weight, head_bias).view(1, 2, -1)

        # Each token's rotary row, worked out once for the pass: the joint stream's, and the text refiner's.
        positions = torch.arange(h * w + caption.shape[1], device=rope.device).unsqueeze(0)
        text_positions = torch.arange(caption.shape[1], device=rope.device).unsqueeze(0)

        video = t.video_embedder(latents.flatten(2).transpose(1, 2).to(torch.float16)).float()
        text = self.text(caption, cos[h * w:], sin[h * w:], text_positions)
        x = torch.cat((video, text), 1)
        # The LoRA branches run only while a LoRA is active: a picture without one pays nothing for them.
        x = torch.cond(lora_strength != 0,
                       lambda x: self.blocks(x, cos, sin, positions, modulation, lora_down, lora_up, lora_strength, skip, True),
                       lambda x: self.blocks(x, cos, sin, positions, modulation, lora_down, lora_up, lora_strength, skip, False),
                       (x,))
        x = rms_norm(x[:, : h * w], t.final_norm_video.weight.float())
        x = x * (1 + head[:, 1, None]) + head[:, 0, None]
        x = t.final_linear_video(x.to(torch.float16)).float()
        return x.transpose(1, 2).reshape(1, channels, h, w)


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


def branch_name(module_name):
    """layers.N.proj.o -> layers.N.attention, text_embedder.blocks.N.ffn_down -> text.N.feed_forward."""
    if module_name.startswith("text_embedder.blocks."):
        index, part = module_name.split(".")[2], module_name.split(".")[3]
        return f"text.{index}." + ("attention" if part == "proj" else "feed_forward")
    index, part = module_name.split(".")[1], module_name.split(".")[3]
    return f"layers.{index}." + ("attention" if part == "o" else "feed_forward")


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


class Projection(torch.nn.Module):
    """A block's projection — bias-free, like all of MOSS-Image's — holding its weight transposed, [in, out], so the
    exported graph multiplies by the stored weight itself. Inside the If of the two paths the exporter folds no
    transpose, and ONNX Runtime's quantiser rewrites only a MatMul on a stored weight."""

    def __init__(self, linear):
        super().__init__()
        if linear.bias is not None:
            raise SystemExit("a block's projection has a bias, which Projection does not carry")
        self.in_features, self.out_features = linear.in_features, linear.out_features
        self.weight = torch.nn.Parameter(linear.weight.detach().t().contiguous(), requires_grad=False)

    def forward(self, x):
        return torch.matmul(x, self.weight)


def transpose_projections(transformer):
    """Every block's q, k, v, o, ffn_up_gate and ffn_down as a Projection, one at a time, so only one extra matrix is
    held while it is made."""
    for layer in transformer.layers:
        for p in PROJECTIONS:
            layer.proj[p] = Projection(layer.proj[p])
    torch.cuda.empty_cache()


def fp32_timing(transformer):
    """The timestep's MLP and the modulations it makes stay fp32: they decide every block's scale and shift."""
    for name in ("time_embedding", "shared_block_modulation", "video_head_modulation"):
        transformer.get_submodule(name).float()


def lora_hooks(reference, downs, ups, strength):
    """Adds a LoRA to the reference transformer as its makers apply one, unmerged: each projection's output plus
    strength · (x down) up, in fp32."""
    hooks = []
    for i in range(LAYERS):
        for j, p in enumerate(PROJECTIONS):
            down, up = downs[i * len(PROJECTIONS) + j].float(), ups[i * len(PROJECTIONS) + j].float()

            def add(_m, inputs, out, down=down, up=up):
                return out + (inputs[0].float() @ down @ up * strength).to(out.dtype)
            hooks.append(reference.layers[i].proj[p].register_forward_hook(add))
    return hooks


def check_against_original(transformer, reference, denoiser, device):
    """The rebuilt pass gives the original's answer — with and without block 22, with no LoRA and with one — for a
    prompt and a size that are not round numbers."""
    latents = torch.randn(1, 128, 48, 80, device=device)
    caption = torch.randn(1, 45, 4096, device=device)
    timestep = 640.0
    rope = rope_rows(transformer, 48, 80, 45, device)
    time = time_rows(transformer, timestep, device)
    times = time_defaults(transformer)
    zeros = no_lora(transformer, device)
    generator = torch.Generator(device).manual_seed(7)
    rank = 8
    lora = ([torch.randn(d.shape[0], rank, device=device, generator=generator).half() * 0.02 for d in zeros[0]],
            [torch.randn(rank, u.shape[1], device=device, generator=generator).half() * 0.02 for u in zeros[1]])
    for skip, with_lora in ((False, False), (True, False), (False, True)):
        downs, ups = lora if with_lora else zeros
        strength = 0.8 if with_lora else 0.0
        hooks = lora_hooks(reference, downs, ups, strength) if with_lora else []
        with torch.no_grad():
            original = reference(latents, timestep, caption, skip_layers=[SKIPPED_BLOCK] if skip else None).sample.float()
            rebuilt = denoiser(latents, caption, rope, time, torch.tensor(skip, device=device),
                               torch.tensor(strength, device=device), downs, ups, times)
        for hook in hooks:
            hook.remove()
        error = (original - rebuilt).abs().max().item()
        scale = original.abs().max().item()
        print(f"rebuilt denoiser against the original (skip={skip}, lora={with_lora}): max difference {error:.4g} on values up to {scale:.4g}", flush=True)
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


def save(model, path):
    import onnx
    path.parent.mkdir(parents=True, exist_ok=True)
    clear(path)
    model.ir_version = min(model.ir_version, 13)
    onnx.save_model(model, str(path), save_as_external_data=True, all_tensors_to_one_file=True, location=path.name + ".data")


def export(module, args, path, input_names, output_names, dynamic_shapes, opset=OPSET):
    path.parent.mkdir(parents=True, exist_ok=True)
    clear(path)
    program = torch.onnx.export(module, args, dynamo=True, input_names=input_names, output_names=output_names,
                                dynamic_shapes=dynamic_shapes, optimize=True, opset_version=opset,
                                custom_translation_table=TRANSLATIONS)
    program.save(str(path), external_data=True)
    # The exporter stamps the newest IR its onnx knows; ONNX Runtime 1.31 reads up to 13. It also gives every node its
    # stack trace — inside an If, the whole generated code each time, most of the graph's size — which nothing reads.
    # The weights stay where they are.
    import onnx
    model = onnx.load(str(path), load_external_data=False)
    model.ir_version = min(model.ir_version, 13)
    pending = [model.graph]
    while pending:
        graph = pending.pop()
        for node in graph.node:
            del node.metadata_props[:]
            pending.extend(a.g for a in node.attribute if a.type == onnx.AttributeProto.GRAPH)
            pending.extend(g for a in node.attribute if a.type == onnx.AttributeProto.GRAPHS for g in a.graphs)
    onnx.save_model(model, str(path))
    print(f"exported {path.name}", flush=True)


def shared_weight_quantizer(config):
    """ONNX Runtime's weight-only quantiser, quantising each weight once. The denoiser's two paths, with a LoRA and
    without, read the same weights, and the quantiser alone would quantise a weight, and add it to the graph, for every
    MatMul that reads it; here a MatMul reading a weight already quantised takes the earlier one's."""
    from onnx import helper
    from onnxruntime.quantization.matmul_nbits_quantizer import DefaultWeightOnlyQuantizer

    class SharedWeightQuantizer(DefaultWeightOnlyQuantizer):
        def __init__(self, config):
            super().__init__(config)
            self.quantized = {}

        def quantize_matmul(self, node, graph_stack):
            earlier = self.quantized.get(node.input[1])
            if earlier is None:
                nodes = super().quantize_matmul(node, graph_stack)
                if len(nodes) == 1 and nodes[0].op_type == "MatMulNBits":
                    self.quantized[node.input[1]] = nodes[0]
                return nodes
            again = helper.make_node("MatMulNBits", [node.input[0], *earlier.input[1:]], list(node.output),
                                     name=f"{node.name}_Q{self.config.bits}" if node.name else "", domain=earlier.domain)
            again.attribute.extend(earlier.attribute)
            return [again]

    return SharedWeightQuantizer(config)


def quantize(src, dst, block_size, defaults):
    """Quantise every constant matrix product to 8 bits, then give the overridable inputs their defaults — after, so
    the quantiser never takes a LoRA's zeros or the timestep's weights for a weight of its own."""
    import onnx
    from onnx import numpy_helper
    from onnxruntime.quantization.matmul_nbits_quantizer import MatMulNBitsQuantizer, DefaultWeightOnlyQuantConfig

    model = onnx.load(str(src), load_external_data=True)
    config = DefaultWeightOnlyQuantConfig(block_size=block_size, is_symmetric=True, accuracy_level=0, bits=8,
                                          op_types_to_quantize=("MatMul",))
    quantizer = MatMulNBitsQuantizer(model, algo_config=config)
    quantizer.node_quantizer = shared_weight_quantizer(config)
    quantizer.process()
    model = quantizer.model.model
    graph = model.graph
    stored = {t.name for t in graph.initializer}
    pending, left = [graph], 0
    while pending:
        g = pending.pop()
        left += sum(1 for n in g.node if n.op_type == "MatMul" and n.input[1] in stored)
        pending.extend(a.g for n in g.node for a in n.attribute if a.type == onnx.AttributeProto.GRAPH)
    if left:
        raise SystemExit(f"{left} MatMul(s) on a stored weight were left unquantised")
    inputs = {i.name for i in graph.input}
    missing = set(defaults) - inputs
    if missing:
        raise SystemExit(f"the exported graph takes no input named {sorted(missing)[:3]}")
    for name, value in defaults.items():
        graph.initializer.append(numpy_helper.from_array(value, name))
    save(model, dst)
    print(f"quantized {dst.name} to 8 bits, {len(defaults)} input(s) given defaults", flush=True)


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


def defaults_of(transformer):
    """Each overridable input's default: rank-1 zeros for the LoRA branches, the model's own timestep path."""
    downs, ups = no_lora(transformer, "cpu")
    values = {name: t.numpy() for name, t in zip(LORA_DOWN, downs)}
    values.update({name: t.numpy() for name, t in zip(LORA_UP, ups)})
    values.update({name: transformer.get_parameter(param).detach().float().cpu().numpy() for name, param in TIME_PARAMETERS.items()})
    return values


def build_denoiser(checkpoint, out, block_size):
    module = moss(checkpoint)
    device = "cuda"
    full = out / "denoiser-fp16" / "denoiser.onnx"
    defaults = out / "denoiser-fp16" / "defaults.npz"
    if full.exists() and defaults.exists() and (out / "denoiser" / "branch-scales.json").exists():
        # An fp16 graph already exported is quantised again, as for another block size, without tracing it anew.
        quantize(full, out / "denoiser" / "denoiser.onnx", block_size, dict(np.load(defaults)))
        return

    cached = out / "denoiser" / "branch-peaks.json"
    if cached.exists():
        peaks = json.loads(cached.read_text())
    else:
        peaks = measure_peaks(reference_pipeline(module, checkpoint).to(device), CALIBRATION)
        cached.parent.mkdir(parents=True, exist_ok=True)
        cached.write_text(json.dumps(peaks, indent=1))
        torch.cuda.empty_cache()

    reference = module.MOSSImageTransformer.from_pretrained(checkpoint / "transformer", torch_dtype=torch.float32).to(device).eval()
    transformer = module.MOSSImageTransformer.from_pretrained(checkpoint / "transformer", torch_dtype=torch.float16).to(device).eval()
    scales = scale_branches(transformer, peaks)
    (out / "denoiser").mkdir(parents=True, exist_ok=True)
    (out / "denoiser" / "branch-scales.json").write_text(json.dumps(scales, indent=1))
    fp32_timing(transformer)
    transpose_projections(transformer)
    denoiser = Denoiser(transformer, scales).eval()
    check_against_original(transformer, reference, denoiser, device)
    del reference
    torch.cuda.empty_cache()

    h = torch.export.Dim("h", min=16, max=128)
    w = torch.export.Dim("w", min=16, max=128)
    rows = torch.export.Dim("caption_rows", min=1, max=4096)
    tokens = torch.export.Dim("rope_rows", min=257, max=20480)
    rank = torch.export.Dim("lora_rank", min=1, max=256)
    downs, ups = no_lora(transformer, device)
    sample_rank = 4   # a rank of 1 would be specialised away
    downs = [torch.zeros(d.shape[0], sample_rank, device=device, dtype=torch.float16) for d in downs]
    ups = [torch.zeros(sample_rank, u.shape[1], device=device, dtype=torch.float16) for u in ups]
    sample = (torch.randn(1, 128, 64, 64, device=device), torch.randn(1, 77, 4096, device=device),
              rope_rows(transformer, 64, 64, 77, device), time_rows(transformer, 500.0, device),
              torch.tensor(False, device=device), torch.tensor(0.0, device=device), downs, ups, time_defaults(transformer))
    with torch.no_grad():
        export(denoiser, sample, full,
               ["latents", "caption", "rope", "time", "skip", "lora_strength", *LORA_DOWN, *LORA_UP, *TIME_PARAMETERS],
               ["velocity"],
               {"latents": {2: h, 3: w}, "caption": {1: rows}, "rope": {0: tokens}, "time": None, "skip": None,
                "lora_strength": None, "lora_down": [{1: rank}] * len(LORA_DOWN), "lora_up": [{0: rank}] * len(LORA_UP),
                "time_parameters": [None] * len(TIME_PARAMETERS)})
    values = defaults_of(transformer)
    np.savez(defaults, **values)
    del transformer, denoiser
    torch.cuda.empty_cache()
    quantize(full, out / "denoiser" / "denoiser.onnx", block_size, values)


def build_decoder(checkpoint, out):
    from diffusers import AutoencoderKLFlux2
    device = "cuda"
    vae = AutoencoderKLFlux2.from_pretrained(checkpoint / "vae", torch_dtype=torch.float16).to(device).eval()
    decoder = Decoder(vae).to(device).eval()
    h = torch.export.Dim("h", min=16, max=128)
    w = torch.export.Dim("w", min=16, max=128)
    with torch.no_grad():
        export(decoder, (torch.randn(1, 128, 64, 64, device=device),), out / "decoder" / "decoder.onnx",
               ["latents"], ["pixels"], {"latents": {2: h, 3: w}}, opset=DECODER_OPSET)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("checkpoint")
    p.add_argument("out")
    p.add_argument("--block-size", type=int, default=32)

    p.add_argument("--only", choices=["denoiser", "decoder"], default=None)
    args = p.parse_args()
    checkpoint, out = Path(args.checkpoint), Path(args.out)
    if args.only in (None, "denoiser"):
        build_denoiser(checkpoint, out, args.block_size)
    if args.only in (None, "decoder"):
        build_decoder(checkpoint, out)


if __name__ == "__main__":
    main()