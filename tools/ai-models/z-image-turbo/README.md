---
license: apache-2.0
base_model: Tongyi-MAI/Z-Image-Turbo
library_name: onnxruntime
pipeline_tag: text-to-image
tags:
  - onnx
  - onnxruntime
  - z-image
  - text-to-image
---

# Z-Image-Turbo for ONNX Runtime

[Tongyi-MAI/Z-Image-Turbo](https://huggingface.co/Tongyi-MAI/Z-Image-Turbo) (revision `f332072a`) as three ONNX graphs
for ONNX Runtime on CUDA. Built for [Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host draws with them
from C#; usable by anything that runs ONNX Runtime 1.30 or later with its CUDA execution provider.

| Folder | What | Weights | Arithmetic | Size |
|---|---|---|---|---|
| `encoder/` | the prompt encoder: Qwen3-4B up to its 35th layer, and its `tokenizer.json` | 8-bit (MatMulNBits) | fp32 | 4.4 GB |
| `denoiser/` | the diffusion transformer, one denoising step | 8-bit (MatMulNBits) | fp16 | 6.2 GB |
| `decoder/` | the VAE decoder, latents to pixels | fp16 | fp16 | 0.1 GB |

Twelve prompts drawn from the same starting noise by these graphs and by the PyTorch pipeline in bf16 came out at a
median of 29 dB PSNR apart (17 dB at worst) — as close as PyTorch's own fp16 run comes to its bf16 — with the same
pictures and the same text in them. On an RTX PRO 6000 a 1024×1024 picture takes 2.3 s; at its largest, during the
denoiser's run, a picture holds 9.4 GB of GPU memory when the parts are loaded one at a time.

## How it was built

`build_z_image.py` traces the published PyTorch pipeline with `torch.onnx.export`; `check_z_image.py` draws twelve
prompts with the result and with PyTorch and compares them; `Build-ZImageTurbo.ps1` pins every version and runs both.
The graphs differ from the PyTorch model only where they had to, and each change keeps its arithmetic:

- **fp16 overflow.** The denoiser's attention and feed-forward branches reach values over 10⁶, past fp16's range, so
  fp16 draws black. Every branch ends in an RMSNorm, which cancels a constant scale, so each branch that needs it is
  scaled down where its values are made — the attention's value projection, the feed-forward's `w3` — by a power of
  two chosen from a calibration run (`denoiser/branch-scales.json`). Exact in floating point.
- **No complex numbers.** Rotary embeddings are real (cos, sin) pairs computed in fp32.
- **Fused attention.** Attention is ONNX Runtime's `com.microsoft.MultiHeadAttention`; written out as MatMul → Softmax
  → MatMul, it held 9 GB of scores at 1024×1024, and in the decoder overflowed fp16.
- **Shapes from the host.** Z-Image pads the prompt and numbers every token in Python integers, which would freeze the
  graph's shapes; the denoiser takes those sequences ready-made instead (below), and gives the original's output
  exactly.
- **Precision where it shows.** The timestep embedder is fp32; the prompt encoder's arithmetic is fp32 — fp16, or
  4-bit weights, there changed the text drawn.

## Using it

The encoder takes `input_ids` [1, tokens] — the prompt in Qwen3's chat template,
`<|im_start|>user\n{prompt}<|im_end|>\n<|im_start|>assistant\n`, at most 512 tokens — and gives `caption`
[1, tokens, 2560] (fp32).

The denoiser takes one step:

| Input | Shape | Type | |
|---|---|---|---|
| `image_tokens` | [1, N, 64] | fp32 | the latents [16, h, w] cut into 2×2 patches, row by row; within a patch its row, its column, then the channel |
| `image_positions` | [N, 3] | int64 | (caption rows + 1, patch row, patch column) |
| `caption` | [1, L, 2560] | fp16 | the encoder's output with its last row repeated up to a multiple of 32 |
| `caption_padding` | [L] | bool | true on the repeated rows |
| `caption_positions` | [L, 3] | int64 | (1 + i, 0, 0) for every row, repeated ones included |
| `timestep` | [1] | fp32 | 1 − σ |

and gives `velocity` [1, N, 64] (fp32) in the image tokens' layout. N must be a multiple of 32: 1024×1024, 1344×768
and 768×1344 all are.

Sampling is nine Euler steps over σ = `linspace(1, 1/9, 9)`, each shifted to 3σ / (1 + 2σ), then 0:
`latents += (σ_next − σ) · velocity`. The decoder takes the final `latents` [1, 16, h, w] (fp32) and gives `pixels`
[1, 3, 8h, 8w] in [0, 1].

## Licence

Apache-2.0, as the original model. The graphs are a conversion of its weights — the scaling described above aside,
which the model's norms undo — and the recipe here is Apache-2.0 too.
