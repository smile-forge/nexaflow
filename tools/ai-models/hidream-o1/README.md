---
license: mit
base_model: HiDream-ai/HiDream-O1-Image
library_name: onnxruntime
pipeline_tag: text-to-image
tags:
  - onnx
  - onnxruntime
  - hidream
  - text-to-image
---

# HiDream-O1-Image for ONNX Runtime

[HiDream-ai/HiDream-O1-Image](https://huggingface.co/HiDream-ai/HiDream-O1-Image) (revision `0b0901d9`) as an ONNX
graph for ONNX Runtime on CUDA — its denoiser, for drawing from a prompt — in two builds: 8-bit weights for a 16 GB GPU,
fp16 weights for a 24 GB one. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host draws with it from C#; usable by anything that runs
ONNX Runtime 1.30 or later with its CUDA execution provider.

| Folder | What | Weights | Arithmetic | Size |
|---|---|---|---|---|
| `denoiser/` | the denoiser — Qwen3-VL-8B's language model with HiDream's timestep and patch embedders and output projection — and its `tokenizer.json` | 8-bit (MatMulNBits) | fp16, on an fp32 residual stream | 8.8 GB |
| `denoiser-fp16/` | the same graph, and its `tokenizer.json` | fp16 | fp16, on an fp32 residual stream | 15.3 GB |

HiDream-O1 draws in pixels: there is no VAE. The vision tower, which only reads reference pictures, is left out.

Twelve prompts drawn from the same noise by each build and by HiDream's PyTorch pipeline in bf16 came out at a median of
18.6 dB PSNR apart with 8-bit weights and 19.1 dB with fp16 (12.7 and 12.2 dB at worst): the same pictures, with the
same text in them, things moved about. Fifty guided steps carry a small difference a long way — HiDream's own pipeline
in bf16 and in fp32 draws pictures 15.8–39.6 dB apart, where unrelated pictures are 4–11.5 dB apart. Against HiDream's
model drawn in fp32, the fp16 build comes 0.1–2.3 dB closer than the 8-bit one. On an RTX PRO 6000 a 2048×2048 picture
takes 35 s with 8-bit weights and 34 s with fp16, against 95 s for the PyTorch pipeline; in Nexaflow's host it holds
13.2 GB of GPU memory at its peak with 8-bit weights and 19.1 GB with fp16.

## How it was built

`build_hidream_o1.py` traces HiDream's PyTorch model (its code at commit `2c2d29ff`) with `torch.onnx.export`;
`check_hidream_o1.py` draws twelve prompts with the result and with HiDream's own pipeline and compares them;
`Build-HiDreamO1.ps1` pins every version and runs both. The graph differs from the PyTorch model only where it had to,
and each change keeps its arithmetic:

- **The prompt is read once.** The prompt's tokens attend only to the prompt before them, so their keys and values are
  the same at every step; the graph takes them as a cache (below). Against the original in fp32, both a call that reads
  the prompt and one that takes it from the cache give the original's prediction to 2×10⁻⁷.
- **fp16 on an fp32 residual stream.** A few tokens' activations pass 10⁵ from layer 6 on and reach 2.8×10⁶ in the last
  layer, past fp16's range. The residual stream is kept in fp32; every branch runs in fp16, and a branch that would
  leave fp16's range is scaled down by a power of two where its values are made — `up_proj` and `down_proj`, or
  `v_proj` — and scaled back up as it rejoins the stream (`denoiser/branch-scales.json`). Exact in floating point.
- **Precision where it shows.** The timestep and patch embedders and the output projection are fp32.
- **Two builds of one graph.** `denoiser-fp16/` is the export as it is; `denoiser/` quantises its layers' matrices to
  8-bit MatMulNBits (blocks of 32, symmetric), leaving the fp32 projections and the embedding table as they are.
- **Fused attention.** Attention is ONNX Runtime's `com.microsoft.MultiHeadAttention`, the prompt's causal mask an
  additive attention bias; it never holds every token-by-token score.

## Using it

Either build is used the same way. A prompt is read as HiDream writes it — `<|im_start|>user\n{prompt}<|im_end|>\n<|im_start|>assistant\n<|boi_token|>` —
and so is the empty prompt guidance steers from, `" "`. The denoiser takes one call for one prompt:

| Input | Shape | Type | |
|---|---|---|---|
| `input_ids` | [1, T] | int64 | the prompt's tokens on its first call; none (T = 0) after |
| `image_tokens` | [1, N, 3072] | fp32 | the picture cut into 32×32 patches, row by row; within a patch, the channel, its row, then its column |
| `image_positions` | [3, N] | int64 | (4096, 4096 + patch row, 4096 + patch column) |
| `timestep` | [1] | fp32 | 1 − t/1000 for the step's timestep t |
| `past_keys`, `past_values` | [36, 8, P, 128] | fp16 | the prompt's keys and values from its first call; none (P = 0) on it |

and gives `prediction` [1, N, 3072] (fp32), the clean picture in the image tokens' layout in [−1, 1], with
`present_keys` and `present_values` [36, 8, P + T, 128] (fp16) to pass back. The prompt's tokens are numbered P, P + 1,
… on all three rotary axes and the timestep token P + T; the graph does that itself.

Sampling is HiDream's own: fifty steps of its flow-matching UniPC sampler at shift 3, the noise starting at 8 × standard
normal, each step asking the denoiser with the prompt and without it and taking
`v = v_without + 5 (v_with − v_without)`, where `v = (prediction − sample) / (t/1000)`; the sampler steps along `−v`.
The finished sample, `(x + 1) / 2`, is the picture. It draws at the sizes it was trained on — 2048×2048, 2560×1440,
1440×2560, 2304×1728, 1728×2304, 2496×1664, 1664×2496, 2304×1792, 1792×2304, 3104×1312, 1312×3104.

## Licence

MIT, as the original model. The graph is a conversion of its weights — the scaling described above aside, which the
graph undoes — and the recipe here is MIT too.
