"""PaddleOCR's preparation of a picture for its text detector, and the regions read from the detector's map — the steps
the AI host ports, kept here so the build, the reference and the port share one definition.

Regions follow PaddleOCR's DBPostProcess as a box at a time: pixels above 0.3 joined into regions (8-connected), each
scored by the mean probability over its bounding box, kept from 0.6, and grown back out by the shrink the detector was
trained with (unclip ratio 1.5: by area × 1.5 / perimeter on every side)."""
import cv2
import numpy as np

LONG_SIDE = 960
MEAN = np.array([0.485, 0.456, 0.406], np.float32)
STD = np.array([0.229, 0.224, 0.225], np.float32)
THRESHOLD, BOX_THRESHOLD, UNCLIP, MIN_SIDE = 0.3, 0.6, 1.5, 3


def prepare(path):
    """BGR, long side to 960 with each side a multiple of 32 (at least 32), 0-1, ImageNet-normalised, planar."""
    img = cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)
    h, w = img.shape[:2]
    scale = LONG_SIDE / max(h, w)
    nh = max(32, int(round(h * scale / 32)) * 32)
    nw = max(32, int(round(w * scale / 32)) * 32)
    x = cv2.resize(img, (nw, nh), interpolation=cv2.INTER_LINEAR).astype(np.float32) / 255.0
    x = (x - MEAN) / STD
    return np.ascontiguousarray(x.transpose(2, 0, 1)[None], dtype=np.float32)


def regions(prob):
    """Each region as (x, y, width, height, score) in the map's pixels, grown back to the text it covers."""
    h, w = prob.shape
    count, labels, stats, _ = cv2.connectedComponentsWithStats((prob > THRESHOLD).astype(np.uint8), connectivity=8)
    found = []
    for i in range(1, count):
        x, y, bw, bh, _ = (int(v) for v in stats[i])
        if min(bw, bh) < MIN_SIDE:
            continue
        score = float(prob[y:y + bh, x:x + bw].mean())
        if score < BOX_THRESHOLD:
            continue
        grow = bw * bh * UNCLIP / (2 * (bw + bh))
        x0, y0 = max(0.0, x - grow), max(0.0, y - grow)
        x1, y1 = min(float(w), x + bw + grow), min(float(h), y + bh + grow)
        found.append((x0, y0, x1 - x0, y1 - y0, score))
    return found
