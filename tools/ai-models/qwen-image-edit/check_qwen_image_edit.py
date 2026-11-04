"""Make four edits with each built Qwen-Image-Edit graph set — the denoiser with 8-bit weights and with fp16 — and with
diffusers' own QwenImageEditPlusPipeline, from the same pictures, and compare them, so a build is never published
unchecked.

The graphs are driven as Nexaflow's AI host drives them: each picture read by the picture reader at the size the
processor reads it, the instruction and the empty prompt read with those pictures by the prompt encoder, each picture's
latents read by the picture encoder at its own size, forty steps of true guidance through the denoiser with the
pipeline's own scheduler stepping between, and the latents decoded. The pipeline's noise is the graphs' too, so what
differs is the graphs.

usage: python -I -X utf8 check_qwen_image_edit.py <checkpoint> <build-dir> <pictures-dir> <out-dir>
"""
import json
import sys
import time
from pathlib import Path

import numpy as np
import torch

sys.path.insert(0, str(Path(__file__).parent))  # the builder beside this script; -I leaves it off the path
from build_qwen_image_edit import (CONDITION_AREA, STEPS, TEMPLATE_TOKENS, TRUE_CFG, VAE_AREA, edit, image_positions, load_pipeline,
                                   prompt_inputs, prompt_positions, reader_inputs)

# Edits of the pictures in <pictures-dir>, by name: an instruction and the pictures it works from.
EDITS = [
    ("Make it night, the harbour lit by street lamps.", ["00"]),
    ("Change the menu to read 'Plat du jour : Soupe 8 €'.", ["02"]),
    ("Turn the fox into a red panda.", ["05"]),
    ("Put the mug from the second picture on the desk in the first.", ["06", "09"]),
]
# dB. Forty guided steps carry a small difference a long way; an edit keeps most of its picture, so the same edit made
# twice sits closer than a drawing does.
WORST_PSNR, MEDIAN_PSNR = 16.0, 20.0
BUILDS = ("denoiser", "denoiser-fp16")


def session(path):
    import onnxruntime as ort
    return ort.InferenceSession(str(path), providers=[("CUDAExecutionProvider", {}), "CPUExecutionProvider"])


def psnr(a, b):
    mse = np.mean((a.astype(np.float64) - b.astype(np.float64)) ** 2)
    return float("inf") if mse == 0 else 10 * np.log10(255.0 ** 2 / mse)


class Graphs:
    def __init__(self, build, denoiser):
        self.reader = session(build / "picture-reader" / "picture-reader.onnx")
        self.encoder = session(build / "prompt-encoder" / "prompt-encoder.onnx")
        self.pictures = session(build / "picture-encoder" / "picture-encoder.onnx")
        self.denoiser = session(build / denoiser / "denoiser.onnx")
        self.decoder = session(build / "decoder" / "decoder.onnx")


def edit_with_graphs(g, pipe, instruction, pictures, latents):
    """An edit as the host makes it, from the pipeline's own starting latents [1, N, 64]."""
    from diffusers.pipelines.qwenimage.pipeline_qwenimage_edit_plus import calculate_dimensions, calculate_shift
    conditions, vae_pictures = [], []
    for p in pictures:
        w, h = calculate_dimensions(CONDITION_AREA, p.width / p.height)
        conditions.append(pipe.image_processor.resize(p, h, w))
        w, h = calculate_dimensions(VAE_AREA, p.width / p.height)
        vae_pictures.append(p.resize((w, h), resample=__import__("PIL").Image.LANCZOS))
    embeds = []
    for c in conditions:
        patches, positions, order, windows, _ = reader_inputs(pipe.processor, pipe.text_encoder.model.visual, c)
        embeds.append(g.reader.run(None, {"patches": patches.float().numpy(), "patch_positions": positions.numpy(),
                                          "window_order": order.numpy(), "windows": windows.numpy()})[0])
    embeds = np.concatenate(embeds)
    prompts = []
    for caption in (instruction, " "):
        inputs, positions = prompt_inputs(pipe, caption, conditions)
        hidden = g.encoder.run(None, {"input_ids": inputs.input_ids.numpy(), "vision_embeds": embeds, "positions": positions.numpy()})[0]
        prompts.append(hidden[TEMPLATE_TOKENS:][None])
    sources = [g.pictures.run(None, {"rgb": np.asarray(p)})[0] for p in vae_pictures]

    width, height = calculate_dimensions(1024 * 1024, pictures[-1].width / pictures[-1].height)   # as the pipeline sizes an edit
    rows, cols = height // 16, width // 16
    grids = [(rows, cols)] + [(s.shape[0], s.shape[1]) for s in sources]
    image_at = image_positions(grids).numpy()
    clean = np.concatenate([s.reshape(-1, 64) for s in sources])
    scheduler = pipe.scheduler
    mu = calculate_shift(rows * cols, scheduler.config.base_image_seq_len, scheduler.config.max_image_seq_len,
                         scheduler.config.base_shift, scheduler.config.max_shift)
    scheduler.set_timesteps(sigmas=np.linspace(1.0, 1 / STEPS, STEPS), mu=mu)
    scheduler.set_begin_index(0)
    z = latents.float().cpu()
    for t in scheduler.timesteps:
        sigma = np.array([float(t) / 1000], dtype=np.float32)
        tokens = np.concatenate([z[0].numpy(), clean])[None].astype(np.float32)
        predictions = []
        for prompt in prompts:
            v = g.denoiser.run(None, {"image_tokens": tokens, "image_positions": image_at, "drawn": np.array([rows * cols]),
                                      "prompt": prompt, "prompt_positions": prompt_positions(prompt.shape[1], grids).numpy(),
                                      "timestep": sigma})[0]
            predictions.append(torch.from_numpy(v[:, : rows * cols]))
        cond, uncond = predictions
        comb = uncond + TRUE_CFG * (cond - uncond)
        v = comb * (torch.norm(cond, dim=-1, keepdim=True) / torch.norm(comb, dim=-1, keepdim=True))
        z = scheduler.step(v, t, z, return_dict=False)[0].float()
    return g.decoder.run(None, {"tokens": z[0].numpy().reshape(rows, cols, 64)})[0]


def main():
    checkpoint, build, pictures_dir, out = (Path(a) for a in sys.argv[1:5])
    from PIL import Image
    (out / "pytorch").mkdir(parents=True, exist_ok=True)
    pipe = load_pipeline(checkpoint, torch.bfloat16)

    # PyTorch first, as Qwen publishes it, keeping each edit's starting latents for the graphs.
    starts = {}
    for i, (instruction, names) in enumerate(EDITS):
        pictures = [Image.open(pictures_dir / f"{n}.png").convert("RGB") for n in names]
        def keep(_pipe, step, _t, kwargs):
            return {}

        path = out / "pytorch" / f"edit-{i:02d}.png"
        if path.exists():
            image = Image.open(path)
        else:
            started = time.perf_counter()
            image = pipe(image=pictures, prompt=instruction, negative_prompt=" ", true_cfg_scale=TRUE_CFG, guidance_scale=1.0,
                         num_inference_steps=STEPS, generator=torch.Generator("cuda").manual_seed(3000 + i),
                         callback_on_step_end=keep, callback_on_step_end_tensor_inputs=["latents"]).images[0]
            image.save(path)
            print(f"pytorch edit {i:02d} {time.perf_counter() - started:.1f}s", flush=True)
        # The pipeline's noise, drawn again from the same seed.
        from diffusers.utils.torch_utils import randn_tensor
        h, w = image.height // 8, image.width // 8
        noise = randn_tensor((1, 1, 16, h, w), generator=torch.Generator("cuda").manual_seed(3000 + i), device=torch.device("cuda"),
                             dtype=torch.bfloat16)
        starts[i] = pipe._pack_latents(noise, 1, 16, h, w)
    for module in (pipe.transformer, pipe.text_encoder, pipe.vae):
        module.to("cpu")
    torch.cuda.empty_cache()

    failed = []
    for folder in BUILDS:
        graphs = Graphs(build, folder)
        (out / folder).mkdir(parents=True, exist_ok=True)
        results = []
        for i, (instruction, names) in enumerate(EDITS):
            pictures = [Image.open(pictures_dir / f"{n}.png").convert("RGB") for n in names]
            started = time.perf_counter()
            ours = edit_with_graphs(graphs, pipe, instruction, pictures, starts[i])
            seconds = time.perf_counter() - started
            Image.fromarray(ours).save(out / folder / f"edit-{i:02d}.png")
            theirs = np.asarray(Image.open(out / "pytorch" / f"edit-{i:02d}.png").convert("RGB"))
            results.append({"edit": instruction, "psnr": round(psnr(ours, theirs), 2), "seconds": round(seconds, 1)})
            print(f"{folder} edit {i:02d} {seconds:.1f}s  {results[-1]['psnr']} dB", flush=True)
        graphs = None
        values = sorted(r["psnr"] for r in results)
        summary = {"build": folder, "median_psnr": values[len(values) // 2], "worst_psnr": values[0], "results": results}
        (out / f"check-{folder}.json").write_text(json.dumps(summary, indent=1))
        print(json.dumps({k: v for k, v in summary.items() if k != "results"}), flush=True)
        if summary["worst_psnr"] < WORST_PSNR or summary["median_psnr"] < MEDIAN_PSNR:
            failed.append(folder)
    if failed:
        raise SystemExit(f"the edits of {', '.join(failed)} are not the PyTorch pipeline's")


if __name__ == "__main__":
    main()
