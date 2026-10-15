"""Build HiDream-O1-Image as one ONNX graph for Nexaflow's AI host: its denoiser, which reads a prompt once and keeps it.

HiDream-O1 draws in pixels with a whole Qwen3-VL language model: the prompt's tokens, a timestep token and one token
per 32x32 patch of the picture go through its 36 layers together, the prompt's tokens attending only to the prompt
before them, the others to everything. So the prompt's keys and values are the same at every step, and the graph takes
them as a cache: the first call for a prompt reads the prompt and hands its keys and values back with the step's
prediction; every later call reads only the timestep and the picture, the prompt coming from the cache.

The graph is traced from the published PyTorch model with only these changes, each exact or the model's own:
  * the residual stream is fp32 — a few tokens' activations pass 10^5 from layer 6 on and 2.8x10^6 at the last layer,
    past fp16's range — and every branch is fp16; a branch that would leave fp16's range is scaled down by a power of
    two where its values are made (up_proj or v_proj, and down_proj or o_proj) and scaled back up as it rejoins the
    fp32 stream;
  * the timestep and patch embedders and the final projection are fp32: the timestep's precision decides fine detail,
    and the picture comes in and goes out through them;
  * attention is ONNX Runtime's fused MultiHeadAttention with an additive bias for the prompt's causal mask, which never
    holds every token-by-token score;
  * rotary positions for the prompt and the timestep token are numbered in the graph from the cache's length, as
    HiDream numbers them; the picture's come from the host.

usage: python -I -X utf8 build_hidream_o1.py <hidream-o1-image checkpoint> <hidream-o1-image code> <out-dir>
       [--bits 4|8]

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
LAYERS, KV_HEADS, HEAD_DIM, HEADS = 36, 8, 128, 32
PATCH = 32
TMS_TOKEN, BOI_TOKEN = "<|tms_token|>", "<|boi_token|>"

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

def measure_branch_peaks(model, processor, pipeline, prompts):
    """The largest value each layer's branches reach across full drawings in bf16 — where they are made and where
    they end."""
    peaks = {}

    def note(key, value):
        peaks[key] = max(peaks.get(key, 0.0), float(value.detach().abs().float().max()))

    hooks = []
    for i, layer in enumerate(model.model.language_model.layers):
        hooks.append(layer.self_attn.v_proj.register_forward_hook(lambda _m, _i, o, i=i: note(f"{i}.v_proj", o)))
        hooks.append(layer.self_attn.o_proj.register_forward_hook(lambda _m, _i, o, i=i: note(f"{i}.o_proj", o)))
        hooks.append(layer.mlp.down_proj.register_forward_pre_hook(lambda _m, a, i=i: note(f"{i}.down_in", a[0])))
        hooks.append(layer.mlp.down_proj.register_forward_hook(lambda _m, _i, o, i=i: note(f"{i}.down_proj", o)))
    for i, prompt in enumerate(prompts):
        pipeline.generate_image(model=model, processor=processor, prompt=prompt, ref_image_paths=[], height=2048, width=2048,
                                seed=1000 + i, num_inference_steps=50, guidance_scale=5.0, shift=3.0, timesteps_list=None,
                                scheduler_name="default")
        print(f"calibration {i + 1}/{len(prompts)}", flush=True)
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

      input_ids       [1, T]             int64  the prompt's tokens not yet read — all of it on the first call, then none
      image_tokens    [1, N, 3072]       fp32   the picture, 32x32 patches row by row; within a patch, the channel, its
                                                row, then its column
      image_positions [3, N]             int64  (4096, 4096 + patch row, 4096 + patch column)
      timestep        [1]                fp32   1 - t/1000 for the step's timestep t
      past_keys       [36, 8, P, 128]    fp16   the prompt's keys read so far, per layer — none on the first call
      past_values     [36, 8, P, 128]    fp16
    -> prediction     [1, N, 3072]       fp32   the clean picture, in the image tokens' layout, in [-1, 1]
       present_keys   [36, 8, P + T, 128] fp16  the prompt's keys and values, to hand back next call
       present_values [36, 8, P + T, 128] fp16

    The prompt is the chat template's text up to and including <|boi_token|>; its tokens are numbered P, P + 1, ...
    on all three rotary axes, and the timestep token, which follows them, P + T. This is HiDream's _forward_generation
    for text to image, without the vision tower and with the prompt cached.
    """

    def __init__(self, model, attention_factors, mlp_factors):
        super().__init__()
        m = model.model
        self.lm = m.language_model
        self.t_embedder, self.x_embedder, self.final = m.t_embedder1, m.x_embedder, m.final_layer2
        self.register_buffer("attention_factors", torch.tensor(attention_factors, dtype=torch.float32), persistent=False)
        self.register_buffer("mlp_factors", torch.tensor(mlp_factors, dtype=torch.float32), persistent=False)
        self.qwen3_vl = sys.modules[type(model).__module__]

    def forward(self, input_ids, image_tokens, image_positions, timestep, past_keys, past_values):
        lm = self.lm
        P, T, N = past_keys.shape[2], input_ids.shape[1], image_tokens.shape[1]
        device = image_tokens.device
        dtype = lm.layers[0].self_attn.q_proj.weight.dtype  # fp16 in the graph; fp32 when checked against the original

        # The residual stream, fp32: the prompt's embeddings, the timestep's, the picture's.
        prompt = lm.embed_tokens(input_ids).float()
        step = self.t_embedder(timestep.float()).float().unsqueeze(1)
        picture = self.x_embedder(image_tokens.float()).float()
        h = torch.cat([prompt, step, picture], dim=1)
        Q = T + 1 + N

        # Positions: the prompt's tokens and the timestep token on the diagonal, from P; the picture's from the host.
        diagonal = (P + torch.arange(T + 1, device=device)).unsqueeze(0).expand(3, -1)
        positions = torch.cat([diagonal, image_positions], dim=1).unsqueeze(1)
        cos, sin = lm.rotary_emb(torch.empty(0, dtype=dtype, device=device), positions)

        # The prompt's tokens see the prompt before them; the timestep and picture see everything.
        K = P + Q
        sees = torch.cat([P + 1 + torch.arange(T, device=device), torch.full((1 + N,), K, device=device)])
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

        h = lm.norm(h).float()
        prediction = self.final(h[:, T + 1:])
        return prediction, torch.stack(present_keys), torch.stack(present_values)


def fp32_heads(model):
    """The timestep and patch embedders, the final norm and the projection stay fp32; the layers go to fp16."""
    m = model.model
    m.language_model.to(torch.float16)
    for head in (m.t_embedder1, m.x_embedder, m.final_layer2, m.language_model.norm):
        head.float()


def prompt_ids(processor, prompt):
    """The prompt's tokens as HiDream writes them, up to and including <|boi_token|> — the timestep token is the graph's."""
    text = processor.apply_chat_template([{"role": "user", "content": prompt}], tokenize=False, add_generation_prompt=True)
    return processor.tokenizer.encode(text + BOI_TOKEN, add_special_tokens=False)


def image_positions(rows, cols, device):
    r, c = torch.meshgrid(torch.arange(rows), torch.arange(cols), indexing="ij")
    return torch.stack([torch.full_like(r, 4096), 4096 + r, 4096 + c]).reshape(3, -1).to(device)


def empty_cache(device, dtype=torch.float16):
    return torch.zeros(LAYERS, KV_HEADS, 0, HEAD_DIM, dtype=dtype, device=device)


class Reference:
    """The original model's prediction for one small picture at one timestep, taken before anything is changed, for
    the rebuilt denoiser to be held to."""

    SIZE = 256

    def __init__(self, model, processor, pipeline):
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

    def check(self, denoiser, tolerance):
        """The denoiser's prediction on its first call, which reads the prompt, and on a later one, which reads it
        from the cache, within <paramref name="tolerance"/> of the largest value."""
        device = "cuda"
        dtype = denoiser.lm.layers[0].self_attn.k_proj.weight.dtype
        positions = image_positions(self.rows, self.cols, device)
        with torch.no_grad():
            first, keys, values = denoiser(self.ids, self.z, positions, self.t, empty_cache(device, dtype), empty_cache(device, dtype))
            later, _, _ = denoiser(self.ids[:, :0], self.z, positions, self.t, keys, values)
        scale = self.prediction.abs().max().item()
        for name, ours in (("first call", first[0]), ("cached call", later[0])):
            error = (ours.float() - self.prediction).abs().max().item()
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
    prompt = torch.export.Dim("prompt_tokens", min=2, max=1024)
    cached = torch.export.Dim("cached_tokens", min=2, max=1024)
    tokens = torch.export.Dim("image_tokens", min=2, max=10240)
    sample = (torch.ones(1, 21, dtype=torch.int64, device=device), torch.randn(1, 64, PATCH * PATCH * 3, device=device),
              image_positions(8, 8, device), torch.tensor([0.5], device=device),
              torch.zeros(LAYERS, KV_HEADS, 9, HEAD_DIM, dtype=torch.float16, device=device),
              torch.zeros(LAYERS, KV_HEADS, 9, HEAD_DIM, dtype=torch.float16, device=device))
    with torch.no_grad():
        program = torch.onnx.export(
            denoiser, sample, dynamo=True,
            input_names=["input_ids", "image_tokens", "image_positions", "timestep", "past_keys", "past_values"],
            output_names=["prediction", "present_keys", "present_values"],
            dynamic_shapes={"input_ids": {1: prompt}, "image_tokens": {1: tokens}, "image_positions": {1: tokens},
                            "timestep": None, "past_keys": {2: cached}, "past_values": {2: cached}},
            optimize=True, custom_translation_table={torch.ops.nexaflow.biased_attention.default: biased_attention_onnx})
    program.save(str(path), external_data=True)
    print(f"exported {path.name}", flush=True)


def quantize(src, dst, block_size, bits):
    """The layers' weights to <paramref name="bits"/>-bit MatMulNBits; the fp32 heads and the embedding table stay as
    they are."""
    import onnx
    from onnx import numpy_helper
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
        peaks = measure_branch_peaks(model, processor, pipeline, CALIBRATION)
        cached.parent.mkdir(parents=True, exist_ok=True)
        cached.write_text(json.dumps(peaks, indent=1))
        del model
        torch.cuda.empty_cache()

    # The original's prediction in fp32, then the rebuilt call held to it: in fp32 — exact but for summation order and
    # the powers of two — and in the graph's precision.
    model, processor, pipeline, _ = load_model(checkpoint, code, torch.float32)
    reference = Reference(model, processor, pipeline)
    attention, mlp = scale_branches(model, peaks)
    (out / "denoiser" / "branch-scales.json").write_text(json.dumps({"attention": attention, "mlp": mlp}, indent=1))
    denoiser = Denoiser(model, attention, mlp).eval()
    reference.check(denoiser, 1e-3)
    fp32_heads(model)
    reference.check(denoiser, 3e-2)

    # Two builds of one graph: fp16 weights, and the same quantised to 8 bits for a 16 GB card. Each folder is whole.
    export(denoiser, out / "denoiser-fp16" / "denoiser.onnx")
    for folder in ("denoiser", "denoiser-fp16"):
        shutil.copyfile(checkpoint / "tokenizer.json", out / folder / "tokenizer.json")
        if folder != "denoiser":
            shutil.copyfile(out / "denoiser" / "branch-scales.json", out / folder / "branch-scales.json")
    del model, denoiser
    torch.cuda.empty_cache()
    quantize(out / "denoiser-fp16" / "denoiser.onnx", out / "denoiser" / "denoiser.onnx", args.block_size, args.bits)


if __name__ == "__main__":
    main()
