"""Hold the FLUX.2 [klein] 4B graphs to Black Forest Labs' pipelines in diffusers, from the same noise.

Each part is checked on its own against the PyTorch model in fp32 — the prompt encoder's rows, the picture encoder's
latents, the decoder's pixels — and then whole edits are made with the graphs, sampled as the host samples, and with
diffusers' Flux2KleinPipeline and Flux2KleinInpaintPipeline in bf16 from the same noise:

  * a picture drawn from a prompt, which every other check edits;
  * an instruction edit of the whole picture;
  * an edit with a second picture to work from;
  * a fill: a masked region drawn again from a prompt (the inpaint pipeline);
  * an expansion: the picture placed on a larger canvas and its new border filled (the inpaint pipeline).

It writes each pair of pictures and their PSNR to <out>/check, and the sampler's figures the host's tests hold its C#
sampler to, <out>/check/klein-sampler.json.

usage: python -I check_flux2_klein.py <FLUX.2-klein-4B checkpoint> <graphs> <out>
"""
import json
import math
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch
from PIL import Image

PROMPT_TOKENS = 512
REFERENCE_SPACING = 10

EDITS = [
    ("Make it night time, the street lamps lit and warm light in the windows", None),
    ("Turn the picture into a watercolour painting", None),
    ("Put the dog from the second picture at one of the tables", "dog"),
]
FILLS = [
    ("a large potted olive tree", (0.30, 0.55, 0.62, 0.95)),
    ("a red vintage bicycle leaning against the wall", (0.05, 0.60, 0.35, 0.98)),
]
SOURCE = "A photo of a red-brick café on a street corner, people at tables outside, morning light"
DOG = "A studio photo of a golden retriever sitting, plain grey background"


def session(path):
    options = ort.SessionOptions()
    options.enable_mem_pattern = False
    return ort.InferenceSession(str(path), options, providers=[("CUDAExecutionProvider", {"arena_extend_strategy": "kSameAsRequested"})])


def psnr(a, b):
    a, b = np.asarray(a, dtype=np.float64), np.asarray(b, dtype=np.float64)
    mse = ((a - b) ** 2).mean()
    return 99.0 if mse == 0 else 10 * math.log10(255.0 ** 2 / mse)


# ── the host's side, as the C# sampler does it ───────────────────────────────────────────────────────────────────

def empirical_mu(image_tokens, steps):
    a1, b1 = 8.73809524e-05, 1.89833333
    a2, b2 = 0.00016927, 0.45666666
    if image_tokens > 4300:
        return a2 * image_tokens + b2
    m200, m10 = a2 * image_tokens + b2, a1 * image_tokens + b1
    a = (m200 - m10) / 190.0
    return a * steps + (m200 - 200.0 * a)


def sigmas(steps, image_tokens):
    """FlowMatchEulerDiscreteScheduler with dynamic exponential shifting at the pipeline's mu, then 0."""
    mu = empirical_mu(image_tokens, steps)
    s = np.linspace(1.0, 1.0 / steps, steps)
    s = math.exp(mu) / (math.exp(mu) + (1.0 / s - 1.0))
    return np.append(s, 0.0).astype(np.float32)


def grid(t, h, w):
    rows, cols = np.meshgrid(np.arange(h), np.arange(w), indexing="ij")
    return np.stack([np.full_like(rows, t), rows, cols, np.zeros_like(rows)], axis=-1).reshape(-1, 4).astype(np.int64)


def text_positions():
    ids = np.zeros((PROMPT_TOKENS, 4), dtype=np.int64)
    ids[:, 3] = np.arange(PROMPT_TOKENS)
    return ids


def tokens_of(latents):
    """[1, 128, h, w] -> [1, h*w, 128], row by row."""
    _, c, h, w = latents.shape
    return latents.reshape(1, c, h * w).transpose(0, 2, 1)


def latents_of(tokens, h, w):
    return tokens.transpose(0, 2, 1).reshape(1, -1, h, w)


class Graphs:
    def __init__(self, folder, tokenizer):
        folder = Path(folder)
        self.tokenizer = tokenizer
        self.encoder = session(folder / "encoder" / "encoder.onnx")
        self.denoiser = session(folder / "denoiser" / "denoiser.onnx")
        self.picture_encoder = session(folder / "picture-encoder" / "picture-encoder.onnx")
        self.decoder = session(folder / "decoder" / "decoder.onnx")

    def prompt(self, text):
        chat = self.tokenizer.apply_chat_template([{"role": "user", "content": text}], tokenize=False,
                                                  add_generation_prompt=True, enable_thinking=False)
        enc = self.tokenizer(chat, padding="max_length", truncation=True, max_length=PROMPT_TOKENS, return_tensors="np")
        return self.encoder.run(None, {"input_ids": enc["input_ids"].astype(np.int64),
                                       "attention_mask": enc["attention_mask"].astype(np.int64)})[0]

    def encode(self, picture):
        return self.picture_encoder.run(None, {"rgb": np.ascontiguousarray(np.asarray(picture.convert("RGB"), dtype=np.uint8))})[0]

    def decode(self, latents):
        pixels = self.decoder.run(None, {"latents": latents.astype(np.float32)})[0][0]
        return Image.fromarray((pixels.transpose(1, 2, 0) * 255.0).round().clip(0, 255).astype(np.uint8))

    def sample(self, prompt, noise, references, steps=4, mask=None, source=None, strength=1.0):
        """noise [1,128,h,w]; references [1,128,hi,wi] each; for a fill, the source's latents and a mask [h*w] (1 to
        draw again) — the inpaint pipeline's loop."""
        _, _, h, w = noise.shape
        n = h * w
        s = sigmas(steps, n)
        start = int(max(steps - min(steps * strength, steps), 0))
        x = tokens_of(noise).astype(np.float32)
        noise_tokens = x.copy()
        if source is not None:
            x0 = tokens_of(source).astype(np.float32)
            x = s[start] * noise_tokens + (1.0 - s[start]) * x0
        refs = [tokens_of(r) for r in references]
        positions = np.concatenate([grid(0, h, w)] + [grid(REFERENCE_SPACING * (i + 1), r.shape[2], r.shape[3])
                                                      for i, r in enumerate(references)])
        conditioning = np.concatenate([x] + refs, axis=1) if refs else x
        text = text_positions()
        for i in range(start, steps):
            tokens = np.concatenate([x] + refs, axis=1).astype(np.float32)
            v = self.denoiser.run(None, {"image_tokens": tokens, "image_positions": positions, "prompt": prompt,
                                         "prompt_positions": text, "timestep": np.array([s[i]], dtype=np.float32)})[0]
            x = x + (s[i + 1] - s[i]) * v[:, :n]
            if mask is not None:
                kept = x0 if i == steps - 1 else s[i + 1] * noise_tokens + (1.0 - s[i + 1]) * x0
                m = mask[None, :, None]
                x = (1.0 - m) * kept + m * x
        return latents_of(x, h, w)


# ── the checks ───────────────────────────────────────────────────────────────────────────────────────────────────

def check_parts(checkpoint, pipe, graphs, out):
    """Each part against its PyTorch model in fp32."""
    lines = []
    from diffusers import AutoencoderKLFlux2, Flux2KleinPipeline
    from transformers import AutoModelForCausalLM
    text_encoder = AutoModelForCausalLM.from_pretrained(checkpoint / "text_encoder", dtype=torch.float32).to("cuda")
    for text in [SOURCE, EDITS[0][0], "Bonjour — une enseigne « Café des Arts »"]:
        with torch.no_grad():
            ref = Flux2KleinPipeline._get_qwen3_prompt_embeds(text_encoder, pipe.tokenizer, text, dtype=torch.float32,
                                                              device="cuda").float().cpu().numpy()
            bf16 = Flux2KleinPipeline._get_qwen3_prompt_embeds(pipe.text_encoder, pipe.tokenizer, text, dtype=torch.float32,
                                                               device="cuda").float().cpu().numpy()
        cosine = lambda a, b: (a * b).sum(-1) / (np.linalg.norm(a, axis=-1) * np.linalg.norm(b, axis=-1) + 1e-12)
        got, own = cosine(ref, graphs.prompt(text)), cosine(ref, bf16)
        lines.append(f"prompt encoder: rows' cosine to Qwen3 in fp32 min {got.min():.4f}, mean {got.mean():.5f}; "
                     f"the pipeline's own bf16 min {own.min():.4f}, mean {own.mean():.5f}")
    del text_encoder
    torch.cuda.empty_cache()
    vae = AutoencoderKLFlux2.from_pretrained(checkpoint / "vae", torch_dtype=torch.float32).to("cuda").eval()
    picture = Image.open(out / "source-pytorch.png").convert("RGB")
    with torch.no_grad():
        x = torch.from_numpy((np.asarray(picture, dtype=np.float32) / 255.0).transpose(2, 0, 1)[None]).cuda()
        z = vae.encode(x * 2 - 1).latent_dist.mode()
        mean = vae.bn.running_mean.view(1, -1, 1, 1).float()
        std = torch.sqrt(vae.bn.running_var.view(1, -1, 1, 1).float() + vae.config.batch_norm_eps)
        ref = ((Flux2KleinPipeline._patchify_latents(z) - mean) / std).cpu().numpy()
    got = graphs.encode(picture)
    lines.append(f"picture encoder: max difference {np.abs(ref - got).max():.4g} on latents up to {np.abs(ref).max():.4g}")
    with torch.no_grad():
        z = Flux2KleinPipeline._unpatchify_latents(torch.from_numpy(ref).cuda() * std + mean)
        pixels = (vae.decode(z, return_dict=False)[0].float() / 2 + 0.5).clamp(0, 1)
        ref_picture = Image.fromarray((pixels[0].permute(1, 2, 0).cpu().numpy() * 255).round().astype(np.uint8))
    lines.append(f"decoder: {psnr(ref_picture, graphs.decode(ref)):.1f} dB from the VAE in fp32")
    del vae
    torch.cuda.empty_cache()
    return lines


def noise_for(seed, h, w):
    g = torch.Generator("cuda").manual_seed(seed)
    return torch.randn((1, 128, h, w), generator=g, device="cuda", dtype=torch.bfloat16)


def fill_mask(width, height, box):
    m = np.zeros((height, width), dtype=np.float32)
    x0, y0, x1, y1 = box
    m[int(y0 * height):int(y1 * height), int(x0 * width):int(x1 * width)] = 1.0
    return m


def packed_mask(mask, h, w):
    t = torch.nn.functional.interpolate(torch.from_numpy(mask)[None, None], size=(h, w), mode="bilinear")
    return t.reshape(-1).numpy()


def main():
    checkpoint, folder, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]) / "check"
    out.mkdir(parents=True, exist_ok=True)
    from diffusers import Flux2KleinPipeline, Flux2KleinInpaintPipeline
    pipe = Flux2KleinPipeline.from_pretrained(checkpoint, torch_dtype=torch.bfloat16).to("cuda")
    inpaint = Flux2KleinInpaintPipeline(**pipe.components)
    graphs = Graphs(folder, pipe.tokenizer)
    report = []

    # the pictures every check starts from, drawn by the pipeline itself
    pipe(prompt=SOURCE, height=1024, width=1024, num_inference_steps=4, guidance_scale=1.0,
         latents=noise_for(1, 64, 64)).images[0].save(out / "source-pytorch.png")
    pipe(prompt=DOG, height=768, width=768, num_inference_steps=4, guidance_scale=1.0,
         latents=noise_for(2, 48, 48)).images[0].save(out / "dog-pytorch.png")
    source = Image.open(out / "source-pytorch.png").convert("RGB")
    dog = Image.open(out / "dog-pytorch.png").convert("RGB")
    drawn = graphs.decode(graphs.sample(graphs.prompt(SOURCE), noise_for(1, 64, 64).float().cpu().numpy(), []))
    drawn.save(out / "source-onnx.png")
    report.append(f"drawn from a prompt: {psnr(source, drawn):.1f} dB")
    report += check_parts(checkpoint, pipe, graphs, out)

    for i, (instruction, extra) in enumerate(EDITS):
        pictures = [source] + ([dog] if extra else [])
        noise = noise_for(10 + i, 64, 64)
        ref = pipe(image=pictures, prompt=instruction, num_inference_steps=4, guidance_scale=1.0, latents=noise).images[0]
        got = graphs.decode(graphs.sample(graphs.prompt(instruction), noise.float().cpu().numpy(),
                                          [graphs.encode(p) for p in pictures]))
        ref.save(out / f"edit{i}-pytorch.png")
        got.save(out / f"edit{i}-onnx.png")
        report.append(f"edit '{instruction}': {psnr(ref, got):.1f} dB")

    for i, (prompt, box) in enumerate(FILLS):
        mask = fill_mask(1024, 1024, box)
        noise = noise_for(20 + i, 64, 64)
        ref = inpaint(prompt=prompt, image=source, mask_image=Image.fromarray((mask * 255).astype(np.uint8)),
                      strength=1.0, num_inference_steps=4, guidance_scale=1.0, latents=noise).images[0]
        latents = graphs.encode(source)
        got = graphs.decode(graphs.sample(graphs.prompt(prompt), noise.float().cpu().numpy(), [latents],
                                          mask=packed_mask(mask, 64, 64), source=latents))
        ref.save(out / f"fill{i}-pytorch.png")
        got.save(out / f"fill{i}-onnx.png")
        report.append(f"fill '{prompt}': {psnr(ref, got):.1f} dB")

    # expansion: the picture, at 832x832, centred on a canvas 1024 wide — the pipeline works within a megapixel — and
    # the new border filled
    wide = Image.new("RGB", (1024, 832), (127, 127, 127))
    wide.paste(source.resize((832, 832), Image.LANCZOS), (96, 0))
    mask = np.ones((832, 1024), dtype=np.float32)
    mask[:, 96:928] = 0.0
    noise = noise_for(30, 52, 64)
    prompt = "Continue the street to both sides"
    ref = inpaint(prompt=prompt, image=wide, mask_image=Image.fromarray((mask * 255).astype(np.uint8)), strength=1.0,
                  num_inference_steps=4, guidance_scale=1.0, latents=noise).images[0]
    latents = graphs.encode(wide)
    got = graphs.decode(graphs.sample(graphs.prompt(prompt), noise.float().cpu().numpy(), [latents],
                                      mask=packed_mask(mask, 52, 64), source=latents))
    ref.save(out / "expand-pytorch.png")
    got.save(out / "expand-onnx.png")
    report.append(f"expand '{prompt}': {psnr(ref, got):.1f} dB")

    fixture = {"steps": 4, "cases": [{"imageTokens": n, "sigmas": [float(v) for v in sigmas(4, n)]}
                                     for n in (256, 1024, 4096, 5120, 6400)]}
    with torch.no_grad():
        pipe.scheduler.set_timesteps(sigmas=np.linspace(1.0, 0.25, 4), mu=empirical_mu(4096, 4), device="cpu")
        fixture["diffusers4096"] = [float(v) for v in pipe.scheduler.sigmas]
    (out / "klein-sampler.json").write_text(json.dumps(fixture, indent=1))
    (out / "report.txt").write_text("\n".join(report) + "\n", encoding="utf-8")
    print("\n".join(report))


if __name__ == "__main__":
    main()
