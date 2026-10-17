"""What OpenCV makes of small pictures — Lanczos resizes, affine warps and fits, Gaussian blurs — for the C# ports in
Nexaflow.IO.Common's PictureResampling and Affine2D to be held to without OpenCV."""
import json, sys
import cv2, numpy as np
rng = np.random.default_rng(5)
out = {}
src = rng.integers(0, 256, (7, 9, 3), dtype=np.uint8)
out["lanczos"] = {"width": 9, "height": 7, "channels": 3, "pixels": src.flatten().tolist(),
                  "down": {"width": 5, "height": 4, "pixels": cv2.resize(src, (5, 4), interpolation=cv2.INTER_LANCZOS4).flatten().tolist()},
                  "up": {"width": 20, "height": 15, "pixels": cv2.resize(src, (20, 15), interpolation=cv2.INTER_LANCZOS4).flatten().tolist()}}
img = rng.integers(0, 256, (12, 16, 3), dtype=np.uint8)
m = np.array([[0.83, -0.21, 3.4], [0.19, 0.91, -1.7]])
warped = cv2.warpAffine(img, m, (14, 11), borderMode=cv2.BORDER_CONSTANT, borderValue=(135, 133, 132))
plane = rng.random((12, 16)).astype(np.float32)
warped_plane = cv2.warpAffine(plane, m, (14, 11))
out["warp"] = {"width": 16, "height": 12, "pixels": img.flatten().tolist(), "plane": plane.flatten().tolist(), "map": m.flatten().tolist(),
               "into": [14, 11], "border": [135, 133, 132], "warped": warped.flatten().tolist(), "warped_plane": warped_plane.flatten().tolist()}
blur_src = (rng.random((29, 31)) * 255).astype(np.float32)
out["blur"] = {"width": 31, "height": 29, "size": 11, "sigma": 3.0, "plane": blur_src.flatten().tolist(),
               "blurred": cv2.GaussianBlur(blur_src, (11, 11), 3.0).flatten().tolist()}
template = np.array([[192.98138, 239.94708], [318.90277, 240.1936], [256.63416, 314.01935], [201.26117, 371.41043], [313.08905, 371.15118]])
fits = []
for case in json.load(open(sys.argv[1]))["cases"]:
    for face in case["found"]:
        pts = np.array(face["landmarks"])
        fits.append({"from": pts.tolist(), "affine": cv2.estimateAffinePartial2D(pts, template, method=cv2.LMEDS)[0].flatten().tolist()})
# One face with a point far off — a mouth corner misplaced — which LMEDS sets aside.
bad = np.array(fits[0]["from"]); bad[4] += [40, -25]
fits.append({"from": bad.tolist(), "affine": cv2.estimateAffinePartial2D(bad, template, method=cv2.LMEDS)[0].flatten().tolist()})
out["similarity"] = {"to": template.tolist(), "fits": fits[:6] + fits[-1:]}
json.dump(out, open(sys.argv[2], "w"), separators=(",", ":"))
