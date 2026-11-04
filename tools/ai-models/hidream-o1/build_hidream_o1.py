"""Build HiDream-O1-Image as two ONNX graphs for Nexaflow's AI host: its denoiser, which reads a prompt once and keeps
it, and its picture reader, the Qwen3-VL vision tower that reads the pictures an edit works from.

HiDream-O1 draws in pixels with a whole Qwen3-VL language model: the prompt's tokens, a timestep token and one token
per 32x32 patch of the picture go through its 36 layers together, the prompt's tokens attending only to the prompt
before them, the others to everything. So the prompt's keys and values are the same at every step, and the graph takes
them as a cache: the first call for a prompt reads the prompt and hands its keys and values back with the step's
prediction; every later call reads only the timestep and the pictures, the prompt coming from the cache.

An edit is the same call with pictures in it. Each picture to work from is read twice: small, by the vision tower, into
the prompt — its tokens standing where the prompt's image placeholders are, and the tower's DeepStack features added to
them after the first three layers — and whole, as clean 32x32 patches after the noisy picture's, which the step reads
like the picture it draws. The rotary positions of every token come from the host, as HiDream numbers them.

The graphs are traced from the published PyTorch model with only these changes, each exact or the model's own:
  * the residual stream is fp32 — a few tokens' activations pass 10^5 from layer 6 on and 2.8x10^6 at the last layer,
    past fp16's range — and every branch is fp16; a branch that would leave fp16's range is scaled down by a power of
    two where its values are made (up_proj or v_proj, and down_proj or o_proj) and scaled back up as it rejoins the
    fp32 stream;
  * the timestep and patch embedders and the final projection are fp32: the timestep's precision decides fine detail,
    and the picture comes in and goes out through them;
  * attention is ONNX Runtime's fused MultiHeadAttention with an additive bias for the prompt's causal mask, which never
    holds every token-by-token score;
  * the vision tower is fp32, as small as it is, and reads one picture a call, so its attention is over that picture alone, as HiDream's sequence
    lengths make it; where its position embedding is sampled from, and each patch's rotary row and column, come from
    the host.

usage: python -I -X utf8 build_hidream_o1.py <hidream-o1-image checkpoint> <hidream-o1-image code> <out-dir>
       [--bits 4|8]

The calibration peaks are kept in <out-dir>/denoiser/branch-peaks.json and reused when present; the factors they give
are written beside them, branch-scales.json. The drawings calibration makes are kept in <out-dir>/calibration, where
the edits it calibrates with read them.
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
LAYERS, KV_HEADS, HEAD_DIM, HEADS, HIDDEN = 36, 8, 128, 32, 4096
PATCH = 32
TMS_TOKEN, BOI_TOKEN = "<|tms_token|>", "<|boi_token|>"
IMAGE_TOKEN, VIDEO_TOKEN, VISION_START = 151655, 151656, 151652
DEEPSTACK = 3                     # the decoder layers the vision tower's DeepStack features are added after
VISION_PATCH, VISION_MERGE = 16, 2
VISION_INPUT = 3 * 2 * VISION_PATCH * VISION_PATCH  # a patch: its channel, two identical frames, its row, its column
CONDITION_SIZE = 384             # the side of the square a picture is read by the vision tower within, for up to four

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

# Edits calibrated with, of the calibration drawings above: an instruction and the drawings it works from.
CALIBRATION_EDITS = [
    ("Make it night, the harbour lit by street lamps.", [0]),
    ("Change the menu to read 'Soupe du jour : Pistou 9 €'.", [2]),
    ("Turn it into a pencil sketch.", [5]),
    ("Remove the tram and make it a sunny day.", [3]),
    ("Put the mug from the second picture on the desk in the first.", [6, 9]),
    ("The fisherman from the first picture standing on the street of the second.", [0, 7]),
]


# ── HiDream's own code ───────────────────────────────────────────────────────────────────────────────────────────

def load_hidream(code_dir):
    """HiDream's model and pipeline modules, with PyTorch's attention standing in for flash-attn, which has no wheel
    here: the pipeline calls flash_attn_func(q, k, v, softmax_scale, causal) on [batch, seq, heads, dim], which
    scaled-dot-product attention computes the same."""
    sys.path.insert(0, str(code_dir))
    import models.qwen3_vl_transformers as qwen3_vl
    import models.pipeline as pipeline

    def sdpa_flash_attn(q, k, v, softmax_scale=None, causal=False):
        out = F.scaled_dot_product_attention(q.transpose(1, 2), k.transpose(1, 2), v.transpose(1, 2), is_causal=causal,
                                             scale=softmax_scale, enable_gqa=k.shape[2] != q.shape[2])
        return out.transpose(1, 2)

    if qwen3_vl._flash_attn_func is None:
        qwen3_vl._flash_attn_func = sdpa_flash_attn
    return qwen3_vl, pipeline


def load_model(checkpoint, code_dir, dtype):
    qwen3_vl, pipeline = load_hidream(code_dir)
    from transformers import AutoProcessor
    processor = AutoProcessor.from_pretrained(checkpoint)
    for name in ("boi", "bor", "eor", "bot", "tms"):
        setattr(processor.tokenizer, f"{name}_token", f"<|{name}_token|>")
    model = qwen3_vl.Qwen3VLForConditionalGeneration.from_pretrained(checkpoint, torch_dtype=dtype, device_map="cuda").eval()
    return model, processor, pipeline, qwen3_vl


# ── attention ────────────────────────────────────────────────────────────────────────────────────────────────────
# Exported attention otherwise becomes MatMul -> Softmax -> MatMul, which holds every token-by-token score at once.
# This op runs as PyTorch's attention when traced and checked, and is written into the graph as ONNX Runtime's fused
# com.microsoft.MultiHeadAttention with its additive attention bias.

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
    part of com.microsoft.MultiHeadAttention this graph uses — query, key, value and the additive attention bias."""
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


def biased_attention_onnx(query, key, value, bias, heads: int):
    import onnxscript
    declare_multi_head_attention()
    msft = onnxscript.values.Opset("com.microsoft", 1)
    return msft.MultiHeadAttention(query, key, value, None, None, bias, num_heads=heads)


# ── calibration ──────────────────────────────────────────────────────────────────────────────────────────────────

def measure_branch_peaks(model, processor, pipeline, prompts, edits, drawings):
    """The largest value each layer's branches reach across full drawings and edits in bf16 — where they are made and
    where they end. The drawings are saved in <paramref name="drawings"/> for the edits to work from."""
    peaks = {}

    def note(key, value):
        peaks[key] = max(peaks.get(key, 0.0), float(value.detach().abs().float().max()))

    hooks = []
    for i, layer in enumerate(model.model.language_model.layers):
        hooks.append(layer.self_attn.v_proj.register_forward_hook(lambda _m, _i, o, i=i: note(f"{i}.v_proj", o)))
        hooks.append(layer.self_attn.o_proj.register_forward_hook(lambda _m, _i, o, i=i: note(f"{i}.o_proj", o)))
        hooks.append(layer.mlp.down_proj.register_forward_pre_hook(lambda _m, a, i=i: note(f"{i}.down_in", a[0])))
        hooks.append(layer.mlp.down_proj.register_forward_hook(lambda _m, _i, o, i=i: note(f"{i}.down_proj", o)))
    drawings.mkdir(parents=True, exist_ok=True)
    for i, prompt in enumerate(prompts):
        image = pipeline.generate_image(model=model, processor=processor, prompt=prompt, ref_image_paths=[], height=2048,
                                        width=2048, seed=1000 + i, num_inference_steps=50, guidance_scale=5.0, shift=3.0,
                                        timesteps_list=None, scheduler_name="default")
        image.save(drawings / f"{i:02d}.png")
        print(f"calibration {i + 1}/{len(prompts)}", flush=True)
    for i, (instruction, sources) in enumerate(edits):
        pipeline.generate_image(model=model, processor=processor, prompt=instruction,
                                ref_image_paths=[str(drawings / f"{s:02d}.png") for s in sources], height=2048, width=2048,
                                seed=1100 + i, num_inference_steps=50, guidance_scale=5.0, shift=3.0, timesteps_list=None,
                                scheduler_name="default", keep_original_aspect=len(sources) == 1)
        print(f"calibration edit {i + 1}/{len(edits)}", flush=True)
    for h in hooks:
        h.remove()
    return peaks


def power_of_two_over(peak):
    """The smallest power of two that brings <paramref name="peak"/> within the headroom, or 1."""
    return 1.0 if peak <= HEADROOM else 2.0 ** math.ceil(math.log2(peak / HEADROOM))


def scale_branches(model, peaks):
    """Scale each branch that would leave fp16's range down where its values are made; give the factor each branch's
    output is multiplied by as it rejoins the fp32 residual stream."""
    attention, mlp = [1.0] * LAYERS, [1.0] * LAYERS
    for i, layer in enumerate(model.model.language_model.layers):
        # The attention's output is a weighted average of its values, then o_proj: scaling v_proj scales both.
        a = power_of_two_over(max(peaks.get(f"{i}.v_proj", 0.0), peaks.get(f"{i}.o_proj", 0.0)))
        if a > 1:
            layer.self_attn.v_proj.weight.data /= a
        # The feed-forward's product silu(gate) * up scales with up_proj; down_proj's output with both.
        up = power_of_two_over(peaks.get(f"{i}.down_in", 0.0))
        down = power_of_two_over(peaks.get(f"{i}.down_proj", 0.0) / up)
        if up > 1:
            layer.mlp.up_proj.weight.data /= up
        if down > 1:
            layer.mlp.down_proj.weight.data /= down
        attention[i], mlp[i] = a, up * down
    return attention, mlp


# ── the denoiser ─────────────────────────────────────────────────────────────────────────────────────────────────

class Denoiser(torch.nn.Module):
    """
    One call of HiDream-O1's denoiser for one prompt (the one with the prompt, or the empty one guidance steers from):

      input_ids        [1, T]              int64  the prompt's tokens not yet read — all of it on the first call, then
                                                  none; a picture to work from is IMAGE_TOKEN once per vision token
      vision_embeds    [V, 4096]           fp32   the picture reader's tokens for those placeholders, in order
      deepstack_embeds [3, V, 4096]        fp32   and its DeepStack features, added after the first three layers
      image_tokens     [1, M, 3072]        fp32   the picture, 32x32 patches row by row — within a patch, the channel,
                                                  its row, then its column — then each picture worked from, clean
      prompt_positions [3, T + 1]          int64  the rotary positions of the prompt's unread tokens and the timestep
                                                  token
      image_positions  [3, M]              int64  and of the image tokens
      timestep         [1]                 fp32   1 - t/1000 for the step's timestep t
      past_keys        [36, 8, P, 128]     fp16   the prompt's keys read so far, per layer — none on the first call
      past_values      [36, 8, P, 128]     fp16
    -> prediction      [1, M, 3072]        fp32   the clean picture, in the image tokens' layout, in [-1, 1]; the
                                                  first N are the picture's
       present_keys    [36, 8, P + T, 128] fp16   the prompt's keys and values, to hand back next call
       present_values  [36, 8, P + T, 128] fp16

    The prompt is the chat template's text up to and including <|boi_token|>; the timestep token follows it. This is
    HiDream's _forward_generation without the vision tower and with the prompt cached.
    """

    def __init__(self, model, attention_factors, mlp_factors):
        super().__init__()
        m = model.model
        self.lm = m.language_model
        self.t_embedder, self.x_embedder, self.final = m.t_embedder1, m.x_embedder, m.final_layer2
        self.register_buffer("attention_factors", torch.tensor(attention_factors, dtype=torch.float32), persistent=False)
        self.register_buffer("mlp_factors", torch.tensor(mlp_factors, dtype=torch.float32), persistent=False)
        self.qwen3_vl = sys.modules[type(model).__module__]

    def forward(self, input_ids, vision_embeds, deepstack_embeds, image_tokens, prompt_positions, image_positions, timestep,
                past_keys, past_values):
        lm = self.lm
        P, T, M = past_keys.shape[2], input_ids.shape[1], image_tokens.shape[1]
        device = image_tokens.device
        dtype = lm.layers[0].self_attn.q_proj.weight.dtype  # fp16 in the graph; fp32 when checked against the original

        # The prompt's embeddings, each vision token where its placeholder is: the n-th placeholder takes the picture
        # reader's n-th row, every other token the zero row put before them, then its own embedding.
        visual = input_ids[0] == IMAGE_TOKEN
        rows = torch.where(visual, torch.cumsum(visual.long(), 0), torch.zeros_like(input_ids[0]))
        vision = torch.cat([vision_embeds.new_zeros(1, HIDDEN), vision_embeds], dim=0).float()[rows]
        deepstack = torch.cat([deepstack_embeds.new_zeros(DEEPSTACK, 1, HIDDEN), deepstack_embeds], dim=1).float()[:, rows]
        prompt = torch.where(visual.view(1, T, 1), vision.unsqueeze(0), lm.embed_tokens(input_ids).float())

        # The residual stream, fp32: the prompt's embeddings, the timestep's, the pictures'.
        step = self.t_embedder(timestep.float()).float().unsqueeze(1)
        pictures = self.x_embedder(image_tokens.float()).float()
        h = torch.cat([prompt, step, pictures], dim=1)
        Q = T + 1 + M

        positions = torch.cat([prompt_positions, image_positions], dim=1).unsqueeze(1)
        cos, sin = lm.rotary_emb(torch.empty(0, dtype=dtype, device=device), positions)

        # The prompt's tokens see the prompt before them; the timestep and the pictures see everything.
        K = P + Q
        sees = torch.cat([P + 1 + torch.arange(T, device=device), torch.full((1 + M,), K, device=device)])
        bias = torch.where(torch.arange(K, device=device).unsqueeze(0) < sees.unsqueeze(1),
                           torch.zeros((), dtype=dtype, device=device),
                           torch.full((), torch.finfo(dtype).min, dtype=dtype, device=device))
        bias = bias.view(1, 1, Q, K)

        present_keys, present_values = [], []
        for i, layer in enumerate(lm.layers):
            a = layer.self_attn
            x = layer.input_layernorm(h).to(dtype)
            q = a.q_norm(a.q_proj(x).view(1, Q, HEADS, HEAD_DIM)).transpose(1, 2)
            k = a.k_norm(a.k_proj(x).view(1, Q, KV_HEADS, HEAD_DIM)).transpose(1, 2)
            v = a.v_proj(x).view(1, Q, KV_HEADS, HEAD_DIM).transpose(1, 2)
            q, k = self.qwen3_vl.apply_rotary_pos_emb(q, k, cos, sin)

            keys = torch.cat([past_keys[i].unsqueeze(0), k], dim=2)
            values = torch.cat([past_values[i].unsqueeze(0), v], dim=2)
            present_keys.append(keys[0, :, : P + T])
            present_values.append(values[0, :, : P + T])

            flat = lambda t: t.repeat_interleave(HEADS // KV_HEADS, dim=1).transpose(1, 2).reshape(1, K, HEADS * HEAD_DIM)
            out = biased_attention(q.transpose(1, 2).reshape(1, Q, HEADS * HEAD_DIM), flat(keys), flat(values), bias, HEADS)
            h = h + a.o_proj(out).float() * self.attention_factors[i]

            x = layer.post_attention_layernorm(h).to(dtype)
            h = h + layer.mlp(x).float() * self.mlp_factors[i]

            # DeepStack: the vision tower's features of a picture added to its tokens after each of the first layers.
            if i < DEEPSTACK:
                h = torch.cat([h[:, :T] + deepstack[i].unsqueeze(0), h[:, T:]], dim=1)

        h = lm.norm(h).float()
        prediction = self.final(h[:, T + 1:])
        return prediction, torch.stack(present_keys), torch.stack(present_values)


def fp32_heads(model):
    """The timestep and patch embedders, the final norm and the projection stay fp32; the layers go to fp16."""
    m = model.model
    m.language_model.to(torch.float16)
    for head in (m.t_embedder1, m.x_embedder, m.final_layer2, m.language_model.norm):
        head.float()


# ── the picture reader ───────────────────────────────────────────────────────────────────────────────────────────

class PictureReader(torch.nn.Module):
    """
    Qwen3-VL's vision tower reading one picture to work from, as HiDream's model reads each:

      patches            [S, 1536]  fp32   the picture's 16x16 patches as Qwen3-VL's processor lays them out: in 2x2
                                           groups row by row, each patch its channel, two identical frames, its row
                                           and its column, normalised to [-1, 1]
      embedding_index    [4, S]     int64  the four cells of the 48x48 position embedding each patch is sampled from
      embedding_weights  [4, S]     fp32   and their bilinear weights
      patch_positions    [S, 2]     int64  each patch's row and column, for its rotary position
    -> vision_embeds     [S/4, 4096]    fp32  one token per 2x2 group, for the prompt
       deepstack_embeds  [3, S/4, 4096] fp32  the DeepStack features of layers 8, 16 and 24 for the same tokens

    The weights and cells are those fast_pos_embed_interpolate samples; the rotary positions those rot_pos_emb gives.
    """

    def __init__(self, visual):
        super().__init__()
        self.visual = visual
        self.qwen3_vl = sys.modules[type(visual).__module__]

    def forward(self, patches, embedding_index, embedding_weights, patch_positions):
        v = self.visual
        dtype = v.patch_embed.proj.weight.dtype
        x = v.patch_embed(patches)
        cells = v.pos_embed(embedding_index) * embedding_weights.to(dtype).unsqueeze(-1)
        x = x + (cells[0] + cells[1] + cells[2] + cells[3])

        angles = patch_positions.float().unsqueeze(-1) * v.rotary_pos_emb.inv_freq
        angles = angles.flatten(1)
        angles = torch.cat([angles, angles], dim=-1)
        cos, sin = angles.cos(), angles.sin()

        S = x.shape[0]
        deepstack = []
        for layer, block in enumerate(v.blocks):
            a = block.attn
            q, k, w = a.qkv(block.norm1(x)).reshape(S, 3, a.num_heads, -1).permute(1, 0, 2, 3).unbind(0)
            q, k = self.qwen3_vl.apply_rotary_pos_emb_vision(q, k, cos, sin)
            out = F.scaled_dot_product_attention(q.transpose(0, 1).unsqueeze(0), k.transpose(0, 1).unsqueeze(0),
                                                 w.transpose(0, 1).unsqueeze(0), scale=a.scaling)
            x = x + a.proj(out[0].transpose(0, 1).reshape(S, -1))
            x = x + block.mlp(block.norm2(x))
            if layer in v.deepstack_visual_indexes:
                deepstack.append(v.deepstack_merger_list[v.deepstack_visual_indexes.index(layer)](x).float())
        return v.merger(x).float(), torch.stack(deepstack)


def vision_sampling(grid_h, grid_w, side=48):
    """
    What the picture reader takes besides the patches, for a picture of <paramref name="grid_h"/> x
    <paramref name="grid_w"/> patches: the cells and bilinear weights of the position embedding each patch is sampled
    from, and its rotary row and column — both in the processor's order, 2x2 groups row by row — as
    fast_pos_embed_interpolate and rot_pos_emb compute them. The host computes the same.
    """
    h_idxs = torch.linspace(0, side - 1, grid_h)
    w_idxs = torch.linspace(0, side - 1, grid_w)
    h_floor, w_floor = h_idxs.int(), w_idxs.int()
    h_ceil, w_ceil = (h_floor + 1).clip(max=side - 1), (w_floor + 1).clip(max=side - 1)
    dh, dw = h_idxs - h_floor, w_idxs - w_floor
    base_h, base_h_ceil = h_floor * side, h_ceil * side
    index = torch.stack([(base_h[:, None] + w_floor[None]).flatten(), (base_h[:, None] + w_ceil[None]).flatten(),
                         (base_h_ceil[:, None] + w_floor[None]).flatten(), (base_h_ceil[:, None] + w_ceil[None]).flatten()])
    weights = torch.stack([((1 - dh)[:, None] * (1 - dw)[None]).flatten(), ((1 - dh)[:, None] * dw[None]).flatten(),
                           (dh[:, None] * (1 - dw)[None]).flatten(), (dh[:, None] * dw[None]).flatten()])

    def grouped(t):  # [..., grid_h * grid_w] row by row -> [..., S] in 2x2 groups
        m = VISION_MERGE
        lead = t.shape[:-1]
        t = t.reshape(*lead, grid_h // m, m, grid_w // m, m)
        return t.permute(*range(len(lead)), -4, -2, -3, -1).reshape(*lead, -1)

    rows, cols = torch.meshgrid(torch.arange(grid_h), torch.arange(grid_w), indexing="ij")
    positions = torch.stack([grouped(rows.reshape(-1)), grouped(cols.reshape(-1))], dim=1)
    return grouped(index.long()), grouped(weights.float()), positions.long()


# ── held to the original ─────────────────────────────────────────────────────────────────────────────────────────

def prompt_ids(processor, prompt):
    """The prompt's tokens as HiDream writes them, up to and including <|boi_token|> — the timestep token is the graph's."""
    text = processor.apply_chat_template([{"role": "user", "content": prompt}], tokenize=False, add_generation_prompt=True)
    return processor.tokenizer.encode(text + BOI_TOKEN, add_special_tokens=False)


def image_positions(rows, cols, device):
    r, c = torch.meshgrid(torch.arange(rows), torch.arange(cols), indexing="ij")
    return torch.stack([torch.full_like(r, 4096), 4096 + r, 4096 + c]).reshape(3, -1).to(device)


def diagonal(prompt_tokens, device, cached=0):
    """The positions of a prompt without pictures in it: its unread tokens and the timestep token on the diagonal, from
    the cache's length."""
    return (cached + torch.arange(prompt_tokens + 1, device=device)).unsqueeze(0).expand(3, -1)


def empty_cache(device, dtype=torch.float16):
    return torch.zeros(LAYERS, KV_HEADS, 0, HEAD_DIM, dtype=dtype, device=device)


def no_vision(device):
    return torch.zeros(0, HIDDEN, device=device), torch.zeros(DEEPSTACK, 0, HIDDEN, device=device)


def edit_sample(processor, pipeline, prompt, pictures, height, width):
    """An edit's sample as HiDream's generate_image builds it — <paramref name="pictures"/> already patch-aligned at
    the size they are worked from — with what the rebuilt graphs take for the same: the prompt's tokens without the
    timestep token, every token's position, each picture's reader inputs and its clean patches."""
    import einops
    utils = sys.modules[pipeline.__name__.rsplit(".", 1)[0] + ".utils"]
    tokenizer = processor.tokenizer
    K = len(pictures)
    vlm = [p.resize(utils.calculate_dimensions(CONDITION_SIZE, p.width / p.height), resample=__import__("PIL").Image.LANCZOS)
           for p in pictures]
    content = [{"type": "image"} for _ in range(K)] + [{"type": "text", "text": prompt}]
    template = processor.apply_chat_template([{"role": "user", "content": content}], tokenize=False, add_generation_prompt=True)
    proc = processor(text=[template], images=vlm, padding="longest", return_tensors="pt")
    input_ids = torch.cat([proc.input_ids, tokenizer.encode(BOI_TOKEN + TMS_TOKEN, return_tensors="pt", add_special_tokens=False)], -1)

    clean = [einops.rearrange(pipeline.TENSOR_TRANSFORM(p), "C (H p1) (W p2) -> (H W) (C p1 p2)", p1=PATCH, p2=PATCH)
             for p in pictures]
    target = (height // PATCH) * (width // PATCH)
    grids = proc.image_grid_thw.clone()
    grids[:, 1:] //= VISION_MERGE
    grids = torch.cat([grids, torch.tensor([[1, height // PATCH, width // PATCH]]),
                       torch.tensor([[1, p.height // PATCH, p.width // PATCH] for p in pictures])])
    placeholders = []
    for length in [target] + [c.shape[0] for c in clean]:
        run = torch.full((1, length), IMAGE_TOKEN, dtype=input_ids.dtype)
        run[0, 0] = VISION_START
        placeholders.append(run)
    positions, _ = utils.get_rope_index_fix_point(1, IMAGE_TOKEN, VIDEO_TOKEN, VISION_START,
                                                  input_ids=torch.cat([input_ids] + placeholders, -1), image_grid_thw=grids,
                                                  skip_vision_start_token=[0] * K + [1] + [1] * K)
    T = input_ids.shape[1]
    token_types = torch.zeros(1, positions.shape[-1], dtype=input_ids.dtype)
    token_types[0, T - 1:] = 1

    reader = []
    for i in range(K):
        t, gh, gw = (int(x) for x in proc.image_grid_thw[i])
        start = int(proc.image_grid_thw[:i].prod(-1).sum())
        index, weights, at = vision_sampling(gh, gw)
        reader.append((proc.pixel_values[start: start + gh * gw], index, weights, at, proc.image_grid_thw[i: i + 1]))
    return {"input_ids": input_ids, "position_ids": positions, "token_types": token_types, "clean": torch.cat(clean),
            "pixel_values": proc.pixel_values, "image_grid_thw": proc.image_grid_thw, "reader": reader, "target": target}


class Reference:
    """The original model's prediction for one small picture at one timestep — drawn from a prompt, and edited from two
    pictures — taken before anything is changed, for the rebuilt graphs to be held to."""

    SIZE = 256

    def __init__(self, model, processor, pipeline):
        from PIL import Image
        device = "cuda"
        self.rows = self.cols = self.SIZE // PATCH
        sample = pipeline.build_t2i_text_sample("A lighthouse at dusk, in watercolour.", self.SIZE, self.SIZE,
                                                processor.tokenizer, processor, model.config)
        assert int(sample["input_ids"][0, -1]) == processor.tokenizer.convert_tokens_to_ids(TMS_TOKEN)
        generator = torch.Generator(device).manual_seed(5)
        self.z = 8 * torch.randn(1, self.rows * self.cols, PATCH * PATCH * 3, device=device, generator=generator)
        self.t = torch.tensor([0.37], device=device)
        self.ids = sample["input_ids"][:, :-1].to(device)  # without the timestep token, which the denoiser adds
        with torch.no_grad():
            out = model(input_ids=sample["input_ids"].to(device), position_ids=sample["position_ids"].to(device),
                        vinputs=self.z, timestep=self.t, token_types=sample["token_types"].to(device), use_flash_attn=False)
        self.prediction = out.x_pred[0, sample["vinput_mask"][0].to(device)].float()

        # Two pictures worked from, each 192x192 as HiDream sizes two for a 256x256 edit; noise for the first two.
        g = torch.Generator().manual_seed(7)
        pictures = [Image.fromarray((torch.rand(6, 6, 3, generator=g) * 255).byte().numpy()).resize((192, 192), Image.BICUBIC)
                    for _ in range(2)]
        self.edit = edit_sample(processor, pipeline, "Put the first picture's pattern on a teapot from the second.",
                                pictures, self.SIZE, self.SIZE)
        e = self.edit
        vinputs = torch.cat([self.z.cpu(), e["clean"].unsqueeze(0)], 1).to(device)
        with torch.no_grad():
            out = model(input_ids=e["input_ids"].to(device), position_ids=e["position_ids"].to(device), vinputs=vinputs,
                        timestep=self.t, token_types=e["token_types"].to(device), use_flash_attn=False,
                        pixel_values=e["pixel_values"].to(device), image_grid_thw=e["image_grid_thw"].to(device))
            self.edit_prediction = out.x_pred[0, e["input_ids"].shape[1]:][: e["target"]].float()
            embeds, deepstack = model.model.get_image_features(e["pixel_values"].to(device), e["image_grid_thw"].to(device))
        self.vision = torch.cat(embeds).float(), torch.stack([d.float() for d in deepstack])
        self.vinputs = vinputs

    def read(self, reader):
        """The rebuilt picture reader's tokens and DeepStack features for the edit's two pictures."""
        embeds, deepstack = [], []
        with torch.no_grad():
            for patches, index, weights, at, _ in self.edit["reader"]:
                e, d = reader(patches.float().cuda(), index.cuda(), weights.cuda(), at.cuda())
                embeds.append(e)
                deepstack.append(d)
        return torch.cat(embeds), torch.cat(deepstack, dim=1)

    def check_reader(self, reader, tolerance):
        embeds, deepstack = self.read(reader)
        dtype = reader.visual.patch_embed.proj.weight.dtype
        for name, ours, theirs in (("tokens", embeds, self.vision[0]), ("DeepStack", deepstack, self.vision[1])):
            scale = theirs.abs().max().item()
            error = (ours - theirs).abs().max().item()
            print(f"picture reader against the original ({name}, {dtype}): max difference {error:.4g} on values up to {scale:.4g}",
                  flush=True)
            if error > tolerance * scale:
                raise SystemExit(f"the picture reader's {name} do not match the original")

    def check(self, denoiser, vision, tolerance):
        """The denoiser's prediction on its first call, which reads the prompt, and on a later one, which reads it
        from the cache — drawing from a prompt and editing — within <paramref name="tolerance"/> of the largest value."""
        device = "cuda"
        dtype = denoiser.lm.layers[0].self_attn.k_proj.weight.dtype
        T = self.ids.shape[1]
        drawn = image_positions(self.rows, self.cols, device)
        e = self.edit
        ids = e["input_ids"][:, :-1].to(device)
        at = e["position_ids"][:, 0].to(device)
        T_edit = ids.shape[1] + 1
        none = no_vision(device)
        with torch.no_grad():
            first, keys, values = denoiser(self.ids, *none, self.z, diagonal(T, device), drawn, self.t,
                                           empty_cache(device, dtype), empty_cache(device, dtype))
            later, _, _ = denoiser(self.ids[:, :0], *none, self.z, diagonal(0, device, T), drawn, self.t, keys, values)
            edit_first, keys, values = denoiser(ids, *vision, self.vinputs, at[:, :T_edit], at[:, T_edit:], self.t,
                                                empty_cache(device, dtype), empty_cache(device, dtype))
            edit_later, _, _ = denoiser(ids[:, :0], *none, self.vinputs, at[:, T_edit - 1: T_edit], at[:, T_edit:], self.t,
                                        keys, values)
        for name, ours, theirs in (("first call", first[0], self.prediction), ("cached call", later[0], self.prediction),
                                   ("edit's first call", edit_first[0, : e["target"]], self.edit_prediction),
                                   ("edit's cached call", edit_later[0, : e["target"]], self.edit_prediction)):
            scale = theirs.abs().max().item()
            error = (ours.float() - theirs).abs().max().item()
            print(f"denoiser against the original ({name}, {dtype}): max difference {error:.4g} on values up to {scale:.4g}",
                  flush=True)
            if error > tolerance * scale:
                raise SystemExit(f"the denoiser's {name} does not match the original")


# ── export ───────────────────────────────────────────────────────────────────────────────────────────────────────

def clear(path):
    """onnx appends external data to a file already there, so a graph and its data are removed before writing."""
    for stale in path.parent.glob(path.name + "*"):
        stale.unlink()


def export(denoiser, path):
    device = "cuda"
    path.parent.mkdir(parents=True, exist_ok=True)
    clear(path)
    prompt = torch.export.Dim("prompt_tokens", min=2, max=4096)
    vision = torch.export.Dim("vision_tokens", min=2, max=4096)
    cached = torch.export.Dim("cached_tokens", min=2, max=4096)
    tokens = torch.export.Dim("image_tokens", min=2, max=20480)
    sample = (torch.tensor([[1] * 9 + [IMAGE_TOKEN] * 4 + [1] * 8], dtype=torch.int64, device=device),
              torch.randn(4, HIDDEN, device=device), torch.randn(DEEPSTACK, 4, HIDDEN, device=device),
              torch.randn(1, 64, PATCH * PATCH * 3, device=device), diagonal(21, device, 9), image_positions(8, 8, device),
              torch.tensor([0.5], device=device),
              torch.zeros(LAYERS, KV_HEADS, 9, HEAD_DIM, dtype=torch.float16, device=device),
              torch.zeros(LAYERS, KV_HEADS, 9, HEAD_DIM, dtype=torch.float16, device=device))
    with torch.no_grad():
        program = torch.onnx.export(
            denoiser, sample, dynamo=True,
            input_names=["input_ids", "vision_embeds", "deepstack_embeds", "image_tokens", "prompt_positions",
                         "image_positions", "timestep", "past_keys", "past_values"],
            output_names=["prediction", "present_keys", "present_values"],
            dynamic_shapes={"input_ids": {1: prompt}, "vision_embeds": {0: vision}, "deepstack_embeds": {1: vision},
                            "image_tokens": {1: tokens}, "prompt_positions": {1: prompt + 1}, "image_positions": {1: tokens},
                            "timestep": None,
                            "past_keys": {2: cached}, "past_values": {2: cached}},
            optimize=True, custom_translation_table={torch.ops.nexaflow.biased_attention.default: biased_attention_onnx})
    program.save(str(path), external_data=True)
    print(f"exported {path.name}", flush=True)


def export_reader(reader, path):
    device = "cuda"
    path.parent.mkdir(parents=True, exist_ok=True)
    clear(path)
    patches = torch.export.Dim("patches", min=4, max=16384)
    index, weights, at = vision_sampling(8, 12)
    sample = (torch.randn(96, VISION_INPUT, device=device), index.to(device), weights.to(device), at.to(device))
    with torch.no_grad():
        program = torch.onnx.export(
            reader, sample, dynamo=True,
            input_names=["patches", "embedding_index", "embedding_weights", "patch_positions"],
            output_names=["vision_embeds", "deepstack_embeds"],
            dynamic_shapes={"patches": {0: patches}, "embedding_index": {1: patches}, "embedding_weights": {1: patches},
                            "patch_positions": {0: patches}},
            optimize=True)
    program.save(str(path), external_data=True)
    print(f"exported {path.name}", flush=True)


def quantize(src, dst, block_size, bits):
    """The layers' weights to <paramref name="bits"/>-bit MatMulNBits; the fp32 heads and the embedding table stay as
    they are."""
    import onnx
    from onnxruntime.quantization.matmul_nbits_quantizer import MatMulNBitsQuantizer, DefaultWeightOnlyQuantConfig

    model = onnx.load(str(src), load_external_data=True)
    initializers = {init.name: init for init in model.graph.initializer}
    fp32 = [node.name for node in model.graph.node if node.op_type == "MatMul" and node.input[1] in initializers
            and initializers[node.input[1]].data_type == onnx.TensorProto.FLOAT]
    print(f"keeping {len(fp32)} fp32 projection(s) unquantised", flush=True)
    config = DefaultWeightOnlyQuantConfig(block_size=block_size, is_symmetric=True, accuracy_level=0, bits=bits,
                                          op_types_to_quantize=("MatMul",))
    quantizer = MatMulNBitsQuantizer(model, algo_config=config, nodes_to_exclude=fp32)
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
    p.add_argument("code")
    p.add_argument("out")
    p.add_argument("--block-size", type=int, default=32)
    p.add_argument("--bits", type=int, choices=[4, 8], default=8)
    args = p.parse_args()
    checkpoint, code, out = Path(args.checkpoint), Path(args.code), Path(args.out)

    cached = out / "denoiser" / "branch-peaks.json"
    if cached.exists():
        peaks = json.loads(cached.read_text())
    else:
        model, processor, pipeline, _ = load_model(checkpoint, code, torch.bfloat16)
        peaks = measure_branch_peaks(model, processor, pipeline, CALIBRATION, CALIBRATION_EDITS, out / "calibration")
        cached.parent.mkdir(parents=True, exist_ok=True)
        cached.write_text(json.dumps(peaks, indent=1))
        del model
        torch.cuda.empty_cache()

    # The original's predictions and the vision tower's reading in fp32, then the rebuilt graphs held to them: in fp32 —
    # exact but for summation order and the powers of two — and in the graphs' precision.
    model, processor, pipeline, _ = load_model(checkpoint, code, torch.float32)
    reference = Reference(model, processor, pipeline)
    reader = PictureReader(model.model.visual).eval()
    reference.check_reader(reader, 1e-4)
    attention, mlp = scale_branches(model, peaks)
    (out / "denoiser" / "branch-scales.json").write_text(json.dumps({"attention": attention, "mlp": mlp}, indent=1))
    denoiser = Denoiser(model, attention, mlp).eval()
    reference.check(denoiser, reference.vision, 1e-3)
    fp32_heads(model)
    reference.check(denoiser, reference.read(reader), 3e-2)

    # The picture reader, fp32 — in fp16 its tokens come 4% from the original's, and it is small — which either build
    # reads pictures with; the denoiser in two builds of one graph: fp16 weights, and the same quantised to 8 bits for a
    # 16 GB card. Each folder is whole.
    export_reader(reader, out / "picture-reader" / "picture-reader.onnx")
    export(denoiser, out / "denoiser-fp16" / "denoiser.onnx")
    for folder in ("denoiser", "denoiser-fp16"):
        shutil.copyfile(checkpoint / "tokenizer.json", out / folder / "tokenizer.json")
        if folder != "denoiser":
            shutil.copyfile(out / "denoiser" / "branch-scales.json", out / folder / "branch-scales.json")
    del model, denoiser, reader
    torch.cuda.empty_cache()
    quantize(out / "denoiser-fp16" / "denoiser.onnx", out / "denoiser" / "denoiser.onnx", args.block_size, args.bits)


if __name__ == "__main__":
    main()
