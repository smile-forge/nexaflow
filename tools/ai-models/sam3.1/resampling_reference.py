"""What PIL and PyTorch make of small pictures — the two resizes SAM 3.1's paths go through — for the C# ports in
Nexaflow.IO.Common's PictureResampling to be held to without either:

  pil      PIL's bilinear resize of an 8-bit RGB picture, as SAM 3.1's predictor squashes a frame to 1008 square (its
           loader's `torchvision.transforms.functional.resize` of a PIL picture): the filter widened by the reduction,
           fixed-point weights, across then down, each pass rounded to 8 bits
  torch    `torch.nn.functional.interpolate(mode="bilinear", align_corners=False)` of a float plane, as the masks are
           brought to 1008 square and then to the picture's size

usage: resampling_reference.py <out.json>
"""
import json, sys

import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image

rng = np.random.default_rng(31)
out = {"pil": [], "torch": []}

for (w, h), (nw, nh) in [((37, 23), (16, 11)), ((9, 7), (5, 4)), ((9, 7), (20, 15)), ((30, 5), (12, 14)), ((64, 48), (21, 21)),
                         ((13, 13), (13, 6))]:
    src = rng.integers(0, 256, (h, w, 3), dtype=np.uint8)
    resized = np.asarray(Image.fromarray(src, "RGB").resize((nw, nh), Image.BILINEAR))
    out["pil"].append({"width": w, "height": h, "pixels": src.flatten().tolist(),
                       "into": {"width": nw, "height": nh, "pixels": resized.flatten().tolist()}})

for (w, h), (nw, nh) in [((13, 11), (40, 36)), ((13, 11), (5, 4)), ((7, 9), (7, 23)), ((288 // 16, 288 // 16), (63, 7))]:
    plane = (rng.standard_normal((h, w)) * 8).astype(np.float32)
    resized = F.interpolate(torch.from_numpy(plane)[None, None], size=(nh, nw), mode="bilinear", align_corners=False)[0, 0]
    out["torch"].append({"width": w, "height": h, "plane": plane.flatten().tolist(),
                         "into": {"width": nw, "height": nh, "plane": resized.numpy().flatten().tolist()}})

json.dump(out, open(sys.argv[1], "w"), separators=(",", ":"))
