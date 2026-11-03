"""What OpenCV and PyTorch make of small pictures — the two resizes Depth Anything V2's infer_image goes through — for the C#
ports in Nexaflow.IO.Common's PictureResampling to be held to without either:

  cubic    cv2.resize(INTER_CUBIC) of a float64 RGB picture, as infer_image brings the picture over 255 to the size the
           network reads: the cubic convolution with A = -0.75, the edge repeated, nothing averaged when it shrinks
  aligned  torch.nn.functional.interpolate(mode="bilinear", align_corners=True) of a float plane, as infer_image brings
           the disparity to the picture's size

usage: resampling_reference.py <out.json>
"""
import json, sys

import cv2
import numpy as np
import torch
import torch.nn.functional as F

rng = np.random.default_rng(518)
out = {"cubic": [], "aligned": []}

for (w, h), (nw, nh) in [((37, 23), (16, 11)), ((9, 7), (28, 14)), ((30, 5), (12, 14)), ((64, 48), (42, 28)), ((13, 13), (13, 6))]:
    src = rng.integers(0, 256, (h, w, 3), dtype=np.uint8).astype(np.float64) / 255.0
    resized = cv2.resize(src, (nw, nh), interpolation=cv2.INTER_CUBIC)
    out["cubic"].append({"width": w, "height": h, "pixels": src.flatten().tolist(),
                         "into": {"width": nw, "height": nh, "pixels": resized.flatten().tolist()}})

for (w, h), (nw, nh) in [((13, 11), (40, 36)), ((37, 26), (10, 7)), ((7, 9), (7, 23)), ((5, 1), (12, 1))]:
    plane = (rng.standard_normal((h, w)) * 4).astype(np.float32)
    resized = F.interpolate(torch.from_numpy(plane)[None, None], size=(nh, nw), mode="bilinear", align_corners=True)[0, 0]
    out["aligned"].append({"width": w, "height": h, "plane": plane.flatten().tolist(),
                           "into": {"width": nw, "height": nh, "plane": resized.numpy().flatten().tolist()}})

json.dump(out, open(sys.argv[1], "w"), separators=(",", ":"))
