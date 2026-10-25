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
  - lora
---

# MOSS-Image for ONNX Runtime

[OpenMOSS-Team/MOSS-Image-SFT](https://huggingface.co/OpenMOSS-Team/MOSS-Image-SFT) (revision `f972bd4b`) as ONNX
graphs for ONNX Runtime on CUDA — its denoiser and its decoder — with a curated set of LoRAs as ONNX Runtime adapters.
Built for [Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host draws with them from C#; usable by anything
that runs ONNX Runtime 1.31 or later with its CUDA execution provider.

| Folder | What | Weights | Arithmetic | Size |
|---|---|---|---|---|
| `denoiser/` | the 5B diffusion transformer, one pass | 8-bit (MatMulNBits) | fp16, on an fp32 residual stream | 5.3 GiB |
| `decoder/` | the FLUX.2 small VAE decoder, latents to pixels | fp16 | fp16 | 0.05 GiB |
| `loras/` | an adapter for each LoRA below, and the 8-step distillation's sampler | fp16; the distillation's timestep path fp32 | | 0.07 GiB each; the distillation 0.6 GiB |

The prompt encoder is not here. MOSS-Image reads a prompt with Qwen/Qwen3.5-9B unchanged — its last hidden state — so
it is read with an existing llama.cpp build of that model, `Qwen3.5-9B-Q8_0.gguf` from
[unsloth/Qwen3.5-9B-GGUF](https://huggingface.co/unsloth/Qwen3.5-9B-GGUF) at revision `3885219b`
(`Compare-MossEncoder.ps1`, below).

## How close it comes

Twelve prompts drawn from the same noise by the build and by MOSS-Image's PyTorch pipeline in bf16, at 1024×1024,
1456×1456, 1344×768 and 768×1344, both reading the prompt with the pipeline's own bf16 encoder: PSNR against PyTorch
median 23.8 dB (worst 15.3). Every text asked for is drawn right, and the same noise gives the same picture with details moved — a tram's
livery, a background figure.

Fifty guided steps carry a small difference a long way: drawing with MOSS-Image's PyTorch pipeline and only the prompt
encoder changed — llama.cpp's bf16 build of Qwen3.5-9B, closer to fp32 than PyTorch's own bf16 — gives pictures 13.9–49.9
dB from PyTorch's (median 25.6 dB). The build is as close as that. PyTorch's own pictures miscount at times (three pears
asked for as two), and so does the build.

On an RTX PRO 6000, one part loaded at a time as Nexaflow's host loads them:

| | GPU memory at 1024² | at 1456² | Seconds a 1024² picture |
|---|---|---|---|
| `denoiser/` | 7.8 GiB | 9.1 GiB | 22.9 |
| `decoder/` | 3.0 GiB | 5.4 GiB | (included above) |
| PyTorch pipeline, bf16 | | | 27.7 |

The memory is the device's use at its peak less its use before the part was loaded, CUDA's context included. The
largest part decides the card a picture needs: the prompt encoder's 8.5 GiB, or the denoiser's at the size drawn.

## LoRAs

Each LoRA is converted to an ONNX Runtime adapter (`loras/<id>.onnx_adapter`) and applied unmerged, as its makers ask:
one at a time, each block projection's output plus strength · (x down) up, in fp32. All are Apache-2.0.

| Id | Source | Trigger | Strength |
|---|---|---|---|
| `moss-fast-8step` | [lodestones/MOSS-Image-SFT-8-steps-dmd](https://huggingface.co/lodestones/MOSS-Image-SFT-8-steps-dmd) — a distillation: eight steps, its own sampler | | 1.0 |
| `moss-anime` | [axiomofmind/Moss-image-anime-lora](https://huggingface.co/axiomofmind/Moss-image-anime-lora) | `moss_seinen_style` | 1.0 |
| `moss-gouache` | [axiomofmind/Moss-image-gouache-lora](https://huggingface.co/axiomofmind/Moss-image-gouache-lora) | `moss_vgouache_style` | 1.0 |
| `moss-psychedelic` | [axiomofmind/Moss-image-psychedelic-lora](https://huggingface.co/axiomofmind/Moss-image-psychedelic-lora) | `moss_psychpop_style` | 1.0 |
| `moss-beauty` | [axiomofmind/Moss-image-beauty-lora](https://huggingface.co/axiomofmind/Moss-image-beauty-lora) | `moss_beauty_style` | 1.0 |
| `moss-nsfw` | [axiomofmind/Moss-image-nsfw-uncensored-lora](https://huggingface.co/axiomofmind/Moss-image-nsfw-uncensored-lora) — adult content | `moss_xvstyle` | 0.7 |
| `moss-animated` | [axiomofmind/Moss-image-animated-lora](https://huggingface.co/axiomofmind/Moss-image-animated-lora), trained on MOSS-Image-Base | `moss_ac_style` | 0.7 |
| `moss-impressionism` | [Kandoo/MOSS-Image-Impressionism-LoRA](https://huggingface.co/Kandoo/MOSS-Image-Impressionism-LoRA), trained on MOSS-Image-Base | | 1.0 |

`check_moss_loras.py` draws three prompts at 1024² with each, the trigger leading the prompt, both in PyTorch — the LoRA
read straight from its own file and added unmerged in fp32 — and with the build and its adapter:

| LoRA | Build against PyTorch with the LoRA, median (range) | PyTorch with the LoRA against without, median | Seconds a 1024² picture |
|---|---|---|---|
| `moss-fast-8step` | 15.9 dB (15.1–22.9) | 11.6 dB | 3.8 |
| `moss-anime` | 26.1 dB (19.5–30.7) | 15.4 dB | 37.1 |
| `moss-gouache` | 22.8 dB (22.1–24.7) | 11.7 dB | 36.0 |
| `moss-psychedelic` | 26.1 dB (20.3–29.9) | 16.8 dB | 36.5 |
| `moss-beauty` | 24.1 dB (19.8–31.3) | 16.1 dB | 36.3 |
| `moss-nsfw` | 25.3 dB (21.2–27.9) | 18.2 dB | 35.5 |
| `moss-animated` | 24.8 dB (21.1–25.3) | 14.8 dB | 35.3 |
| `moss-impressionism` | 32.2 dB (28.5–38.2) | 15.6 dB | 36.2 |

A style LoRA follows PyTorch as closely as the build does without one, and changes the picture far more than the build
differs from PyTorch. The two trained on MOSS-Image-Base draw their style on SFT cleanly. The distillation draws the
same picture as PyTorch with details moved: eight large steps carry the 8-bit weights' difference further than fifty.

## The prompt encoder

`compare_moss_encoder.py` reads fourteen prompts — among them French, German and Chinese text and a JSON layout — with
Qwen3.5-9B in PyTorch and with llama.cpp b11606 at four precisions, and draws each with MOSS-Image's pipeline:

| llama.cpp build | GPU memory, read 512 tokens at a time | Hidden state against fp32, median error | Pictures against PyTorch's bf16 encoder, median | Drawn |
|---|---|---|---|---|
| BF16 | 15.5 GiB | 0.5 % | 25.6 dB | as PyTorch |
| Q8_0 | 8.5 GiB | 2.8 % | 26.6 dB | as PyTorch |
| Q6_K | 6.8 GiB | 4.5 % | 24.8 dB | text right; a layout rearranged |
| Q4_K_M | 5.4 GiB | 16.6 % | 22.2 dB | misspelt "Bahnnofstraße"; a diagram's labels moved |

PyTorch's own bf16 reading is 1.3 % from fp32. Q8_0 is the encoder.

## How it was built

`build_moss_image.py` traces the published PyTorch model (its `pipeline_moss_image.py`) with `torch.onnx.export`;
`convert_moss_loras.py` turns each LoRA in `loras.json` into an adapter for it; `check_moss_image.py` and
`check_moss_loras.py` draw with the result and with PyTorch and measure each part; `Build-MossImage.ps1` pins every
version (`MossImage.Pins.ps1`) and runs them all. The graphs differ from the PyTorch model only where they had to, and
each change keeps its arithmetic:

- **fp16 on fp32 residual streams.** The joint image-and-text stream and the text refiner's stream stay fp32; every
  branch runs in fp16. A branch whose values would leave fp16's range is scaled down by a power of two where its last
  projection makes them and back up as it rejoins the stream (`branch-scales.json`, calibrated on bf16 runs). Against
  the original in fp32 the rebuilt pass differs by at most 1 % of its largest value — with block 22 left out, and with
  a LoRA.
- **The negative prompt's pass.** MOSSImagePipeline leaves block 22 out of the pass that reads the negative prompt; the
  `skip` input does the same, an `If` around that block.
- **LoRA branches only while one is active.** Every block's q, k, v, o, ffn_up_gate and ffn_down projection has a LoRA
  branch beside it, and the 42 blocks are an `If` of two paths over the same weights: with a LoRA, and without one,
  where nothing of the branches runs. `lora_strength` other than 0 chooses the first.
- **Laid out by the host.** The rotary angles and the timestep's sinusoid come in ready-made, computed as the reference
  computes them — the angles in fp32, the sinusoid in fp64 — so no Python integer fixes a shape in the graph.
- **Fused operations.** Every norm is one `RMSNormalization` and every rotation one `RotaryEmbedding` (opset 23), and
  attention is ONNX Runtime's `com.microsoft.MultiHeadAttention`, which never holds every token-by-token score.
- **Precision where it shows.** The timestep's MLP and the modulations it makes are fp32.
- **8-bit weights.** Every matrix of the fp16 export is quantised (blocks of 32, symmetric), each once though both paths
  read it.
- **The decoder** undoes the VAE's batch norm and its 2×2 patching itself, and gives pixels in [0, 1]. It is exported
  at opset 20: from 21 its group norms become `GroupNormalization`, which ONNX Runtime 1.31 runs with 2 GiB more at
  1024² and a fifth slower.

## Using it

The denoiser takes one pass for one picture:

| Input | Shape | Type | |
|---|---|---|---|
| `latents` | [1, 128, h, w] | fp32 | the picture's latents, h and w its size over 16 |
| `caption` | [1, L, 4096] | fp32 | Qwen3.5-9B's last hidden state over the prompt, every row a real token |
| `rope` | [h·w + L, 128] | fp32 | rotary rows, image first: per token the angles of (0, L, row − (h−1)/2, column − (w−1)/2) for the image and (0, i, 0, 0) for the prompt, over 16 bands 10000^(−k/16), sines of all four then cosines |
| `time` | [1, 256] | fp32 | cos then sin of t · 10000^(−k/128), t the step's timestep, computed in fp64 |
| `skip` | [] | bool | leave out block 22 — for the negative prompt's pass |
| `lora_strength` | [] | fp32 | how much of the active adapter is added; 0 for none |

and gives `velocity` [1, 128, h, w] (fp32). The LoRA matrices (`lora.<block>.<projection>.down` and `.up`) and the
timestep path (`time.*`) are inputs too, defaulting to no LoRA and the model's own; an adapter sets them for a run
(`RunOptions.add_active_adapter`, or `OrtLoraAdapter` from C#). The prompt is read as MOSS-Image writes it — Qwen's chat
template, one user turn and the start of the reply, thinking on — and so is the negative prompt, by default empty. A
style LoRA's trigger leads the prompt.

Sampling is MOSS-Image's own: Euler over 50 noise levels, a grid 1 → 1/1000 shifted by 5 (σ' = 5σ / (1 + 4σ)) and each
snapped to the nearest k/1000, repeats dropped, then 0; at each, timestep 1000σ, the denoiser asked with the prompt and,
`skip` set, with the negative prompt, and `v = v_negative + 3 (v_prompt − v_negative)`; `x += (σ_next − σ) v`. The noise
starts as standard normal in fp32. The decoder takes the last latents. Any size whose sides are multiples of 16 draws.

The 8-step distillation samples in data space instead, with no negative prompt and no guidance
(`loras/moss-fast-8step.sampler.json` holds its step codes and its latent mix Q): starting from x0 = 0 and the noise z,
at each step code c, the denoiser is asked at `z + Q x0` with c as its timestep, and `x0 = z − v`. The decoder takes the
last x0.

## Licence

Apache-2.0, as the original model and every LoRA here. The graphs and adapters are a conversion of their weights — the
scaling described above aside, which the graphs undo — and the recipe here is Apache-2.0 too.
