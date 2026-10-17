"""
Builds big-lama's ONNX graph from the upstream PyTorch generator, holding it to its original as it goes:

  inpainter/inpainter.onnx   rgb [1,3,H,W] 0..1, mask [1,1,H,W] 1 where filled, H and W multiples of 8
                             -> inpainted [1,3,H,W] 0..1: the picture, the generator's prediction inside the mask

The graph is LaMa's predict around the generator: the picture blacked out under the mask, the mask beside it, the
generator, and the prediction laid in the mask over the picture (out_key 'inpainted'). Its Fourier units take their FFTs
as matrix products (fourier_matmul.py), so the graph needs no DFT operator.

usage: build_lama.py <big-lama folder> <upstream sources (shim)> <out folder> <picture>...

Each picture is checked with a drawn mask: the generator with matrix products against the generator with torch.fft, then
the graph on CPU and CUDA against the generator with torch.fft, each on the CPU in fp32.
"""
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch
from torch import nn
from torch.nn import functional as F

MODEL, SHIM, OUT = (Path(a) for a in sys.argv[1:4])
PICTURES = [Path(a) for a in sys.argv[4:]]
sys.path.insert(0, str(SHIM))
sys.path.insert(0, str(Path(__file__).parent))
from lama_common import drawn_mask, load_generator, picture  # noqa: E402
from fourier_matmul import fourier_unit_forward  # noqa: E402
from saicinpainting.training.modules.ffc import FourierUnit  # noqa: E402

torch.backends.cudnn.allow_tf32 = False
torch.backends.cuda.matmul.allow_tf32 = False


class Inpainter(nn.Module):
    def __init__(self, generator):
        super().__init__()
        self.generator = generator

    def forward(self, rgb, mask):
        predicted = self.generator(torch.cat([rgb * (1 - mask), mask], 1))
        return mask * predicted + (1 - mask) * rgb


def padded(rgb, mask):
    """As the makers' dataset pads: to a multiple of 8 at the right and bottom, mirrored symmetrically."""
    h, w = mask.shape
    ph, pw = (-h) % 8, (-w) % 8
    image = np.pad(rgb.astype(np.float32).transpose(2, 0, 1) / 255, ((0, 0), (0, ph), (0, pw)), mode="symmetric")
    hole = np.pad((mask > 0).astype(np.float32)[None], ((0, 0), (0, ph), (0, pw)), mode="symmetric")
    return image[None], hole[None]


def main():
    original_forward = FourierUnit.forward
    model = Inpainter(load_generator(MODEL))
    cases = []
    for i, path in enumerate(PICTURES):
        rgb = picture(path)
        cases.append((path.name, *padded(rgb, drawn_mask((rgb.shape[1], rgb.shape[0]), i))))

    originals = []
    with torch.no_grad():
        for name, image, hole in cases:
            FourierUnit.forward = original_forward
            reference = model(torch.from_numpy(image), torch.from_numpy(hole)).numpy()
            FourierUnit.forward = fourier_unit_forward
            products = model(torch.from_numpy(image), torch.from_numpy(hole)).numpy()
            diff = np.abs(products - reference).max()
            print(f"{name}: matrix-product FFT against torch.fft, largest difference {diff:.2e}")
            assert diff < 1e-4, name
            originals.append(reference)

    path = OUT / "inpainter" / "inpainter.onnx"
    path.parent.mkdir(parents=True, exist_ok=True)
    height, width = torch.export.Dim("height", min=8, max=512), torch.export.Dim("width", min=8, max=512)
    sample = (torch.from_numpy(cases[0][1]), torch.from_numpy(cases[0][2]))
    with torch.no_grad():
        program = torch.onnx.export(model, sample, dynamo=True, input_names=["rgb", "mask"], output_names=["inpainted"],
                                    dynamic_shapes={"rgb": {2: 8 * height, 3: 8 * width}, "mask": {2: 8 * height, 3: 8 * width}},
                                    optimize=True)
    program.save(str(path), external_data=False)
    print(f"{path}: {path.stat().st_size / 2**20:.1f} MB")

    for providers in (["CPUExecutionProvider"], [("CUDAExecutionProvider", {"use_tf32": "0"})]):
        session = ort.InferenceSession(str(path), providers=providers)
        for (name, image, hole), reference in zip(cases, originals):
            out = session.run(None, {"rgb": image, "mask": hole})[0]
            diff = np.abs(out - reference).max()
            print(f"{name} on {providers[0] if isinstance(providers[0], str) else providers[0][0]}: graph against the original, largest difference {diff:.2e}")
            assert diff < 2e-3, name


if __name__ == "__main__":
    main()
