"""Check a build of Z-Image-Turbo against the PyTorch pipeline it was made from: the same prompts drawn by both from
the same starting noise — PyTorch in bf16, the build in ONNX Runtime with the sampling loop written out as Nexaflow's
host runs it — and how close each picture comes, as PSNR. Both sets of pictures are saved to look at.

usage: python -I check_z_image.py <z-image-turbo checkpoint> <build-dir> <pictures-dir>
"""
import json
import math
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch
from PIL import Image

PROMPTS = [
    "A photorealistic portrait of an elderly fisherman with a weathered face, wearing a yellow raincoat, a harbour at dawn behind him, shallow depth of field.",
    "A minimalist flat app icon of a paper plane inside a rounded square, gradient from teal to blue, plain white background.",
    "A café chalkboard menu that reads 'Plat du jour : Ratatouille 12 €' in neat handwritten chalk lettering.",
    "A German street sign on a lamppost that reads 'Bahnhofstraße', a tram passing behind it on an overcast afternoon.",
    "A conference poster titled 'NEXAFLOW SUMMIT 2026' with the subtitle 'Build. Ship. Repeat.', bold modern typography and geometric shapes.",
    "Three red apples and two green pears arranged in a single row on a wooden table, studio lighting.",
    "A watercolour illustration of a fox reading a book under a tree in autumn, children's book style.",
    "An isometric 3D illustration of a small home office with a desk, a laptop, a potted plant and a bookshelf, soft pastel colours.",
    "A close-up photograph of two hands shaking, business attire, natural window light.",
    "An educational diagram of the water cycle with labelled arrows reading 'Evaporation', 'Condensation' and 'Precipitation'.",
    "A cyberpunk city street at night in the rain, neon signs, reflections on wet asphalt, cinematic wide shot.",
    "A product photo of a matte black coffee mug with the word 'Bonjour' printed in white serif letters, on a marble countertop.",
]
STEPS, SHIFT, SIZE = 9, 3.0, 1024


def sigmas():
    """linspace(1, 1/steps, steps), shifted: s' = shift*s / (1 + (shift-1)*s), then a final 0."""
    base = np.linspace(1.0, 1.0 / STEPS, STEPS)
    return np.append(SHIFT * base / (1 + (SHIFT - 1) * base), 0.0).astype(np.float32)


def patchify(latents):
    _, c, h, w = latents.shape
    return latents[0].reshape(c, h // 2, 2, w // 2, 2).transpose(1, 3, 2, 4, 0).reshape(1, (h // 2) * (w // 2), 4 * c)


def unpatchify(tokens, h, w, c=16):
    return tokens[0].reshape(h // 2, w // 2, 2, 2, c).transpose(4, 0, 2, 1, 3).reshape(1, c, h, w)


def prompt_rows(caption):
    length = caption.shape[1]
    padded = length + (-length) % 32
    rows = np.concatenate([caption, np.repeat(caption[:, -1:], padded - length, axis=1)], axis=1)
    positions = np.stack([np.arange(1, padded + 1), np.zeros(padded), np.zeros(padded)], axis=1).astype(np.int64)
    return rows, np.arange(padded) >= length, positions


def image_positions(h, w, caption_rows):
    rows, cols = np.meshgrid(np.arange(h // 2), np.arange(w // 2), indexing="ij")
    return np.stack([np.full_like(rows, caption_rows + 1), rows, cols], axis=-1).reshape(-1, 3).astype(np.int64)


def session(path):
    options = ort.SessionOptions()
    options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    return ort.InferenceSession(str(path), options, providers=[("CUDAExecutionProvider", {"device_id": 0, "arena_extend_strategy": "kSameAsRequested"})])


def psnr(a, b):
    x, y = (np.asarray(i.convert("RGB"), dtype=np.float32) for i in (a, b))
    mse = ((x - y) ** 2).mean()
    return 99.0 if mse == 0 else 10 * math.log10(255**2 / mse)


def noise(i):
    return torch.randn((1, 16, SIZE // 8, SIZE // 8), generator=torch.Generator("cuda").manual_seed(1000 + i), device="cuda")


def main():
    checkpoint, build, pictures = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    (pictures / "pytorch").mkdir(parents=True, exist_ok=True)
    (pictures / "onnx").mkdir(parents=True, exist_ok=True)
    from diffusers import ZImagePipeline
    from transformers import AutoTokenizer

    pipe = ZImagePipeline.from_pretrained(checkpoint, torch_dtype=torch.bfloat16).to("cuda")
    for i, prompt in enumerate(PROMPTS):
        pipe(prompt=prompt, width=SIZE, height=SIZE, num_inference_steps=STEPS, guidance_scale=0.0,
             latents=noise(i)).images[0].save(pictures / "pytorch" / f"{i:02d}.png")
    pipe = None
    torch.cuda.empty_cache()

    ort.preload_dlls()
    tokenizer = AutoTokenizer.from_pretrained(checkpoint / "tokenizer")   # with its chat template, as the pipeline reads
    encoder, denoiser, decoder = (session(build / part / f"{part}.onnx") for part in ("encoder", "denoiser", "decoder"))
    s, scores = sigmas(), []
    for i, prompt in enumerate(PROMPTS):
        text = tokenizer.apply_chat_template([{"role": "user", "content": prompt}], tokenize=False, add_generation_prompt=True, enable_thinking=True)
        ids = np.array([tokenizer(text).input_ids], dtype=np.int64)
        rows, padding, caption_positions = prompt_rows(encoder.run(None, {"input_ids": ids})[0].astype(np.float16))
        latents = noise(i).cpu().numpy()
        h, w = latents.shape[2], latents.shape[3]
        positions = image_positions(h, w, rows.shape[1])
        for k in range(STEPS):
            velocity = denoiser.run(None, {"image_tokens": patchify(latents), "image_positions": positions, "caption": rows,
                                           "caption_padding": padding, "caption_positions": caption_positions,
                                           "timestep": np.array([1.0 - s[k]], dtype=np.float32)})[0]
            latents = latents + (s[k + 1] - s[k]) * unpatchify(velocity, h, w)
        pixels = decoder.run(None, {"latents": latents.astype(np.float32)})[0][0]
        image = Image.fromarray((pixels.transpose(1, 2, 0) * 255).round().clip(0, 255).astype(np.uint8))
        image.save(pictures / "onnx" / f"{i:02d}.png")
        scores.append(psnr(Image.open(pictures / "pytorch" / f"{i:02d}.png"), image))
        print(f"{i:02d} psnr {scores[-1]:.1f} dB  {prompt[:60]}", flush=True)

    summary = {"psnr_min": round(min(scores), 1), "psnr_median": round(sorted(scores)[len(scores) // 2], 1)}
    (pictures / "check.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary), flush=True)


if __name__ == "__main__":
    main()
