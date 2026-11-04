---
license: apache-2.0
base_model: Qwen/Qwen-Image-Edit-2511
library_name: onnxruntime
pipeline_tag: image-to-image
tags:
  - onnx
  - onnxruntime
  - qwen-image
  - image-editing
---

# Qwen-Image-Edit-2511 for ONNX Runtime

[Qwen/Qwen-Image-Edit-2511](https://huggingface.co/Qwen/Qwen-Image-Edit-2511) (revision `6f3ccc0b`) as five ONNX graphs
for ONNX Runtime on CUDA — the picture reader, the prompt encoder, the picture encoder, the denoiser and the decoder —
the denoiser in two builds: 8-bit weights for a 32 GB GPU, fp16 weights for a 48 GB one. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host edits pictures with them from C#; usable by anything
that runs ONNX Runtime 1.30 or later with its CUDA execution provider.

| Folder | What | Weights | Arithmetic | Size |
|---|---|---|---|---|
| `picture-reader/` | Qwen2.5-VL's vision tower, which reads each picture an edit works from into the prompt | fp32 | fp32 | 2.7 GB |
| `prompt-encoder/` | Qwen2.5-VL-7B's language model, and its `tokenizer.json` | 8-bit (MatMulNBits) | fp32 | 8.4 GB |
| `picture-encoder/` | Qwen-Image's VAE encoder, which reads each picture into latents | fp16 | fp16 | 0.1 GB |
| `denoiser/` | Qwen-Image's 60-layer MMDiT | 8-bit (MatMulNBits) | fp16, on fp32 residual streams | 21.9 GB |
| `denoiser-fp16/` | the same graph | fp16 | fp16, on fp32 residual streams | 40.9 GB |
| `decoder/` | Qwen-Image's VAE decoder | fp16 | fp16 | 0.1 GB |

Four edits of the same pictures made by each build and by diffusers' pipeline in bf16, from the same noise — night at a
harbour, a chalkboard's menu rewritten, a fox made a red panda, a mug from a second picture put on a desk — came out
33.7, 33.8, 36.5 and 32.9 dB PSNR apart with 8-bit weights and 35.3, 30.5, 30.4 and 32.7 dB with fp16: the same
edits, the same text written. On an RTX PRO 6000 an edit at a megapixel takes 82 s with one picture and 136 s with two,
as diffusers' pipeline takes 76 and 139 s; a fill and an expansion under a mask 82 and 84 s. At their peak, with three
pictures at a megapixel, the 8-bit denoiser holds 27.6 GiB and the fp16 one 44.4 GiB; the prompt encoder 9.6 GiB, the
picture reader 3.5 GiB, the picture encoder 4.3 GiB and the decoder 4.0 GiB, each loaded only while it is needed.

## How it was built

`build_qwen_image_edit.py` traces diffusers' (0.39) and transformers' (4.57) PyTorch model with `torch.onnx.export`,
holding each graph to the original as it goes — transformers 4.57, as the pipeline was written against it: from 5.0,
Qwen2.5-VL numbers a prompt's pictures in three dimensions only when it is given `mm_token_type_ids`, which the pipeline
does not pass, and reads every token on the diagonal instead; `check_qwen_image_edit.py` makes four edits with the result and with diffusers' own
`QwenImageEditPlusPipeline` and compares them; `edit_reference.py` records what the pipeline and Qwen2-VL's processor do
around the graphs, which Nexaflow's host is held to; `Build-QwenImageEdit.ps1` pins every version and runs them. The
graphs differ from the PyTorch model only where they had to, and each change keeps its arithmetic:

- **fp16 branches on fp32 residual streams.** diffusers clips both of the denoiser's streams at fp16's limit; here they
  stay fp32 and every branch runs in fp16, a branch that would leave fp16's range scaled down by a power of two where its
  values are made — its weights and bias together — and back up as it rejoins its stream (`denoiser/branch-scales.json`),
  its peaks measured over seven edits. The streams reach 10⁹, so a step is held to the precision Qwen runs the model at, bf16, rather than to fp32. The embedders, the timestep's embedding, the final norm and the projection are fp32; the blocks' modulations, a third of the model's weights, are fp16 with their branches.
  Against the original in fp32 the rebuilt step is exact to 4×10⁻⁶; on a real edit's values, the fp16 branches come 0.9% of the largest value from it, where the model in bf16 comes 1.6%. The queries and keys are scaled too — a key reaches 1.3×10⁵ — their RMS norm's epsilon with them, so the norm is the model's to the bit; a feed-forward whose values before its activation would pass half fp16's range takes that projection in fp32.
- **Rotary angles from tables**, every position's from −4096 to 4095 on each axis as diffusers computes them, where ONNX
  would compute them in fp32 from the positions.
- **The prompt encoder is the language model on 8-bit weights in fp32**; the picture reader's tokens stand where the
  prompt's image placeholders are. Against the original in fp32 it is exact on the CPU, and on the GPU 1.1×10⁻³ of the largest value — the attention kernels' summation order, carried by activations past 10³.
- **One picture a call to the picture reader.** Qwen2.5-VL's vision tower attends within each picture, and in most of
  its layers within each window of it; the reader reads one picture a call, the windows' order and each patch's window
  given by the caller. In fp32, as small as it is; against the original its tokens differ by 6×10⁻⁵.
- **The VAE carries its normalisation and the denoiser's packing**: the picture encoder takes 8-bit RGB and gives the
  denoiser's 64-channel tokens, the decoder takes them and gives 8-bit RGB. The picture encoder's latents are within 3×10⁻³ of the original's in fp16, and the decoder's pictures 50.8 dB from its.
- **Fused attention.** Attention is ONNX Runtime's `com.microsoft.MultiHeadAttention` — with an additive bias in the
  vision tower's windowed layers — which never holds every token-by-token score.

## Using it

An edit is the pipeline's. Each picture worked from is resized within 384² — each side rounded to 32, Lanczos — then to
Qwen2-VL's processor's multiple of 28 (PyTorch's antialiased bicubic) for the picture reader:

| Input | Shape | Type | |
|---|---|---|---|
| `patches` | [S, 1176] | fp32 | its 14×14 patches as Qwen2-VL's processor lays them out — in 2×2 groups row by row, each patch its channel, two identical frames, its row and its column — normalised by CLIP's mean and deviation |
| `patch_positions` | [S, 2] | int64 | each patch's row and column |
| `window_order` | [S/4] | int64 | the groups in the order the windows hold them, as `get_vision_window_index` gives it |
| `windows` | [S] | int64 | the window each patch falls in, in that order |

which gives `vision_embeds` [S/4, 3584] (fp32), in the processor's order. The prompt is the pipeline's system prompt
and turn — `Picture 1: <|vision_start|><|image_pad|>…<|vision_end|>` for each picture, then the instruction — and so is
the empty prompt guidance steers from, `" "`, with the same pictures. The prompt encoder takes `input_ids` [1, T]
(int64), `vision_embeds` [V, 3584] for its placeholders, and `positions` [3, T] (int64, as `get_rope_index` numbers
them), and gives `hidden` [T, 3584]; the denoiser reads it from its 65th row.

Each picture is also resized within 1024² — each side rounded to 32, Lanczos — for the picture encoder, which takes
`rgb` [H, W, 3] (uint8) and gives `tokens` [H/16, W/16, 64] (fp32). The edit is drawn within 1024² at its picture's
proportions. The denoiser takes one step:

| Input | Shape | Type | |
|---|---|---|---|
| `image_tokens` | [1, N, 64] | fp32 | the latents' tokens, row by row, then each picture's |
| `image_positions` | [N, 3] | int64 | (picture, row, column): the edit 0, the i-th picture i, rows and columns centred on 0 |
| `drawn` | [1] | int64 | how many of the tokens are the edit's; the rest are modulated as at timestep zero |
| `prompt` | [1, T, 3584] | fp32 | the prompt encoder's rows from the 65th |
| `prompt_positions` | [T, 3] | int64 | (m + i, m + i, m + i), m the largest half side of any picture |
| `timestep` | [1] | fp32 | the step's noise level, sigma |

and gives `velocity` [1, N, 64] (fp32); the edit's are the first. Forty Euler steps from standard normal noise, at the
scheduler's noise levels — 1 down to 1/40, shifted exponentially by the edit's size and stretched to end at 0.02 — each
asking the denoiser with the prompt and without it, `v = v_without + 4 (v_with − v_without)`, each token's `v` then
brought back to the length `v_with` has. The decoder takes the finished `tokens` [H/16, W/16, 64] and gives `rgb`
[H, W, 3] (uint8). Under a mask, as diffusers' inpaint pipeline does, the first picture's tokens are put back after each
step where the mask leaves it, noised to the level reached with the noise the edit started from.

## Licence

Apache-2.0, as the original model. The graphs are a conversion of its weights — the scaling described above aside,
which the graph undoes — and the recipe here is Apache-2.0 too.
