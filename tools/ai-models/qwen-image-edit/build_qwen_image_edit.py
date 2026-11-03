"""Build Qwen-Image-Edit-2511 as five ONNX graphs for Nexaflow's AI host: the picture reader (Qwen2.5-VL's vision tower),
the prompt encoder (Qwen2.5-VL-7B's language model), the denoiser (Qwen-Image's 60-layer MMDiT), and Qwen-Image's VAE
as the picture encoder and the decoder.

An edit reads each picture it works from twice, as diffusers' QwenImageEditPlusPipeline does: small, by the vision tower,
into the prompt the language model reads; and at its own size, by the VAE, into latents the denoiser reads after the
picture's own at every step — modulated as at timestep zero, as 2511's zero_cond_t has it.

The graphs are traced from the published PyTorch model with only these changes, each exact or the model's own:
  * the denoiser's two residual streams are fp32 and every branch fp16 — diffusers clips the streams at fp16's limit, and
    they reach 10^9, so a step is held to the precision Qwen runs the model at, bf16, not to fp32's —
    a branch that would leave fp16's range scaled down by a power of two where its values are made and back up as it
    rejoins its stream; its embedders, timestep embedding, final norm and projection are fp32, the blocks'
    modulations fp16 with their branches;
  * its rotary angles come from tables of every position's, as diffusers computes them, where ONNX would compute them
    in fp32 from the positions;
  * the prompt encoder is the language model with 8-bit weights and fp32 arithmetic; the picture reader's tokens stand
    where the prompt's image placeholders are;
  * the vision tower is fp32, as small as it is, and reads one picture a call, its windows and their order — which the model works out from the
    picture's grid — given by the host;
  * attention is ONNX Runtime's fused MultiHeadAttention, which never holds every token-by-token score;
  * the picture encoder and the decoder carry the VAE's latent normalisation and the denoiser's 2x2 packing, so the host
    deals only in the denoiser's 64-channel tokens; the picture encoder takes 8-bit RGB as the host has it, the decoder
    gives it back.

usage: python -I -X utf8 build_qwen_image_edit.py <Qwen-Image-Edit-2511 checkpoint> <out-dir> [--only reader|encoder|denoiser|vae]

The calibration peaks are kept in <out-dir>/denoiser/branch-peaks.json and reused when present; the factors they give
are written beside them, branch-scales.json.
"""
import argparse
import json
import math
import shutil
import sys
from pathlib import Path

import torch
import torch.nn.functional as F

HEADROOM = 4096.0  # the largest value a scaled branch may reach in calibration: 16x under fp16's limit
IMAGE_TOKEN, VISION_START = 151655, 151652
TEMPLATE_TOKENS = 64        # the system prompt's tokens, which the pipeline drops from the prompt's hidden states
ROPE_RANGE = 4096           # positions on each rotary axis run from -ROPE_RANGE to ROPE_RANGE - 1
CONDITION_AREA = 384 * 384  # the area a picture is read within by the vision tower
VAE_AREA = 1024 * 1024      # and by the VAE
STEPS, TRUE_CFG = 40, 4.0

# Edits calibrated with, of the pictures in --pictures by name: an instruction and the pictures it works from. Nexaflow
# calibrates with HiDream-O1's own calibration drawings (tools/ai-models/hidream-o1): a fisherman at a harbour, a café
# chalkboard, a street sign, a conference poster, a watercolour fox, a home office, a night street, a mug.
CALIBRATION_EDITS = [
    ("Make it night, the harbour lit by street lamps and boats' lights.", ["00"]),
    ("Change the menu to read 'Plat du jour : Boeuf bourguignon 15 €'.", ["02"]),
    ("Remove the tram and make it a sunny day with snow on the ground.", ["03"]),
    ("Turn it into a pencil sketch.", ["05"]),
    ("Change the title to 'FORUM 2027' and make the shapes orange.", ["04"]),
    ("Put the mug from the second picture on the desk in the first.", ["06", "09"]),
    ("The fisherman from the first picture walking down the street of the second.", ["00", "07"]),
]


# ── attention ────────────────────────────────────────────────────────────────────────────────────────────────────
# Traced and checked as PyTorch's attention, written into the graph as ONNX Runtime's fused MultiHeadAttention.

@torch.library.custom_op("nexaflow::attention", mutates_args=())
def attention(query: torch.Tensor, key: torch.Tensor, value: torch.Tensor, heads: int) -> torch.Tensor:
    """query, key and value [1, S, heads * d] -> [1, S, heads * d]."""
    b, s, e = query.shape
    split = lambda x: x.view(b, -1, heads, e // heads).transpose(1, 2)
    out = F.scaled_dot_product_attention(split(query), split(key), split(value))
    return out.transpose(1, 2).reshape(b, s, e)


@attention.register_fake
def _(query, key, value, heads):
    return torch.empty_like(query)


@torch.library.custom_op("nexaflow::biased_attention", mutates_args=())
def biased_attention(query: torch.Tensor, key: torch.Tensor, value: torch.Tensor, bias: torch.Tensor, heads: int) -> torch.Tensor:
    """query [1, Q, heads * d], key and value [1, K, heads * d], bias [1, 1, Q, K] -> [1, Q, heads * d]."""
    b, q, e = query.shape
    split = lambda x: x.view(b, -1, heads, e // heads).transpose(1, 2)
    out = F.scaled_dot_product_attention(split(query), split(key), split(value), attn_mask=bias)
    return out.transpose(1, 2).reshape(b, q, e)


@biased_attention.register_fake
def _(query, key, value, bias, heads):
    return torch.empty_like(query)


def declare_multi_head_attention():
    """ONNX Runtime's contrib ops are not registered with onnx, which the exporter looks every op up in: declare the
    part of com.microsoft.MultiHeadAttention these graphs use — query, key, value and an additive attention bias."""
    from onnx import defs
    try:
        defs.get_schema("MultiHeadAttention", 1, "com.microsoft")
        return
    except defs.SchemaError:
        pass
    p = defs.OpSchema.FormalParameter
    optional = defs.OpSchema.FormalParameterOption.Optional
    defs.register_schema(defs.OpSchema(
        "MultiHeadAttention", "com.microsoft", 1,
        doc="Multi-head attention (ONNX Runtime contrib op), as used here.",
        inputs=[p("query", "T"), p("key", "T"), p("value", "T"), p("bias", "T", param_option=optional),
                p("key_padding_mask", "M", param_option=optional), p("attention_bias", "T", param_option=optional)],
        outputs=[p("output", "T")],
        type_constraints=[("T", ["tensor(float)", "tensor(float16)"], "query, key, value, bias and output"),
                          ("M", ["tensor(int32)"], "key padding mask")],
        attributes=[defs.OpSchema.Attribute("num_heads", defs.OpSchema.AttrType.INT, "attention heads")],
    ))


def attention_onnx(query, key, value, heads: int):
    import onnxscript
    declare_multi_head_attention()
    msft = onnxscript.values.Opset("com.microsoft", 1)
    return msft.MultiHeadAttention(query, key, value, num_heads=heads)


def biased_attention_onnx(query, key, value, bias, heads: int):
    import onnxscript
    declare_multi_head_attention()
    msft = onnxscript.values.Opset("com.microsoft", 1)
    return msft.MultiHeadAttention(query, key, value, None, None, bias, num_heads=heads)


TRANSLATIONS = {torch.ops.nexaflow.attention.default: attention_onnx,
                torch.ops.nexaflow.biased_attention.default: biased_attention_onnx}


# ── the pipeline ─────────────────────────────────────────────────────────────────────────────────────────────────

def load_pipeline(checkpoint, dtype, transformer=True):
    """The pipeline on the GPU; without its transformer, for what reads the prompt and the pictures."""
    from diffusers import QwenImageEditPlusPipeline
    extra = {} if transformer else {"transformer": None}
    return QwenImageEditPlusPipeline.from_pretrained(checkpoint, torch_dtype=dtype, **extra).to("cuda")


def edit(pipe, instruction, pictures, seed, steps=STEPS):
    """An edit as Qwen publishes it: true guidance at 4 from the empty prompt, forty steps."""
    return pipe(image=pictures, prompt=instruction, negative_prompt=" ", true_cfg_scale=TRUE_CFG, guidance_scale=1.0,
                num_inference_steps=steps, generator=torch.Generator("cuda").manual_seed(seed)).images[0]


# ── the picture reader ───────────────────────────────────────────────────────────────────────────────────────────

def rms(x, weight, eps):
    """Qwen2's RMSNorm, in fp32, cast back to the input's type."""
    f = x.float()
    return (weight.float() * (f * torch.rsqrt(f.pow(2).mean(-1, keepdim=True) + eps))).to(x.dtype)


class PictureReader(torch.nn.Module):
    """
    Qwen2.5-VL's vision tower reading one picture:

      patches          [S, 1176]  fp32   its 14x14 patches as Qwen2-VL's processor lays them out: in 2x2 groups row by
                                         row, each its channel, two identical frames, its row and column, normalised
      patch_positions  [S, 2]     int64  each patch's row and column, for its rotary position
      window_order     [S/4]      int64  the groups in the order the windows hold them, as get_vision_window_index gives it
      windows          [S]        int64  the window each patch falls in, in that order
    -> vision_embeds   [S/4, 3584] fp32  one token per group, in the processor's order, for the prompt

    Attention in the layers that look through windows is over each window alone, as the model's sequence lengths make it.
    """

    def __init__(self, visual):
        super().__init__()
        self.visual = visual

    def forward(self, patches, patch_positions, window_order, windows):
        v = self.visual
        dtype = v.patch_embed.proj.weight.dtype
        x = v.patch_embed(patches)
        S = x.shape[0]
        unit = v.spatial_merge_unit

        def ordered(t):
            return t.reshape(S // unit, unit, -1)[window_order].reshape(S, -1)

        x = ordered(x)
        angles = patch_positions.float().unsqueeze(-1) * v.rotary_pos_emb.inv_freq.float()   # [S, 2, d/4]
        angles = torch.cat([angles[:, 0], angles[:, 1]], dim=-1)
        angles = ordered(torch.cat([angles, angles], dim=-1))
        cos, sin = angles.cos(), angles.sin()
        within = torch.where(windows.unsqueeze(0) == windows.unsqueeze(1), torch.zeros((), dtype=dtype, device=x.device),
                             torch.full((), torch.finfo(dtype).min, dtype=dtype, device=x.device)).view(1, 1, S, S)

        from transformers.models.qwen2_5_vl.modeling_qwen2_5_vl import apply_rotary_pos_emb_vision
        for layer, block in enumerate(v.blocks):
            a = block.attn
            q, k, w = a.qkv(block.norm1(x)).reshape(S, 3, a.num_heads, -1).permute(1, 0, 2, 3).unbind(0)
            q, k = apply_rotary_pos_emb_vision(q, k, cos, sin)
            flat = lambda t: t.reshape(1, S, -1)
            if layer in v.fullatt_block_indexes:
                out = attention(flat(q), flat(k), flat(w), a.num_heads)
            else:
                out = biased_attention(flat(q), flat(k), flat(w), within, a.num_heads)
            x = x + a.proj(out[0])
            x = x + block.mlp(block.norm2(x))
        merged = v.merger(x).float()
        # Back from the windows' order to the processor's: the inverse of the order, by scattering each group's place.
        groups = torch.arange(window_order.shape[0], device=window_order.device)
        return merged[torch.zeros_like(window_order).scatter(0, window_order, groups)]


def reader_inputs(processor, visual, picture):
    """What the picture reader takes for <paramref name="picture"/>, as the processor and the vision tower make it: the
    patches, each one's row and column as rot_pos_emb numbers them, and the windows get_window_index cuts them into."""
    proc = processor.image_processor(images=[picture], return_tensors="pt")
    grid = proc.image_grid_thw
    _, h, w = (int(v) for v in grid[0])
    m = visual.spatial_merge_size
    rows = torch.arange(h).unsqueeze(1).expand(-1, w).reshape(h // m, m, w // m, m).permute(0, 2, 1, 3).flatten()
    cols = torch.arange(w).unsqueeze(0).expand(h, -1).reshape(h // m, m, w // m, m).permute(0, 2, 1, 3).flatten()
    order, cu = visual.get_window_index(grid)
    cu = torch.unique_consecutive(torch.tensor(cu))
    windows = torch.repeat_interleave(torch.arange(len(cu) - 1), cu[1:] - cu[:-1])
    return proc.pixel_values, torch.stack([rows, cols], dim=-1).long(), order.long(), windows.long(), grid


# ── the prompt encoder ───────────────────────────────────────────────────────────────────────────────────────────

class PromptEncoder(torch.nn.Module):
    """
      input_ids      [1, T]       int64  the prompt as the pipeline writes it, a picture's placeholders IMAGE_TOKEN
      vision_embeds  [V, 3584]    fp32   the picture reader's tokens for those placeholders, in order
      positions      [3, T]       int64  every token's rotary position, as get_rope_index numbers them
    -> hidden        [T, 3584]    fp32   the language model's last hidden states, which the denoiser reads from the
                                         65th on
    """

    def __init__(self, text_encoder):
        super().__init__()
        self.lm = text_encoder.model.language_model
        embed = self.lm.embed_tokens.half()
        self.embed = lambda ids: embed(ids).float()

    def forward(self, input_ids, vision_embeds, positions):
        T = input_ids.shape[1]
        visual = input_ids[0] == IMAGE_TOKEN
        rows = torch.where(visual, torch.cumsum(visual.long(), 0), torch.zeros_like(input_ids[0]))
        vision = torch.cat([vision_embeds.new_zeros(1, vision_embeds.shape[1]), vision_embeds], dim=0).float()[rows]
        embeds = torch.where(visual.view(1, T, 1), vision.unsqueeze(0), self.embed(input_ids))
        out = self.lm(inputs_embeds=embeds, position_ids=positions.unsqueeze(1), attention_mask=torch.ones_like(input_ids),
                      use_cache=False)
        return out.last_hidden_state[0]


def prompt_inputs(pipe, instruction, conditions):
    """The prompt encoder's inputs for an edit, as _get_qwen_prompt_embeds builds them: the template with a 'Picture n:'
    for each picture, the processor's tokens, and the positions the model numbers them with."""
    text = pipe.prompt_template_encode.format("".join(f"Picture {i + 1}: <|vision_start|><|image_pad|><|vision_end|>"
                                                      for i in range(len(conditions))) + instruction)
    inputs = pipe.processor(text=[text], images=conditions, padding=True, return_tensors="pt")
    model = pipe.text_encoder.model
    positions, _ = model.get_rope_index(inputs.input_ids, image_grid_thw=inputs.image_grid_thw, attention_mask=inputs.attention_mask)
    return inputs, positions[:, 0]


# ── the denoiser ─────────────────────────────────────────────────────────────────────────────────────────────────

class RopeTables(torch.nn.Module):
    """QwenEmbedRope's angles for every position from -ROPE_RANGE to ROPE_RANGE - 1 on each axis, as diffusers computes
    them: positions [S, 3] int64 -> (cos, sin) [S, 128] each, every frequency repeated for its pair."""

    def __init__(self, pos_embed):
        super().__init__()
        table = torch.cat([pos_embed.neg_freqs, pos_embed.pos_freqs], dim=0)   # [2 * ROPE_RANGE, 64] complex, index p + ROPE_RANGE
        self.splits = [d // 2 for d in pos_embed.axes_dim]
        for i, part in enumerate(table.split(self.splits, dim=1)):
            self.register_buffer(f"cos_{i}", part.real.float().repeat_interleave(2, dim=1), persistent=False)
            self.register_buffer(f"sin_{i}", part.imag.float().repeat_interleave(2, dim=1), persistent=False)

    def forward(self, positions):
        at = positions + ROPE_RANGE
        cos = torch.cat([getattr(self, f"cos_{i}")[at[:, i]] for i in range(3)], dim=1)
        sin = torch.cat([getattr(self, f"sin_{i}")[at[:, i]] for i in range(3)], dim=1)
        return cos, sin


def rotate(x, cos, sin):
    """apply_rotary_emb_qwen on interleaved pairs, as its complex product, for x [1, S, heads, 128], in fp32."""
    real, imag = x.float().reshape(*x.shape[:-1], -1, 2).unbind(-1)
    rotated = torch.stack([-imag, real], dim=-1).flatten(3)
    return (x.float() * cos[None, :, None, :] + rotated * sin[None, :, None, :]).to(x.dtype)


def heads(x, norm, head_dim):
    """x [1, S, heads * head_dim] cut into heads, each RMS-normed in fp32."""
    x = x.unflatten(-1, (-1, head_dim))
    return rms(x, norm.weight, norm.eps)


def layer_norm(x, eps=1e-6):
    """The blocks' LayerNorm without affine weights, in fp32."""
    return F.layer_norm(x.float(), (x.shape[-1],), eps=eps)


class Denoiser(torch.nn.Module):
    """
    One step, on sequences the host lays out:

      image_tokens     [1, N, 64]   fp32  the latents' tokens, 2x2 patches packed row by row, then each picture worked
                                          from's
      image_positions  [N, 3]       int64 (picture, row, column) as QwenEmbedRope numbers them: the picture drawn 0, the
                                          i-th worked from i; rows and columns centred on 0
      drawn            [1]          int64 how many of the tokens are the picture drawn's; the rest are modulated as at
                                          timestep zero
      prompt           [1, T, 3584] fp32  the prompt encoder's hidden states from the 65th on
      prompt_positions [T, 3]       int64 (m + i, m + i, m + i), m the largest half side of any picture
      timestep         [1]          fp32  the step's noise level, sigma
    -> velocity        [1, N, 64]   fp32  for every image token; the host steps the picture's along the first
    """

    def __init__(self, transformer, attention_factors, mlp_factors):
        super().__init__()
        self.t = transformer
        self.rope = RopeTables(transformer.pos_embed).to(transformer.device)
        self.register_buffer("attention_factors", torch.tensor(attention_factors, dtype=torch.float32), persistent=False)
        self.register_buffer("mlp_factors", torch.tensor(mlp_factors, dtype=torch.float32), persistent=False)
        self.trace = None   # a list to keep each block's streams in, when a check asks where two precisions part

    def forward(self, image_tokens, image_positions, drawn, prompt, prompt_positions, timestep):
        t = self.t
        dtype = t.transformer_blocks[0].attn.to_q.weight.dtype
        N, T = image_tokens.shape[1], prompt.shape[1]
        image = t.img_in(image_tokens.float())
        text = t.txt_in(rms(prompt.float(), t.txt_norm.weight, t.txt_norm.eps))
        temb = t.time_text_embed(torch.cat([timestep, timestep * 0]).float(), image)   # [2, dim]: the step's, and zero's
        worked_from = (torch.arange(N, device=image.device) >= drawn).view(1, N, 1)
        cos, sin = self.rope(torch.cat([prompt_positions, image_positions], dim=0))

        def per_token(mod):   # [2, 3 * dim] -> shift, scale, gate [1, N, dim], each token its own row
            rows = torch.where(worked_from, mod[1].view(1, 1, -1), mod[0].view(1, 1, -1))
            return rows.chunk(3, dim=-1)

        for i, block in enumerate(t.transformer_blocks):
            # On three axes, as the branches are, so they are matrix products the 8-bit build quantises, not Gemms it skips.
            img_mod1, img_mod2 = block.img_mod(temb.to(dtype).unsqueeze(0))[0].float().chunk(2, dim=-1)
            txt_mod1, txt_mod2 = block.txt_mod(temb[:1].to(dtype).unsqueeze(0)).float().chunk(2, dim=-1)
            shift, scale, gate = per_token(img_mod1)
            c_shift, c_scale, c_gate = txt_mod1.chunk(3, dim=-1)
            x = (layer_norm(image) * (1 + scale) + shift).to(dtype)
            c = (layer_norm(text) * (1 + c_scale) + c_shift).to(dtype)

            a, d = block.attn, block.attn.inner_dim // block.attn.heads
            query = torch.cat([heads(a.add_q_proj(c), a.norm_added_q, d), heads(a.to_q(x), a.norm_q, d)], dim=1)
            key = torch.cat([heads(a.add_k_proj(c), a.norm_added_k, d), heads(a.to_k(x), a.norm_k, d)], dim=1)
            value = torch.cat([a.add_v_proj(c), a.to_v(x)], dim=1)
            out = attention(rotate(query, cos, sin).flatten(2), rotate(key, cos, sin).flatten(2), value, a.heads)
            factor = self.attention_factors[i]
            image = image + gate * (a.to_out[0](out[:, T:]).float() * factor)
            text = text + c_gate * (a.to_add_out(out[:, :T]).float() * factor)

            shift, scale, gate = per_token(img_mod2)
            c_shift, c_scale, c_gate = txt_mod2.chunk(3, dim=-1)
            image = image + gate * (block.img_mlp((layer_norm(image) * (1 + scale) + shift).to(dtype)).float() * self.mlp_factors[i, 0])
            text = text + c_gate * (block.txt_mlp((layer_norm(text) * (1 + c_scale) + c_shift).to(dtype)).float() * self.mlp_factors[i, 1])
            if self.trace is not None:
                self.trace.append((image.detach().cpu(), text.detach().cpu()))

        return t.proj_out(t.norm_out(image, temb[:1]))


def fp32_conditioning(transformer, dtype=torch.float16):
    """The embedders, the timestep's embedding, the final norm and the projection in fp32; the blocks, their modulations
    too, fp16 — each block's modulation is 113M weights, a third of the model's, and Qwen runs them in bf16."""
    transformer.to(dtype)
    for module in (transformer.img_in, transformer.txt_in, transformer.txt_norm, transformer.time_text_embed,
                   transformer.norm_out, transformer.proj_out):
        module.float()


def measure_branch_peaks(pipe, edits, pictures):
    """The largest value each block's branches reach across full edits in bf16 — where they are made and where they end."""
    peaks = {}

    def note(key, value):
        peaks[key] = max(peaks.get(key, 0.0), float(value.detach().abs().float().max()))

    hooks = []
    for i, block in enumerate(pipe.transformer.transformer_blocks):
        a = block.attn
        for name, module in (("v", a.to_v), ("v", a.add_v_proj), ("out", a.to_out[0]), ("out", a.to_add_out),
                             ("img_up", block.img_mlp.net[0]), ("img_down", block.img_mlp.net[2]),
                             ("txt_up", block.txt_mlp.net[0]), ("txt_down", block.txt_mlp.net[2])):
            hooks.append(module.register_forward_hook(lambda _m, _i, o, k=f"{i}.{name}": note(k, o)))
    for i, (instruction, names) in enumerate(edits):
        edit(pipe, instruction, [pictures[n] for n in names], seed=1000 + i)
        print(f"calibration edit {i + 1}/{len(edits)}", flush=True)
    for h in hooks:
        h.remove()
    return peaks


def power_of_two_over(peak):
    return 1.0 if peak <= HEADROOM else 2.0 ** math.ceil(math.log2(peak / HEADROOM))


def scale_branches(transformer, peaks):
    """Scale each branch that would leave fp16's range down where its values are made — its weights and its bias, so the
    branch is a scaled copy of itself — and give the factor it is multiplied by as it rejoins its fp32 stream. The two
    streams' attention shares one softmax, so their values share one factor; each stream's feed-forward has its own."""
    layers = len(transformer.transformer_blocks)
    attention, mlp = [1.0] * layers, [[1.0, 1.0] for _ in range(layers)]

    def scale(linear, factor):
        linear.weight.data /= factor
        if linear.bias is not None:
            linear.bias.data /= factor

    for i, block in enumerate(transformer.transformer_blocks):
        a = block.attn
        f = power_of_two_over(max(peaks.get(f"{i}.v", 0.0), peaks.get(f"{i}.out", 0.0)))
        if f > 1:
            for linear in (a.to_v, a.add_v_proj):
                scale(linear, f)
            # The output projections' biases are added after the weighted values: scaled with them.
            a.to_out[0].bias.data /= f
            a.to_add_out.bias.data /= f
        attention[i] = f
        for j, (up, down, mlp_module) in enumerate((("img_up", "img_down", block.img_mlp), ("txt_up", "txt_down", block.txt_mlp))):
            if peaks.get(f"{i}.{up}", 0.0) > 6.0e4:
                raise SystemExit(f"block {i}'s {up} passes fp16's range before its activation, where no factor reaches")
            g = power_of_two_over(peaks.get(f"{i}.{down}", 0.0))
            if g > 1:
                scale(mlp_module.net[2], g)
            mlp[i][j] = g
    return attention, mlp


# ── the picture encoder and the decoder ──────────────────────────────────────────────────────────────────────────

def patch_vae(vae):
    """Qwen-Image's VAE is Wan 2.1's: its upsampling as each pixel repeated into a 2x2 block, and its channel RMS norm
    reduced in fp32 without an fp32 copy of the input — as Nexaflow's Wan build has them — for the decoder's memory."""
    import types
    from diffusers.models.autoencoders.autoencoder_kl_qwenimage import QwenImageRMS_norm, QwenImageUpsample

    def rms_norm_channels(self, x):
        norm = torch.linalg.vector_norm(x, ord=2, dim=1, keepdim=True, dtype=torch.float32)
        inverse = (self.scale / norm.clamp_min(1e-12)).clamp_max(torch.finfo(x.dtype).max).to(x.dtype)
        return x * inverse * self.gamma + self.bias

    def upsample_in_place(self, x):
        assert tuple(self.scale_factor) == (2.0, 2.0) and self.mode == "nearest-exact"
        n, c, h, w = x.shape
        return x[:, :, :, None, :, None].expand(n, c, h, 2, w, 2).reshape(n, c, 2 * h, 2 * w)

    for module in list(vae.decoder.modules()) + list(vae.encoder.modules()):
        if isinstance(module, QwenImageRMS_norm) and module.channel_first:
            module.forward = types.MethodType(rms_norm_channels, module)
        elif isinstance(module, QwenImageUpsample):
            module.forward = types.MethodType(upsample_in_place, module)


class PictureEncoder(torch.nn.Module):
    """rgb [H, W, 3] uint8, a picture to work from at the size it is read -> tokens [H/16, W/16, 64] fp32: the
    encoder's mean, normalised as the pipeline normalises latents, in the denoiser's 2x2 packing."""

    def __init__(self, vae):
        super().__init__()
        self.vae = vae
        z = vae.config.z_dim
        self.register_buffer("mean", torch.tensor(vae.config.latents_mean).view(1, z, 1, 1, 1), persistent=False)
        self.register_buffer("std", torch.tensor(vae.config.latents_std).view(1, z, 1, 1, 1), persistent=False)

    def forward(self, rgb):
        dtype = self.vae.quant_conv.weight.dtype
        H, W = rgb.shape[0], rgb.shape[1]
        x = (rgb.permute(2, 0, 1).float() / 255.0 * 2.0 - 1.0).view(1, 3, 1, H, W).to(dtype)
        h = self.vae.quant_conv(self.vae.encoder(x, feat_cache=[None] * self.vae._cached_conv_counts["encoder"], feat_idx=[0]))
        z = self.vae.config.z_dim
        latents = (h[:, :z].float() - self.mean) / self.std                # [1, 16, 1, H/8, W/8]
        latents = latents[0, :, 0].view(z, H // 16, 2, W // 16, 2)
        return latents.permute(1, 3, 0, 2, 4).reshape(H // 16, W // 16, z * 4)


class Decoder(torch.nn.Module):
    """tokens [H/16, W/16, 64] fp32, the denoiser's -> rgb [H, W, 3] uint8, as the pipeline post-processes it."""

    def __init__(self, vae):
        super().__init__()
        self.vae = vae
        z = vae.config.z_dim
        self.register_buffer("mean", torch.tensor(vae.config.latents_mean).view(1, z, 1, 1, 1), persistent=False)
        self.register_buffer("std", torch.tensor(vae.config.latents_std).view(1, z, 1, 1, 1), persistent=False)

    def forward(self, tokens):
        z = self.vae.config.z_dim
        rows, cols = tokens.shape[0], tokens.shape[1]
        latents = tokens.view(rows, cols, z, 2, 2).permute(2, 0, 3, 1, 4).reshape(1, z, 1, 2 * rows, 2 * cols)
        dtype = self.vae.post_quant_conv.weight.dtype
        x = self.vae.post_quant_conv((latents * self.std + self.mean).to(dtype))
        out = self.vae.decoder(x, feat_cache=[None] * self.vae._cached_conv_counts["decoder"], feat_idx=[0])
        image = (out[0, :, 0].float() / 2 + 0.5).clamp(0, 1)
        return (image * 255).round().to(torch.uint8).permute(1, 2, 0)


# ── export ───────────────────────────────────────────────────────────────────────────────────────────────────────

def clear(path):
    for stale in path.parent.glob(path.name + "*"):
        stale.unlink()


def export(module, args, path, input_names, output_names, dynamic_shapes):
    path.parent.mkdir(parents=True, exist_ok=True)
    clear(path)
    with torch.no_grad():
        program = torch.onnx.export(module, args, dynamo=True, input_names=input_names, output_names=output_names,
                                    dynamic_shapes=dynamic_shapes, optimize=True, custom_translation_table=TRANSLATIONS)
    program.save(str(path), external_data=True)
    print(f"exported {path.name}", flush=True)


def quantize(src, dst, block_size=32, bits=8, keep_fp32=True):
    """The graph's matrices to <paramref name="bits"/>-bit MatMulNBits; fp32 projections stay as they are."""
    import onnx
    from onnxruntime.quantization.matmul_nbits_quantizer import MatMulNBitsQuantizer, DefaultWeightOnlyQuantConfig

    model = onnx.load(str(src), load_external_data=True)
    initializers = {init.name: init for init in model.graph.initializer}
    exclude = [node.name for node in model.graph.node if keep_fp32 and node.op_type == "MatMul" and node.input[1] in initializers
               and initializers[node.input[1]].data_type == onnx.TensorProto.FLOAT]
    print(f"keeping {len(exclude)} fp32 projection(s) unquantised", flush=True)
    config = DefaultWeightOnlyQuantConfig(block_size=block_size, is_symmetric=True, accuracy_level=0, bits=bits,
                                          op_types_to_quantize=("MatMul",))
    quantizer = MatMulNBitsQuantizer(model, algo_config=config, nodes_to_exclude=exclude)
    quantizer.process()
    graph = quantizer.model.model.graph
    used = {name for node in graph.node for name in node.input}
    for init in [i for i in graph.initializer if i.name not in used]:
        graph.initializer.remove(init)
    dst.parent.mkdir(parents=True, exist_ok=True)
    clear(dst)
    onnx.save_model(quantizer.model.model, str(dst), save_as_external_data=True, all_tensors_to_one_file=True,
                    location=dst.name + ".data")
    print(f"quantized {dst.name}", flush=True)


# ── held to the original ─────────────────────────────────────────────────────────────────────────────────────────

def report(name, ours, theirs, tolerance):
    scale = theirs.abs().max().item()
    error = (ours.float() - theirs.float()).abs().max().item()
    print(f"{name}: max difference {error:.4g} on values up to {scale:.4g}", flush=True)
    if error > tolerance * scale:
        raise SystemExit(f"the {name} does not match the original")


def test_pictures(size=512):
    """Pictures with something in them for the checks: smooth gradients under a pattern."""
    from PIL import Image
    import numpy as np
    pictures = []
    for seed, (w, h) in enumerate([(size, size * 3 // 4), (size * 2 // 3, size)]):
        rng = np.random.default_rng(seed)
        y, x = np.mgrid[0:h, 0:w]
        base = np.stack([x * 255 / w, y * 255 / h, ((x // 32 + y // 32) % 2) * 200], -1) + rng.normal(0, 20, (h, w, 3))
        pictures.append(Image.fromarray(np.clip(base, 0, 255).astype(np.uint8)))
    return pictures


def check_reader(pipe, reader, tolerance):
    for picture in test_pictures():
        patches, positions, order, windows, grid = reader_inputs(pipe.processor, reader.visual, picture)
        with torch.no_grad():
            theirs = torch.cat(list(pipe.text_encoder.model.get_image_features(patches.cuda().to(pipe.text_encoder.dtype), grid.cuda()))).float()
            ours = reader(patches.float().cuda(), positions.cuda(), order.cuda(), windows.cuda())
        report(f"picture reader ({reader.visual.patch_embed.proj.weight.dtype}, {tuple(grid[0].tolist())})", ours, theirs, tolerance)


def check_prompt_encoder(pipe, encoder, reader, tolerance):
    from diffusers.pipelines.qwenimage.pipeline_qwenimage_edit_plus import calculate_dimensions
    pictures = test_pictures()
    conditions = []
    for p in pictures:
        w, h = calculate_dimensions(CONDITION_AREA, p.width / p.height)
        conditions.append(pipe.image_processor.resize(p, h, w))
    for instruction in ("Put the pattern of the first picture on the second.", " "):
        with torch.no_grad():
            theirs, _ = pipe._get_qwen_prompt_embeds(instruction, conditions, device="cuda", dtype=torch.float32)
            inputs, positions = prompt_inputs(pipe, instruction, conditions)
            # The model's own vision tokens, all pictures read together as the pipeline reads them; then the reader's, one
            # picture a call, whose summation order the language model's large activations carry further.
            model_tokens = torch.cat(list(pipe.text_encoder.model.get_image_features(
                inputs.pixel_values.cuda().to(pipe.text_encoder.dtype), inputs.image_grid_thw.cuda()))).float()
            exact = encoder(inputs.input_ids.cuda(), model_tokens, positions.cuda())[TEMPLATE_TOKENS:]
            embeds = torch.cat([reader(*[t.cuda() for t in reader_inputs(pipe.processor, reader.visual, c)[:4]]) for c in conditions])
            read = encoder(inputs.input_ids.cuda(), embeds, positions.cuda())[TEMPLATE_TOKENS:]
        report(f"prompt encoder ('{instruction[:20]}', the model's vision tokens)", exact, theirs[0], tolerance)
        report(f"prompt encoder ('{instruction[:20]}', the reader's)", read, theirs[0], 1e-2)


class DenoiserCheck:
    """The original transformer's step for a picture worked from beside the latents at one noise level, taken before
    anything is changed, for the rebuilt step to be held to."""

    ROWS, COLS, REF_ROWS, REF_COLS, TOKENS = 24, 32, 16, 20, 40

    def __init__(self, transformer):
        device = "cuda"
        g = torch.Generator(device).manual_seed(3)
        drawn, worked = self.ROWS * self.COLS, self.REF_ROWS * self.REF_COLS
        self.tokens = torch.randn(1, drawn + worked, 64, device=device, generator=g)
        self.prompt = torch.randn(1, self.TOKENS, 3584, device=device, generator=g)
        self.sigma = torch.tensor([0.6], device=device)
        self.grids = [(self.ROWS, self.COLS), (self.REF_ROWS, self.REF_COLS)]
        with torch.no_grad():
            self.expected = transformer(hidden_states=self.tokens, timestep=self.sigma, encoder_hidden_states=self.prompt,
                                        encoder_hidden_states_mask=torch.ones(1, self.TOKENS, device=device),
                                        img_shapes=[[(1, r, c) for r, c in self.grids]], return_dict=False)[0][:, :drawn].float()

    def error(self, denoiser):
        """The rebuilt step's largest difference from the original's, as a share of its largest value."""
        drawn = self.ROWS * self.COLS
        dtype = denoiser.t.transformer_blocks[0].attn.to_q.weight.dtype
        with torch.no_grad():
            ours = denoiser(self.tokens, image_positions(self.grids).cuda(), torch.tensor([drawn], device="cuda"), self.prompt,
                            prompt_positions(self.TOKENS, self.grids).cuda(), self.sigma)
        error = ((ours[:, :drawn].float() - self.expected).abs().max() / self.expected.abs().max()).item()
        print(f"denoiser ({dtype}): max difference {error:.4g} of the largest value", flush=True)
        return error

    def check(self, denoiser, tolerance):
        if self.error(denoiser) > tolerance:
            raise SystemExit("the denoiser does not match the original")


def image_positions(grids):
    """Each picture's tokens' (picture, row, column), rows and columns centred as QwenEmbedRope's scale_rope centres them."""
    out = []
    for i, (rows, cols) in enumerate(grids):
        r = torch.arange(rows) - (rows - rows // 2)
        c = torch.arange(cols) - (cols - cols // 2)
        rr, cc = torch.meshgrid(r, c, indexing="ij")
        out.append(torch.stack([torch.full_like(rr, i), rr, cc], dim=-1).reshape(-1, 3))
    return torch.cat(out)


def prompt_positions(length, grids):
    start = max(max(r // 2, c // 2) for r, c in grids)
    return (start + torch.arange(length)).unsqueeze(1).expand(-1, 3).contiguous()


def check_vae(vae, encoder, decoder, tolerance):
    import numpy as np
    picture = test_pictures(1024)[0]
    rgb = torch.from_numpy(np.asarray(picture)).cuda()
    x = (rgb.permute(2, 0, 1).float() / 255.0 * 2.0 - 1.0).view(1, 3, 1, rgb.shape[0], rgb.shape[1])
    mean = torch.tensor(vae.config.latents_mean).view(1, 16, 1, 1, 1).cuda()
    std = torch.tensor(vae.config.latents_std).view(1, 16, 1, 1, 1).cuda()
    with torch.no_grad():
        dtype = vae.dtype
        theirs = ((vae.encode(x.to(dtype)).latent_dist.mode().float() - mean) / std)
        ours = encoder(rgb)
        H, W = rgb.shape[0], rgb.shape[1]
        report(f"picture encoder ({encoder.vae.quant_conv.weight.dtype})",
               ours.view(H // 16, W // 16, 16, 2, 2).permute(2, 0, 3, 1, 4).reshape(1, 16, 1, H // 8, W // 8), theirs, tolerance)
        decoded = vae.decode((theirs * std + mean).to(dtype), return_dict=False)[0][0, :, 0].float()
        expected = ((decoded / 2 + 0.5).clamp(0, 1) * 255).round()
        got = decoder(ours).permute(2, 0, 1).float()
    mse = ((expected - got) ** 2).mean().item()
    psnr = 99.0 if mse == 0 else 10 * math.log10(255 ** 2 / mse)
    print(f"decoder: {psnr:.1f} dB from the original's", flush=True)
    if psnr < 40:
        raise SystemExit("the decoder does not match the original")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("checkpoint")
    p.add_argument("out")
    p.add_argument("--only", choices=["reader", "encoder", "denoiser", "vae"])
    p.add_argument("--pictures", help="a folder of the calibration pictures, named as CALIBRATION_EDITS names them (png)")
    args = p.parse_args()
    checkpoint, out = Path(args.checkpoint), Path(args.out)

    if args.only in (None, "reader", "encoder"):
        pipe = load_pipeline(checkpoint, torch.float32, transformer=False)
        reader = PictureReader(pipe.text_encoder.model.visual).eval()
        check_reader(pipe, reader, 1e-4)
        encoder = PromptEncoder(pipe.text_encoder).eval()
        # Exact on the CPU; on the GPU the attention kernels' summation order, carried by the language model's activations
        # of 10^3 and more, leaves about 10^-3 of the largest value.
        check_prompt_encoder(pipe, encoder, reader, 3e-3)
        if args.only in (None, "reader"):
            sample = reader_inputs(pipe.processor, reader.visual, test_pictures()[0])[:4]
            patches = torch.export.Dim("patches", min=16, max=16384)
            groups = torch.export.Dim("groups", min=4, max=4096)
            export(reader, tuple(t.cuda() if t.dtype != torch.float32 else t.float().cuda() for t in sample),
                   out / "picture-reader" / "picture-reader.onnx", ["patches", "patch_positions", "window_order", "windows"],
                   ["vision_embeds"], {"patches": {0: 4 * groups}, "patch_positions": {0: 4 * groups},
                                       "window_order": {0: groups}, "windows": {0: 4 * groups}})
        if args.only in (None, "encoder"):
            inputs, positions = prompt_inputs(pipe, "Make it night.", [test_pictures()[0].resize((384, 288))])
            embeds = reader(*[t.cuda() for t in reader_inputs(pipe.processor, reader.visual, test_pictures()[0].resize((384, 288)))[:4]])
            tokens = torch.export.Dim("tokens", min=2, max=4096)
            vision = torch.export.Dim("vision", min=2, max=4096)
            export(encoder, (inputs.input_ids.cuda(), embeds, positions.cuda()), out / "prompt-encoder-fp32" / "prompt-encoder.onnx",
                   ["input_ids", "vision_embeds", "positions"], ["hidden"],
                   {"input_ids": {1: tokens}, "vision_embeds": {0: vision}, "positions": {1: tokens}})
        del pipe
        torch.cuda.empty_cache()
        if args.only in (None, "encoder"):
            # Quantised once the pipeline is gone: the fp32 graph alone is 27 GB to hold in memory.
            quantize(out / "prompt-encoder-fp32" / "prompt-encoder.onnx", out / "prompt-encoder" / "prompt-encoder.onnx", keep_fp32=False)
            shutil.copyfile(checkpoint / "processor" / "tokenizer.json", out / "prompt-encoder" / "tokenizer.json")

    if args.only in (None, "vae"):
        from diffusers import AutoencoderKLQwenImage
        reference = AutoencoderKLQwenImage.from_pretrained(checkpoint, subfolder="vae", torch_dtype=torch.float32).to("cuda").eval()
        vae = AutoencoderKLQwenImage.from_pretrained(checkpoint, subfolder="vae", torch_dtype=torch.float16).to("cuda").eval()
        patch_vae(vae)
        encoder, decoder = PictureEncoder(vae).cuda().eval(), Decoder(vae).cuda().eval()
        check_vae(reference, encoder, decoder, 2e-2)
        del reference
        rows = torch.export.Dim("rows", min=2, max=128)
        cols = torch.export.Dim("cols", min=2, max=128)
        export(encoder, (torch.zeros(256, 384, 3, dtype=torch.uint8, device="cuda"),), out / "picture-encoder" / "picture-encoder.onnx",
               ["rgb"], ["tokens"], {"rgb": {0: 16 * rows, 1: 16 * cols}})
        export(decoder, (torch.zeros(16, 24, 64, device="cuda"),), out / "decoder" / "decoder.onnx", ["tokens"], ["rgb"],
               {"tokens": {0: rows, 1: cols}})
        del vae, encoder, decoder
        torch.cuda.empty_cache()

    if args.only in (None, "denoiser"):
        cached = out / "denoiser" / "branch-peaks.json"
        if cached.exists():
            peaks = json.loads(cached.read_text())
        else:
            from PIL import Image
            pipe = load_pipeline(checkpoint, torch.bfloat16)
            folder = Path(args.pictures)
            pictures = {name: Image.open(folder / f"{name}.png").convert("RGB")
                        for name in {n for _, names in CALIBRATION_EDITS for n in names}}
            peaks = measure_branch_peaks(pipe, CALIBRATION_EDITS, pictures)
            cached.parent.mkdir(parents=True, exist_ok=True)
            cached.write_text(json.dumps(peaks, indent=1))
            del pipe
            torch.cuda.empty_cache()

        # The original's step in fp32, then the rebuilt step held to it: in fp32 — exact but for summation order and the
        # powers of two — and in the graph's precision.
        from diffusers import QwenImageTransformer2DModel
        transformer = QwenImageTransformer2DModel.from_pretrained(checkpoint, subfolder="transformer", torch_dtype=torch.float32,
                                                                  device_map="cuda").eval()
        expected = DenoiserCheck(transformer)
        attention_factors, mlp_factors = scale_branches(transformer, peaks)
        (out / "denoiser" / "branch-scales.json").write_text(json.dumps({"attention": attention_factors, "mlp": mlp_factors}, indent=1))
        denoiser = Denoiser(transformer, attention_factors, mlp_factors).eval()
        expected.check(denoiser, 1e-3)
        # Its streams reach 10^9, so no 16-bit type holds a step to fp32's: the fp16 branches are held to bf16's, the
        # precision Qwen publishes and runs the model at — the checkpoint's weights are bf16, so each cast is exact.
        fp32_conditioning(transformer, torch.bfloat16)
        torch.cuda.empty_cache()
        published = expected.error(denoiser)
        fp32_conditioning(transformer)
        torch.cuda.empty_cache()
        if expected.error(denoiser) > published:
            raise SystemExit("the denoiser's fp16 branches part from the original further than bf16 does")
        grids = [(24, 32), (16, 20)]
        tokens = torch.export.Dim("image_tokens", min=8, max=32768)
        text = torch.export.Dim("prompt_tokens", min=2, max=4096)
        export(denoiser, (torch.randn(1, 24 * 32 + 16 * 20, 64, device="cuda"), image_positions(grids).cuda(),
                          torch.tensor([24 * 32], device="cuda"), torch.randn(1, 40, 3584, device="cuda"),
                          prompt_positions(40, grids).cuda(), torch.tensor([0.5], device="cuda")),
               out / "denoiser-fp16" / "denoiser.onnx",
               ["image_tokens", "image_positions", "drawn", "prompt", "prompt_positions", "timestep"], ["velocity"],
               {"image_tokens": {1: tokens}, "image_positions": {0: tokens}, "drawn": None, "prompt": {1: text},
                "prompt_positions": {0: text}, "timestep": None})
        shutil.copyfile(out / "denoiser" / "branch-scales.json", out / "denoiser-fp16" / "branch-scales.json")
        del transformer, denoiser
        torch.cuda.empty_cache()
        quantize(out / "denoiser-fp16" / "denoiser.onnx", out / "denoiser" / "denoiser.onnx")


if __name__ == "__main__":
    main()
