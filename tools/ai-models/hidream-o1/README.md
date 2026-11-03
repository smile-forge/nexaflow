---
license: mit
base_model: HiDream-ai/HiDream-O1-Image
library_name: onnxruntime
pipeline_tag: image-to-image
tags:
  - onnx
  - onnxruntime
  - hidream
  - text-to-image
  - image-editing
---

# HiDream-O1-Image for ONNX Runtime

[HiDream-ai/HiDream-O1-Image](https://huggingface.co/HiDream-ai/HiDream-O1-Image) (revision `0b0901d9`) as ONNX graphs
for ONNX Runtime on CUDA — its denoiser, which draws from a prompt and edits pictures, in two builds (8-bit weights for a
16 GB GPU, fp16 weights for a 24 GB one), and its picture reader, the Qwen3-VL vision tower an edit reads its pictures
with. Built for [Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host draws and edits with them from C#;
usable by anything that runs ONNX Runtime 1.30 or later with its CUDA execution provider.

| Folder | What | Weights | Arithmetic | Size |
|---|---|---|---|---|
| `denoiser/` | the denoiser — Qwen3-VL-8B's language model with HiDream's timestep and patch embedders and output projection — and its `tokenizer.json` | 8-bit (MatMulNBits) | fp16, on an fp32 residual stream | 8.8 GB |
| `denoiser-fp16/` | the same graph, and its `tokenizer.json` | fp16 | fp16, on an fp32 residual stream | 15.3 GB |
| `picture-reader/` | Qwen3-VL's vision tower with its DeepStack mergers, which reads each picture an edit works from | fp32 | fp32 | 2.3 GB |

HiDream-O1 draws in pixels: there is no VAE.

Twelve prompts drawn from the same noise by each build and by HiDream's PyTorch pipeline in bf16 came out at a median of
19.9 dB PSNR apart with 8-bit weights and 20.3 dB with fp16 (12.7 and 12.2 dB at worst): the same pictures, with the
same text in them, things moved about. Fifty guided steps carry a small difference a long way — HiDream's own pipeline
in bf16 and in fp32 draws pictures 15.8–39.6 dB apart, where unrelated pictures are 4–11.5 dB apart. Four edits of the
check's own drawings — night at a harbour, a chalkboard's menu rewritten, a fox made a red panda, and a mug from a second
picture put on a desk — came out 29.1, 30.2, 29.7 and 20.8 dB from the pipeline's with 8-bit weights and 29.4, 30.2,
30.1 and 20.6 dB with fp16: the same edits, the same text written. On an RTX PRO 6000 a 2048×2048 picture takes 35 s
with 8-bit weights and 33 s with fp16, an edit of one picture 80 s and of two 87 s; at their peak the 8-bit denoiser
holds 11.1 GiB drawing from a prompt and 14.5 GiB editing, the fp16 one 17.1 and 21.5 GiB, the picture reader 3.1 GiB.

## How it was built

`build_hidream_o1.py` traces HiDream's PyTorch model (its code at commit `2c2d29ff`) with `torch.onnx.export`;
`check_hidream_o1.py` draws twelve prompts and makes four edits with the result and with HiDream's own pipeline and
compares them; `edit_reference.py` records what HiDream's code does around the graphs, which Nexaflow's host is held to;
`Build-HiDreamO1.ps1` pins every version and runs them. The graphs differ from the PyTorch model only where they had
to, and each change keeps its arithmetic:

- **The prompt is read once.** The prompt's tokens attend only to the prompt before them, so their keys and values are
  the same at every step; the denoiser takes them as a cache (below). Against the original in fp32, a call that reads
  the prompt and one that takes it from the cache both give the original's prediction to 2×10⁻⁷ drawing from a prompt
  and 1×10⁻⁶ editing.
- **An edit's pictures are read twice, as HiDream reads them.** Small, by the picture reader, into the prompt — its
  tokens standing where the prompt's image placeholders are, its DeepStack features added to them after the first three
  layers — and whole, as clean 32×32 patches after the noisy picture's, which every step reads like the picture it
  draws. Every token's rotary position comes from the caller, as `get_rope_index_fix_point` numbers it.
- **fp16 on an fp32 residual stream.** A few tokens' activations pass 10⁵ from layer 6 on and reach 2.8×10⁶ in the last
  layer, past fp16's range. The residual stream is kept in fp32; every branch runs in fp16, and a branch that would
  leave fp16's range is scaled down by a power of two where its values are made — `up_proj` and `down_proj`, or
  `v_proj` — and scaled back up as it rejoins the stream (`denoiser/branch-scales.json`), its peaks measured over ten
  drawings and six edits. Exact in floating point.
- **Precision where it shows.** The timestep and patch embedders and the output projection are fp32.
- **Two builds of one denoiser.** `denoiser-fp16/` is the export as it is; `denoiser/` quantises its layers' matrices to
  8-bit MatMulNBits (blocks of 32, symmetric), leaving the fp32 projections and the embedding table as they are.
- **The reader is fp32 and reads one picture a call.** In fp16 its tokens come 4% from the original's, and it is small.
  HiDream's vision tower attends within each picture alone; the reader reads one
  per call, the cells of its 48×48 position embedding each patch is sampled from, their weights and each patch's rotary
  row and column given by the caller. Against the original in fp32 its tokens differ by 6×10⁻⁵ on values up to 4.4.
- **Fused attention.** Attention is ONNX Runtime's `com.microsoft.MultiHeadAttention`, the prompt's causal mask an
  additive attention bias; it never holds every token-by-token score.

## Using it

Either denoiser build is used the same way. A prompt is read as HiDream writes it —
`<|im_start|>user\n{prompt}<|im_end|>\n<|im_start|>assistant\n<|boi_token|>` — and so is the empty prompt guidance
steers from, `" "`. An edit's prompt has each picture's vision tokens before the text,
`<|vision_start|><|image_pad|>…<|vision_end|>`, one `<|image_pad|>` for each token the picture reader gives — and the
empty prompt has them too. The denoiser takes one call for one prompt:

| Input | Shape | Type | |
|---|---|---|---|
| `input_ids` | [1, T] | int64 | the prompt's tokens on its first call; none (T = 0) after |
| `vision_embeds` | [V, 4096] | fp32 | the picture reader's tokens for the prompt's `<|image_pad|>` placeholders, in order; none after the first call |
| `deepstack_embeds` | [3, V, 4096] | fp32 | and their DeepStack features |
| `image_tokens` | [1, M, 3072] | fp32 | the picture cut into 32×32 patches, row by row — within a patch, the channel, its row, then its column — then each picture worked from, clean, in [−1, 1] |
| `prompt_positions` | [3, T + 1] | int64 | the rotary positions of the unread prompt and of the timestep token after it — on the diagonal, from the cache's length, for a prompt without pictures |
| `image_positions` | [3, M] | int64 | the picture's (4096, 4096 + row, 4096 + column); each picture worked from numbered on from one past the largest before it |
| `timestep` | [1] | fp32 | 1 − t/1000 for the step's timestep t |
| `past_keys`, `past_values` | [36, 8, P, 128] | fp16 | the prompt's keys and values from its first call; none (P = 0) on it |

and gives `prediction` [1, M, 3072] (fp32), the clean pictures in the image tokens' layout in [−1, 1] — the first N
the picture drawn — with `present_keys` and `present_values` [36, 8, P + T, 128] (fp16) to pass back.

The picture reader takes a picture at a size of whole 32-pixel groups — HiDream reads one within 384² —:

| Input | Shape | Type | |
|---|---|---|---|
| `patches` | [S, 1536] | fp32 | its 16×16 patches as Qwen3-VL's processor lays them out — in 2×2 groups row by row, each patch its channel, two identical frames, its row and its column — normalised to [−1, 1] |
| `embedding_index`, `embedding_weights` | [4, S] | int64, fp32 | the four cells of the 48×48 position embedding each patch is sampled from bilinearly, and their weights |
| `patch_positions` | [S, 2] | int64 | each patch's row and column |

and gives `vision_embeds` [S/4, 4096] and `deepstack_embeds` [3, S/4, 4096] (fp32).

Sampling is HiDream's own: fifty steps of its flow-matching UniPC sampler at shift 3, the noise starting at 8 × standard
normal, each step asking the denoiser with the prompt and without it and taking
`v = v_without + 5 (v_with − v_without)`, where `v = (prediction − sample) / (t/1000)`; the sampler steps along `−v`.
The finished sample, `(x + 1) / 2`, is the picture. It draws from a prompt at the sizes it was trained on — 2048×2048,
2560×1440, 1440×2560, 2304×1728, 1728×2304, 2496×1664, 1664×2496, 2304×1792, 1792×2304, 3104×1312, 1312×3104 — and edits
one picture at its own proportions within 2048², each picture of several fitted within a square of three quarters of
the picture's longest side for two, half for three or four.

## Licence

MIT, as the original model. The graphs are a conversion of its weights — the scaling described above aside, which the
graph undoes — and the recipe here is MIT too.
