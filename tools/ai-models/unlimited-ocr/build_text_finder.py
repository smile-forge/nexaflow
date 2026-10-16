"""Converts PaddleOCR's PP-OCRv5 mobile text detector to ONNX and holds the graph to Paddle's own predictor.

    build_text_finder.py <paddle model folder> <out.onnx> <sample pictures folder>

The graph takes the picture as PaddleOCR prepares it for detection — BGR, the long side brought to 960 in multiples of
32, scaled to 0-1 and normalised by ImageNet's mean and deviation, planar: x [1, 3, H, W] — and gives the probability
that each pixel is text, [1, 1, H, W]."""
import sys
from pathlib import Path

import paddle  # noqa: F401 — paddle2onnx's native module binds to Paddle's, which must be loaded first
import paddle.inference as pi
import paddle2onnx
import numpy as np
import onnxruntime as ort

from text_finding import prepare

source, out, samples = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
out.parent.mkdir(parents=True, exist_ok=True)
paddle2onnx.export(str(source / "inference.json"), str(source / "inference.pdiparams"), str(out), opset_version=17)

config = pi.Config(str(source / "inference.json"), str(source / "inference.pdiparams"))
config.disable_gpu()
predictor = pi.create_predictor(config)
session = ort.InferenceSession(str(out), providers=["CPUExecutionProvider"])

worst = 0.0
for picture in sorted(p for p in samples.iterdir() if p.suffix.lower() in (".png", ".jpg")):
    x = prepare(picture)
    ours = session.run(None, {"x": x})[0]
    handle = predictor.get_input_handle(predictor.get_input_names()[0])
    handle.reshape(x.shape)
    handle.copy_from_cpu(x)
    predictor.run()
    theirs = predictor.get_output_handle(predictor.get_output_names()[0]).copy_to_cpu()
    diff = float(np.abs(ours - theirs).max())
    worst = max(worst, diff)
    print(f"{picture.name}: {x.shape[3]}x{x.shape[2]}, largest difference {diff:.6f}")
if worst > 1e-3:
    sys.exit(f"The graph is not Paddle's detector: it differs by {worst:.6f}.")
