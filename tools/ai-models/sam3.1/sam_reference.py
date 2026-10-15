"""
What a port of SAM 3.1's still-picture paths is held to: the makers' own paths, in fp32, on their own sample pictures.

  reference.json   for each picture, each click case's points, labels, IoUs and object score, and each phrase's
                   scores and boxes (x0 y0 x1 y1, picture pixels); the tokens of each of PHRASES
  <picture>-click-<case>.png, <picture>-find-<case>-<n>.png   each mask at the picture's size, 1-bit

usage: sam_reference.py <checkpoint> <pictures folder> <reference folder>
"""
import json, sys
from pathlib import Path

import numpy as np
from PIL import Image

from sam_graphs import CASES, PHRASES, Sam


def save_mask(mask, path: Path):
    Image.fromarray(np.asarray(mask, bool)).convert("1").save(path, optimize=True)


def main():
    checkpoint, pictures, out = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
    out.mkdir(parents=True, exist_ok=True)
    sam = Sam(checkpoint)
    reference = {"pictures": {}, "tokens": {}}
    for name, (click_cases, find_cases) in CASES.items():
        picture = Image.open(pictures / name).convert("RGB")
        w, h = picture.size
        stem = Path(name).stem
        entry = {"width": w, "height": h, "clicks": [], "finds": []}
        features = sam.click_features(picture)
        for case, points, labels in click_cases:
            mask, ious, obj = sam.click(features, points, labels, w, h)
            file = f"{stem}-click-{case}.png"
            save_mask((mask > 0).cpu().numpy(), out / file)
            entry["clicks"].append({"case": case, "points": points, "labels": labels, "ious": ious.cpu().tolist(),
                                    "object": obj, "mask": file})
        for case, phrase, boxes, labels in find_cases:
            scores, xyxy, masks = sam.find(picture, phrase, boxes, labels)
            files = []
            for i, m in enumerate(masks.cpu().numpy()):
                files.append(f"{stem}-find-{case}-{i}.png")
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
