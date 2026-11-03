"""
Builds Depth Anything V2 Small's ONNX graph from the upstream PyTorch model, holding it to its original as it goes:

  estimator/depth-anything-v2-small.onnx   pixels [1,3,H,W] float32, the picture as infer_image prepares it, H and W
                                           multiples of 14 -> disparity [1,H,W] float32: the model's relative inverse
                                           depth, larger nearer, at the input's size (its forward, ReLU and all)

The graph is the makers' forward, two things left symbolic so it takes any size: its positional embedding brought to the
picture's grid by matrix products (position_matmul.py), and its head's last resize to the input's own size (head_size.py).

usage: build_depth.py <weights .pth> <upstream repository> <out folder> <picture>...

Each picture, and a square one, is checked as infer_image prepares it: the model with those two as written here against
the model as the makers wrote it, then the graph on CPU and CUDA against the model as the makers wrote it, each on
the CPU in fp32.
"""
import sys
from pathlib import Path

import cv2
import numpy as np
import onnx
import onnxruntime as ort
import torch
from torch import nn

WEIGHTS, UPSTREAM, OUT = (Path(a) for a in sys.argv[1:4])
PICTURES = [Path(a) for a in sys.argv[4:]]
sys.path.insert(0, str(UPSTREAM))
sys.path.insert(0, str(Path(__file__).parent))
from depth_common import load_model, picture, prepared  # noqa: E402
from position_matmul import interpolate_pos_encoding  # noqa: E402
from head_size import dpt_head_forward  # noqa: E402
from depth_anything_v2.dinov2 import DinoVisionTransformer  # noqa: E402
from depth_anything_v2.dpt import DPTHead  # noqa: E402

torch.backends.cudnn.allow_tf32 = False
torch.backends.cuda.matmul.allow_tf32 = False


class Estimator(nn.Module):
    def __init__(self, model):
        super().__init__()
        self.model = model

    def forward(self, pixels):
        return self.model(pixels)


def main():
    original_encoding, original_head = DinoVisionTransformer.interpolate_pos_encoding, DPTHead.forward
    model = load_model(WEIGHTS)
    estimator = Estimator(model).eval()

    cases = []
    for path in PICTURES:
        image, _ = prepared(model, picture(path))
        cases.append((path.name, image))
    # A square picture: the makers' grid is then the trained one, untouched.
    square = cv2.resize(picture(PICTURES[0]), (700, 700), interpolation=cv2.INTER_AREA)
    cases.append(("square", prepared(model, square)[0]))

    originals = []
    with torch.no_grad():
        for name, image in cases:
            DinoVisionTransformer.interpolate_pos_encoding, DPTHead.forward = original_encoding, original_head
            reference = estimator(torch.from_numpy(image)).numpy()
            DinoVisionTransformer.interpolate_pos_encoding, DPTHead.forward = interpolate_pos_encoding, dpt_head_forward
            products = estimator(torch.from_numpy(image)).numpy()
            diff = np.abs(products - reference).max()
            print(f"{name} {image.shape[2]}x{image.shape[3]}: symbolic sizes against the makers' own, "
                  f"largest difference {diff:.2e} of a range to {reference.max():.2f}")
            assert diff < 1e-4 * max(1, reference.max()), name
            originals.append(reference)

    path = OUT / "estimator" / "depth-anything-v2-small.onnx"
    path.parent.mkdir(parents=True, exist_ok=True)
    rows, cols = torch.export.Dim("rows", min=2, max=320), torch.export.Dim("cols", min=2, max=320)
    sample = (torch.from_numpy(cases[0][1]),)
    with torch.no_grad():
        program = torch.onnx.export(estimator, sample, dynamo=True, input_names=["pixels"], output_names=["disparity"],
                                    dynamic_shapes={"pixels": {2: 14 * rows, 3: 14 * cols}}, optimize=True)
    program.save(str(path), external_data=False)
    shape = [d.dim_param or d.dim_value for d in onnx.load(str(path)).graph.output[0].type.tensor_type.shape.dim]
    assert all(isinstance(d, str) for d in shape[1:]), f"the graph's output is fixed at {shape}: a size was traced as a constant"
    print(f"{path}: {path.stat().st_size / 2**20:.1f} MB")

    for providers in (["CPUExecutionProvider"], [("CUDAExecutionProvider", {"use_tf32": "0"})]):
        session = ort.InferenceSession(str(path), providers=providers)
        provider = providers[0] if isinstance(providers[0], str) else providers[0][0]
        for (name, image), reference in zip(cases, originals):
            out = session.run(None, {"pixels": image})[0]
            diff = np.abs(out - reference).max()
            print(f"{name} on {provider}: graph against the original, largest difference {diff:.2e} of a range to {reference.max():.2f}")
            assert diff < 1e-3 * max(1, reference.max()), name


if __name__ == "__main__":
    main()
