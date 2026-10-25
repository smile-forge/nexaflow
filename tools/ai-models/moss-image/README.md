---
license: apache-2.0
base_model: OpenMOSS-Team/MOSS-Image-SFT
library_name: onnxruntime
pipeline_tag: text-to-image
tags:
  - onnx
  - onnxruntime
  - moss-image
  - text-to-image
---

# MOSS-Image for ONNX Runtime

[OpenMOSS-Team/MOSS-Image-SFT](https://huggingface.co/OpenMOSS-Team/MOSS-Image-SFT) (revision `f972bd4b`) as ONNX
graphs for ONNX Runtime on CUDA — its denoiser, in two builds, and its decoder. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host draws with them from C#; usable by anything that runs
ONNX Runtime 1.31 or later with its CUDA execution provider.

| Folder | What | Weights | Arithmetic | Size |
|---|---|---|---|---|
| `denoiser/` | the 5B diffusion transformer, one pass | 8-bit (MatMulNBits) | fp16, on an fp32 residual stream | 5.3 GB |
| `denoiser-4bit/` | the same graph, for cards of 8 GB | 4-bit (MatMulNBits) | fp16, on an fp32 residual stream | 2.9 GB |
| `decoder/` | the FLUX.2 small VAE decoder, latents to pixels | fp16 | fp16 | 0.05 GB |

The prompt encoder is not here. MOSS-Image reads a prompt with Qwen/Qwen3.5-9B unchanged — its last hidden state — so
it is read with an existing llama.cpp build of that model: `Qwen3.5-9B-Q8_0.gguf`, or `Qwen3.5-9B-Q6_K.gguf` on a card
of 8 GB, both from [unsloth/Qwen3.5-9B-GGUF](https://huggingface.co/unsloth/Qwen3.5-9B-GGUF) at revision `3885219b`
(`Compare-MossEncoder.ps1`, below).

## How close it comes

Twelve prompts drawn from the same noise by each build and by MOSS-Image's PyTorch pipeline in bf16, at 1024×1024,
1456×1456, 1344×768 and 768×1344, all reading the prompt with the pipeline's own bf16 encoder:

| | 8-bit | 4-bit |
|---|---|---|
| PSNR against PyTorch, median (worst) | 23.9 dB (15.1) | 14.6 dB (10.2) |
| Text asked for, drawn right | 6 of 6 prompts | 5 of 6 — a small rotated label garbled |
| The same picture from the same noise | yes; details move — a tram's livery, a background figure | no: another picture of the prompt |

Fifty guided steps carry a small difference a long way: drawing with MOSS-Image's PyTorch pipeline and only the prompt
encoder changed — llama.cpp's bf16 build of Qwen3.5-9B, closer to fp32 than PyTorch's own bf16 — gives pictures 13.9–49.9
dB from PyTorch's (median 25.6 dB). The 8-bit build is as close as that; the 4-bit one draws the prompt as well, but not
the same picture from a seed. PyTorch's own pictures miscount at times (three pears asked for as two), and so do both.

On an RTX PRO 6000, one part loaded at a time as Nexaflow's host loads them:

| | GPU memory at 1024² | at 1456² | Seconds a 1024² picture |
|---|---|---|---|
| `denoiser/` | 8.4 GB | 10.0 GB | 27.3 |
| `denoiser-4bit/` | 6.1 GB | 7.7 GB | 25.6 |
| `decoder/` | 3.1 GB | 5.5 GB | (included above) |
| PyTorch pipeline, bf16 | | | 28.6 |

The memory is the device's use at its peak less its use before the part was loaded, CUDA's context included.

## The prompt encoder

`compare_moss_encoder.py` reads fourteen prompts — among them French, German and Chinese text and a JSON layout — with
Qwen3.5-9B in PyTorch and with llama.cpp b11606 at four precisions, and draws each with MOSS-Image's pipeline:

| llama.cpp build | GPU memory, read 512 tokens at a time | Hidden state against fp32, median error | Pictures against PyTorch's bf16 encoder, median | Drawn |
|---|---|---|---|---|
| BF16 | 15.8 GB | 0.5 % | 25.6 dB | as PyTorch |
| Q8_0 | 8.7 GB | 2.8 % | 26.6 dB | as PyTorch |
| Q6_K | 7.0 GB | 4.5 % | 24.8 dB | text right; a layout rearranged |
| Q4_K_M | 5.6 GB | 16.6 % | 22.2 dB | misspelt "Bahnnofstraße"; a diagram's labels moved |

PyTorch's own bf16 reading is 1.3 % from fp32. Q8_0 is the encoder; Q6_K the one for a card of 8 GB.

## How it was built

`build_moss_image.py` traces the published PyTorch model (its `pipeline_moss_image.py`) with `torch.onnx.export`;
`check_moss_image.py` draws twelve prompts with the result and with PyTorch and measures each part;
`Build-MossImage.ps1` pins every version (`MossImage.Pins.ps1`) and runs both. The graphs differ from the PyTorch model
only where they had to, and each change keeps its arithmetic:

- **fp16 on fp32 residual streams.** The joint image-and-text stream and the text refiner's stream stay fp32; every
  branch runs in fp16. A branch whose values would leave fp16's range is scaled down by a power of two where its last
  projection makes them and back up as it rejoins the stream (`branch-scales.json`, calibrated on bf16 runs). Against
  the original in fp32 the rebuilt pass differs by at most 0.5 % of its largest value, 1 % with block 22 left out.
- **The negative prompt's pass.** MOSSImagePipeline leaves block 22 out of the pass that reads the negative prompt; the
  `skip` input does the same, an `If` around that block.
- **Laid out by the host.** The rotary angles and the timestep's sinusoid come in ready-made, computed as the reference
  computes them — the angles in fp32, the sinusoid in fp64 — so no Python integer fixes a shape in the graph.
- **Precision where it shows.** The timestep's MLP and the modulations it makes are fp32.
- **Two builds of one graph.** Both quantise the fp16 export's matrices (blocks of 32, symmetric); block 22's stay
  fp16 inside its `If`.
- **Fused attention.** Attention is ONNX Runtime's `com.microsoft.MultiHeadAttention`; it never holds every
  token-by-token score.
- **The decoder** undoes the VAE's batch norm and its 2×2 patching itself, and gives pixels in [0, 1].

## Using it

The denoiser takes one pass for one picture:

| Input | Shape | Type | |
|---|---|---|---|
| `latents` | [1, 128, h, w] | fp32 | the picture's latents, h and w its size over 16 |
| `caption` | [1, L, 4096] | fp32 | Qwen3.5-9B's last hidden state over the prompt, every row a real token |
| `rope` | [h·w + L, 128] | fp32 | rotary rows, image first: per token the angles of (0, L, row − (h−1)/2, column − (w−1)/2) for the image and (0, i, 0, 0) for the prompt, over 16 bands 10000^(−k/16), sines of all four then cosines |
| `time` | [1, 256] | fp32 | cos then sin of t · 10000^(−k/128), t the step's timestep, computed in fp64 |
| `skip` | [] | bool | leave out block 22 — for the negative prompt's pass |

and gives `velocity` [1, 128, h, w] (fp32). The prompt is read as MOSS-Image writes it — Qwen's chat template, one user
turn and the start of the reply, thinking on — and so is the negative prompt, by default empty.

Sampling is MOSS-Image's own: Euler over 50 noise levels, a grid 1 → 1/1000 shifted by 5 (σ' = 5σ / (1 + 4σ)) and each
snapped to the nearest k/1000, repeats dropped, then 0; at each, timestep 1000σ, the denoiser asked with the prompt and,
`skip` set, with the negative prompt, and `v = v_negative + 3 (v_prompt − v_negative)`; `x += (σ_next − σ) v`. The noise
starts as standard normal in fp32. The decoder takes the last latents. Any size whose sides are multiples of 16 draws.

## Licence

Apache-2.0, as the original model. The graphs are a conversion of its weights — the scaling described above aside,
which the graphs undo — and the recipe here is Apache-2.0 too.
