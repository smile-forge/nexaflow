"""Record what diffusers' QwenImageEditPlusPipeline and Qwen2-VL's processor do around Qwen-Image-Edit's graphs, for
Nexaflow's host to be held to: the sizes it reads pictures at, the vision tower's inputs and their windows, an edit's
prompt tokens and rotary positions, the image tokens' positions, the noise levels, and how a mask lies over the latents
— for src/Nexaflow.Tests/Nexaflow.Tests.AiHost/Drawing/qwen-image-edit.json; and PyTorch's 8-bit antialiased resizes,
which the processor sizes pictures with, for src/Nexaflow.Tests/Nexaflow.Tests.IO/Common/Imaging/torch-resampling-reference.json.
Both are written to <out-dir>, to be put there. Everything here runs on the CPU.

usage: python -I -X utf8 edit_reference.py <Qwen-Image-Edit-2511 checkpoint> <out-dir>
"""
import json
import sys
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))  # the builder beside this script; -I leaves it off the path
from build_qwen_image_edit import CONDITION_AREA, STEPS, TEMPLATE_TOKENS, VAE_AREA, image_positions, prompt_positions, reader_inputs


def picture(width, height, seed):
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:height, 0:width]
    base = np.stack([x * 255 / max(width - 1, 1), y * 255 / max(height - 1, 1), (x + y) % 37 * 7], -1)
    return Image.fromarray(np.clip(base + rng.normal(0, 30, base.shape), 0, 255).astype(np.uint8), "RGB")


def pixels(image):
    return [int(v) for v in np.asarray(image).reshape(-1)]


def main():
    checkpoint, out = Path(sys.argv[1]), Path(sys.argv[2])
    from diffusers import FlowMatchEulerDiscreteScheduler
    from diffusers.image_processor import VaeImageProcessor
    from diffusers.pipelines.qwenimage.pipeline_qwenimage_edit_plus import calculate_dimensions, calculate_shift
    from transformers import AutoProcessor, AutoTokenizer
    processor = AutoProcessor.from_pretrained(checkpoint / "processor")
    from transformers import Qwen2_5_VLConfig
    from transformers.models.qwen2_5_vl.modeling_qwen2_5_vl import Qwen2_5_VLModel, Qwen2_5_VisionTransformerPretrainedModel
    config = Qwen2_5_VLConfig.from_pretrained(checkpoint / "text_encoder")
    with torch.device("meta"):
        model = Qwen2_5_VLModel(config)
    visual = model.visual

    # PyTorch's 8-bit antialiased resizes, as torchvision and the processor do them.
    resampling = []
    for mode, cases in (("bicubic", [(37, 23, 16, 11), (9, 7, 20, 15), (30, 5, 12, 14), (96, 72, 98, 70), (64, 48, 64, 21)]),
                        ("bilinear", [(37, 23, 16, 11), (9, 7, 20, 15), (64, 48, 21, 48)])):
        for i, (w, h, nw, nh) in enumerate(cases):
            source = picture(w, h, 200 + i)
            t = torch.from_numpy(np.asarray(source)).permute(2, 0, 1)[None]
            into = F.interpolate(t, size=(nh, nw), mode=mode, antialias=True)[0].permute(1, 2, 0).contiguous()
            resampling.append({"filter": mode, "width": w, "height": h, "pixels": pixels(source),
                               "into": {"width": nw, "height": nh, "pixels": [int(v) for v in into.reshape(-1)]}})

    # The sizes a picture is read at: within 384² by the vision tower, 1024² by the VAE, each side rounded to 32.
    sizes = []
    for w, h in [(1024, 1024), (1920, 1080), (1080, 1920), (640, 480), (3000, 1000), (333, 777), (100, 100)]:
        sizes.append({"width": w, "height": h, "condition": list(calculate_dimensions(CONDITION_AREA, w / h)),
                      "vae": list(calculate_dimensions(VAE_AREA, w / h))})

    # The vision tower's inputs for a picture the processor resizes: its patches, as the bytes they normalise, and the
    # windows. Two more grids' windows besides.
    read = picture(110, 80, 11)
    patches, positions, order, windows, grid = reader_inputs(processor, visual, read)
    _, gh, gw = (int(v) for v in grid[0])
    mean = torch.tensor(processor.image_processor.image_mean).repeat_interleave(2 * 14 * 14)
    std = torch.tensor(processor.image_processor.image_std).repeat_interleave(2 * 14 * 14)
    patch_bytes = torch.round((patches * std + mean) * 255)
    error = float((patches - (patch_bytes / 255 - mean) / std).abs().max())
    assert error < 1e-5, f"the processor normalises otherwise ({error})"
    grids = []
    for rows, cols in [(gh, gw), (28, 28), (20, 36), (6, 10)]:
        _, at, o, wnd, _ = reader_inputs(processor, visual, picture(cols * 14, rows * 14, 3)) if (rows, cols) != (gh, gw) else (None, positions, order, windows, None)
        grids.append({"rows": rows, "cols": cols, "positions": at.reshape(-1).tolist(), "order": o.tolist(), "windows": wnd.tolist()})

    # An edit's prompt: two pictures as the pipeline writes them, the processor's tokens and the model's positions.
    tokenizer = AutoTokenizer.from_pretrained(checkpoint / "tokenizer")
    template = ("<|im_start|>system\nDescribe the key features of the input image (color, shape, size, texture, objects, background), "
                "then explain how the user's text instruction should alter or modify the image. Generate a new image that meets the "
                "user's requirements while maintaining consistency with the original input where appropriate.<|im_end|>\n"
                "<|im_start|>user\n{}<|im_end|>\n<|im_start|>assistant\n")
    vlm = [picture(*calculate_dimensions(CONDITION_AREA, 4 / 3), 12), picture(*calculate_dimensions(CONDITION_AREA, 3 / 4), 13)]
    instruction = "Put the cat from the second picture on the sofa in the first."
    prompts = {}
    for name, text in (("prompt", instruction), ("empty", " ")):
        full = template.format("".join(f"Picture {i + 1}: <|vision_start|><|image_pad|><|vision_end|>" for i in range(2)) + text)
        inputs = processor(text=[full], images=vlm, padding=True, return_tensors="pt")
        rope, _ = model.get_rope_index(inputs.input_ids, image_grid_thw=inputs.image_grid_thw, attention_mask=inputs.attention_mask)
        prompts[name] = {"text": text, "ids": inputs.input_ids[0].tolist(), "positions": rope[:, 0].tolist(),
                         "grids": [[int(g[1]) // 2, int(g[2]) // 2] for g in inputs.image_grid_thw]}

    # The image tokens' and the prompt's positions in the denoiser, for a picture drawn and two worked from.
    shapes = [(64, 48), (40, 56), (48, 64)]
    denoiser = {"grids": shapes, "image_positions": image_positions(shapes).reshape(-1).tolist(),
                "prompt_tokens": 7, "prompt_positions": prompt_positions(7, shapes).reshape(-1).tolist()}

    # The noise levels for forty steps, at the shift each size of picture gives.
    scheduler = FlowMatchEulerDiscreteScheduler.from_pretrained(checkpoint, subfolder="scheduler")
    schedules = []
    for tokens in (256, 1024, 4096, 3600, 8192, 12000):
        mu = calculate_shift(tokens, scheduler.config.base_image_seq_len, scheduler.config.max_image_seq_len,
                             scheduler.config.base_shift, scheduler.config.max_shift)
        scheduler.set_timesteps(sigmas=np.linspace(1.0, 1 / STEPS, STEPS), mu=mu)
        schedules.append({"tokens": tokens, "sigmas": scheduler.sigmas.tolist(), "timesteps": scheduler.timesteps.tolist()})

    # A mask over the picture as the inpaint pipeline lays it over the latents: binarised, sampled at each latent pixel,
    # in the denoiser's 2x2 packing.
    width, height = 96, 64
    rng = np.random.default_rng(5)
    mask = (rng.random((height, width)) * 255).astype(np.uint8)
    mask[20:44, 30:70] = 255
    processor_mask = VaeImageProcessor(vae_scale_factor=16, vae_latent_channels=16, do_normalize=False, do_binarize=True,
                                       do_convert_grayscale=True)
    m = processor_mask.preprocess(Image.fromarray(mask, "L"), height=height, width=width)
    m = F.interpolate(m, size=(height // 8, width // 8))[0, 0]
    packed = m.view(height // 16, 2, width // 16, 2).permute(0, 2, 1, 3).reshape(-1, 4)
    masks = {"width": width, "height": height, "mask": [int(v) for v in mask.reshape(-1)],
             "quads": packed.reshape(-1).tolist()}

    reference = {"sizes": sizes, "template_tokens": TEMPLATE_TOKENS,
                 "reader": {"width": read.width, "height": read.height, "pixels": pixels(read), "rows": gh, "cols": gw,
                            "patch_bytes": [int(b) for b in patch_bytes.reshape(-1)],
                            "mean": processor.image_processor.image_mean, "std": processor.image_processor.image_std},
                 "grids": grids, "prompts": prompts, "denoiser": denoiser, "schedules": schedules, "mask": masks}
    out.mkdir(parents=True, exist_ok=True)
    (out / "qwen-image-edit.json").write_text(json.dumps(reference, separators=(",", ":")))
    (out / "torch-resampling-reference.json").write_text(json.dumps(resampling, separators=(",", ":")))
    for f in sorted(out.glob("*.json")):
        print(f"wrote {f} ({f.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
