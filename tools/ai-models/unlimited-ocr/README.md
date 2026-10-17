---
license: mit
library_name: llama.cpp
pipeline_tag: image-text-to-text
base_model: baidu/Unlimited-OCR
tags:
  - gguf
  - llama.cpp
  - ocr
  - document-parsing
  - onnx
---

# Unlimited-OCR for llama.cpp, with a text finder

Baidu's [Unlimited-OCR](https://huggingface.co/baidu/Unlimited-OCR) as GGUF for llama.cpp, which reads a page — prose,
columns, tables, formulas, figures and their captions — into its text in reading order, each block marked with what it
is and where it stands; and PaddleOCR's [PP-OCRv5 mobile text detector](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det)
as ONNX, which says in a few tens of milliseconds on a CPU whether, and where, a picture holds writing. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host runs both from C# to turn pictures and scanned PDF
pages into Markdown.

| File | What | Precision | Size |
|---|---|---|---|
| `reader/unlimited-ocr-q8_0.gguf` | The language model: DeepSeek-V2 mixture of experts, 3B parameters, 0.5B active | 8-bit | 3.1 GB |
| `reader/unlimited-ocr-mmproj-f16.gguf` | The vision encoder (SAM ViT-B and CLIP-L) and its projector | fp16 | 0.8 GB |
| `text-finder/pp-ocrv5-mobile-det.onnx` | The text detector: a probability of text at every pixel | fp32 | 4.8 MB |

## How it was built

`Build-PageReading.ps1` fetches the makers' weights at pinned revisions, checked by hash, and llama.cpp's converter at
the commit of release b11606, whose own quantizer makes the 8-bit build. On the sample pages a 4-bit build read the
same, but in llama.cpp's own tests 4- and 5-bit builds gave nothing back for a page of three lines. `build_text_finder.py`
converts the detector with paddle2onnx and holds it to Paddle's own predictor: the probabilities agree to 1e-4.

`page_reference.py` runs the makers' transformers code on the sample pages — pages of the Unlimited-OCR paper, the
paper's text set in small print on one page, and in two scanned columns — and `text_finder_reference.py` runs Paddle's
detector on them; that is what a port is held to.

## Using it

**Reading a page.** Load the language model with the projector (llama.cpp's `mtmd`), and give it the page picture and
`document parsing.` — the start token, the picture, the words, no roles: the GGUF carries the makers' own chat template
(`chat_template.jinja`) after the start token their code puts first, in place of the converter's, which prints a turn
given as parts as a list. Decode greedily, as the makers do, and forbid any token that would repeat a 35-token run from
the last 128. The block markers are special tokens: keep them when turning tokens into text.

The decoder was trained with **reference sliding-window attention**: every token sees the whole prompt — the picture and
the instruction — and only the last 128 tokens written before it. llama.cpp runs its attention over everything written,
which reads a short page the same, but past about 1,500 tokens it loses its place and reads passages again. Keep the
window yourself: before decoding the token written n-th (from 0), once n reaches 128, remove the cell of the token
written (n − 128)-th from the KV cache (`llama_memory_seq_rm`) — positions stay as they were, so this is the makers'
ring buffer exactly. With it, a page of 3,100 tokens reads as the makers' code reads it. The cache then holds the page
and 128 tokens at most, so 8,192 cells serve any page; let positions run on past them, to the 32,768 the model was trained
to. On an RTX PRO 6000 a dense page reads in 2–5 s at about 850 tokens a second, peaking at 6.8 GB with 8,192 cells; on a
32-thread CPU it takes about 30 s.

The answer is one block a line:

    <|det|>kind [x0, y0, x1, y1]<|/det|>content

with the box in thousandths of the page's width and height, and kinds `title`, `text`, `equation`, `table`, `image`,
`image_caption`, `page_footnote`, `header` and `page_number`. Text holds LaTeX between `\(` and `\)`; an equation is
LaTeX between `\[` and `\]`; a table is HTML; an image has no content — its box is where the figure is.

**Finding text.** Give the detector the picture as PaddleOCR prepares it — BGR, the long side brought to 960 with each
side a multiple of 32, scaled to 0-1 and normalised by ImageNet's mean (0.485, 0.456, 0.406) and deviation (0.229, 0.224,
0.225), planar: `x` `[1, 3, H, W]`. It gives the probability of text at every pixel, `[1, 1, H, W]`. Join the pixels
above 0.3 into regions, keep those whose box averages 0.6 or more, and grow each by its area × 1.5 / perimeter on every
side, as the detector was trained (`text_finding.py`).

## Licences

Unlimited-OCR is Baidu's, under the MIT licence ([LICENSE](LICENSE)); PP-OCRv5 is PaddlePaddle's, under the Apache
License 2.0 ([LICENSE-PaddleOCR](LICENSE-PaddleOCR)). The files here are conversions of their weights under those
licences. The recipe is Apache-2.0.
