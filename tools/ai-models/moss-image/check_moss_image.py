"""Check a build of MOSS-Image against the PyTorch pipeline it was made from: the same prompts drawn by both from the same
starting noise — PyTorch in bf16 as its makers run it, the build in ONNX Runtime with the sampling loop written out as
Nexaflow's host runs it — and how close each picture comes, as PSNR. Both read the prompt with the pipeline's own bf16
text encoder, so what is measured is the graphs and the loop; compare_moss_encoder.py measures the encoder.

Each part's peak GPU memory is measured too, in a process of its own holding that part alone, as the host loads them:
the card a picture needs at each size. So is how long the build takes to draw a picture, the decoder included,
against the PyTorch pipeline's own time.

usage: python -I check_moss_image.py <moss-image checkpoint> <build-dir> <pictures-dir>
       python -I check_moss_image.py --memory <graph> <part> <height> <width>     (one part's peak, in MiB)
"""
import json
import math
import subprocess
import sys
import threading
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort

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
SIZES = [(1024, 1024), (1456, 1456), (1344, 768), (768, 1344)]   # (height, width), a prompt each in turn
STEPS, GUIDANCE, SHIFT, TRAINING_STEPS = 50, 3.0, 5.0, 1000
SKIPPED_BLOCK = 22
MEMORY_SIZES = [(1024, 1024), (1456, 1456)]


# ── the loop, as the host runs it ────────────────────────────────────────────────────────────────────────────────

def sigmas(steps=STEPS):
    """MOSSImageScheduler's noise levels: a grid shifted by 5, each snapped to the nearest of the 1000 it was trained on
    — k/1000, each rounded once to float32 — repeats dropped, then a final 0. The grid is made in float32, as
    torch.linspace makes it, so the same levels are chosen."""
    lower = np.float32(SHIFT / (TRAINING_STEPS + SHIFT - 1))
    values = np.linspace(np.float32(1.0), lower, steps, dtype=np.float32)
    wanted = (np.float32(SHIFT) * values / (np.float32(1.0) + np.float32(SHIFT - 1) * values)).astype(np.float32)
    training = (np.arange(1, TRAINING_STEPS + 1, dtype=np.float64) / TRAINING_STEPS).astype(np.float32)
    upper = np.minimum(np.searchsorted(training, wanted), TRAINING_STEPS - 1)
    lower_index = np.maximum(upper - 1, 0)
    nearest = np.where(np.abs(wanted - training[lower_index]) <= np.abs(training[upper] - wanted), lower_index, upper)
    snapped = training[nearest]
    kept = snapped[np.concatenate(([True], snapped[1:] != snapped[:-1]))]
    return np.append(kept, np.float32(0.0)).astype(np.float32)


def rope(height, width, text_length, head_dim=128, base=10000.0):
    """_RoPE.forward: per token the angles of (r, f, h, w) — image (0, L, centred row, centred column), then text
    (0, i, 0, 0) — over head_dim/8 bands, sin of all four then cos of all four: [tokens, head_dim] fp32."""
    bands_count = head_dim // 8
    bands = (1 / (np.float32(base) ** (np.arange(bands_count, dtype=np.float32) / np.float32(bands_count)))).astype(np.float32)
    rows, cols = np.meshgrid(np.arange(height), np.arange(width), indexing="ij")
    image = np.stack([np.zeros_like(rows), np.full_like(rows, text_length), rows, cols], -1).reshape(-1, 4).astype(np.float32)
    image[:, 2] -= (height - 1) / 2
    image[:, 3] -= (width - 1) / 2
    text = np.zeros((text_length, 4), dtype=np.float32)
    text[:, 1] = np.arange(text_length)
    phase = np.concatenate([image, text], 0)[:, :, None] * bands
    return np.concatenate([np.sin(phase), np.cos(phase)], 1).reshape(len(phase), -1).astype(np.float32)


def time_rows(timestep, freq_dim=256):
    """The timestep's sinusoid, computed in float64 as the reference does: [1, freq_dim] fp32."""
    half = freq_dim // 2
    freq = 10000.0 ** (-np.arange(half, dtype=np.float64) / half)
    phase = np.float64(timestep) * freq
    return np.concatenate([np.cos(phase), np.sin(phase)])[None].astype(np.float32)


def draw(denoiser, decoder, caption, negative, noise, strength=0.0, adapter=None):
    """Fifty Euler steps, each asking the denoiser with the prompt and, block 22 left out, with the negative prompt,
    and steering by 3 — MOSSImagePipeline's defaults — then the decoder. A LoRA adapter, where given, is active on every
    pass at `strength`."""
    latents = noise.copy()
    _, _, h, w = latents.shape
    rope_prompt, rope_negative = rope(h, w, caption.shape[1]), rope(h, w, negative.shape[1])
    run = run_options(adapter)
    s = sigmas()
    for k in range(len(s) - 1):
        time = time_rows(s[k] * TRAINING_STEPS)
        ask = lambda rows, rotary, skip: denoiser.run(None, {"latents": latents, "caption": rows, "rope": rotary, "time": time,
                                                            "skip": np.array(skip), "lora_strength": np.array(strength, np.float32)}, run)[0]
        cond = ask(caption, rope_prompt, False)
        uncond = ask(negative, rope_negative, True)
        latents = (latents + (s[k + 1] - s[k]) * (uncond + GUIDANCE * (cond - uncond))).astype(np.float32)
    return pixels_of(decoder, latents)


def draw_dataspace(denoiser, decoder, caption, noise, sampler, strength, adapter):
    """A distillation's data-space loop, as its makers run it: from the noise z and a clean estimate x0 of zeros, each
    step asks the denoiser at z + Q x0 — Q its latent mix — with the step's code as the timestep, and takes z − v as the
    new estimate; no negative prompt. The last estimate is the picture."""
    z = noise.astype(np.float32)
    _, _, h, w = z.shape
    rotary = rope(h, w, caption.shape[1])
    mix = np.asarray(sampler["latent_mix"], dtype=np.float32)
    run = run_options(adapter)
    clean = np.zeros_like(z)
    for code in sampler["step_codes"]:
        latents = (z + np.einsum("oc,bchw->bohw", mix, clean)).astype(np.float32)
        velocity = denoiser.run(None, {"latents": latents, "caption": caption, "rope": rotary, "time": time_rows(code),
                                       "skip": np.array(False), "lora_strength": np.array(strength, np.float32)}, run)[0]
        clean = (z - velocity).astype(np.float32)
    return pixels_of(decoder, clean)


def run_options(adapter):
    run = ort.RunOptions()
    if adapter is not None:
        run.add_active_adapter(adapter)
    return run


def pixels_of(decoder, latents):
    pixels = decoder.run(None, {"latents": latents})[0][0]
    return (pixels.transpose(1, 2, 0) * 255).round().clip(0, 255).astype(np.uint8)


# ── memory ───────────────────────────────────────────────────────────────────────────────────────────────────────

def session(path):
    options = ort.SessionOptions()
    options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    return ort.InferenceSession(str(path), options, providers=[("CUDAExecutionProvider", {"device_id": 0, "arena_extend_strategy": "kSameAsRequested"})])


def measure(graph, part, height, width):
    """The GPU memory one part takes to load and run once at a size, in MiB: the device's use at its peak, less its use
    before the part was loaded. (On Windows the driver gives no process's own use, so the device's is read; nothing else
    should be starting on the card while it is measured.)"""
    import pynvml
    pynvml.nvmlInit()
    device = pynvml.nvmlDeviceGetHandleByIndex(0)
    used = lambda: pynvml.nvmlDeviceGetMemoryInfo(device).used
    before = used()
    peak, running = [before], [True]

    def watch():
        while running[0]:
            peak[0] = max(peak[0], used())
            time.sleep(0.005)

    watcher = threading.Thread(target=watch)
    watcher.start()
    ort.preload_dlls()
    graph_session = session(graph)
    h, w = height // 16, width // 16
    if part == "decoder":
        graph_session.run(None, {"latents": np.random.randn(1, 128, h, w).astype(np.float32)})
    else:
        rows = 300
        graph_session.run(None, {"latents": np.random.randn(1, 128, h, w).astype(np.float32),
                                 "caption": np.random.randn(1, rows, 4096).astype(np.float32),
                                 "rope": rope(h, w, rows), "time": time_rows(500.0), "skip": np.array(False),
                                 "lora_strength": np.array(0.0, np.float32)})
    time.sleep(0.2)
    running[0] = False
    watcher.join()
    print(json.dumps({"peak_mib": round((peak[0] - before) / 2**20)}))


def peak_memory(graph, part, height, width):
    run = subprocess.run([sys.executable, "-I", "-X", "utf8", __file__, "--memory", str(graph), part, str(height), str(width)],
                         capture_output=True, encoding="utf-8")
    if run.returncode:
        raise SystemExit(f"measuring {graph} failed:\n{run.stderr[-3000:]}")
    return json.loads(run.stdout.strip().splitlines()[-1])["peak_mib"]


# ── the check ────────────────────────────────────────────────────────────────────────────────────────────────────

def psnr(a, b):
    x, y = (np.asarray(i, dtype=np.float32) for i in (a, b))
    mse = ((x - y) ** 2).mean()
    return 99.0 if mse == 0 else 10 * math.log10(255**2 / mse)


def main():
    if sys.argv[1] == "--memory":
        measure(Path(sys.argv[2]), sys.argv[3], int(sys.argv[4]), int(sys.argv[5]))
        return
    checkpoint, build, pictures = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    import torch
    from PIL import Image
    sys.path.insert(0, str(Path(__file__).parent))
    from build_moss_image import moss, reference_pipeline

    module = moss(checkpoint)
    scheduler = module.MOSSImageScheduler.from_pretrained(checkpoint / "scheduler")
    scheduler.set_timesteps(STEPS)
    # The same training levels, in the same order; float32's last bit is torch.linspace's own rounding, not the schedule.
    theirs, ours = scheduler.sigmas.numpy().astype(np.float64), sigmas().astype(np.float64)
    if theirs.shape != ours.shape or not np.array_equal(np.rint(theirs * TRAINING_STEPS), np.rint(ours * TRAINING_STEPS)) \
            or np.abs(theirs - ours).max() > 1e-6:
        raise SystemExit("the host's noise levels are not MOSSImageScheduler's")

    pipe = reference_pipeline(module, checkpoint).to("cuda")
    (pictures / "pytorch").mkdir(parents=True, exist_ok=True)
    readings, noises, pytorch_seconds = [], [], []
    for i, prompt in enumerate(PROMPTS):
        height, width = SIZES[i % len(SIZES)]
        noise = torch.randn((1, 128, height // 16, width // 16), generator=torch.Generator("cuda").manual_seed(1000 + i),
                            device="cuda", dtype=torch.float32)
        torch.cuda.synchronize()
        started = time.perf_counter()
        drawn = pipe(prompt, height=height, width=width, num_inference_steps=STEPS, guidance_scale=GUIDANCE,
                     latents=noise).images[0]
        torch.cuda.synchronize()
        pytorch_seconds.append(time.perf_counter() - started)
        drawn.save(pictures / "pytorch" / f"{i:02d}.png")
        with torch.no_grad():
            caption = pipe.encode_prompt(prompt)[0].float().cpu().numpy()
            negative = pipe.encode_prompt("")[0].float().cpu().numpy()
        readings.append((caption, negative))
        noises.append(noise.cpu().numpy())
    del pipe
    torch.cuda.empty_cache()

    ort.preload_dlls()
    decoder = session(build / "decoder" / "decoder.onnx")
    report = {"pytorch": {"seconds_1024": round(float(np.median([s for i, s in enumerate(pytorch_seconds) if SIZES[i % len(SIZES)] == (1024, 1024)])), 1)}}
    denoiser = session(build / "denoiser" / "denoiser.onnx")
    (pictures / "denoiser").mkdir(parents=True, exist_ok=True)
    scores, seconds = [], []
    for i, prompt in enumerate(PROMPTS):
        started = time.perf_counter()
        image = Image.fromarray(draw(denoiser, decoder, *readings[i], noises[i]))
        seconds.append((SIZES[i % len(SIZES)], time.perf_counter() - started))
        image.save(pictures / "denoiser" / f"{i:02d}.png")
        scores.append(psnr(Image.open(pictures / "pytorch" / f"{i:02d}.png").convert("RGB"), image))
        print(f"denoiser {i:02d} psnr {scores[-1]:.1f} dB  {prompt[:60]}", flush=True)
    del denoiser
    report["denoiser"] = {"psnr_min": round(min(scores), 1), "psnr_median": round(float(np.median(scores)), 1),
                          "psnr": [round(s, 1) for s in scores],
                          "seconds_1024": round(float(np.median([t for size, t in seconds if size == (1024, 1024)])), 1),
                          "peak_mib": {f"{h}x{w}": peak_memory(build / "denoiser" / "denoiser.onnx", "denoiser", h, w) for h, w in MEMORY_SIZES}}
    report["decoder"] = {"peak_mib": {f"{h}x{w}": peak_memory(build / "decoder" / "decoder.onnx", "decoder", h, w) for h, w in MEMORY_SIZES}}
    (pictures / "check.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(report), flush=True)


if __name__ == "__main__":
    main()
