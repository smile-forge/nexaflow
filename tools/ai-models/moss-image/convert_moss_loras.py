"""Convert the LoRAs loras.json lists into ONNX Runtime adapters for the denoiser build_moss_image.py builds.

Each LoRA is a pair of matrices for some of the 42 blocks' q, k, v, o, ffn_up_gate and ffn_down projections, written in
one of three ways — kohya's (lora_down, lora_up, alpha), PEFT's (lora_A, lora_B, alpha in the metadata), or a
distillation's generator file (lora_A, lora_B, with the timestep path it replaces and its sampler's data). Each becomes
the denoiser's inputs: lora.{block}.{projection}.down = A transposed [in, r] and .up = B transposed times alpha / r
[r, out], in fp16, so the graph adds (x down) up; and for a distillation, the time.* inputs in fp32. Nothing is merged:
the LoRAs' makers ask for them unmerged. Every key must be one of these, every projection must have both halves, and
alpha must be known — from the file, or from loras.json where the file carries none.

A distillation's sampler — its latent mix and its step codes — is written beside its adapter as <id>.sampler.json.

usage: python -I convert_moss_loras.py <loras.json> <downloads-dir> <out-dir>
       (each LoRA's file at <downloads-dir>/<id>/<file>; adapters written to <out-dir>/loras/)
"""
import json
import re
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
from safetensors import safe_open

sys.path.insert(0, str(Path(__file__).parent))   # run isolated (-I), the recipe's folder is not on the path
from build_moss_image import LAYERS, PROJECTIONS, TIME_PARAMETERS

# What each projection takes and makes, by name: MOSS-Image's width 3072, its feed-forward 8192 (gate and up 16384).
SHAPES = {"q": (3072, 3072), "k": (3072, 3072), "v": (3072, 3072), "o": (3072, 3072),
          "ffn_up_gate": (3072, 16384), "ffn_down": (8192, 3072)}

DOTTED = re.compile(r"^(?:diffusion_model\.|transformer\.)?layers\.(\d+)\.proj\.(q|k|v|o|ffn_up_gate|ffn_down)\."
                    r"(lora_A|lora_B|lora_down|lora_up|alpha)(?:\.weight)?$")
KOHYA = re.compile(r"^lora_unet_layers_(\d+)_proj_(q|k|v|o|ffn_up_gate|ffn_down)\.(lora_down|lora_up|alpha)(?:\.weight)?$")
SAMPLER_KEYS = {"latent_mix.Q", "step_codes"}


def read(path):
    """Every tensor as fp32 numpy — read through torch, as numpy has no bf16 — and the file's metadata."""
    with safe_open(str(path), framework="pt") as f:
        return {k: f.get_tensor(k).float().numpy() for k in f.keys()}, f.metadata() or {}


def peft_alpha(metadata):
    """alpha / r from PEFT's adapter metadata, which the ComfyUI and diffusers files carry."""
    config = metadata.get("lora_adapter_metadata")
    if not config:
        return None
    config = json.loads(config)
    return config["lora_alpha"] / config["r"]


def parse(entry, path):
    """A LoRA file as its matrices: for each projection it changes, (A [r, in], B [out, r], alpha / r), the change being
    B A x times alpha / r; with a distillation's timestep path and sampler data. Refuses anything it cannot place."""
    tensors, metadata = read(path)
    pairs, alphas, time, sampler = {}, {}, {}, {}
    for key, value in tensors.items():
        if key in SAMPLER_KEYS:
            sampler[key] = value
            continue
        if key in TIME_PARAMETERS.values():
            time[next(name for name, param in TIME_PARAMETERS.items() if param == key)] = value.astype(np.float32)
            continue
        match = DOTTED.match(key) or KOHYA.match(key)
        if not match:
            raise SystemExit(f"{entry['id']}: '{key}' is not a LoRA matrix of a projection this graph has")
        layer, projection, part = int(match.group(1)), match.group(2), match.group(3)
        if part == "alpha":
            alphas[(layer, projection)] = float(value)
        else:
            pairs.setdefault((layer, projection), {})["down" if part in ("lora_A", "lora_down") else "up"] = value

    if entry["kind"] == "fast" and (set(time) != set(TIME_PARAMETERS) or set(sampler) != SAMPLER_KEYS):
        raise SystemExit(f"{entry['id']}: a distillation brings its whole timestep path and its sampler's data")
    if entry["kind"] != "fast" and (time or sampler):
        raise SystemExit(f"{entry['id']}: a style LoRA replaces no timestep path")

    whole = peft_alpha(metadata)
    matrices = {}
    for (layer, projection), halves in sorted(pairs.items()):
        if set(halves) != {"down", "up"}:
            raise SystemExit(f"{entry['id']}: layer {layer} {projection} has only its {', '.join(halves)} matrix")
        down, up = halves["down"].astype(np.float32), halves["up"].astype(np.float32)
        rank = down.shape[0]
        if (down.shape[1], up.shape[0]) != SHAPES[projection] or up.shape[1] != rank:
            raise SystemExit(f"{entry['id']}: layer {layer} {projection} is {down.shape} and {up.shape}, not {SHAPES[projection]} at rank {rank}")
        if (layer, projection) in alphas:
            scale = alphas[(layer, projection)] / rank
        elif whole is not None:
            scale = whole
        elif "alpha" in entry:
            scale = entry["alpha"] / rank
        else:
            raise SystemExit(f"{entry['id']}: nothing says this LoRA's alpha")
        matrices[(layer, projection)] = (down, up, scale)
    if not matrices:
        raise SystemExit(f"{entry['id']}: no LoRA matrix found")
    return matrices, time, sampler


def convert(entry, path):
    """The adapter's parameters — each projection's down [in, r] and up [r, out], alpha / r folded into up, fp16; a
    distillation's timestep path in fp32 — and its sampler data."""
    matrices, time, sampler = parse(entry, path)
    parameters = {}
    for (layer, projection), (down, up, scale) in matrices.items():
        parameters[f"lora.{layer}.{projection}.down"] = np.ascontiguousarray(down.T).astype(np.float16)
        parameters[f"lora.{layer}.{projection}.up"] = np.ascontiguousarray(up.T * scale).astype(np.float16)
    parameters.update(time)
    return parameters, sampler


def main():
    listing, downloads, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    (out / "loras").mkdir(parents=True, exist_ok=True)
    for entry in json.loads(listing.read_text(encoding="utf-8"))["loras"]:
        parameters, sampler = convert(entry, downloads / entry["id"] / entry["file"])
        adapter = ort.AdapterFormat()
        adapter.set_adapter_version(1)
        adapter.set_model_version(1)
        adapter.set_parameters({name: ort.OrtValue.ortvalue_from_numpy(value) for name, value in parameters.items()})
        adapter.export_adapter(str(out / "loras" / f"{entry['id']}.onnx_adapter"))
        if sampler:
            (out / "loras" / f"{entry['id']}.sampler.json").write_text(json.dumps({
                "kind": entry["sampler"]["kind"],
                "step_codes": [float(c) for c in sampler["step_codes"]],
                "latent_mix": sampler["latent_mix.Q"].astype(np.float32).tolist(),
            }))
        projections = sum(1 for name in parameters if name.endswith(".down"))
        print(f"converted {entry['id']}: {projections} projection(s) of {LAYERS * len(PROJECTIONS)}"
              + (", its timestep path and its sampler" if sampler else ""), flush=True)


if __name__ == "__main__":
    main()
