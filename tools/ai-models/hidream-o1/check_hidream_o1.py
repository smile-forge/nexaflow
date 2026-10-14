"""Draw twelve prompts with each built HiDream-O1-Image graph — 8-bit weights and fp16 — and with HiDream's own PyTorch
pipeline, from the same noise, and compare them — so a build is never published unchecked.

The graph is driven as Nexaflow's AI host drives it: the prompt read on its first call and cached, the empty prompt
guidance steers from likewise, both asked at every step, and HiDream's own sampler stepping between.

usage: python -I -X utf8 check_hidream_o1.py <checkpoint> <hidream-o1-image code> <build-dir> <out-dir>
"""
import json
import sys
import time
from pathlib import Path

import numpy as np
import torch

sys.path.insert(0, str(Path(__file__).parent))  # the builder beside this script; -I leaves it off the path
from build_hidream_o1 import CALIBRATION, LAYERS, KV_HEADS, HEAD_DIM, PATCH, load_model, prompt_ids, image_positions

SIZE = 2048
STEPS, SHIFT, GUIDANCE, NOISE_SCALE = 50, 3.0, 5.0, 8.0
PROMPTS = CALIBRATION + [
    "A macro photograph of a dew-covered spider web at sunrise, shallow depth of field.",
    "A hand-lettered wedding invitation that reads 'Anna & Luc — 12 juin 2027' with floral borders.",
]
# dB. Fifty guided steps carry a small difference a long way, so the same picture drawn twice moves things about: HiDream's
# own pipeline in bf16 and in fp32 drew four prompts 15.8-39.6 dB apart, and twelve unrelated pictures are 4-11.5 dB apart.
WORST_PSNR, MEDIAN_PSNR = 12.0, 15.0

# The builds checked, by folder: 8-bit weights, and fp16 weights for a GPU with the room.
BUILDS = ("denoiser", "denoiser-fp16")


def noise(seed):
    """The noise HiDream's pipeline starts a picture from, in the tokens' layout."""
    import einops
    n = NOISE_SCALE * torch.randn((1, 3, SIZE, SIZE), generator=torch.Generator("cpu").manual_seed(seed + 1))
    return einops.rearrange(n, "B C (H p1) (W p2) -> B (H W) (C p1 p2)", p1=PATCH, p2=PATCH)


def pixels(tokens):
    import einops
    img = (tokens.float() + 1) / 2
    img = einops.rearrange(img.cpu(), "B (H W) (C p1 p2) -> B C (H p1) (W p2)", H=SIZE // PATCH, W=SIZE // PATCH, p1=PATCH, p2=PATCH)
    return np.round(np.clip(img[0].numpy().transpose(1, 2, 0) * 255, 0, 255)).astype(np.uint8)


class Graph:
    """The built denoiser on ONNX Runtime's CUDA execution provider, one prompt's cache per Prompt."""

    def __init__(self, path):
        import onnxruntime as ort
        self.session = ort.InferenceSession(str(path), providers=[("CUDAExecutionProvider", {}), "CPUExecutionProvider"])
        self.positions = image_positions(SIZE // PATCH, SIZE // PATCH, "cpu").numpy()

    def predict(self, prompt, tokens, timestep):
        ids = prompt.ids if prompt.keys.shape[2] == 0 else []
        unread = np.array(ids, dtype=np.int64).reshape(1, len(ids))
        prediction, keys, values = self.session.run(None, {
            "input_ids": unread, "image_tokens": tokens.astype(np.float32), "image_positions": self.positions,
            "timestep": np.array([timestep], dtype=np.float32), "past_keys": prompt.keys, "past_values": prompt.values})
        prompt.keys, prompt.values = keys, values
        return prediction


class Prompt:
    def __init__(self, ids):
        self.ids = ids
        self.keys = np.zeros((LAYERS, KV_HEADS, 0, HEAD_DIM), dtype=np.float16)
        self.values = np.zeros((LAYERS, KV_HEADS, 0, HEAD_DIM), dtype=np.float16)


def draw_with_graph(graph, processor, pipeline, prompt, z):
    sched = pipeline.build_scheduler(STEPS, None, SHIFT, "cpu", "default")
    cond, uncond = Prompt(prompt_ids(processor, prompt)), Prompt(prompt_ids(processor, " "))
    z = z.float()
    for step_t in sched.timesteps:
        t = 1.0 - float(step_t) / 1000.0
        sigma = max(float(step_t) / 1000.0, 0.001)
        x_cond = torch.from_numpy(graph.predict(cond, z.numpy(), t))
        x_uncond = torch.from_numpy(graph.predict(uncond, z.numpy(), t))
        v_cond, v_uncond = (x_cond - z) / sigma, (x_uncond - z) / sigma
        v = v_uncond + GUIDANCE * (v_cond - v_uncond)
        z = sched.step(-v, step_t.to(torch.float32), z, return_dict=False)[0].float()
    return pixels(z)


def psnr(a, b):
    mse = np.mean((a.astype(np.float64) - b.astype(np.float64)) ** 2)
    return float("inf") if mse == 0 else 10 * np.log10(255.0 ** 2 / mse)


def main():
    checkpoint, code, build, out = (Path(a) for a in sys.argv[1:5])
    from PIL import Image
    (out / "pytorch").mkdir(parents=True, exist_ok=True)

    # PyTorch first, as HiDream publishes it: bf16, its own pipeline.
    model, processor, pipeline, _ = load_model(checkpoint, code, torch.bfloat16)
    torch_s = []
    for i, prompt in enumerate(PROMPTS):
        path = out / "pytorch" / f"{i:02d}.png"
        if path.exists():
            continue
        started = time.perf_counter()
        image = pipeline.generate_image(model=model, processor=processor, prompt=prompt, ref_image_paths=[], height=SIZE, width=SIZE,
                                        seed=2000 + i, num_inference_steps=STEPS, guidance_scale=GUIDANCE, shift=SHIFT,
                                        timesteps_list=None, scheduler_name="default")
        torch_s.append(time.perf_counter() - started)
        image.save(path)
        print(f"pytorch {i:02d} {torch_s[-1]:.1f}s", flush=True)
    del model
    torch.cuda.empty_cache()

    failed = []
    for folder in BUILDS:
        graph = Graph(build / folder / "denoiser.onnx")
        (out / folder).mkdir(parents=True, exist_ok=True)
        results, onnx_s = [], []
        for i, prompt in enumerate(PROMPTS):
            started = time.perf_counter()
            ours = draw_with_graph(graph, processor, pipeline, prompt, noise(2000 + i))
            onnx_s.append(time.perf_counter() - started)
            Image.fromarray(ours).save(out / folder / f"{i:02d}.png")
            theirs = np.asarray(Image.open(out / "pytorch" / f"{i:02d}.png").convert("RGB"))
            results.append({"prompt": prompt, "psnr": round(psnr(ours, theirs), 2), "seconds": round(onnx_s[-1], 1)})
            print(f"{folder} {i:02d} {onnx_s[-1]:.1f}s  {results[-1]['psnr']} dB", flush=True)
        graph = None  # its session's GPU memory goes before the next build's loads

        values = sorted(r["psnr"] for r in results)
        summary = {"build": folder, "median_psnr": values[len(values) // 2], "worst_psnr": values[0],
                   "median_onnx_s": sorted(onnx_s)[len(onnx_s) // 2], "results": results}
        (out / f"check-{folder}.json").write_text(json.dumps(summary, indent=1))
        print(json.dumps({k: v for k, v in summary.items() if k != "results"}), flush=True)
        if summary["worst_psnr"] < WORST_PSNR or summary["median_psnr"] < MEDIAN_PSNR:
            failed.append(folder)
    if failed:
        raise SystemExit(f"the pictures of {', '.join(failed)} are not the PyTorch pipeline's")


if __name__ == "__main__":
    main()
