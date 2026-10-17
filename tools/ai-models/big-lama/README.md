---
license: apache-2.0
library_name: onnxruntime
pipeline_tag: image-to-image
tags:
  - onnx
  - onnxruntime
  - lama
  - inpainting
  - object-removal
base_model: smartywu/big-lama
---

# big-lama for ONNX Runtime

LaMa's big-lama — Resolution-robust Large Mask Inpainting with Fourier Convolutions — as one ONNX graph for ONNX Runtime
on CPU or CUDA: a picture and a mask in, the picture back with what the mask covers filled from what is around it.
Scratches, dust, wires, a person or a sign rubbed out of a photo. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host runs it from C# for its image editor's eraser;
usable by anything that runs ONNX Runtime 1.30 or later.

| Folder | What | From | Size |
|---|---|---|---|
| `inpainter/` | big-lama, fp32 | [advimman/lama](https://github.com/advimman/lama) at 786f593, weights from [smartywu/big-lama](https://huggingface.co/smartywu/big-lama) as LaMa's README names | 209 MB |

It takes `rgb` `[1, 3, H, W]` from 0 to 1 and `mask` `[1, 1, H, W]`, 1 where the picture is filled and 0 where it is
kept, H and W multiples of 8 from 64 to 4096, and gives `inpainted` `[1, 3, H, W]` from 0 to 1: the picture, with the
generator's prediction inside the mask — LaMa's predict around its generator, `out_key: inpainted`.

## How it was built

`upstream.py` fetches LaMa at a pinned commit and keeps only the generator's modules; `Build-Lama.ps1` downloads the
weights and checks them against their hash, and reads the checkpoint with an unpickler that keeps tensors alone, so
nothing it names is imported or run; `build_lama.py` traces the generator with `torch.onnx.export` and holds the graph
to the original in fp32, on CPU and on CUDA.

The graph differs from the original in one place: **its Fourier units take their FFTs as matrix products**
(`fourier_matmul.py`). Exported as it is, each unit becomes ONNX `DFT` operators, which ONNX Runtime's CUDA provider does
not implement and its CPU provider loops over in O(n²) for any length that is not a power of two — over two minutes for
a 1800×1200 picture. Written as products with cosine and sine matrices built from the input's size, the same sums at
the same `ortho` scale run as any other layer on every provider. The generator with products agrees with the generator
with `torch.fft` to 4×10⁻⁶ — a thousandth of an 8-bit level — and the graph with the original to 8×10⁻⁵, on CPU and CUDA.

`lama_reference.py` runs the makers' own predict — the dataset's reading, symmetric padding to a multiple of 8, the
mask taken where it is over 0, the result clipped and truncated to 8 bits — on SAM 3's sample pictures with a drawn
mask each, which is what a port is held to. Nexaflow's C# eraser, through this graph, fills the same pixels as that
predict to 58–62 dB PSNR on a GPU and 85 dB on a CPU, and leaves every other pixel as it was.

## Using it

As LaMa's predict: read the picture as RGB over 255 and the mask as grey over 255, taken as 1 where it is over 0; pad
both at the right and bottom to a multiple of 8 by symmetric mirroring; run the graph; cut the padding off; make each
value 8 bits by clipping its product with 255 and truncating.

The generator was trained on 256-pixel crops and holds up to about two thousand pixels a side. Nexaflow works round
each part of a mask with as much picture again on every side, at least 512 pixels each way, and brings a region larger
than 1536 pixels square down to that before the graph — by an averaging resize — and its fill back up after, by
Lanczos. At 1536 square the graph peaks at 4 GB on a GPU and takes 0.1 s on an RTX PRO 6000, 4.6 s on 16 CPU threads.

## Licence

big-lama's code and weights are Apache-2.0, a copy of which is `LICENSE`; so is the recipe here.

> Suvorov, Logacheva, Mashikhin, Remizova, Ashukha, Silvestrov, Kong, Goka, Park, Lempitsky. *Resolution-robust Large
> Mask Inpainting with Fourier Convolutions.* WACV 2022.
