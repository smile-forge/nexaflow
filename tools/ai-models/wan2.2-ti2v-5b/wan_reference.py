"""What Wan2.2's own sampling and text handling do on small made-up inputs, so the host's port can be checked against it:
the UniPC scheduler's timesteps and noise levels for fifty steps at shift 5 and the samples a made-up model's guided flow
steps to; the noise levels FastWan's distilled timesteps stand for; prompts cleaned as Wan's pipeline cleans them; and
their tokens, as UMT5's tokenizer reads them.

usage: python -I -X utf8 wan_reference.py <Wan2.2-TI2V-5B-Diffusers> src/Nexaflow.Tests/Nexaflow.Tests.AiHost/Filming/wan-reference.json
"""
import html
import json
import re
import sys

import ftfy
import numpy as np
import torch

STEPS, SHIFT, GUIDANCE = 50, 5.0, 5.0
DISTILLED = [1000, 757, 522]

CLEANING = [
    "A fox’s “first” snow — at dawn.",
    "Tom &amp;amp; Jerry &lt;3",
    "  tabs\tand\nnew\r\nlines   everywhere  ",
    "ﬁne ﬂour ＡＢＣ　１２３",
    "\x1b[36mcoloured\x1b[0m text\x07",
    "Café — naïve façade, Größe 10.000€",
    "色调艳丽，过曝，静态，细节模糊不清",
]

TOKENS = [
    "A red fox trots through fresh snow in a birch forest at dawn, its breath steaming; the camera tracks alongside at ground level.",
    "A café chalkboard menu that reads 'Plat du jour : Ratatouille 12 €' in neat handwritten chalk lettering.",
    "A German street sign that reads 'Bahnhofstraße', 1999 trams, Übergröße — Größe 10.000!",
    "一只红狐狸在雪地里奔跑，镜头跟随。",
    "色调艳丽，过曝，静态，细节模糊不清，字幕，风格，作品，画作，画面，静止，整体发灰，最差质量，低质量，JPEG压缩残留，丑陋的，残缺的，"
    "多余的手指，画得不好的手部，画得不好的脸部，畸形的，毁容的，形态畸形的肢体，手指融合，静止不动的画面，杂乱的背景，三条腿，背景人很多，倒着走",
]


def clean(text):
    """Wan's prompt_clean: ftfy, HTML unescaped twice, stripped, whitespace run together."""
    text = html.unescape(html.unescape(ftfy.fix_text(text))).strip()
    return re.sub(r"\s+", " ", text).strip()


def main():
    base, out = sys.argv[1:3]
    from diffusers import UniPCMultistepScheduler
    from transformers import AutoTokenizer

    sched = UniPCMultistepScheduler.from_pretrained(base, subfolder="scheduler")
    sched.set_timesteps(STEPS)
    record = {"steps": STEPS, "shift": SHIFT, "guidance": GUIDANCE,
              "timesteps": [int(t) for t in sched.timesteps], "sigmas": [float(s) for s in sched.sigmas]}

    # A made-up "model": a flow that depends on the sample and the step, guided as the pipeline guides it.
    g = torch.Generator().manual_seed(7)
    z = torch.randn(1, 4, 6, generator=g)
    record["start"] = z.flatten().tolist()
    cond_bias, uncond_bias = torch.randn(1, 4, 6, generator=g), torch.randn(1, 4, 6, generator=g)
    trace = []
    for i, t in enumerate(sched.timesteps):
        cond = torch.tanh(0.1 * z + cond_bias * (i + 1) / STEPS)
        uncond = torch.tanh(0.1 * z + uncond_bias * (i + 1) / STEPS)
        z = sched.step(uncond + GUIDANCE * (cond - uncond), t, z, return_dict=False)[0]
        if i in (0, 1, 2, 24, 48, 49):
            trace.append({"step": i, "sample": z.flatten().tolist()})
    record["cond_bias"], record["uncond_bias"], record["trace"] = cond_bias.flatten().tolist(), uncond_bias.flatten().tolist(), trace

    # FastVideo's training-noise table (FlowMatchEulerDiscreteScheduler.__init__ at shift DMD_TRAINING_NOISE_SHIFT = 8,
    # fastvideo at ba371e33): 1000 levels made and shifted in float32, read in float64 by the timestep nearest.
    timesteps = torch.from_numpy(np.linspace(1, 1000, 1000, dtype=np.float32)[::-1].copy())
    sigmas = timesteps / 1000
    sigmas = 8.0 * sigmas / (1 + (8.0 - 1) * sigmas)
    sigmas, timesteps = sigmas.double(), (sigmas * 1000).double()
    record["distilled"] = [{"timestep": t, "sigma": float(sigmas[torch.argmin((timesteps - t).abs())])} for t in DISTILLED]

    record["cleaning"] = [{"text": t, "clean": clean(t)} for t in CLEANING]
    tokenizer = AutoTokenizer.from_pretrained(base, subfolder="tokenizer")
    record["tokens"] = [{"text": t, "ids": tokenizer(clean(t), add_special_tokens=True).input_ids} for t in TOKENS]

    with open(out, "w", encoding="utf-8") as f:
        json.dump(record, f, ensure_ascii=False)
    print("timesteps", record["timesteps"][:5], "...", record["timesteps"][-3:])
    print("distilled", record["distilled"])


if __name__ == "__main__":
    main()
