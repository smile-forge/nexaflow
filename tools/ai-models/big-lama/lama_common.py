"""
What the build and the reference share: big-lama's generator built from its own config and checkpoint, and the masks
the graphs are checked and the reference made on.
"""
import io, pickle, types
from pathlib import Path

import numpy as np
import torch
from omegaconf import OmegaConf
from PIL import Image, ImageDraw


class _Ignored(dict):
    """Anything in the checkpoint but tensors and their containers — PyTorch Lightning's callback state, the optimizer's
    hyper-parameters — read as nothing, so no code the checkpoint names is ever imported or run. Whatever the pickle puts
    into one — state, items, list entries — is taken and dropped."""
    def __init__(self, *args, **kwargs):
        super().__init__()

    def __setstate__(self, state):
        pass

    def append(self, item):
        pass

    def extend(self, items):
        pass


_KEPT = {("collections", "OrderedDict"), ("torch._utils", "_rebuild_tensor_v2"), ("torch._utils", "_rebuild_parameter"),
         ("torch", "Size"), ("builtins", "set"), ("builtins", "frozenset"), ("builtins", "slice")}


class _Unpickler(pickle.Unpickler):
    def find_class(self, module, name):
        if (module, name) in _KEPT or (module == "torch" and name.endswith("Storage")):
            return super().find_class(module, name)
        return _Ignored


checkpoint_pickle = types.ModuleType("checkpoint_pickle")
checkpoint_pickle.Unpickler = _Unpickler
checkpoint_pickle.load = lambda f, **kw: _Unpickler(f, **kw).load()


def load_generator(model: Path):
    """FFCResNetGenerator as big-lama's config builds it, its weights from best.ckpt's state dict, in eval mode."""
    from saicinpainting.training.modules.ffc import FFCResNetGenerator
    config = OmegaConf.load(model / "config.yaml")
    kwargs = OmegaConf.to_container(config.generator, resolve=True)
    assert kwargs.pop("kind") == "ffc_resnet"
    generator = FFCResNetGenerator(**kwargs)
    state = torch.load(model / "models" / "best.ckpt", map_location="cpu", pickle_module=checkpoint_pickle,
                       weights_only=False)["state_dict"]
    generator.load_state_dict({k[len("generator."):]: v for k, v in state.items() if k.startswith("generator.")}, strict=True)
    return generator.eval()


def drawn_mask(size, seed):
    """Scratches, dust and a blotch, as a person heals a photo with a brush — one scratch running corner to corner, so a
    part of the mask spans the picture and an eraser that works round each part takes the whole picture, as the makers'
    predict does."""
    w, h = size
    rng = np.random.default_rng(seed)
    mask = Image.new("L", size, 0)
    draw = ImageDraw.Draw(mask)
    width = max(4, min(w, h) // 90)
    for _ in range(5):
        x, y, a = rng.uniform(0, w), rng.uniform(0, h), rng.uniform(0, np.pi)
        points = [(x, y)]
        for _ in range(6):
            a += rng.normal(0, 0.3)
            step = rng.uniform(0.03, 0.08) * max(w, h)
            x, y = x + np.cos(a) * step, y + np.sin(a) * step
            points.append((x, y))
        draw.line(points, fill=255, width=width, joint="curve")
    for _ in range(6):
        x, y, r = rng.uniform(0, w), rng.uniform(0, h), rng.uniform(1, 2.5) * width
        draw.ellipse([x - r, y - r, x + r, y + r], fill=255)
    cx, cy, rx, ry = w * rng.uniform(0.3, 0.7), h * rng.uniform(0.3, 0.7), w * 0.08, h * 0.1
    draw.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=255)
    draw.line([(width, width), (w * 0.35, h * 0.55), (w * 0.7, h * 0.4), (w - width, h - width)], fill=255, width=max(2, width // 2),
              joint="curve")
    return np.asarray(mask)


def picture(path: Path):
    """A picture as the makers' predict reads it: RGB, 8 bits."""
    return np.asarray(Image.open(path).convert("RGB"))
