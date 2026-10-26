"""Check each LoRA's adapter against MOSS-Image's PyTorch pipeline with the LoRA applied as its makers apply it —
unmerged, each projection's output plus strength · alpha/r · B A x in fp32, read straight from the LoRA's own file — the
same prompts drawn by both from the same noise, the build in ONNX Runtime with the adapter active and the sampling loop
written out as the host runs it. A style LoRA is drawn by MOSS-Image's own fifty guided steps, its trigger leading the
prompt; the distillation by its own data-space loop, its timestep path replaced, with no negative prompt. Each prompt is
drawn by PyTorch without the LoRA too, so what the LoRA does shows beside how closely the build follows it.

usage: python -I check_moss_loras.py <moss-image checkpoint> <build-dir> <loras.json> <downloads-dir> <pictures-dir> [<lora id>...]
"""
import json
import sys
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
from build_moss_image import moss, reference_pipeline, TIME_PARAMETERS
from check_moss_image import draw, draw_dataspace, psnr, session
from convert_moss_loras import parse

PROMPTS = [
    "A portrait of a young woman reading a book by a café window, soft afternoon light.",
    "A fox standing in a forest clearing at dawn, mist between the trees.",
    "A small harbour town with colourful houses and fishing boats at sunset.",
]
SIZE = 1024


def with_trigger(entry, prompt):
    return f"{entry['trigger']}, {prompt}" if entry.get("trigger") else prompt


class Applied:
    """A LoRA added to the PyTorch transformer for as long as it is held, and, for a distillation, its timestep path."""

    def __init__(self, transformer, matrices, time, strength):
        self.hooks, self.saved, self.dtypes = [], {}, {}
        for (layer, projection), (down, up, scale) in matrices.items():
            a = torch.from_numpy(down).cuda()
            b = torch.from_numpy(up * scale).cuda()

            def add(_m, inputs, out, a=a, b=b):
                return out + (torch.nn.functional.linear(torch.nn.functional.linear(inputs[0].float(), a), b) * strength).to(out.dtype)
            self.hooks.append(transformer.layers[layer].proj[projection].register_forward_hook(add))
        if time:
            # The distillation keeps its timestep path in fp32, as its makers ask.
            for module in ("time_embedding", "shared_block_modulation", "video_head_modulation"):
                self.dtypes[module] = next(transformer.get_submodule(module).parameters()).dtype
                transformer.get_submodule(module).float()
            for name, param in TIME_PARAMETERS.items():
                target = transformer.get_parameter(param)
                self.saved[param] = target.detach().clone()
                target.data = torch.from_numpy(time[name]).to(target.device, target.dtype)
        self.transformer = transformer

    def close(self):
        for hook in self.hooks:
            hook.remove()
        for param, value in self.saved.items():
            self.transformer.get_parameter(param).data = value
        for module, dtype in self.dtypes.items():
            self.transformer.get_submodule(module).to(dtype)


def decode(pipe, latents):
    """MOSSImagePipeline's own decoding of finished latents."""
    vae = pipe.vae
    z = latents.to(vae.dtype)
    z = z * (vae.bn.running_var + vae.config.batch_norm_eps).sqrt().to(z).view(1, -1, 1, 1) + vae.bn.running_mean.to(z).view(1, -1, 1, 1)
    b, c, h, w = z.shape
    z = z.reshape(b, c // 4, 2, 2, h, w).permute(0, 1, 4, 2, 5, 3).reshape(b, c // 4, 2 * h, 2 * w)
    return pipe.image_processor.postprocess(vae.decode(z, return_dict=False)[0].clamp(-1, 1), output_type="pil")[0]


def dataspace_reference(pipe, embeds, mask, noise, sampler):
    """The distillation's loop in PyTorch, as its model card writes it."""
    mix = torch.tensor(sampler["latent_mix"], device="cuda", dtype=torch.float32)
    z = noise.float()
    clean = torch.zeros_like(z)
    with torch.no_grad():
        for code in sampler["step_codes"]:
            x = z + torch.einsum("oc,bchw->bohw", mix, clean)
            v = pipe.transformer(x, code, embeds, mask).sample.float()
            clean = z - v
        return decode(pipe, clean)


def main():
    checkpoint, build, listing, downloads, pictures = (Path(a) for a in sys.argv[1:6])
    wanted = set(sys.argv[6:])
    entries = [e for e in json.loads(listing.read_text(encoding="utf-8"))["loras"] if not wanted or e["id"] in wanted]
    pipe = reference_pipeline(moss(checkpoint), checkpoint).to("cuda")

    ort.preload_dlls()
    denoiser, decoder = session(build / "denoiser" / "denoiser.onnx"), session(build / "decoder" / "decoder.onnx")
    report = {}
    for entry in entries:
        matrices, time_path, _ = parse(entry, downloads / entry["id"] / entry["file"])
        adapter = ort.LoraAdapter()
        adapter.Load(str(build / "loras" / f"{entry['id']}.onnx_adapter"))
        sampler_path = build / "loras" / f"{entry['id']}.sampler.json"
        sampler = json.loads(sampler_path.read_text()) if sampler_path.exists() else None
        strength = float(entry["strength"])
        folder = pictures / entry["id"]
        folder.mkdir(parents=True, exist_ok=True)
        follows, effect, seconds = [], [], []
        for i, prompt in enumerate(PROMPTS):
            text = with_trigger(entry, prompt)
            noise = torch.randn((1, 128, SIZE // 16, SIZE // 16), generator=torch.Generator("cuda").manual_seed(2000 + i),
                                device="cuda", dtype=torch.float32)
            with torch.no_grad():
                embeds, mask = pipe.encode_prompt(text)
                negative = pipe.encode_prompt("")[0]
            pipe(prompt_embeds=embeds, negative_prompt_embeds=negative, height=SIZE, width=SIZE, latents=noise).images[0] \
                .save(folder / f"without-{i:02d}.png")
            applied = Applied(pipe.transformer, matrices, time_path, strength)
            try:
                if sampler:
                    with_lora = dataspace_reference(pipe, embeds, mask, noise, sampler)
                else:
                    with_lora = pipe(prompt_embeds=embeds, negative_prompt_embeds=negative, height=SIZE, width=SIZE, latents=noise).images[0]
            finally:
                applied.close()
            with_lora.save(folder / f"pytorch-{i:02d}.png")

            caption, nothing = embeds.float().cpu().numpy(), negative.float().cpu().numpy()
            started = time.perf_counter()
            pixels = (draw_dataspace(denoiser, decoder, caption, noise.cpu().numpy(), sampler, strength, adapter) if sampler
                      else draw(denoiser, decoder, caption, nothing, noise.cpu().numpy(), strength, adapter))
            seconds.append(time.perf_counter() - started)
            image = Image.fromarray(pixels)
            image.save(folder / f"onnx-{i:02d}.png")
            follows.append(psnr(with_lora.convert("RGB"), image))
            effect.append(psnr(Image.open(folder / f"without-{i:02d}.png").convert("RGB"), with_lora.convert("RGB")))
            print(f"{entry['id']} {i:02d}: build against PyTorch with the LoRA {follows[-1]:.1f} dB; "
                  f"PyTorch with it against without {effect[-1]:.1f} dB; {seconds[-1]:.1f} s", flush=True)
        report[entry["id"]] = {"psnr_median": round(float(np.median(follows)), 1), "psnr": [round(s, 1) for s in follows],
                               "change_psnr_median": round(float(np.median(effect)), 1),
                               "seconds_1024": round(float(np.median(seconds)), 1)}
    (pictures / "loras.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(report), flush=True)


if __name__ == "__main__":
    main()