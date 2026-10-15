---
license: apache-2.0
base_model:
  - Wan-AI/Wan2.2-TI2V-5B-Diffusers
  - FastVideo/FastWan2.2-TI2V-5B-FullAttn-Diffusers
library_name: onnxruntime
pipeline_tag: text-to-video
tags:
  - onnx
  - onnxruntime
  - wan
  - text-to-video
  - image-to-video
---

# Wan2.2-TI2V-5B and FastWan2.2-TI2V-5B for ONNX Runtime

[Wan-AI/Wan2.2-TI2V-5B-Diffusers](https://huggingface.co/Wan-AI/Wan2.2-TI2V-5B-Diffusers) (revision `b8fff731`), and
[FastVideo/FastWan2.2-TI2V-5B-FullAttn-Diffusers](https://huggingface.co/FastVideo/FastWan2.2-TI2V-5B-FullAttn-Diffusers)
(revision `3e187042`) — its transformer distilled to three steps — as ONNX graphs for ONNX Runtime on CUDA: clips from a
prompt, and from a picture to start from, at 1280×704 or 704×1280 and 24 frames a second. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host makes clips with them from C#; usable by anything that
runs ONNX Runtime 1.30 or later with its CUDA execution provider.

The two models share every part but the denoiser:

| Folder | What | Weights | Arithmetic | Size |
|---|---|---|---|---|
| `prompt-encoder/` | UMT5-XXL's encoder, and its `spiece.model` | 8-bit (MatMulNBits); the embedding fp16 | fp32 | 6.8 GB |
| `denoiser/` | Wan2.2-TI2V-5B's transformer | fp16 | fp16; norms, modulation and positions fp32 | 9.4 GB |
| `denoiser-fast/` | FastWan2.2-TI2V-5B's transformer, the same graph | fp16 | the same | 9.4 GB |
| `decoder/` | the VAE decoder, a latent frame at a time: `decoder-first.onnx` and `decoder.onnx`, one weights file, and `caches.json`, the shape of the cache it carries | fp16 | fp16; norms accumulated in fp32 | 1.0 GB |
| `frame-encoder/` | the VAE encoder, for the picture a clip starts from | fp16 | fp16 | 0.3 GB |

On an RTX PRO 6000 (Blackwell), a five-second 720p clip takes FastWan 7–8 s of denoising and the full model 235–240 s
(fifty steps, each asking the denoiser twice); decoding takes 10 s more. Made from the same prompts and noise as the
PyTorch pipelines in bf16, the clips are 14.9–26.1 dB PSNR from theirs — FastWan's median 17.9 dB, the full model's
19.6 dB, its clip from a picture 26.1 dB — where unrelated clips are 5–9.5 dB apart: the same clips, fine detail
drifting over the steps. Against the full model in fp32, the graphs' clip is as close (14.2 dB) as the bf16 pipeline's.

## How it was built

`build_wan.py` traces the PyTorch models (diffusers 0.39, transformers 4.57.1) with `torch.onnx.export`, holding each
part to diffusers' and transformers' own as it goes — in fp32, where the rebuilt arithmetic must be theirs, and in the
graph's precision; `check_wan.py` makes clips with the graphs and with the PyTorch models from the same noise and
compares them; `wan_reference.py` records what Wan's own sampling and text handling do, for a port to be held to;
`Build-Wan.ps1` pins every version and runs them. The graphs differ from the PyTorch models only where they had to, and
each change keeps the arithmetic:

- **The prompt encoder is fp32 on 8-bit weights.** UMT5-XXL's feed-forward outputs reach 1.6×10⁵ and its residual
  stream 3.8×10⁵, past fp16's range. It reads one prompt a clip, so fp32 costs nothing; 8-bit weights round its bf16
  weights about as far as bf16 itself does. Against UMT5 in fp32 on four prompts its rows agree to a cosine of 0.998 or
  better, where the bf16 encoder the pipelines run agrees to 0.82–0.996. The prompt is read unpadded: Wan pads it to 512
  tokens and masks the padding, and T5's attention bias depends on distances alone.
- **The denoiser is fp16 throughout its layers.** Its activations stay under 900, far inside fp16's range; the timestep
  embedding, the modulation, the norms and the rotary positions stay fp32, as diffusers keeps them. Against the original
  in fp32, the rebuilt model gives its prediction to 4×10⁻⁵; in fp16, to 0.3% of its largest value.
- **A timestep per latent frame.** Wan2.2-TI2V gives every token its own timestep so that a picture to start from — the
  first latent frame — can sit at timestep 0 while the rest is denoised. Every token of a frame has the same one, so the
  graph takes one a frame and makes the modulation once a frame, not once a token: the same arithmetic, without holding
  27,280 × 6 × 3072 values.
- **Fused attention.** Attention is ONNX Runtime's `com.microsoft.MultiHeadAttention`; it never holds every
  token-by-token score.
- **The decoder keeps its cache outside.** Wan's VAE decodes a latent frame at a time — the first to one picture, each
  after to four — each causal convolution keeping the last two frames it saw. The graphs take and give that cache, about
  3 GB at 720p in fp16, so a caller keeps it on the GPU between calls. The first latent frame has its own graph, since
  it is not doubled in time; each cache is two frames, zero where the first left fewer, which is exact for a causal
  convolution. Both graphs read one weights file. Each call gives its frames as 8-bit RGB, `[frames, height, width, 3]`.
  `caches.json` lists each cache's channels and its scale against the latent grid, so a caller can allocate both sets
  before the first call and have each call read one and write the other.
- **The VAE holds no full-size fp32 copies.** Its RMS norms, as diffusers writes them, cast the activations to fp32 and
  divide by a norm expanded to their full size — three fp32 tensors as large as the input, 880 MB each at 720p. Here the
  norm over channels is a reduction accumulated in fp32 (`ReduceL2`), inverted in fp32 on the small per-pixel tensor,
  and the input scaled by it in fp16. Its upsampling, nearest-exact by two, traces as a gather over int64 indices for
  every output value, 1.8 GB at 720p; for a factor of two that is each pixel repeated into a 2×2 block, which is what
  the graph does.

## Using it

**Reading the prompt.** Clean it as Wan's pipeline does — ftfy's `fix_text`, HTML unescaped twice, whitespace run
together — tokenise it with `spiece.model` (SentencePiece, Unigram) with `</s>` closing it, at most 512 tokens, and run
`prompt-encoder.onnx`: `input_ids` [1, T] int64 → `text` [1, T, 4096] fp32. Pad the rows with zeros to 512.

**The denoiser**, either build: `latents` [1, 48, F, H/16, W/16] fp32, `timesteps` [1, F] fp32 — each latent frame's
timestep out of 1000, 0 for a frame given — and `text` [1, 512, 4096] fp32 → `velocity` [1, 48, F, H/16, W/16] fp32, the
flow (noise − clean). A clip of N frames (N = 4k + 1) has F = (N − 1)/4 + 1 latent frames; height and width are multiples
of 32.

**Sampling Wan2.2-TI2V-5B**: fifty steps of diffusers' `UniPCMultistepScheduler` with flow sigmas at shift 5, each
asking the denoiser with the prompt and with Wan's negative prompt and taking `v = v_negative + 5 (v_prompt −
v_negative)`. **Sampling FastWan**: three steps at timesteps 1000, 757 and 522, unguided; at each, the clean clip is
`x − σ·v`, σ read from a flow-matching table of 1000 levels at shift 8 by the timestep nearest, and every step but the
last noises it afresh to the next step's level, `(1 − σ′)·clean + σ′·noise`. **Starting from a picture**: encode it with
`frame-encoder.onnx` — `rgb` [H, W, 3] uint8 → [1, 48, 1, H/16, W/16] — and put it in as the first latent frame at every
step and at the end, its timestep 0.

**Decoding**: run `decoder-first.onnx` on the first latent frame — `latent` [1, 48, 1, H/16, W/16] → `frames` and
`next_cache_*` — then `decoder.onnx` on each later one with the cache the last call gave (`cache_*`).

**Memory.** Run one part at a time, with ONNX Runtime's memory pattern off and its arena shrunk after the first decoder
call and after the last. Measured at 720p in a 121-frame clip, the parts peak at: prompt encoder 8.1 GB, denoiser
15.4 GB, decoder 20.8 GB with its caches bound in place, frame encoder 1.8 GB. A clip needs a 24 GB card.

## Licence

Apache-2.0, as both originals. The graphs are conversions of their weights; the recipe here is Apache-2.0 too.
