"""Paddle's own detector on each sample picture — its probability map and the regions read from it — which is what the AI
host's text finder is held to.

    text_finder_reference.py <paddle model folder> <sample pictures> <out folder>

Writes <picture>.rgba (the picture as 8-bit RGBA, row by row, what the host is given), <picture>.prob (the map as
little-endian float32, row by row) and <picture>.json (the picture's size, the map's, and the regions in fractions of
it)."""
import json
import sys
from pathlib import Path

import cv2
import numpy as np
import paddle  # noqa: F401
import paddle.inference as pi

from text_finding import prepare, regions

source, samples, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
out.mkdir(parents=True, exist_ok=True)
config = pi.Config(str(source / "inference.json"), str(source / "inference.pdiparams"))
config.disable_gpu()
predictor = pi.create_predictor(config)
for picture in sorted(p for p in samples.iterdir() if p.suffix.lower() in (".png", ".jpg")):
    x = prepare(picture)
    handle = predictor.get_input_handle(predictor.get_input_names()[0])
    handle.reshape(x.shape)
    handle.copy_from_cpu(x)
    predictor.run()
    prob = predictor.get_output_handle(predictor.get_output_names()[0]).copy_to_cpu()[0, 0]
    prob.astype("<f4").tofile(out / f"{picture.stem}.prob")
    rgba = cv2.cvtColor(cv2.imdecode(np.fromfile(str(picture), np.uint8), cv2.IMREAD_COLOR), cv2.COLOR_BGR2RGBA)
    rgba.tofile(out / f"{picture.stem}.rgba")
    found = regions(prob)
    h, w = prob.shape
    (out / f"{picture.stem}.json").write_text(json.dumps({
        "pictureWidth": int(rgba.shape[1]), "pictureHeight": int(rgba.shape[0]), "width": int(w), "height": int(h),
        "regions": [{"x": r[0] / w, "y": r[1] / h, "width": r[2] / w, "height": r[3] / h, "score": r[4]} for r in found],
    }, indent=1))
    print(f"{picture.stem}: {prob.shape[1]}x{prob.shape[0]}, {len(found)} regions")
