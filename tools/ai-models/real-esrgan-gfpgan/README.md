---
license: other
license_name: per-model
library_name: onnxruntime
pipeline_tag: image-to-image
tags:
  - onnx
  - onnxruntime
  - real-esrgan
  - gfpgan
  - super-resolution
  - face-restoration
---

# Real-ESRGAN, GFPGAN, RetinaFace and ParseNet for ONNX Runtime

The models behind Real-ESRGAN's upscaling and GFPGAN's face restoration, as ONNX graphs for ONNX Runtime on CPU or
CUDA: a picture made twice or four times its size — or kept at its size and cleaned — its JPEG blocks, noise and
softness taken out, and each face in it found, remade and pasted back. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host runs them from C# for its picture tools; usable by
anything that runs ONNX Runtime 1.30 or later.

| Folder | What | From | Size |
|---|---|---|---|
| `upscaler-x4/` | Real-ESRGAN x4plus: a picture four times the size | [xinntao/Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN) v0.1.0 | 69 MB |
| `upscaler-x2/` | Real-ESRGAN x2plus: twice the size | Real-ESRGAN v0.2.1 | 69 MB |
| `face-detector/` | RetinaFace, ResNet-50: faces and five points on each | [xinntao/facexlib](https://github.com/xinntao/facexlib) v0.1.0 | 110 MB |
| `face-restorer/` | GFPGAN v1.4: a face remade | [TencentARC/GFPGAN](https://github.com/TencentARC/GFPGAN) v1.3.0 | 341 MB |
| `face-parser/` | ParseNet: which of a remade face is pasted back | facexlib v0.2.2 | 85 MB |

Every graph is fp32 — fast enough on a GPU (a 512-pixel tile through the 2x upscaler in 47 ms on an RTX PRO 6000,
GFPGAN in 13 ms) that half precision is not worth a second build — and takes the same thing: planar RGB from 0 to 1,
`rgb` `[1, 3, height, width]`. Each does its model's own normalisation inside.

## How it was built

`upstream.py` fetches Real-ESRGAN, GFPGAN, facexlib and BasicSR at pinned commits; `Build-PictureTools.ps1` downloads
the weights from their makers' releases and checks each against its hash; `build_picture_tools.py` traces each model
with `torch.onnx.export` and holds the graph to the original in fp32, on CPU and on CUDA: the upscalers and GFPGAN agree
to 2×10⁻⁵ of their largest output, RetinaFace to 2.4×10⁻⁴, and ParseNet chooses every pixel's class as the original does.
The graphs differ from the originals only where they had to:

- **GFPGAN with the noise it was trained with.** StyleGAN2, GFPGAN's decoder, adds fresh noise at each layer on every
  call by default. The weights carry the noise they were trained with, and the graph uses it, so a face is remade the
  same way every time — as GFPGAN's own code does with `randomize_noise=False`. Its encoder's last feature map is
  flattened with `reshape` where GFPGAN views it, which the exporter cannot trace.
- **The detector's outputs are raw.** RetinaFace gives, for each of its priors, the regressions of a box and five points
  against the prior, and the face score alone of its two classes; decoding them against the priors, thresholding and
  suppressing overlaps are the caller's, as in facexlib.
- **The parser's output is the mask.** ParseNet's 19 classes are reduced to the mask GFPGAN pastes through — 1 over the
  face's own parts (skin, brows, eyes, glasses, ears, nose, lips), 0 over background, neck, clothes, hair and hat —
  before the blur the caller gives it.

`picture_reference.py` runs the makers' own pipelines — `RealESRGANer` and `GFPGANer` — on their own sample pictures,
which is what a port is held to. Nexaflow's C# pipeline, through these graphs, makes the same pictures as those
pipelines to within 54–66 dB PSNR at every scale, faces repaired or not, and finds the same faces.

## Using it

**Upscaling**, as `RealESRGANer.enhance`: pad the picture by reflection at the right and bottom to even sides for the
2x model; cut it into tiles, each reading a margin of its neighbours (Nexaflow: 512 pixels through the 2x model, 256 through the 4x — which works at
the tile's full size and peaks near 8 GB at 512 — each with a 24-pixel margin), and keep
each tile's own square of what comes back; round each value to 8 bits. An alpha channel goes through the model as grey.
To keep the picture at its size, make it twice the size with the 2x model and bring it back with Lanczos.

**Faces**, as `GFPGANer.enhance` with facexlib's `FaceRestoreHelper`: run `face-detector.onnx` on the picture; decode
each prior's regressions with variances 0.1 and 0.2 against priors of sizes 16 and 32, 64 and 128, 256 and 512 over steps
of 8, 16 and 32 pixels; keep scores over 0.97, drop boxes overlapping a surer one by more than 0.4, and faces whose eyes
are under 5 pixels apart. Align each face to the template
`(192.98, 239.95) (318.90, 240.19) (256.63, 314.02) (201.26, 371.41) (313.09, 371.15)` with a similarity fit and warp it
into a 512-pixel crop (border RGB 132, 133, 135); remake it with `face-restorer.onnx`; take `face-parser.onnx`'s mask of
the remade face, blur it twice with a 101-pixel Gaussian of sigma 11, clear its outer 10 pixels, and paste the face back
through it with the inverse warp — scaled to the upscaled picture, moved by half a scaled pixel.

## Licences

The graphs are conversions of their makers' weights, under their makers' licences; the recipe here is Apache-2.0.

| Model | Code and weights |
|---|---|
| Real-ESRGAN x4plus, x2plus | BSD-3-Clause |
| GFPGAN v1.4 | Apache-2.0 (the clean architecture: no NVIDIA StyleGAN2 CUDA ops) |
| RetinaFace (facexlib) | MIT |
| ParseNet (facexlib) | MIT |
