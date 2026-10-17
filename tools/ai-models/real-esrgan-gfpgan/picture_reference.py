"""
What the picture tools' C# port is held to: upstream Real-ESRGAN and GFPGAN (fp32, GFPGAN with the noise it was trained
with, the upscaler tiled as the host tiles it) on a few pictures at each scale, faces repaired or not — the pictures
they make, and on the way the faces found, their alignment and their paste-back masks.

usage: picture_reference.py <weights folder> <upstream sources (shim)> <out folder> <picture>...
"""
import json, sys, types
from pathlib import Path

import cv2
import numpy as np
import torch

WEIGHTS, SHIM, OUT = (Path(a) for a in sys.argv[1:4])
PICTURES = {Path(a).name: Path(a) for a in sys.argv[4:]}
sys.path.insert(0, str(SHIM))
sys.path.insert(0, str(Path(__file__).parent))
from basicsr.archs.rrdbnet_arch import RRDBNet  # noqa: E402
from realesrgan.utils import RealESRGANer  # noqa: E402
from gfpgan.utils import GFPGANer  # noqa: E402
from build_picture_tools import gfpgan_forward  # noqa: E402

torch.backends.cudnn.allow_tf32 = False
torch.backends.cuda.matmul.allow_tf32 = False

# As the host tiles the upscalers: 512 pixels through the 2x model, 256 through the 4x, each with a 24-pixel margin.
TILES, TILE_PAD = {2: 512, 4: 256}, 24
# Each picture at the scales it is checked at: faces close and far, several to a picture, one with alpha, one with none.
CASES = [("Blake_Lively.jpg", [1, 2, 4]), ("00.jpg", [1, 2]), ("10045.png", [2]), ("children-alpha.png", [2]), ("0014.jpg", [4])]


def upscaler(scale, device):
    net = RRDBNet(num_in_ch=3, num_out_ch=3, num_feat=64, num_block=23, num_grow_ch=32, scale=scale)
    name = "RealESRGAN_x4plus.pth" if scale == 4 else "RealESRGAN_x2plus.pth"
    return RealESRGANer(scale=scale, model_path=str(WEIGHTS / name), model=net, tile=TILES[scale], tile_pad=TILE_PAD, pre_pad=0,
                        half=False, device=device)


def raw(img, path):
    """A picture as the host reads and writes it: interleaved 8-bit RGBA, nothing else."""
    rgba = cv2.cvtColor(img, cv2.COLOR_BGRA2RGBA if img.shape[2] == 4 else cv2.COLOR_BGR2RGBA)
    path.write_bytes(rgba.tobytes())
    return [int(rgba.shape[1]), int(rgba.shape[0])]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    device = torch.device("cuda")
    ups = {4: upscaler(4, device), 2: upscaler(2, device)}
    record = {"tiles": TILES, "tile_pad": TILE_PAD, "cases": []}
    for file, scales in CASES:
        img = cv2.imread(str(PICTURES[file]), cv2.IMREAD_UNCHANGED)
        if img.ndim == 2:
            img = cv2.cvtColor(img, cv2.COLOR_GRAY2BGR)
        if img.dtype != np.uint8:
            img = (img / 257).round().astype(np.uint8)
        stem = Path(file).stem
        cv2.imwrite(str(OUT / f"{stem}.png"), img)
        size = raw(img, OUT / f"{stem}.rgba")
        for scale in scales:
            bg = ups[4 if scale == 4 else 2]
            plain, _ = bg.enhance(img, outscale=scale)
            cv2.imwrite(str(OUT / f"{stem}-x{scale}.png"), plain)
            case = {"picture": f"{stem}.rgba", "size": size, "scale": scale, "plain": f"{stem}-x{scale}.rgba",
                    "plain_size": raw(plain, OUT / f"{stem}-x{scale}.rgba")}
            restorer = GFPGANer(model_path=str(WEIGHTS / "GFPGANv1.4.pth"), upscale=scale, arch="clean", channel_multiplier=2,
                                bg_upsampler=bg, device=device)
            restorer.gfpgan.forward = types.MethodType(gfpgan_forward, restorer.gfpgan)
            _, _, restored = restorer.enhance(img, has_aligned=False, only_center_face=False, paste_back=True)
            helper = restorer.face_helper
            cv2.imwrite(str(OUT / f"{stem}-x{scale}-faces.png"), restored)
            case["faces"] = f"{stem}-x{scale}-faces.rgba"
            case["faces_size"] = raw(restored, OUT / f"{stem}-x{scale}-faces.rgba")
            case["found"] = [{"box": [round(float(v), 3) for v in face[:4]], "score": round(float(face[4]), 5),
                              "landmarks": [[round(float(x), 3), round(float(y), 3)] for x, y in landmarks],
                              "affine": [[round(float(v), 6) for v in row] for row in affine]}
                             for face, landmarks, affine in zip(helper.det_faces, helper.all_landmarks_5, helper.affine_matrices)]
            for n, (crop, face) in enumerate(zip(helper.cropped_faces, helper.restored_faces)):
                cv2.imwrite(str(OUT / f"{stem}-x{scale}-face{n}-aligned.png"), crop)
                cv2.imwrite(str(OUT / f"{stem}-x{scale}-face{n}-restored.png"), face)
            record["cases"].append(case)
            print(json.dumps({k: v for k, v in case.items() if k != "found"} | {"found": len(case["found"])}), flush=True)
    (OUT / "reference.json").write_text(json.dumps(record, indent=1))


if __name__ == "__main__":
    main()
