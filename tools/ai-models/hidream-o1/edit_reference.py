"""Record what HiDream-O1's own code does around its graphs when it edits, for Nexaflow's host to be held to: the sizes it
reads pictures at, the vision tower's inputs as Qwen3-VL's processor and HiDream's model make them, and an edit's prompt
tokens and rotary positions, for src/Nexaflow.Tests/Nexaflow.Tests.AiHost/Drawing/hidream-edit.json; and for
src/Nexaflow.Tests/Nexaflow.Tests.IO/Common/Imaging/, the sizes and pixels its resize_pilimage fits pictures to
(patch-fitting-reference.json) and PIL's bicubic, Lanczos and box resizes it does that with
(pil-resampling-reference.json). All three are written to <out-dir>, to be put there.

usage: python -I -X utf8 edit_reference.py <hidream-o1-image checkpoint> <hidream-o1-image code> <out-dir>
"""
import json
import sys
from pathlib import Path

import numpy as np
import torch
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))  # the builder beside this script; -I leaves it off the path
from build_hidream_o1 import BOI_TOKEN, CONDITION_SIZE, IMAGE_TOKEN, PATCH, TMS_TOKEN, VISION_MERGE, VISION_START, vision_sampling


def picture(width, height, seed):
    """A small picture with something in it: smooth gradients under noise, so every filter has edges to work on."""
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:height, 0:width]
    base = np.stack([x * 255 / max(width - 1, 1), y * 255 / max(height - 1, 1), (x + y) % 37 * 7], -1)
    return Image.fromarray(np.clip(base + rng.normal(0, 30, base.shape), 0, 255).astype(np.uint8), "RGB")


def pixels(image):
    return [int(v) for v in np.asarray(image).reshape(-1)]


def main():
    checkpoint, code, out = (Path(a) for a in sys.argv[1:4])
    sys.path.insert(0, str(code))
    from models import utils
    from transformers import AutoProcessor
    processor = AutoProcessor.from_pretrained(checkpoint)

    # PIL's filters, which HiDream sizes pictures with: bicubic to the patches, Lanczos for the vision tower, box halving.
    resampling = []
    for filter_name, filter_id, cases in (
            ("bicubic", Image.BICUBIC, [(37, 23, 16, 11), (9, 7, 20, 15), (30, 5, 12, 14), (64, 48, 64, 21)]),
            ("lanczos", Image.LANCZOS, [(37, 23, 16, 11), (9, 7, 20, 15), (30, 5, 12, 14), (64, 48, 21, 48)]),
            ("box", Image.BOX, [(37, 23, 18, 11), (64, 48, 32, 24), (9, 7, 20, 15)])):
        for i, (w, h, nw, nh) in enumerate(cases):
            source = picture(w, h, 100 + i)
            resampling.append({"filter": filter_name, "width": w, "height": h, "pixels": pixels(source),
                               "into": {"width": nw, "height": nh, "pixels": pixels(source.resize((nw, nh), resample=filter_id))}})

    # The sizes a picture is read at: patch-aligned within a square of the side given — enlarged or reduced, box-halved
    # first while it is twice the side, then covered and cropped to the centre.
    fitted = []
    for w, h, side in [(1024, 1024, 2048), (3000, 2000, 2048), (5000, 3300, 2048), (640, 480, 2048), (2048, 2048, 1536),
                       (1531, 2047, 1536), (4096, 1024, 1024), (333, 777, 2048), (2048, 1152, 1024), (37, 23, 64)]:
        image = utils.resize_pilimage(picture(w, h, 7), side, PATCH)
        fitted.append({"width": w, "height": h, "side": side, "into": [image.width, image.height]})
    # And a small one whole, so the host's resize, crop and halving are checked to the value.
    small = picture(150, 70, 9)
    whole = utils.resize_pilimage(small, 96, PATCH)
    halved = picture(140, 130, 10)
    halved_into = utils.resize_pilimage(halved, 64, PATCH)
    conditions = []
    for w, h in [(2048, 2048), (2048, 1152), (1152, 2048), (1536, 1536), (1024, 2048), (2496, 1664), (96, 32)]:
        conditions.append({"width": w, "height": h, "into": list(utils.calculate_dimensions(CONDITION_SIZE, w / h))})

    # The vision tower's inputs for a small picture: the processor's patches and the sampling the model computes. A
    # picture is read at a size the processor leaves as it is — HiDream's are 384 a side or so, well inside its bounds —
    # so this one is let through small, and its patches recorded as the bytes they normalise, (b - 127.5) / 127.5.
    read = picture(96, 64, 11)
    proc = processor.image_processor(images=[read], return_tensors="pt", size={"shortest_edge": 32 * 32, "longest_edge": 16777216})
    patch_bytes = torch.round(proc.pixel_values * 127.5 + 127.5)
    assert (proc.pixel_values - (patch_bytes - 127.5) / 127.5).abs().max() < 1e-6, "the processor normalises otherwise"
    t, gh, gw = (int(v) for v in proc.image_grid_thw[0])
    index, weights, positions = vision_sampling(gh, gw)
    samplings = []
    for rows, cols in [(24, 24), (26, 22), (12, 34), (4, 6)]:
        i, w, p = vision_sampling(rows, cols)
        samplings.append({"rows": rows, "cols": cols, "index": i.reshape(-1).tolist(), "weights": w.reshape(-1).tolist(),
                          "positions": p.reshape(-1).tolist()})

    # An edit's prompt: two pictures, the processor's tokens around them, and every token's position as HiDream numbers
    # it — the prompt's, the timestep token's, the picture drawn and each picture worked from.
    vlm = [picture(96, 64, 12), picture(64, 96, 13)]
    content = [{"type": "image"}, {"type": "image"}, {"type": "text", "text": "Put the cat from the first on the sofa of the second."}]
    template = processor.apply_chat_template([{"role": "user", "content": content}], tokenize=False, add_generation_prompt=True)
    seen = processor(text=[template], images=vlm, return_tensors="pt")
    ids = torch.cat([seen.input_ids, processor.tokenizer.encode(BOI_TOKEN + TMS_TOKEN, return_tensors="pt", add_special_tokens=False)], -1)
    rows, cols = 40, 56
    refs = [(36, 24), (24, 36)]
    grids = seen.image_grid_thw.clone()
    grids[:, 1:] //= VISION_MERGE
    grids = torch.cat([grids, torch.tensor([[1, rows, cols]]), torch.tensor([[1, r, c] for r, c in refs])])
    placeholders = []
    for length in [rows * cols] + [r * c for r, c in refs]:
        run = torch.full((1, length), IMAGE_TOKEN, dtype=ids.dtype)
        run[0, 0] = VISION_START
        placeholders.append(run)
    position_ids, _ = utils.get_rope_index_fix_point(1, IMAGE_TOKEN, 151656, VISION_START, input_ids=torch.cat([ids] + placeholders, -1),
                                                     image_grid_thw=grids, skip_vision_start_token=[0, 0, 1, 1, 1])
    empty_template = processor.apply_chat_template([{"role": "user", "content": content[:2] + [{"type": "text", "text": " "}]}],
                                                   tokenize=False, add_generation_prompt=True)
    empty = processor(text=[empty_template], images=vlm, return_tensors="pt").input_ids

    fitting = {
        "fitted": fitted,
        "whole": {"width": small.width, "height": small.height, "side": 96, "pixels": pixels(small),
                  "into": {"width": whole.width, "height": whole.height, "pixels": pixels(whole)}},
        "halved": {"width": halved.width, "height": halved.height, "side": 64, "pixels": pixels(halved),
                   "into": {"width": halved_into.width, "height": halved_into.height, "pixels": pixels(halved_into)}},
    }
    reference = {
        "conditions": conditions,
        "reader": {"width": read.width, "height": read.height, "pixels": pixels(read), "rows": gh, "cols": gw,
                   "patch_bytes": [int(b) for b in patch_bytes.reshape(-1)], "index": index.reshape(-1).tolist(),
                   "weights": weights.reshape(-1).tolist(), "positions": positions.reshape(-1).tolist()},
        "samplings": samplings,
        "prompt": {"text": content[2]["text"], "grids": [[int(g[1]) // VISION_MERGE, int(g[2]) // VISION_MERGE] for g in seen.image_grid_thw],
                   "ids": ids[0, :-1].tolist(), "empty_ids": empty[0].tolist() + processor.tokenizer.encode(BOI_TOKEN, add_special_tokens=False),
                   "rows": rows, "cols": cols, "refs": refs, "positions": position_ids[:, 0].tolist()},
    }
    out.mkdir(parents=True, exist_ok=True)
    host = out / "hidream-edit.json"
    host.write_text(json.dumps(reference, separators=(",", ":")))
    (out / "patch-fitting-reference.json").write_text(json.dumps(fitting, separators=(",", ":")))
    (out / "pil-resampling-reference.json").write_text(json.dumps(resampling, separators=(",", ":")))
    for f in sorted(out.glob("*.json")):
        print(f"wrote {f} ({f.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
