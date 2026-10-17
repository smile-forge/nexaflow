"""
The reference the AI host's eraser is held to: LaMa's own predict (bin/predict.py with the default prediction config)
on pictures with a drawn mask each, on CUDA in fp32 with the generator's torch.fft Fourier units:

  the picture and mask read as its dataset reads them (RGB and grey over 255; the mask taken where it is over 0), padded
  to a multiple of 8 by symmetric mirroring, the generator run on the picture blacked out under the mask with the mask
  beside it, the prediction laid in the mask over the picture ('inpainted'), the padding cut off, and each value made
  8 bits as predict does: clipped times 255, truncated.

  <out>/<name>.rgba        the picture, RGBA, 8 bits
  <out>/<name>.mask        its mask, one byte a pixel, 255 where filled
  <out>/<name>.erased.rgba what predict makes of it, RGBA, 8 bits
  <out>/reference.json     each name's width and height

usage: lama_reference.py <big-lama folder> <upstream sources (shim)> <out folder> <picture>...
"""
import json, sys
from pathlib import Path

import numpy as np
import torch

MODEL, SHIM, OUT = (Path(a) for a in sys.argv[1:4])
PICTURES = [Path(a) for a in sys.argv[4:]]
sys.path.insert(0, str(SHIM))
sys.path.insert(0, str(Path(__file__).parent))
from lama_common import drawn_mask, load_generator, picture  # noqa: E402

torch.backends.cudnn.allow_tf32 = False
torch.backends.cuda.matmul.allow_tf32 = False


def rgba(rgb):
    return np.concatenate([rgb, np.full(rgb.shape[:2] + (1,), 255, np.uint8)], axis=2)


def main():
    generator = load_generator(MODEL).cuda()
    OUT.mkdir(parents=True, exist_ok=True)
    index = {}
    for i, path in enumerate(PICTURES):
        rgb = picture(path)
        h, w = rgb.shape[:2]
        mask = drawn_mask((w, h), i)
        image = rgb.astype(np.float32).transpose(2, 0, 1) / 255
        hole = (mask.astype(np.float32) / 255 > 0).astype(np.float32)[None]
        ph, pw = (-h) % 8, (-w) % 8
        image = np.pad(image, ((0, 0), (0, ph), (0, pw)), mode="symmetric")
        hole = np.pad(hole, ((0, 0), (0, ph), (0, pw)), mode="symmetric")
        with torch.no_grad():
            img = torch.from_numpy(image)[None].cuda()
            msk = torch.from_numpy(hole)[None].cuda()
            predicted = generator(torch.cat([img * (1 - msk), msk], 1))
            inpainted = msk * predicted + (1 - msk) * img
        result = inpainted[0].permute(1, 2, 0).cpu().numpy()[:h, :w]
        result = np.clip(result * 255, 0, 255).astype("uint8")
        name = path.stem
        rgba(rgb).tofile(OUT / f"{name}.rgba")
        np.where(mask > 0, 255, 0).astype(np.uint8).tofile(OUT / f"{name}.mask")
        rgba(result).tofile(OUT / f"{name}.erased.rgba")
        index[name] = {"width": w, "height": h, "filled": int((mask > 0).sum())}
        print(f"{name}: {w}x{h}, {index[name]['filled']} pixels filled")
    (OUT / "reference.json").write_text(json.dumps(index, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
