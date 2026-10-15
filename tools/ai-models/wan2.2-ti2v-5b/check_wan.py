"""Make clips with the built ONNX graphs and with the PyTorch models from the same noise, and compare them.

Each build — FastWan, three steps, and Wan2.2, fifty guided steps — makes the same prompts from the same seeds both ways,
the full model one of them from a picture — FastWan, distilled from text alone, takes none; the clips are compared frame
by frame (PSNR). Fifty guided steps carry a small
difference a long way, so the full model is held to its own bf16-against-fp32 drift, measured here too.

usage: python -I -X utf8 check_wan.py <Wan2.2-TI2V-5B-Diffusers> <FastWan2.2-TI2V-5B-Diffusers> <build-dir> <work-dir>
"""
import json
import math
import sys
import time
from pathlib import Path

import numpy as np
import torch

HEIGHT, WIDTH, FRAMES = 704, 1280, 121
LATENT = (48, (FRAMES - 1) // 4 + 1, HEIGHT // 16, WIDTH // 16)
TEXT_ROWS = 512
FAST_TIMESTEPS = [1000, 757, 522]
FAST_SHIFT = 8.0
BASE_STEPS, BASE_SHIFT, BASE_GUIDANCE = 50, 5.0, 5.0
# Wan2.2's own negative prompt (wan/configs/shared_config.py at 1ea34ff4), which its guidance steers away from.
NEGATIVE = ("色调艳丽，过曝，静态，细节模糊不清，字幕，风格，作品，画作，画面，静止，整体发灰，最差质量，低质量，JPEG压缩残留，"
            "丑陋的，残缺的，多余的手指，画得不好的手部，画得不好的脸部，畸形的，毁容的，形态畸形的肢体，手指融合，静止不动的画面，"
            "杂乱的背景，三条腿，背景人很多，倒着走")

PROMPTS = [
    ("A red fox trots through fresh snow in a birch forest at dawn, its breath steaming; the camera tracks alongside at ground level.", None),
    ("Close-up of a barista pouring steamed milk into a cup of espresso, forming a heart in the latte art, warm cafe light.", None),
    ("A busy city street at night in the rain, neon signs reflecting in puddles, people with umbrellas crossing; slow dolly forward.", None),
    ("The fox stops, turns its head towards the camera and sits down in the snow as flakes begin to fall.", "fox"),
]


def psnr(a, b):
    mse = float(((a.astype(np.float64) - b.astype(np.float64)) ** 2).mean())
    return 99.0 if mse == 0 else 10 * math.log10(255.0 ** 2 / mse)


def dmd_sigma(t):
    """FastVideo's training-noise table: shift 8 over 1000 levels; a timestep's level is the one nearest it."""
    s = np.linspace(1, 1000, 1000, dtype=np.float32)[::-1].copy().astype(np.float64) / 1000.0
    s = FAST_SHIFT * s / (1 + (FAST_SHIFT - 1) * s)
    return float(s[np.argmin(np.abs(s * 1000 - t))])


def picture(work, name):
    """The picture a clip starts from: the first frame of the fox clip FastWan made in PyTorch, the check's first."""
    return np.load(work / "fast-bf16-0.npy")[0] if name == "fox" else None


# ── PyTorch ──────────────────────────────────────────────────────────────────────────────────────────────────────

def torch_pipeline(base, fast, dtype):
    from diffusers import AutoencoderKLWan, WanPipeline, WanTransformer3DModel
    vae = AutoencoderKLWan.from_pretrained(base, subfolder="vae", torch_dtype=torch.float32)
    pipe = WanPipeline.from_pretrained(base, vae=vae, torch_dtype=dtype).to("cuda")
    fast_tr = WanTransformer3DModel.from_pretrained(fast, subfolder="transformer", torch_dtype=dtype).to("cuda") if fast else None
    return pipe, fast_tr


def torch_clip(pipe, transformer, prompt, start, seed, fast):
    """One clip as the published pipelines make it, from the noise the seed gives."""
    vae = pipe.vae
    mean = torch.tensor(vae.config.latents_mean).view(1, 48, 1, 1, 1).cuda()
    std = torch.tensor(vae.config.latents_std).view(1, 48, 1, 1, 1).cuda()
    dtype = transformer.dtype
    g = torch.Generator("cuda").manual_seed(seed)
    with torch.no_grad():
        embeds, negative = pipe.encode_prompt(prompt, NEGATIVE if not fast else None, do_classifier_free_guidance=not fast,
                                              max_sequence_length=TEXT_ROWS, device="cuda")
        x = torch.randn((1, *LATENT), generator=g, device="cuda", dtype=torch.float32)
        mask = torch.ones_like(x)
        condition = torch.zeros_like(x)
        if start is not None:
            image = torch.from_numpy(start).cuda().permute(2, 0, 1).float().div(127.5).sub(1).view(1, 3, 1, HEIGHT, WIDTH)
            condition[:, :, :1] = (vae.encode(image).latent_dist.mode() - mean) / std
            mask[:, :, 0] = 0
        per_frame = lambda t: (mask[0, 0, :, ::2, ::2] * t).flatten().unsqueeze(0)
        if fast:
            for i, t in enumerate(FAST_TIMESTEPS):
                x = (1 - mask) * condition + mask * x
                v = transformer(x.to(dtype), timestep=per_frame(float(t)), encoder_hidden_states=embeds, return_dict=False)[0].double()
                x0 = x.double() - dmd_sigma(t) * v
                if i < len(FAST_TIMESTEPS) - 1:
                    nxt = dmd_sigma(FAST_TIMESTEPS[i + 1])
                    x = ((1 - nxt) * x0 + nxt * torch.randn(x.shape, generator=g, device="cuda", dtype=torch.float64)).float()
                else:
                    x = x0.float()
        else:
            from diffusers import UniPCMultistepScheduler
            scheduler = UniPCMultistepScheduler.from_config(pipe.scheduler.config)
            scheduler.set_timesteps(BASE_STEPS, device="cuda")
            for t in scheduler.timesteps:
                x = (1 - mask) * condition + mask * x
                ts = per_frame(t.float())
                cond = transformer(x.to(dtype), timestep=ts, encoder_hidden_states=embeds, return_dict=False)[0].float()
                uncond = transformer(x.to(dtype), timestep=ts, encoder_hidden_states=negative, return_dict=False)[0].float()
                x = scheduler.step(uncond + BASE_GUIDANCE * (cond - uncond), t, x, return_dict=False)[0]
        x = (1 - mask) * condition + mask * x
        video = vae.decode(x * std + mean, return_dict=False)[0]
    return ((video[0].clamp(-1, 1) + 1) * 127.5).round().to(torch.uint8).permute(1, 2, 3, 0).cpu().numpy()


# ── ONNX ─────────────────────────────────────────────────────────────────────────────────────────────────────────

def session(path):
    import onnxruntime as ort
    options = ort.SessionOptions()
    options.log_severity_level = 3
    return ort.InferenceSession(str(path), options, providers=[("CUDAExecutionProvider", {"device_id": 0})])


def onnx_text(encoder, tokenizer, prompt):
    from diffusers.pipelines.wan.pipeline_wan import prompt_clean
    ids = tokenizer(prompt_clean(prompt), padding="max_length", max_length=TEXT_ROWS, truncation=True,
                    add_special_tokens=True, return_attention_mask=True, return_tensors="np")
    length = int(ids.attention_mask.sum())
    rows = encoder.run(None, {"input_ids": ids.input_ids[:, :length].astype(np.int64)})[0]
    text = np.zeros((1, TEXT_ROWS, 4096), np.float32)
    text[:, :length] = rows
    return text


def onnx_decode(first, nxt, latents):
    """The clip's latents to its frames, a latent frame at a time, the cache kept on the GPU."""
    names = [i.name for i in nxt.get_inputs()][1:]
    outs = [o.name for o in nxt.get_outputs()]
    b = first.io_binding()
    b.bind_cpu_input("latent", np.ascontiguousarray(latents[:, :, :1]))
    for o in first.get_outputs():
        b.bind_output(o.name, "cpu" if o.name == "frames" else "cuda")
    first.run_with_iobinding(b)
    values = b.get_outputs()
    frames, cache = [values[0].numpy()], values[1:]
    for i in range(1, latents.shape[2]):
        b = nxt.io_binding()
        b.bind_cpu_input("latent", np.ascontiguousarray(latents[:, :, i:i + 1]))
        for n, v in zip(names, cache):
            b.bind_ortvalue_input(n, v)
        for o in outs:
            b.bind_output(o, "cpu" if o == "frames" else "cuda")
        nxt.run_with_iobinding(b)
        values = b.get_outputs()
        frames.append(values[0].numpy())
        cache = values[1:]
    return np.concatenate(frames)


def onnx_clip(parts, tokenizer, prompt, start, seed, fast, timings):
    """One clip through the graphs, the sampler driven here as the host drives it, from the same noise."""
    g = torch.Generator("cuda").manual_seed(seed)
    t0 = time.time()
    text = onnx_text(parts["encoder"], tokenizer, prompt)
    negative = None if fast else onnx_text(parts["encoder"], tokenizer, NEGATIVE)
    x = torch.randn((1, *LATENT), generator=g, device="cuda", dtype=torch.float32).cpu().numpy()
    mask = np.ones_like(x)
    condition = np.zeros_like(x)
    if start is not None:
        condition[:, :, :1] = parts["frame"].run(None, {"rgb": start})[0]
        mask[:, :, 0] = 0
    per_frame = lambda t: (mask[0, 0, :, 0, 0] * t).astype(np.float32)[None]
    denoiser = parts["fast" if fast else "base"]
    t1 = time.time()
    if fast:
        for i, t in enumerate(FAST_TIMESTEPS):
            x = (1 - mask) * condition + mask * x
            v = denoiser.run(None, {"latents": x.astype(np.float32), "timesteps": per_frame(t), "text": text})[0].astype(np.float64)
            x0 = x.astype(np.float64) - dmd_sigma(t) * v
            if i < len(FAST_TIMESTEPS) - 1:
                nxt = dmd_sigma(FAST_TIMESTEPS[i + 1])
                noise = torch.randn(x.shape, generator=g, device="cuda", dtype=torch.float64).cpu().numpy()
                x = ((1 - nxt) * x0 + nxt * noise).astype(np.float32)
            else:
                x = x0.astype(np.float32)
    else:
        from diffusers import UniPCMultistepScheduler
        scheduler = UniPCMultistepScheduler.from_pretrained(parts["base_folder"], subfolder="scheduler")
        scheduler.set_timesteps(BASE_STEPS)
        xt = torch.from_numpy(x)
        for t in scheduler.timesteps:
            xt = torch.from_numpy((1 - mask) * condition) + torch.from_numpy(mask) * xt
            feed = {"latents": xt.numpy().astype(np.float32), "timesteps": per_frame(float(t))}
            cond = denoiser.run(None, {**feed, "text": text})[0]
            uncond = denoiser.run(None, {**feed, "text": negative})[0]
            xt = scheduler.step(torch.from_numpy(uncond + BASE_GUIDANCE * (cond - uncond)), t, xt, return_dict=False)[0]
        x = xt.numpy()
    x = ((1 - mask) * condition + mask * x).astype(np.float32)
    t2 = time.time()
    frames = onnx_decode(parts["decoder_first"], parts["decoder"], x)
    t3 = time.time()
    timings.append({"prompt": t1 - t0, "denoise": t2 - t1, "decode": t3 - t2})
    return frames


def main():
    base, fast, build, work = (Path(a) for a in sys.argv[1:5])
    work.mkdir(parents=True, exist_ok=True)
    results = {}
    for build_name, is_fast in (("fast", True), ("base", False)):
        # The PyTorch clips, bf16 as published — and for the full model fp32 too, to know its own drift.
        precisions = [("bf16", torch.bfloat16)] + ([] if is_fast else [("fp32", torch.float32)])
        for label, dtype in precisions:
            # fp32 for one clip: fifty fp32 steps take half an hour, and one clip says how far the model drifts. FastWan,
            # distilled from text alone, makes none from a picture; a clip already made is kept.
            wanted = [n for n, (_, start) in enumerate(PROMPTS)
                      if (label == "bf16" or n == 0) and not (is_fast and start is not None)
                      and not (work / f"{build_name}-{label}-{n}.npy").exists()]
            if not wanted:
                continue
            pipe, fast_tr = torch_pipeline(base, fast if is_fast else None, dtype)
            transformer = fast_tr if is_fast else pipe.transformer
            for n in wanted:
                prompt, start = PROMPTS[n]
                clip = torch_clip(pipe, transformer, prompt, picture(work, start), 2000 + n, is_fast)
                np.save(work / f"{build_name}-{label}-{n}.npy", clip)
                print(f"pytorch {build_name} {label} clip {n}", flush=True)
            del pipe, fast_tr, transformer
            torch.cuda.empty_cache()

    from transformers import AutoTokenizer
    tokenizer = AutoTokenizer.from_pretrained(base, subfolder="tokenizer")
    parts = {"encoder": session(build / "prompt-encoder" / "prompt-encoder.onnx"),
             "frame": session(build / "frame-encoder" / "frame-encoder.onnx"),
             "decoder_first": session(build / "decoder" / "decoder-first.onnx"),
             "decoder": session(build / "decoder" / "decoder.onnx"),
             "base_folder": str(base)}
    for build_name, is_fast in (("fast", True), ("base", False)):
        parts["fast" if is_fast else "base"] = session(build / ("denoiser-fast" if is_fast else "denoiser") / "denoiser.onnx")
        timings, scores = [], []
        for n, (prompt, start) in enumerate(PROMPTS):
            if is_fast and start is not None:
                continue   # FastWan, distilled from text alone, takes no picture
            ours = onnx_clip(parts, tokenizer, prompt, picture(work, start), 2000 + n, is_fast, timings)
            np.save(work / f"{build_name}-onnx-{n}.npy", ours)
            ref = np.load(work / f"{build_name}-bf16-{n}.npy")
            score = {"onnx_vs_bf16": psnr(ours, ref), "starts_from_picture": start is not None}
            if (work / f"{build_name}-fp32-{n}.npy").exists():
                fp32 = np.load(work / f"{build_name}-fp32-{n}.npy")
                score["onnx_vs_fp32"] = psnr(ours, fp32)
                score["bf16_vs_fp32"] = psnr(ref, fp32)
            scores.append(score)
            print(f"{build_name} clip {n}: " + ", ".join(f"{k} {v:.1f} dB" if isinstance(v, float) else f"{k} {v}" for k, v in score.items())
                  + f"; {timings[-1]['denoise']:.1f} s denoising, {timings[-1]['decode']:.1f} s decoding", flush=True)
        results[build_name] = {"scores": scores, "timings": timings}
        del parts["fast" if is_fast else "base"]
    (work / "check.json").write_text(json.dumps(results, indent=1))

    # Unrelated clips are 5-9.5 dB apart; the builds' clips are the PyTorch models' clips, fine detail drifting over the
    # steps. The full model is held to its own drift: no further from it in fp32 than its bf16 pipeline is.
    failed = []
    for build_name, result in results.items():
        values = sorted(s["onnx_vs_bf16"] for s in result["scores"])
        if values[0] < 14 or values[len(values) // 2] < 16:
            failed.append(f"{build_name}: {values[0]:.1f} dB at worst, {values[len(values) // 2]:.1f} dB median")
        for s in result["scores"]:
            if "onnx_vs_fp32" in s and s["onnx_vs_fp32"] < s["bf16_vs_fp32"] - 1:
                failed.append(f"{build_name}: {s['onnx_vs_fp32']:.1f} dB from fp32, where bf16 is {s['bf16_vs_fp32']:.1f} dB")
    if failed:
        raise SystemExit("the build's clips are not the PyTorch models' clips: " + "; ".join(failed))
    print("check passed", flush=True)


if __name__ == "__main__":
    main()
