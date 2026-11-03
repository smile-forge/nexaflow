---
license: apache-2.0
library_name: onnxruntime
pipeline_tag: depth-estimation
tags:
  - onnx
  - onnxruntime
  - depth-anything
  - depth-estimation
  - monocular-depth
base_model: depth-anything/Depth-Anything-V2-Small
---

# Depth Anything V2 Small for ONNX Runtime

Depth Anything V2 Small — monocular relative depth from one picture — as one ONNX graph for ONNX Runtime on CPU or CUDA
that takes any picture size. Built for [Nexaflow](https://github.com/smile-forge/nexaflow), whose AI host runs it from C#
for its image editor's lens blur, depth haze and selection by distance; usable by anything that runs ONNX Runtime 1.30 or
later.

| Folder | What | From | Size |
|---|---|---|---|
| `estimator/` | Depth Anything V2 Small, fp32 | [DepthAnything/Depth-Anything-V2](https://github.com/DepthAnything/Depth-Anything-V2) at a561b84, weights from [depth-anything/Depth-Anything-V2-Small](https://huggingface.co/depth-anything/Depth-Anything-V2-Small) at 03876f8 | 96 MB |

Only the Small model is Apache-2.0; the makers' Base, Large and Giant models are CC-BY-NC-4.0, and none of them is here.

It takes `pixels` `[1, 3, H, W]`, the picture as the makers' `infer_image` prepares it (below), H and W multiples of 14, and
gives `disparity` `[1, H, W]`: the model's relative inverse depth, larger nearer, its scale and offset its own — the
makers' forward, ReLU and all.

## How it was built

`upstream.py` fetches the makers' code at a pinned commit; `Build-SceneDepth.ps1` downloads the weights and checks them
against their hash, and reads the checkpoint — a plain state dict — with `weights_only`, so nothing it names runs;
`build_depth.py` traces the model with `torch.onnx.export` and holds the graph to the original in fp32, on CPU and on CUDA.

The graph is the makers' forward with two sizes left symbolic, so one graph takes every picture:

- **The positional embedding by matrix products** (`position_matmul.py`). The makers resize DINOv2's 37 × 37 grid of
  positional embeddings to the picture's grid of patches by PyTorch's bicubic interpolation at a scale factor of
  (n + 0.1) / 37, made from the input's shape — a float a traced graph cannot carry into ONNX's `Resize`. Written as
  products with cubic weight matrices built from the size in the graph — PyTorch's source positions, its clamped
  neighbours and its cubic convolution with A = −0.75 — the same sums run at any size, and a square 518-pixel picture keeps
  the trained grid untouched, as the makers' code does.
- **The head's last resize to the input's size** (`head_size.py`), which the makers write with `int()` round each side,
  so a trace fixes it at the sample's size.

The model with both agrees with the makers' own to 1.2×10⁻⁴, and the graph with the original to 3.1×10⁻⁴ on CPU and CUDA
— under 0.01 % of the disparity's range — on the makers' sample pictures at three sizes and on a square one.

`depth_reference.py` runs the makers' own `infer_image` on their sample pictures, keeping what the network was given, what
it gave and the result at the picture's size: what a port is held to. Nexaflow's C# host prepares the same input to the
value and, through this graph, gives the same disparity to 0.002 % of its range on a CPU, and to 0.7 % on a GPU, where ONNX
Runtime's CUDA provider takes its matrix products in TF32. `resampling_reference.py` records what OpenCV and PyTorch make
of small pictures, for the two resizes' ports.

## Using it

As the makers' `infer_image` with its default input size of 518: read the picture as RGB over 255, in double precision;
resize it with OpenCV's `INTER_CUBIC` so that its shorter side is 518 and each side the nearest multiple of 14 (rounded up
where that falls short of 518); subtract ImageNet's mean (0.485, 0.456, 0.406) and divide by its deviation (0.229, 0.224,
0.225); lay it out planar in float32 and run the graph. Bring the disparity to the picture's size with bilinear
interpolation, corners aligned (`align_corners=True`).

Nexaflow brings a picture more than three times as long as it is wide to 1554 along its length instead, so the graph
peaks under 3 GB. A 3:2 photo (784 × 518) takes 9 ms on an RTX PRO 6000 and 0.2 s on 16 CPU threads, peaking at 1.6 GB on
the GPU; at 1554 × 518 it peaks at 2.9 GB.

## Licence

Depth Anything V2 Small's code and weights are Apache-2.0, a copy of which is `LICENSE`; so is the recipe here.

> Yang, Kang, Huang, Zhao, Xu, Feng, Zhao. *Depth Anything V2.* NeurIPS 2024.
