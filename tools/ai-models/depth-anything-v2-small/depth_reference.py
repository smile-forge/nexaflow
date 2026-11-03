"""
The reference the AI host's scene depth is held to: the makers' own infer_image (run.py's way, input size 518) on their
sample pictures, on CUDA in fp32, the model as they wrote it:

  the picture read by cv2.imread, made RGB over 255, cubic-resized by OpenCV to the lower bound of 518 at multiples of
  14, normalised by ImageNet's mean and deviation, through the model, and the disparity brought to the picture's size by
  PyTorch's bilinear interpolation with align_corners.

  <out>/<name>.rgba          the picture, RGBA, 8 bits
  <out>/<name>.input         what the network was given: float32, planar RGB at the working size
  <out>/<name>.disparity     what it gave: float32 at the working size
  <out>/<name>.depth         infer_image's result: float32 at the picture's size
  <out>/reference.json       each name's size and working size

usage: depth_reference.py <weights .pth> <upstream repository> <out folder> <picture>...
"""
import json, sys
from pathlib import Path

import cv2
import numpy as np
import torch
import torch.nn.functional as F

WEIGHTS, UPSTREAM, OUT = (Path(a) for a in sys.argv[1:4])
PICTURES = [Path(a) for a in sys.argv[4:]]
sys.path.insert(0, str(UPSTREAM))
sys.path.insert(0, str(Path(__file__).parent))
from depth_common import INPUT_SIZE, load_model, picture  # noqa: E402

torch.backends.cudnn.allow_tf32 = False
torch.backends.cuda.matmul.allow_tf32 = False


def main():
    model = load_model(WEIGHTS).cuda()
    OUT.mkdir(parents=True, exist_ok=True)
    index = {}
    for path in PICTURES:
        bgr = picture(path)
        h, w = bgr.shape[:2]
        # infer_image, a step at a time, so what the network was given and gave is kept beside its result.
        image, _ = model.image2tensor(bgr, INPUT_SIZE)
        with torch.no_grad():
            disparity = model.forward(image)
            depth = F.interpolate(disparity[:, None], (h, w), mode="bilinear", align_corners=True)[0, 0]
            whole = model.infer_image(bgr, INPUT_SIZE)
        assert np.array_equal(depth.cpu().numpy(), whole), "infer_image is the steps it is made of"
        name = path.stem
        rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
        np.concatenate([rgb, np.full((h, w, 1), 255, np.uint8)], axis=2).tofile(OUT / f"{name}.rgba")
        image[0].cpu().numpy().astype(np.float32).tofile(OUT / f"{name}.input")
        disparity[0].cpu().numpy().astype(np.float32).tofile(OUT / f"{name}.disparity")
        whole.astype(np.float32).tofile(OUT / f"{name}.depth")
        index[name] = {"width": w, "height": h, "workingWidth": int(image.shape[3]), "workingHeight": int(image.shape[2])}
        print(f"{name}: {w}x{h}, worked at {image.shape[3]}x{image.shape[2]}, disparity {whole.min():.3f} to {whole.max():.3f}")
    (OUT / "reference.json").write_text(json.dumps(index, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
