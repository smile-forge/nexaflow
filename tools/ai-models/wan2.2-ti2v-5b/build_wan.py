"""Build Wan2.2-TI2V-5B, and FastWan2.2-TI2V-5B — its transformer distilled to three steps — as ONNX graphs for
Nexaflow's AI host. The two share every part but the denoiser:

  prompt-encoder/  UMT5-XXL's encoder, 8-bit weights, fp32 activations, + spiece.model   (PromptEncoder)
  denoiser/        Wan2.2-TI2V-5B's transformer, fp16                                    (Denoiser)
  denoiser-fast/   FastWan2.2-TI2V-5B's, the same graph                                  (Denoiser)
  decoder/         Wan2.2's VAE decoder, a latent frame at a time with its cache, fp16   (DecoderFirst, DecoderNext)
  frame-encoder/   Wan2.2's VAE encoder for the picture a clip starts from, fp16        (FrameEncoder)

Each part is held to diffusers' and transformers' own models as it is built — in fp32, where the rebuilt arithmetic must
be theirs, and in the graph's precision — and the build stops at the first that is not.

usage: python -I -X utf8 build_wan.py <Wan2.2-TI2V-5B-Diffusers> <FastWan2.2-TI2V-5B-Diffusers> <out-dir>
       [--parts prompt-encoder denoiser denoiser-fast vae]
"""
import argparse
import math
import shutil
import sys
from pathlib import Path

import torch
import torch.nn.functional as F

HEADS, HEAD_DIM, DIM = 24, 128, 3072
TEXT_ROWS = 512


# ── attention ────────────────────────────────────────────────────────────────────────────────────────────────────
# Exported attention otherwise becomes MatMul -> Softmax -> MatMul, which for a clip's 27,280 tokens holds 18 GB of
# scores per head group. This op runs as PyTorch's attention when traced and checked, and is written into the graph as
# ONNX Runtime's fused com.microsoft.MultiHeadAttention.

@torch.library.custom_op("nexaflow::attention", mutates_args=())
def attention(query: torch.Tensor, key: torch.Tensor, value: torch.Tensor, heads: int) -> torch.Tensor:
    """query [1, Q, heads * d], key and value [1, K, heads * d] -> [1, Q, heads * d]."""
    b, q, e = query.shape
    split = lambda x: x.view(b, -1, heads, e // heads).transpose(1, 2)
    out = F.scaled_dot_product_attention(split(query), split(key), split(value))
    return out.transpose(1, 2).reshape(b, q, e)


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
        doc="Multi-head attention (ONNX Runtime contrib op), as used here.",
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

def rms_norm(x, weight, eps):
    """RMS norm in fp32, as PyTorch computes it for a bf16 model, back in the input's precision."""
    y = x.float()
    y = y * torch.rsqrt(y.pow(2).mean(-1, keepdim=True) + eps) * weight.float()
    return y.to(x.dtype)


class Denoiser(torch.nn.Module):
    """
    One step of Wan2.2-TI2V-5B's transformer:

      latents    [1, 48, F, H, W]   fp32  the clip's latents, H and W even
      timesteps  [1, F]             fp32  each latent frame's timestep out of 1000 — 0 for a frame given, not made
      text       [1, 512, 4096]     fp32  the prompt encoder's rows, zero past the prompt's length
    -> velocity  [1, 48, F, H, W]   fp32  noise − clean, as the model predicts it

    This is diffusers' WanTransformer3DModel.forward with only these changes, each exact or more precise:
      * the timestep is per latent frame, not per token: Wan2.2's TI2V pipeline gives every token of a frame the
        same timestep, so the modulation is made once a frame and broadcast over its tokens, not held per token;
      * the timestep embedding, the modulation, the norms and the rotary positions are fp32, as diffusers keeps
        them; the layers are fp16 where the published pipeline runs bf16;
      * attention is ONNX Runtime's fused MultiHeadAttention.
    """

    def __init__(self, transformer):
        super().__init__()
        self.tr = transformer
        rope = transformer.rope
        self.split = (rope.t_dim, rope.h_dim, rope.w_dim)
        self.register_buffer("freqs_cos", rope.freqs_cos.float(), persistent=False)
        self.register_buffer("freqs_sin", rope.freqs_sin.float(), persistent=False)

    def rotary(self, f, h, w):
        """cos and sin for every token, [N, 128], frame by frame, row by row."""
        outs = []
        for table in (self.freqs_cos, self.freqs_sin):
            t_, h_, w_ = table.split(self.split, dim=1)
            parts = [t_[:f].view(f, 1, 1, -1).expand(f, h, w, -1),
                     h_[:h].view(1, h, 1, -1).expand(f, h, w, -1),
                     w_[:w].view(1, 1, w, -1).expand(f, h, w, -1)]
            outs.append(torch.cat(parts, dim=-1).reshape(f * h * w, HEAD_DIM))
        return outs

    @staticmethod
    def rotate(x, cos, sin):
        """Wan's rotary embedding on interleaved pairs, in fp32."""
        n = x.shape[1]
        y = x.float().view(1, n, HEADS, HEAD_DIM // 2, 2)
        x1, x2 = y[..., 0], y[..., 1]
        c = cos.view(1, n, 1, HEAD_DIM // 2, 2)[..., 0]
        s = sin.view(1, n, 1, HEAD_DIM // 2, 2)[..., 1]
        out = torch.stack([x1 * c - x2 * s, x1 * s + x2 * c], dim=-1)
        return out.view(1, n, DIM).to(x.dtype)

    def forward(self, latents, timesteps, text):
        tr = self.tr
        dtype = tr.patch_embedding.weight.dtype
        _, _, f, H, W = latents.shape
        h, w = H // 2, W // 2
        hw = h * w
        n = f * hw
        cos, sin = self.rotary(f, h, w)

        x = tr.patch_embedding(latents.to(dtype)).flatten(2).transpose(1, 2)            # [1, N, D]

        ce = tr.condition_embedder
        temb = ce.time_embedder(ce.timesteps_proj(timesteps.flatten()).float()).float()  # [F, D]
        tproj = ce.time_proj(ce.act_fn(temb)).view(1, f, 1, 6, DIM).float()              # [1, F, 1, 6, D]
        context = ce.text_embedder(text.to(dtype))                                      # [1, 512, D]

        for block in tr.blocks:
            mod = block.scale_shift_table.float().view(1, 1, 1, 6, DIM) + tproj
            shift, scale, gate, c_shift, c_scale, c_gate = mod.unbind(3)                # each [1, F, 1, D]
            frames = lambda t: t.view(1, f, hw, DIM)

            a = block.attn1
            y = (frames(block.norm1(x.float())) * (1 + scale) + shift).view(1, n, DIM).to(dtype)
            q = self.rotate(rms_norm(a.to_q(y), a.norm_q.weight, a.norm_q.eps), cos, sin)
            k = self.rotate(rms_norm(a.to_k(y), a.norm_k.weight, a.norm_k.eps), cos, sin)
            o = a.to_out[0](attention(q, k, a.to_v(y), HEADS))
            x = (frames(x.float()) + frames(o.float()) * gate).view(1, n, DIM).to(dtype)

            c = block.attn2
            y = block.norm2(x.float()).to(dtype)
            q = rms_norm(c.to_q(y), c.norm_q.weight, c.norm_q.eps)
            k = rms_norm(c.to_k(context), c.norm_k.weight, c.norm_k.eps)
            x = x + c.to_out[0](attention(q, k, c.to_v(context), HEADS))

            y = (frames(block.norm3(x.float())) * (1 + c_scale) + c_shift).view(1, n, DIM).to(dtype)
            x = (frames(x.float()) + frames(block.ffn(y).float()) * c_gate).view(1, n, DIM).to(dtype)

        shift, scale = (tr.scale_shift_table.float().view(1, 1, 2, DIM) + temb.view(1, f, 1, DIM)).unsqueeze(2).unbind(3)
        y = (frames(tr.norm_out(x.float())) * (1 + scale) + shift).view(1, n, DIM).to(dtype)
        y = tr.proj_out(y)                                                             # [1, N, 48 * 4]
        y = y.view(1, f, h, w, 1, 2, 2, 48).permute(0, 7, 1, 4, 2, 5, 3, 6)
        return y.reshape(1, 48, f, H, W).float()


def load_transformer(folder, dtype):
    from diffusers import WanTransformer3DModel
    tr = WanTransformer3DModel.from_pretrained(folder, subfolder="transformer", torch_dtype=dtype).to("cuda").eval()
    if dtype != torch.float32:
        # diffusers keeps these fp32 in a bf16 model; so does the graph.
        ce = tr.condition_embedder
        for module in (ce.time_embedder, ce.time_proj):
            module.float()
    return tr


def check_denoiser(tr, ours, tolerance, label):
    """Ours against diffusers' forward on a small clip at one timestep, the first frame given (timestep 0)."""
    g = torch.Generator("cuda").manual_seed(3)
    f, H, W = 3, 12, 16
    latents = torch.randn(1, 48, f, H, W, generator=g, device="cuda")
    text = torch.randn(1, TEXT_ROWS, 4096, generator=g, device="cuda") * 0.1
    text[:, 40:] = 0
    timesteps = torch.tensor([[0.0, 640.0, 640.0]], device="cuda")
    per_token = timesteps.repeat_interleave((H // 2) * (W // 2), dim=1)
    dtype = tr.patch_embedding.weight.dtype
    with torch.no_grad():
        theirs = tr(latents.to(dtype), timestep=per_token, encoder_hidden_states=text.to(dtype), return_dict=False)[0].float()
        mine = ours(latents, timesteps, text)
    scale = theirs.abs().max().item()
    error = (mine - theirs).abs().max().item()
    print(f"denoiser ({label}) against diffusers: max difference {error:.4g} on values up to {scale:.4g}", flush=True)
    if error > tolerance * scale:
        raise SystemExit(f"the denoiser ({label}) does not match diffusers")


# ── the decoder and the frame encoder ────────────────────────────────────────────────────────────────────────────
# Wan's VAE decodes a clip a latent frame at a time — the first to one picture, each after to four — each causal
# convolution keeping the last two frames it saw for the next. The graphs take and give that cache, so a clip is decoded
# in as many calls as it has latent frames, holding one frame's activations at a time. Two graphs: the first frame's,
# which has no cache yet and is not doubled in time, and every later frame's. Each cache is kept as two frames, zero
# where the first frame left fewer: a causal convolution pads with zeros, so this is exact.

def upsample3d(self, x, feat_cache=None, feat_idx=[0]):
    """WanResample's temporal upsampling with its cache always a tensor of two frames: the first frame is not doubled
    in time and leaves an empty cache, where diffusers leaves the marker "Rep" and pads with zeros next time."""
    b, c, t, h, w = x.shape
    idx = feat_idx[0]
    feat_idx[0] += 1
    cached = feat_cache[idx]
    if cached is None:
        feat_cache[idx] = torch.zeros_like(torch.cat([x, x], dim=2))
    else:
        feat_cache[idx] = torch.cat([cached, x], dim=2)[:, :, -2:]
        x = self.time_conv(x, cached)
        x = x.reshape(b, 2, c, t, h, w)
        x = torch.stack((x[:, 0], x[:, 1]), 3).reshape(b, c, t * 2, h, w)
    t = x.shape[2]
    x = x.permute(0, 2, 1, 3, 4).reshape(b * t, c, h, w)
    x = self.resample(x)
    return x.view(b, t, x.size(1), x.size(2), x.size(3)).permute(0, 2, 1, 3, 4)


def rms_norm_channels(self, x):
    """WanRMS_norm without a full fp32 copy of its input: diffusers casts the activations to fp32 and divides by their
    norm expanded to their full size — three fp32 tensors as large as the input, 880 MB each at 720p, held at once. Here
    the norm over channels is a reduction that accumulates in fp32 (ReduceL2), clamped and inverted in fp32 on the small
    per-pixel tensor, and the input is scaled by it in its own precision."""
    norm = torch.linalg.vector_norm(x, ord=2, dim=1, keepdim=True, dtype=torch.float32)
    # A pixel whose channels are all but zero would take an inverse past fp16's range; it scales those near-zeros to
    # near-zeros either way, so the inverse stops at the largest fp16 value rather than becoming infinite.
    inverse = (self.scale / norm.clamp_min(1e-12)).clamp_max(torch.finfo(x.dtype).max).to(x.dtype)
    return x * inverse * self.gamma + self.bias


def upsample_in_place(self, x):
    """WanUpsample — nearest-exact, twice each way — as each pixel repeated into a 2×2 block, in its input's precision.
    Traced as diffusers writes it, it becomes a gather over int64 indices for every output value, 1.8 GB at 720p, and
    an fp32 copy besides; for a factor of two, nearest-exact takes source pixel i // 2 for output i, which is the repeat."""
    assert tuple(self.scale_factor) == (2.0, 2.0) and self.mode == "nearest-exact"
    n, c, h, w = x.shape
    return x[:, :, :, None, :, None].expand(n, c, h, 2, w, 2).reshape(n, c, 2 * h, 2 * w)


def patch_upsamplers(vae):
    import types
    from diffusers.models.autoencoders.autoencoder_kl_wan import WanResample, WanRMS_norm, WanUpsample
    for module in vae.decoder.modules():
        if isinstance(module, WanResample) and module.mode == "upsample3d":
            module.forward = types.MethodType(upsample3d, module)
    for module in list(vae.decoder.modules()) + list(vae.encoder.modules()):
        if isinstance(module, WanRMS_norm) and module.channel_first:
            module.forward = types.MethodType(rms_norm_channels, module)
        elif isinstance(module, WanUpsample):
            module.forward = types.MethodType(upsample_in_place, module)


def to_rgb(out):
    """The decoder's output, [1, 12, T, h, w] patches in [-1, 1], as T frames of interleaved 8-bit RGB, [T, 2h, 2w, 3]."""
    from diffusers.models.autoencoders.autoencoder_kl_wan import unpatchify
    x = unpatchify(out.float(), patch_size=2).clamp(-1, 1)
    return ((x[0] + 1) * 127.5).round().to(torch.uint8).permute(1, 2, 3, 0)


class DecoderBase(torch.nn.Module):
    def __init__(self, vae):
        super().__init__()
        self.vae = vae
        self.count = vae._cached_conv_counts["decoder"]
        self.register_buffer("mean", torch.tensor(vae.config.latents_mean).view(1, 48, 1, 1, 1), persistent=False)
        self.register_buffer("std", torch.tensor(vae.config.latents_std).view(1, 48, 1, 1, 1), persistent=False)

    def decode(self, latent, cache, first):
        dtype = self.vae.post_quant_conv.weight.dtype
        z = self.vae.post_quant_conv((latent * self.std + self.mean).to(dtype))
        out = self.vae.decoder(z, feat_cache=cache, feat_idx=[0], first_chunk=first)
        return to_rgb(out)


class DecoderFirst(DecoderBase):
    """latent [1, 48, 1, h, w] fp32, the clip's first latent frame as the denoiser made it -> frames [1, 16h, 16w, 3]
    uint8, and the cache for the next."""

    def forward(self, latent):
        cache = [None] * self.count
        frames = self.decode(latent, cache, first=True)
        # diffusers counts every causal convolution, the 1x1 shortcuts too, which keep nothing: the slots used come first.
        used = [c for c in cache if c is not None]
        assert all(c is None for c in cache[len(used):]), "the cache's slots are not used in order"
        cache = [c if c.shape[2] == 2 else torch.cat([torch.zeros_like(c), c], dim=2) for c in used]
        return (frames, *cache)


class DecoderNext(DecoderBase):
    """latent [1, 48, 1, h, w] and the cache the last call gave -> frames [4, 16h, 16w, 3] uint8, and the cache for
    the next."""

    def forward(self, latent, cache):
        cache = list(cache)
        frames = self.decode(latent, cache, first=False)
        return (frames, *cache)


class FrameEncoder(torch.nn.Module):
    """rgb [H, W, 3] uint8, the picture a clip starts from -> [1, 48, 1, H/16, W/16] fp32, its latent frame as the
    denoiser reads it: the encoder's mean, normalised."""

    def __init__(self, vae):
        super().__init__()
        self.vae = vae
        self.register_buffer("mean", torch.tensor(vae.config.latents_mean).view(1, 48, 1, 1, 1), persistent=False)
        self.register_buffer("std", torch.tensor(vae.config.latents_std).view(1, 48, 1, 1, 1), persistent=False)

    def forward(self, rgb):
        from diffusers.models.autoencoders.autoencoder_kl_wan import patchify
        dtype = self.vae.quant_conv.weight.dtype
        x = (rgb.permute(2, 0, 1).float() / 127.5 - 1).view(1, 3, 1, rgb.shape[0], rgb.shape[1])
        x = patchify(x, patch_size=2).to(dtype)
        h = self.vae.quant_conv(self.vae.encoder(x, feat_cache=[None] * self.vae._cached_conv_counts["encoder"], feat_idx=[0]))
        return ((h[:, :48].float() - self.mean) / self.std)


def load_vae(folder, dtype):
    from diffusers import AutoencoderKLWan
    return AutoencoderKLWan.from_pretrained(folder, subfolder="vae", torch_dtype=dtype).to("cuda").eval()


def psnr(a, b):
    mse = ((a.float() - b.float()) ** 2).mean().item()
    return 99.0 if mse == 0 else 10 * math.log10(255.0 ** 2 / mse)


def test_clip(frames=121, height=704, width=1280):
    """A clip with something in it to encode and decode: discs of colour crossing a gradient, a ruled pattern, and a
    rotating bar — edges, flat colour and motion, the same every build."""
    import numpy as np
    y, x = np.mgrid[0:height, 0:width].astype(np.float32)
    video = np.empty((frames, height, width, 3), np.uint8)
    for f in range(frames):
        t = f / (frames - 1)
        frame = np.stack([x / width * 255, y / height * 255, np.full_like(x, 96)], axis=-1)
        frame[((x // 32 + y // 32) % 2 == 0) & (y > height * 0.75)] = (240, 240, 240)
        for i, colour in enumerate([(220, 40, 40), (40, 200, 60), (40, 80, 230)]):
            cx = (0.15 + 0.7 * ((t + i / 3) % 1)) * width
            cy = (0.3 + 0.15 * np.sin(2 * np.pi * (t + i / 3))) * height
            frame[(x - cx) ** 2 + (y - cy) ** 2 < 70 ** 2] = colour
        angle = 2 * np.pi * t
        along = (x - width / 2) * np.cos(angle) + (y - height / 2) * np.sin(angle)
        across = -(x - width / 2) * np.sin(angle) + (y - height / 2) * np.cos(angle)
        frame[(np.abs(along) < 200) & (np.abs(across) < 8)] = (250, 220, 30)
        video[f] = frame.clip(0, 255).astype(np.uint8)
    return video


def check_vae(base, clip, dtype):
    """Our first and later decoder calls, and the frame encoder, against diffusers' fp32 VAE on a clip."""
    import numpy as np
    ref = load_vae(base, torch.float32)
    video = torch.from_numpy(clip).cuda()                                    # [121, H, W, 3] uint8
    x = (video.permute(3, 0, 1, 2).float() / 127.5 - 1).unsqueeze(0)         # [1, 3, 121, H, W]
    mean = torch.tensor(ref.config.latents_mean).view(1, 48, 1, 1, 1).cuda()
    std = torch.tensor(ref.config.latents_std).view(1, 48, 1, 1, 1).cuda()
    with torch.no_grad():
        latents = (ref.encode(x).latent_dist.mode() - mean) / std
        decoded = ref.decode(latents * std + mean, return_dict=False)[0]     # [1, 3, T, H, W] in [-1, 1]
        theirs = ((decoded[0].clamp(-1, 1) + 1) * 127.5).round().to(torch.uint8).permute(1, 2, 3, 0)
        first_ref = (ref.encode(x[:, :, :1]).latent_dist.mode() - mean) / std

        vae = load_vae(base, dtype)
        patch_upsamplers(vae)
        first, nxt, encoder = (m.to("cuda").eval() for m in (DecoderFirst(vae), DecoderNext(vae), FrameEncoder(vae)))
        outs = first(latents[:, :, :1])
        frames, cache = [outs[0]], list(outs[1:])
        for i in range(1, latents.shape[2]):
            outs = nxt(latents[:, :, i:i + 1], cache)
            frames.append(outs[0])
            cache = list(outs[1:])
        ours = torch.cat(frames)
        mine_first = encoder(video[0])
    print(f"decoder ({dtype}) against diffusers fp32: {ours.shape[0]} frames, {psnr(ours, theirs):.1f} dB PSNR, "
          f"against the clip itself {psnr(theirs, video):.1f} dB", flush=True)
    error = (mine_first - first_ref).abs().max().item()
    print(f"frame encoder ({dtype}) against diffusers fp32: max difference {error:.4g} on values up to "
          f"{first_ref.abs().max().item():.4g}", flush=True)
    # fp32 is diffusers' arithmetic a frame at a time, its norms summed in another order; fp16 is held to well past what
    # 8-bit pixels show.
    if ours.shape[0] != theirs.shape[0] or psnr(ours, theirs) < (70 if dtype == torch.float32 else 45) \
            or error > (1e-3 if dtype == torch.float32 else 2e-2) * first_ref.abs().max().item():
        raise SystemExit(f"the VAE ({dtype}) does not match diffusers")


# ── the prompt encoder ───────────────────────────────────────────────────────────────────────────────────────────
# UMT5-XXL's feed-forward outputs reach 1.6x10^5 and its residual stream 3.8x10^5, past fp16's range, so the encoder
# runs fp32 activations — it reads one prompt a clip, so their cost is nothing — on 8-bit weights, which round its bf16
# weights about as far as bf16 itself does. The embedding table is fp16, which holds bf16's values exactly.

class PromptEncoder(torch.nn.Module):
    """input_ids [1, T] int64, the prompt's tokens and its closing </s>, no padding -> [1, T, 4096] fp32. Wan pads the
    prompt to 512 tokens and masks the padding; T5's attention bias depends on distances alone, so the unpadded prompt
    reads the same."""

    def __init__(self, encoder):
        super().__init__()
        self.encoder = encoder

    def forward(self, input_ids):
        return self.encoder(input_ids=input_ids).last_hidden_state.float()


def export_prompt_encoder(base, path):
    from transformers import UMT5EncoderModel
    encoder = UMT5EncoderModel.from_pretrained(base, subfolder="text_encoder", torch_dtype=torch.float32).to("cuda").eval()
    model = PromptEncoder(encoder).eval()
    path.parent.mkdir(parents=True, exist_ok=True)
    clear(path)
    tokens = torch.export.Dim("tokens", min=2, max=TEXT_ROWS)
    with torch.no_grad():
        program = torch.onnx.export(model, (torch.ones(1, 17, dtype=torch.int64, device="cuda"),), dynamo=True,
                                    input_names=["input_ids"], output_names=["text"],
                                    dynamic_shapes={"input_ids": {1: tokens}}, optimize=True)
    program.save(str(path), external_data=True)
    print(f"exported {path}", flush=True)
    return model


def quantize_prompt_encoder(src, dst, block_size=32):
    """Every layer's matrices to 8-bit MatMulNBits on fp32 activations; the embedding table to fp16, cast as it is read."""
    import numpy as np
    import onnx
    from onnx import helper, numpy_helper
    from onnxruntime.quantization.matmul_nbits_quantizer import MatMulNBitsQuantizer, DefaultWeightOnlyQuantConfig

    model = onnx.load(str(src), load_external_data=True)
    config = DefaultWeightOnlyQuantConfig(block_size=block_size, is_symmetric=True, accuracy_level=0, bits=8,
                                          op_types_to_quantize=("MatMul",))
    quantizer = MatMulNBitsQuantizer(model, algo_config=config)
    quantizer.process()
    graph = quantizer.model.model.graph

    inits = {i.name: i for i in graph.initializer}
    for node in graph.node:
        if node.op_type == "Gather" and node.input[0] in inits and inits[node.input[0]].data_type == onnx.TensorProto.FLOAT:
            table = inits[node.input[0]]
            half = numpy_helper.from_array(numpy_helper.to_array(table).astype(np.float16), table.name + "_fp16")
            graph.initializer.remove(table)
            graph.initializer.append(half)
            out = node.output[0]
            node.input[0] = half.name
            node.output[0] = out + "_fp16"
            graph.node.append(helper.make_node("Cast", [out + "_fp16"], [out], to=onnx.TensorProto.FLOAT))
    used = {name for node in graph.node for name in node.input}
    for init in [i for i in graph.initializer if i.name not in used]:
        graph.initializer.remove(init)
    # The cast's node was appended last; the graph is sorted again so it comes before its users.
    from onnxruntime.quantization.onnx_model import ONNXModel
    wrapped = ONNXModel(quantizer.model.model)
    wrapped.topological_sort()
    dst.parent.mkdir(parents=True, exist_ok=True)
    clear(dst)
    onnx.save_model(wrapped.model, str(dst), save_as_external_data=True, all_tensors_to_one_file=True, location=dst.name + ".data")
    print(f"quantized {dst}", flush=True)


CHECK_PROMPTS = [
    "A red fox trots through fresh snow in a birch forest at dawn, its breath steaming; the camera tracks alongside at ground level.",
    "Close-up of a barista pouring steamed milk into a cup of espresso, forming a heart in the latte art, warm cafe light.",
    "A busy city street at night in the rain, neon signs reflecting in puddles, people with umbrellas crossing; slow dolly forward.",
    "An old wooden sailing ship on a stormy sea, waves crashing over the bow, lightning in the distance, cinematic wide shot.",
]


def check_prompt_encoder(base, path, with_bf16=False):
    """The graph against UMT5 in fp32 on Wan's own tokenisation of four prompts, padded and masked as Wan reads them —
    and, for the record, the bf16 encoder the published pipelines run, against the same."""
    import numpy as np
    from diffusers.pipelines.wan.pipeline_wan import prompt_clean
    from transformers import AutoTokenizer, UMT5EncoderModel
    tokenizer = AutoTokenizer.from_pretrained(base, subfolder="tokenizer")
    encoder = UMT5EncoderModel.from_pretrained(base, subfolder="text_encoder", torch_dtype=torch.float32).to("cuda").eval()
    s = session(path)
    bf16 = (UMT5EncoderModel.from_pretrained(base, subfolder="text_encoder", torch_dtype=torch.bfloat16).to("cuda").eval()
            if with_bf16 else None)
    worst = 0.0
    for prompt in CHECK_PROMPTS:
        padded = tokenizer(prompt_clean(prompt), padding="max_length", max_length=TEXT_ROWS, truncation=True, add_special_tokens=True,
                           return_attention_mask=True, return_tensors="pt")
        length = int(padded.attention_mask.sum())
        with torch.no_grad():
            theirs = encoder(padded.input_ids.cuda(), padded.attention_mask.cuda()).last_hidden_state[0, :length].float().cpu().numpy()
        ours = s.run(None, {"input_ids": padded.input_ids[:, :length].numpy()})[0][0]
        rel = np.abs(ours - theirs).max() / np.abs(theirs).max()
        cos = (ours * theirs).sum(-1) / (np.linalg.norm(ours, axis=-1) * np.linalg.norm(theirs, axis=-1))
        worst = max(worst, rel)
        print(f"prompt encoder: {length} tokens, max difference {rel:.3%} of the largest value, least row cosine {cos.min():.5f}", flush=True)
        if bf16 is not None:
            with torch.no_grad():
                b = bf16(padded.input_ids.cuda(), padded.attention_mask.cuda()).last_hidden_state[0, :length].float().cpu().numpy()
            rel = np.abs(b - theirs).max() / np.abs(theirs).max()
            cos = (b * theirs).sum(-1) / (np.linalg.norm(b, axis=-1) * np.linalg.norm(theirs, axis=-1))
            print(f"  the pipeline's own bf16 encoder: max difference {rel:.3%}, least row cosine {cos.min():.5f}", flush=True)
    return worst


def export_decoder(vae, folder):
    """decoder-first.onnx and decoder.onnx, one weights file between them: the second graph's weights are the first's."""
    import onnx
    from onnx.external_data_helper import set_external_data
    folder.mkdir(parents=True, exist_ok=True)
    first_path, next_path = folder / "decoder-first.onnx", folder / "decoder.onnx"
    for path in (first_path, next_path):
        clear(path)
    rows = torch.export.Dim("rows", min=2, max=128)
    cols = torch.export.Dim("cols", min=2, max=128)
    latent = torch.randn(1, 48, 1, 12, 16, device="cuda")
    first, nxt = DecoderFirst(vae).to("cuda").eval(), DecoderNext(vae).to("cuda").eval()
    with torch.no_grad():
        outs = first(latent)
        caches = list(outs[1:])
        cache_names = [f"cache_{i}" for i in range(len(caches))]
        scales = [c.shape[3] // 12 for c in caches]
        program = torch.onnx.export(first, (latent,), dynamo=True, input_names=["latent"],
                                    output_names=["frames", *[f"next_{n}" for n in cache_names]],
                                    dynamic_shapes={"latent": {3: rows, 4: cols}}, optimize=True)
        program.save(str(first_path), external_data=True)
        program = torch.onnx.export(nxt, (latent, caches), dynamo=True, input_names=["latent", *cache_names],
                                    output_names=["frames", *[f"next_{n}" for n in cache_names]],
                                    dynamic_shapes={"latent": {3: rows, 4: cols},
                                                    "cache": [{3: k * rows, 4: k * cols} for k in scales]},
                                    optimize=True)
        program.save(str(next_path), external_data=True)

    # The second graph's weights, where they are the first's, read from the first's file.
    shared = {}
    for init in onnx.load(str(first_path), load_external_data=False).graph.initializer:
        info = {e.key: e.value for e in init.external_data}
        if "location" in info:
            shared[init.name] = info
    model = onnx.load(str(next_path), load_external_data=True)
    first_data = onnx.load(str(first_path), load_external_data=True)
    first_values = {i.name: i.raw_data for i in first_data.graph.initializer}
    moved = 0
    for init in model.graph.initializer:
        if init.name in shared and first_values.get(init.name) == init.raw_data:
            info = shared[init.name]
            set_external_data(init, location=info["location"], offset=int(info.get("offset", 0)), length=int(info["length"]))
            init.data_location = onnx.TensorProto.EXTERNAL
            init.ClearField("raw_data")
            moved += 1
    left = sum(len(i.raw_data) for i in model.graph.initializer if i.data_location != onnx.TensorProto.EXTERNAL)
    clear(next_path)
    onnx.save_model(model, str(next_path))

    # The cache's shape, so a caller can hold it in buffers of its own, allocated once: each slot's channels, and its
    # side as a multiple of the latents' — a cache is [1, channels, 2, scale·rows, scale·cols], fp16.
    import json
    (folder / "caches.json").write_text(json.dumps(
        {"caches": [{"name": n, "channels": int(c.shape[1]), "scale": k} for n, c, k in zip(cache_names, caches, scales)]}, indent=1))
    print(f"exported {first_path.name} and {next_path.name}: {moved} weights shared, {left} bytes of the second's own", flush=True)
    return len(caches)


def export_frame_encoder(vae, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    clear(path)
    rows = torch.export.Dim("rows", min=2, max=128)
    cols = torch.export.Dim("cols", min=2, max=128)
    encoder = FrameEncoder(vae).to("cuda").eval()
    with torch.no_grad():
        program = torch.onnx.export(encoder, (torch.zeros(192, 256, 3, dtype=torch.uint8, device="cuda"),), dynamo=True,
                                    input_names=["rgb"], output_names=["latent"],
                                    dynamic_shapes={"rgb": {0: 16 * rows, 1: 16 * cols}}, optimize=True)
    program.save(str(path), external_data=True)
    print(f"exported {path.name}", flush=True)


def check_decoder_onnx(base, folder, clip):
    """The two decoder graphs on a clip's latents, the cache kept on the GPU, against diffusers' fp32 VAE, and how long
    a clip takes."""
    import time
    import numpy as np
    ref = load_vae(base, torch.float32)
    video = torch.from_numpy(clip).cuda()
    x = (video.permute(3, 0, 1, 2).float() / 127.5 - 1).unsqueeze(0)
    mean = torch.tensor(ref.config.latents_mean).view(1, 48, 1, 1, 1).cuda()
    std = torch.tensor(ref.config.latents_std).view(1, 48, 1, 1, 1).cuda()
    with torch.no_grad():
        latents = (ref.encode(x).latent_dist.mode() - mean) / std
        decoded = ref.decode(latents * std + mean, return_dict=False)[0]
        theirs = ((decoded[0].clamp(-1, 1) + 1) * 127.5).round().to(torch.uint8).permute(1, 2, 3, 0).cpu()
        first_ref = ((ref.encode(x[:, :, :1]).latent_dist.mode() - mean) / std).cpu().numpy()
    del ref
    torch.cuda.empty_cache()
    latents = latents.cpu().numpy()

    first, nxt, enc = session(folder / "decoder-first.onnx"), session(folder / "decoder.onnx"), session(folder.parent / "frame-encoder" / "frame-encoder.onnx")
    names = [i.name for i in nxt.get_inputs()][1:]
    for run in range(2):
        t0 = time.time()
        outs = first.run(None, {"latent": latents[:, :, :1]})
        frames, cache = [outs[0]], outs[1:]
        for i in range(1, latents.shape[2]):
            outs = nxt.run(None, {"latent": latents[:, :, i:i + 1], **dict(zip(names, cache))})
            frames.append(outs[0])
            cache = outs[1:]
        took = time.time() - t0
    ours = torch.from_numpy(np.concatenate(frames))
    print(f"onnx decoder: {ours.shape[0]} frames in {took:.1f} s, {psnr(ours, theirs):.1f} dB PSNR against diffusers fp32", flush=True)
    mine = enc.run(None, {"rgb": video[0].cpu().numpy()})[0]
    error = np.abs(mine - first_ref).max()
    print(f"onnx frame encoder: max difference {error:.4g} on values up to {np.abs(first_ref).max():.4g}", flush=True)
    if ours.shape[0] != theirs.shape[0] or psnr(ours, theirs) < 45 or error > 2e-2 * np.abs(first_ref).max():
        raise SystemExit("the VAE's graphs do not match diffusers")


def clear(path):
    """onnx appends external data to a file already there, so a graph and its data are removed before writing."""
    for name in (path.name, path.name + ".data"):
        if (path.parent / name).exists():
            (path.parent / name).unlink()


def export_denoiser(denoiser, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    clear(path)
    frames = torch.export.Dim("frames", min=1, max=64)
    rows = torch.export.Dim("rows", min=2, max=64)
    cols = torch.export.Dim("cols", min=2, max=64)
    sample = (torch.randn(1, 48, 3, 12, 16, device="cuda"), torch.tensor([[0.0, 500.0, 500.0]], device="cuda"),
              torch.zeros(1, TEXT_ROWS, 4096, device="cuda"))
    with torch.no_grad():
        program = torch.onnx.export(
            denoiser, sample, dynamo=True,
            input_names=["latents", "timesteps", "text"], output_names=["velocity"],
            dynamic_shapes={"latents": {2: frames, 3: 2 * rows, 4: 2 * cols}, "timesteps": {1: frames}, "text": None},
            optimize=True, custom_translation_table={torch.ops.nexaflow.attention.default: attention_onnx})
    program.save(str(path), external_data=True)
    print(f"exported {path}", flush=True)


def session(path):
    import onnxruntime as ort
    options = ort.SessionOptions()
    options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    options.log_severity_level = 3
    return ort.InferenceSession(str(path), options, providers=[("CUDAExecutionProvider", {"device_id": 0}), "CPUExecutionProvider"])


def main():
    p = argparse.ArgumentParser()
    p.add_argument("base")
    p.add_argument("fast")
    p.add_argument("out")
    p.add_argument("--parts", nargs="*", default=["prompt-encoder", "denoiser", "denoiser-fast", "vae"])
    args = p.parse_args()
    base, fast, out = Path(args.base), Path(args.fast), Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    if "prompt-encoder" in args.parts:
        work = out / "prompt-encoder-fp32" / "prompt-encoder.onnx"
        export_prompt_encoder(base, work)
        torch.cuda.empty_cache()
        quantize_prompt_encoder(work, out / "prompt-encoder" / "prompt-encoder.onnx")
        shutil.copyfile(base / "tokenizer" / "spiece.model", out / "prompt-encoder" / "spiece.model")
        worst = check_prompt_encoder(base, out / "prompt-encoder" / "prompt-encoder.onnx", with_bf16=True)
        if worst > 0.1:
            raise SystemExit("the prompt encoder does not match UMT5")
        torch.cuda.empty_cache()

    if "denoiser" in args.parts:
        tr = load_transformer(base, torch.float32)
        check_denoiser(tr, Denoiser(tr).eval(), 1e-4, "fp32")
        del tr
        torch.cuda.empty_cache()
        tr = load_transformer(base, torch.float16)
        ref = load_transformer(base, torch.float32)
        ours = Denoiser(tr).eval()
        check_denoiser(ref, ours, 2e-2, "fp16 against fp32")
        del ref
        torch.cuda.empty_cache()
        export_denoiser(ours, out / "denoiser" / "denoiser.onnx")
        del tr, ours
        torch.cuda.empty_cache()

    if "denoiser-fast" in args.parts:
        # FastWan is Wan2.2-TI2V-5B's transformer distilled: the same graph, its weights.
        tr = load_transformer(fast, torch.float16)
        export_denoiser(Denoiser(tr).eval(), out / "denoiser-fast" / "denoiser.onnx")
        del tr
        torch.cuda.empty_cache()

    if "vae" in args.parts:
        clip = test_clip()
        for dtype in (torch.float32, torch.float16):
            check_vae(base, clip, dtype)
            torch.cuda.empty_cache()
        vae = load_vae(base, torch.float16)
        patch_upsamplers(vae)
        export_decoder(vae, out / "decoder")
        export_frame_encoder(vae, out / "frame-encoder" / "frame-encoder.onnx")
        del vae
        torch.cuda.empty_cache()
        check_decoder_onnx(base, out / "decoder", clip)


if __name__ == "__main__":
    main()
