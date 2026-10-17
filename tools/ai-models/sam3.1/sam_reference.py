"""
What a port of SAM 3.1's still-picture paths is held to: the makers' own paths, in fp32, on their own sample pictures.

  reference.json   for each picture, each click case's points, labels, IoUs and object score, and each phrase's
                   scores and boxes (x0 y0 x1 y1, picture pixels); the tokens of each of PHRASES
  <picture>.rgba   each picture as a port is handed it: interleaved 8-bit RGBA
  <picture>-click-<case>.mask, <picture>-find-<case>-<n>.mask   each mask at the picture's size, a byte a pixel: 255 in,
                   0 out

usage: sam_reference.py <checkpoint> <pictures folder> <reference folder>
"""
import json, sys
from pathlib import Path

import numpy as np
from PIL import Image

from sam_graphs import CASES, PHRASES, Sam


def save_mask(mask, path: Path):
    path.write_bytes((np.asarray(mask, bool).astype(np.uint8) * 255).tobytes())


def main():
    checkpoint, pictures, out = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
    out.mkdir(parents=True, exist_ok=True)
    sam = Sam(checkpoint)
    reference = {"pictures": {}, "tokens": {}}
    for name, (click_cases, find_cases) in CASES.items():
        picture = Image.open(pictures / name).convert("RGB")
        w, h = picture.size
        stem = Path(name).stem
        # the picture as a port is handed it: interleaved 8-bit RGBA, nothing else
        (out / f"{stem}.rgba").write_bytes(picture.convert("RGBA").tobytes())
        entry = {"width": w, "height": h, "pixels": f"{stem}.rgba", "clicks": [], "finds": []}
        features = sam.click_features(picture)
        for case, points, labels in click_cases:
            mask, ious, obj = sam.click(features, points, labels, w, h)
            file = f"{stem}-click-{case}.mask"
            save_mask((mask > 0).cpu().numpy(), out / file)
            entry["clicks"].append({"case": case, "points": points, "labels": labels, "ious": ious.cpu().tolist(),
                                    "object": obj, "mask": file})
        for case, phrase, boxes, labels in find_cases:
            scores, xyxy, masks = sam.find(picture, phrase, boxes, labels)
            files = []
            for i, m in enumerate(masks.cpu().numpy()):
                files.append(f"{stem}-find-{case}-{i}.mask")
                save_mask(m, out / files[-1])
            entry["finds"].append({"case": case, "phrase": phrase, "boxes": boxes, "labels": labels,
                                   "tokens": sam.tokens(phrase)[0].tolist(), "scores": scores.cpu().tolist(),
                                   "found": xyxy.cpu().tolist(), "masks": files})
        reference["pictures"][name] = entry
        print(f"{name}: {len(click_cases)} click case(s), {len(find_cases)} phrase(s)", flush=True)
    for phrase in PHRASES:
        reference["tokens"][phrase] = sam.tokens(phrase)[0].tolist()
    (out / "reference.json").write_text(json.dumps(reference, indent=1, ensure_ascii=False), encoding="utf-8")


if __name__ == "__main__":
    main()
