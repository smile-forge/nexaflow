"""
What the build and the reference share: Depth Anything V2 Small built as the makers' run.py builds it, its weights read as
the plain state dict they are, and a picture read and prepared as the makers' infer_image prepares it.
"""
from pathlib import Path

import cv2
import numpy as np
import torch

# run.py's model_configs['vits'].
SMALL = {"encoder": "vits", "features": 64, "out_channels": [48, 96, 192, 384]}

# infer_image's default: the shorter side is brought to this, both sides to multiples of the ViT's 14-pixel patch.
INPUT_SIZE = 518


def load_model(weights: Path):
    """DepthAnythingV2 as run.py builds the Small one, its weights from the checkpoint — a state dict of tensors alone,
    so it is read with weights_only and nothing it names is imported or run — in eval mode."""
    from depth_anything_v2.dpt import DepthAnythingV2
    model = DepthAnythingV2(**SMALL)
    model.load_state_dict(torch.load(weights, map_location="cpu", weights_only=True), strict=True)
    return model.eval()


def picture(path: Path):
    """A picture as run.py reads it: cv2.imread, BGR, 8 bits."""
    bgr = cv2.imread(str(path))
    if bgr is None:
        raise FileNotFoundError(path)
    return bgr


def prepared(model, bgr):
    """The network's input as infer_image makes it — RGB over 255, cubic-resized to the lower bound of 518 at multiples of
    14, normalised by ImageNet's mean and deviation, planar, float32 — and the picture's size."""
    image, (h, w) = model.image2tensor(bgr, INPUT_SIZE)
    return image.cpu().numpy(), (h, w)
