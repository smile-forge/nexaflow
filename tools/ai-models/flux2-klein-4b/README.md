---
license: apache-2.0
base_model: black-forest-labs/FLUX.2-klein-4B
library_name: onnxruntime
pipeline_tag: image-to-image
tags:
  - onnx
  - onnxruntime
  - flux
  - image-editing
  - text-to-image
---

# FLUX.2 [klein] 4B for ONNX Runtime

[black-forest-labs/FLUX.2-klein-4B](https://huggingface.co/black-forest-labs/FLUX.2-klein-4B) (revision `e7b7dc27`) as
four ONNX graphs for ONNX Runtime on CUDA — for drawing from a prompt and for editing pictures: changing them by an
instruction, combining several, and drawing again only what a mask covers. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host samples them from C#; usable by anything that runs ONNX
Runtime 1.30 or later with its CUDA execution provider.

| Folder | What | Weights | Arithmetic | Size |
|---|---|---|---|---|
| `encoder/` | the prompt encoder — Qwen3-4B to its 27th layer — and its `tokenizer.json` | 8-bit (MatMulNBits) | fp32 | 3.8 GB |
| `denoiser/` | the rectified-flow transformer | 8-bit (MatMulNBits) | fp16, on fp32 residual streams | 4.6 GB |
| `picture-encoder/` | the VAE encoder, for the pictures it works from | fp16 | fp16 | 69 MB |
| `decoder/` | the VAE decoder | fp16 | fp16 | 99 MB |

Against Black Forest Labs' pipelines in diffusers (`Flux2KleinPipeline`, `Flux2KleinInpaintPipeline`) in bf16 from the
same noise, edits of a 1024² picture made with the graphs come out 30–37 dB PSNR from the pipeline's — a whole picture
changed to night, restyled as a watercolour, a second picture's dog put into the first — fills under a mask 31–43 dB, and
an expansion of the picture's borders 26 dB: the same pictures. A picture drawn from a prompt alone, with nothing to hold
it, is 18 dB from the pipeline's, the same picture with details moved. The decoder is 63 dB from the VAE in fp32; the
prompt encoder's rows agree with Qwen3 in fp32 as closely as the pipeline's own bf16 encoder does.

## How it was built

`build_flux2_klein.py` traces the PyTorch model with `torch.onnx.export`; `check_flux2_klein.py` makes the edits above
with the graphs and with the pipelines and compares them; `Build-Flux2Klein.ps1` pins every version and runs both. The
graphs differ from the PyTorch model only where they had to, and each change keeps its arithmetic:

- **fp16 branches on fp32 residual streams.** The single-stream blocks' residual stream reaches 1.03×10⁵, past fp16's
  range (diffusers clips it there); no branch passes 3.5×10³. The streams stay fp32 and every branch runs in fp16.
- **Precision where it shows.** The timestep embedder, the modulations, the norms, the rotary positions and the output
  projection are fp32; the rotary angles come from tables computed in fp64, as diffusers computes them.
- **The prompt encoder runs in fp32 on 8-bit weights** — Qwen3-4B, cut after layer 27, giving the outputs of layers 9,
  18 and 27 side by side as the pipeline reads them.
- **8-bit weights.** The denoiser's and the encoder's matrices are 8-bit MatMulNBits (blocks of 32, symmetric).
- **Fused attention.** Attention is ONNX Runtime's `com.microsoft.MultiHeadAttention`; it never holds every
  token-by-token score.
- **The VAE's latent norm inside.** The picture encoder and the decoder carry the VAE's 2×2 patching and its batch norm,
  so a caller deals only in the denoiser's 128-channel latents.

## Using it

A prompt is read as the pipeline writes it — Qwen3's chat template for one user turn with thinking off,
`<|im_start|>user\n{prompt}<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n` — tokenised, padded with
`<|endoftext|>` or cut to 512 tokens:

| Graph | Inputs | Output |
|---|---|---|
| encoder | `input_ids` [1, 512] int64; `attention_mask` [1, 512] int64, 1 on the prompt | `prompt` [1, 512, 7680] fp32 |
| picture-encoder | `rgb` [H, W, 3] uint8, H and W multiples of 16 | `latents` [1, 128, H/16, W/16] fp32 |
| denoiser | `image_tokens` [1, N, 128] fp32 — the noisy latents row by row, then each picture worked from; `image_positions` [N, 4] int64 — (0, row, column, 0) for the latents, (10 (i + 1), row, column, 0) for the i-th picture; `prompt` [1, 512, 7680] fp32; `prompt_positions` [512, 4] int64 — (0, 0, 0, i); `timestep` [1] fp32 — the step's noise level σ | `velocity` [1, N, 128] fp32 |
| decoder | `latents` [1, 128, h, w] fp32 | `pixels` [1, 3, 16h, 16w] fp32 in [0, 1] |

Sampling is the pipeline's own: four Euler steps over `linspace(1, 1/4, 4)`, each level s moved to `e^μ / (e^μ + 1/s − 1)`
with μ from `compute_empirical_mu(tokens, 4)`, then 0; the latents start as standard normal noise and each step adds
`(σ_next − σ) × velocity` of their own tokens. It is distilled, so it takes no guidance. To draw again only what a mask
covers, the first picture worked from is the one being changed: after each step its latents, noised to the level reached
(`σ·noise + (1 − σ)·latents`, with the noise the picture started from) or as they are after the last step, are put back
where the mask — resized to the tokens' grid bilinearly — is 0. Pictures are drawn and read within a megapixel, each side a
multiple of 16.

In Nexaflow's host, opened with no memory pattern and the CUDA arena growing only by what is asked, each part holds at
its peak — a megapixel picture — 4.9 GiB for the prompt encoder, 2.0 GiB for the picture encoder, 4.8 GiB for the
decoder, and for the denoiser 7.3 GiB drawing from a prompt, 9.6 GiB with one picture to work from, 10.7 GiB with two and
13.4 GiB with three. On an RTX PRO 6000 a picture takes about a second.

## Licence

Apache-2.0, as the original model. The graphs are a conversion of its weights, and the recipe here is Apache-2.0 too.
