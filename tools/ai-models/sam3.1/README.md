---
license: other
license_name: sam-license
license_link: LICENSE
library_name: onnxruntime
pipeline_tag: mask-generation
tags:
  - onnx
  - onnxruntime
  - sam3
  - sam3.1
  - segmentation
  - background-removal
---

# SAM 3.1 for ONNX Runtime

Meta's [SAM 3.1](https://github.com/facebookresearch/sam3) as ONNX graphs for ONNX Runtime on CUDA or CPU: a thing in a
picture picked out by a click — more clicks to add to it or take from it, or a box drawn round it — or everything a
phrase names ("wheel", "child in a red vest"), optionally with example boxes of what is and is not meant. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host runs them from C# to select and cut things out of
pictures and paused video; usable by anything that runs ONNX Runtime 1.31 or later (opset 23).

| Folder | What | Precision | Size |
|---|---|---|---|
| `image-encoder/` | The picture, once: the features both the clicks and the phrases read | fp16 | 929 MB |
| `click-decoder/` | Clicks and boxes to a mask | fp32 | 17 MB |
| `text-encoder/` | A phrase | fp16 | 707 MB |
| `exemplar-encoder/` | Example boxes: "like this", "not like this" | fp32 | 13 MB |
| `finder/` | Everything a phrase and its examples name: scores, boxes, masks | fp16 (autocast) | 123 MB |
| `tokenizer/` | The makers' byte-pair vocabulary (`bpe_simple_vocab_16e6.txt.gz`) | | 1.4 MB |

On an RTX PRO 6000 the picture encodes in 45 ms with a peak of 2.5 GB, a click decodes in 4 ms (11 ms on a CPU), a
phrase encodes in 6 ms and is found in 27 ms with a peak of 1.6 GB. On a 32-thread CPU the picture takes about 7 s.

## How it was built

`Build-Sam.ps1` fetches the makers' code at a pinned commit and their `sam3.1_multiplex.pt` — from a public copy, byte
for byte the file in `facebook/sam3.1`, checked by its hash — and `build_sam.py` traces each path. Every graph is held
to the makers' own still-picture paths in fp32 on their own sample pictures, on CUDA and on the CPU: a click's mask
matches theirs to a mean IoU above 0.99, and a phrase finds the same things with the same scores (to 0.01) and masks
that match to a mean IoU above 0.99. The graphs differ from the makers' code only where they had to:

- **Half precision where the weight and the memory are.** The trunk's attention over 5,184 tokens peaks at 12.9 GB in
  fp32 under ONNX Runtime and at 2.5 GB in fp16; the makers themselves run SAM 3.1 in bf16, and the fp16 graphs are
  nearer the fp32 network than the makers' bf16 run is. The image and text encoders are converted outright; the finder
  runs under fp16 autocast, the makers' own low-precision path, since its decoder makes fp32 sine embeddings as it goes.
- **The click path as a first frame takes it.** SAM 3.1's interactive head is reached through its video predictor; the
  graphs take the path a first conditioning frame with clicks goes — the interactive head, the interactive decoder's
  high-resolution convolutions, the no-memory embedding, the interactive prompt encoder and mask decoder.
- **Example boxes encoded on their own.** The geometry encoder concatenates padded, possibly empty, sequences of points
  and boxes; `exemplar-encoder` encodes the boxes, and `finder` runs the rest of the geometry encoder on them as a plain
  concatenation — what it is with no points and nothing padded — so no graph sees an empty sequence it cannot run.
- **One picture for both paths.** SAM 3.1's predictor prepares a frame with PIL's resize for clicks and phrases alike;
  the makers' still-picture processor resizes an 8-bit tensor with torchvision instead, which moves a phrase's scores by
  up to 0.17. The graphs, and the reference, follow SAM 3.1's own preparation, so one encoding serves both paths.
- **Attention masks spelled out.** ONNX Runtime's Attention wants a mask's query axis written out where the opset allows
  it to broadcast; each such mask is expanded to its query length in the graph.

`sam_reference.py` runs the makers' paths on their sample pictures — each click case's mask, IoUs and object score, each
phrase's scores, boxes and masks, and the tokens of phrases chosen to test a tokenizer's cleaning — which is what a port
is held to.

## Using it

**The picture.** Squash it to 1008 × 1008 with PIL's bilinear resize — its triangle filter widened by the reduction, so
a large picture is averaged rather than sampled — then RGB from -1 to 1, planar:
`image` `[1, 3, 1008, 1008]`. `image-encoder` gives `embedding`, `high0`, `high1` for clicks and `fpn0`, `fpn1`, `fpn2`
for phrases; keep them while the picture is worked on.

**Clicks.** `points` `[1, N, 2]` in the 1008-square picture's pixels, `labels` `[1, N]` as floats: 1 for a click on the
thing, 0 for a click off it, 2 and 3 for a box's top-left and bottom-right corners. `click-decoder` gives four mask logits
at 288 × 288, their predicted IoUs and an object score. For one point take the best of masks 1-3 by IoU, for several
take mask 0; if the object score is not above 0, there is no mask. Bring it to 1008 × 1008 and then to the picture's
size, bilinear without aligned corners each time; the thing is where the logit is above 0.

**Phrases.** Clean the phrase as the makers' tokenizer does — `ftfy.fix_text`, HTML entities unescaped twice, runs of
whitespace collapsed, lower case — and encode it with the byte-pair vocabulary between the start (49406) and end (49407)
tokens, cut to 32 and padded with 0. `text-encoder` gives `text` and `text_pad`. Example boxes, if any — `boxes`
`[B, 1, 4]` as centre x, centre y, width, height from 0 to 1, `box_labels` `[B, 1]` 1 or 0 — go through
`exemplar-encoder` with `fpn2` to `box_tokens`; with none, `box_tokens` is `[0, 1, 256]`. `finder` gives 200 queries;
each one's score is `sigmoid(logits) × sigmoid(presence)`, kept above 0.5. A kept query's box is centre x, centre y,
width, height from 0 to 1 of the picture; its mask logits come at 288 × 288 — bring them to the picture's size,
bilinear without aligned corners, the thing where the logit is above 0.

## Licences

SAM 3.1 is Meta's, under the [SAM License](LICENSE), a copy of which comes with these graphs as it requires; the graphs
are conversions of its weights under that licence. The recipe here is Apache-2.0.
