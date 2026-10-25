"""Compare ways of reading a prompt for MOSS-Image: its own text encoder, Qwen3.5-9B, in PyTorch, against the same weights
in llama.cpp at the precisions Nexaflow could ship — so the host's encoder is chosen by what it does to the pictures.

MOSS-Image reads a prompt as Qwen3.5-9B's last hidden state over the prompt in Qwen's chat template, a user turn and the
start of the reply with thinking on (MOSSImagePipeline.encode_prompt). Its text encoder is Qwen/Qwen3.5-9B unchanged,
so llama.cpp's GGUF builds of that model are the same weights. Each is read two ways:

  * the hidden states themselves, against PyTorch in fp32 — per-token cosine and relative error, beside PyTorch's own
    bf16 reading, the precision MOSS-Image runs its encoder at;
  * the pictures they draw: every prompt drawn by the PyTorch pipeline (bf16, MOSS-Image's defaults) from the same noise
    with each reading as its prompt and negative-prompt embeddings, against the picture from PyTorch's bf16 reading, as
    PSNR. Both kinds of number, and every picture, are written to <out-dir>.

llama.cpp is run as llama-server, embeddings only, at the build the host runs (b11606), given the prompt's token ids as
the pipeline's tokenizer makes them; its own tokenisation of the prompt is held to theirs first. Its context holds the longest
prompt MOSS-Image reads, 4,096 tokens, read 512 at a time, and the GPU memory it takes for that — weights, recurrent
state, KV cache and compute buffers, as llama.cpp reports them — is written beside each build's numbers: what decides
whether the encoder fits a small card, MOSS-Image's parts being loaded one at a time.

usage: python -I compare_moss_encoder.py <moss-image checkpoint> <llama-server.exe> <out-dir> <gguf>...
"""
import json
import math
import re
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

import numpy as np
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
    "一张书法作品，宣纸上用毛笔写着“海纳百川”四个大字，旁边有红色印章。",
    json.dumps({
        "summary": {"caption": "A bright kitchen table seen from above, set for breakfast."},
        "items": [
            {"caption": "a white plate with two fried eggs", "bbox": [-300, -320, 60, 40]},
            {"caption": "a glass of orange juice", "bbox": [-360, 120, -120, 300]},
            {"caption": "a folded newspaper whose headline reads 'GOOD MORNING'", "bbox": [80, -380, 400, 360]},
        ],
        "bbox_format": "yxyx",
        "bbox_coord_space": "abs_centralized",
    }, ensure_ascii=False, indent=2),
]
NEGATIVE = ""                    # the pipeline's default negative prompt
SIZE, STEPS, GUIDANCE = 1024, 50, 3.0
CONTEXT = 4096                   # MOSSImagePipeline's longest prompt, in tokens
MICRO_BATCH = 512                # tokens read at once: llama.cpp's compute buffer grows with it, 4.2 GB at 4,096


def formatted(tokenizer, prompt):
    """The prompt as MOSSImagePipeline.encode_prompt hands it to the tokenizer."""
    return tokenizer.apply_chat_template([{"role": "user", "content": prompt}], tokenize=False,
                                         add_generation_prompt=True, enable_thinking=True)


def torch_readings(checkpoint, texts, dtype):
    """Qwen3.5-9B's last hidden state for each text, one unpadded forward each as the pipeline does: [tokens, 4096] fp32."""
    from transformers import AutoModel
    encoder = AutoModel.from_pretrained(checkpoint / "text_encoder", dtype=dtype).to("cuda").eval()
    tokenizer = tokenizer_of(checkpoint)
    out = []
    with torch.no_grad():
        for text in texts:
            inputs = tokenizer(text, return_tensors="pt").to("cuda")
            out.append(encoder(**inputs, use_cache=False, return_dict=True).last_hidden_state[0].float().cpu().numpy())
    del encoder
    torch.cuda.empty_cache()
    return out


def tokenizer_of(checkpoint):
    from transformers import AutoTokenizer
    return AutoTokenizer.from_pretrained(checkpoint / "tokenizer")



CUDA_BUFFER = re.compile(r"CUDA\d+ (?:model|KV|RS|compute) buffer size\s*=\s*([\d.]+) MiB")


class LlamaServer:
    """llama-server holding one GGUF for embeddings, no pooling: each text read from its token ids as the pipeline's
    tokenizer makes them, after llama.cpp's own tokenisation of it is held to theirs."""

    def __init__(self, binary, gguf, log):
        with socket.socket() as probe:
            probe.bind(("127.0.0.1", 0))
            self.port = probe.getsockname()[1]
        self.gguf, self.log = gguf, log
        self.process = subprocess.Popen(
            [str(binary), "-m", str(gguf), "--embeddings", "--pooling", "none", "-np", "1", "-ngl", "999",
             "-c", str(CONTEXT), "-b", str(CONTEXT), "-ub", str(MICRO_BATCH), "-lv", "4", "--no-warmup",
             "--host", "127.0.0.1", "--port", str(self.port)],
            stdout=subprocess.DEVNULL, stderr=open(log, "w", encoding="utf-8"))

        deadline = time.monotonic() + 600
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise SystemExit(f"llama-server stopped loading {gguf.name}:\n{log.read_text(encoding='utf-8')[-4000:]}")
            try:
                if json.loads(urllib.request.urlopen(f"http://127.0.0.1:{self.port}/health", timeout=2).read()).get("status") == "ok":
                    return
            except (urllib.error.URLError, ConnectionError, TimeoutError):
                pass
            time.sleep(1)
        raise SystemExit(f"llama-server did not load {gguf.name} in ten minutes")

    def post(self, path, body):
        request = urllib.request.Request(f"http://127.0.0.1:{self.port}{path}", data=json.dumps(body).encode("utf-8"),
                                         headers={"Content-Type": "application/json"})
        return json.loads(urllib.request.urlopen(request, timeout=600).read())

    def reading(self, text, ids):
        """[tokens, 4096] fp32: the last hidden state for each token, unnormalised."""
        own = self.post("/tokenize", {"content": text, "add_special": False, "parse_special": True})["tokens"]
        if own != ids:
            raise SystemExit(f"{self.gguf.name} tokenises differently: {len(own)} tokens against the pipeline's {len(ids)}")
        # Each text read whole: a recurrent layer's state cannot be taken from a cached prefix and stay exact.
        answer = self.post("/embedding", {"content": ids, "embd_normalize": -1, "cache_prompt": False})
        rows = answer[0]["embedding"] if isinstance(answer, list) else answer["embedding"]
        return np.asarray(rows, dtype=np.float32)

    def memory(self):
        """The GPU memory llama.cpp says its buffers hold — weights, KV cache, recurrent state, compute — in MiB.
        (The driver gives no process's own use on Windows.)"""
        return sum(float(m.group(1)) for line in self.log.read_text(encoding="utf-8").splitlines() if (m := CUDA_BUFFER.search(line)))

    def close(self):
        self.process.terminate()
        self.process.wait(60)

def against(reading, reference):
    """Per-token cosine (mean, least) and the relative error of the whole reading, against the reference."""
    a, b = reading.astype(np.float64), reference.astype(np.float64)
    cos = (a * b).sum(-1) / (np.linalg.norm(a, axis=-1) * np.linalg.norm(b, axis=-1))
    return {"cosine_mean": float(cos.mean()), "cosine_min": float(cos.min()),
            "relative_error": float(np.linalg.norm(a - b) / np.linalg.norm(b))}


def pipeline(checkpoint):
    """MOSS-Image's own pipeline in bf16, without its text encoder: every prompt is given as embeddings."""
    sys.path.insert(0, str(checkpoint))
    from pipeline_moss_image import MOSSImagePipeline, MOSSImageScheduler, MOSSImageTransformer
    from diffusers import AutoencoderKLFlux2
    transformer = MOSSImageTransformer.from_pretrained(checkpoint / "transformer", torch_dtype=torch.bfloat16).eval()
    vae = AutoencoderKLFlux2.from_pretrained(checkpoint / "vae", torch_dtype=torch.float32).eval()
    scheduler = MOSSImageScheduler.from_pretrained(checkpoint / "scheduler")
    return MOSSImagePipeline(transformer, scheduler, vae=vae).to("cuda")


def draw(pipe, prompt, negative, index):
    embeds = lambda r: torch.from_numpy(r).to("cuda", torch.bfloat16)[None]
    noise = torch.randn((1, 128, SIZE // 16, SIZE // 16), generator=torch.Generator("cuda").manual_seed(1000 + index),
                        device="cuda", dtype=torch.float32)
    return pipe(prompt_embeds=embeds(prompt), negative_prompt_embeds=embeds(negative), height=SIZE, width=SIZE,
                num_inference_steps=STEPS, guidance_scale=GUIDANCE, latents=noise).images[0]


def psnr(a, b):
    x, y = (np.asarray(i.convert("RGB"), dtype=np.float32) for i in (a, b))
    mse = ((x - y) ** 2).mean()
    return 99.0 if mse == 0 else 10 * math.log10(255**2 / mse)


def main():
    checkpoint, binary, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    ggufs = [Path(p) for p in sys.argv[4:]]
    out.mkdir(parents=True, exist_ok=True)
    tokenizer = tokenizer_of(checkpoint)
    texts = [formatted(tokenizer, p) for p in PROMPTS] + [formatted(tokenizer, NEGATIVE)]
    ids = [tokenizer(t).input_ids for t in texts]

    # Every reading of every text — the prompts, then the negative prompt — by source.
    readings = {"pytorch-fp32": torch_readings(checkpoint, texts, torch.float32),
                "pytorch-bf16": torch_readings(checkpoint, texts, torch.bfloat16)}
    memory = {}
    for gguf in ggufs:
        source = f"llama-{gguf.stem.split('-')[-1]}"
        server = LlamaServer(binary, gguf, out / f"{source}.log")
        try:
            readings[source] = [server.reading(t, i) for t, i in zip(texts, ids)]
            memory[source] = round(server.memory())
        finally:
            server.close()
        print(f"read every prompt with {gguf.name}: {memory[source]} MiB of GPU memory", flush=True)

    report = {"prompts": len(PROMPTS), "tokens": [len(i) for i in ids], "context": CONTEXT, "sources": {}}
    for source, rows in readings.items():
        per_text = [against(r, ref) for r, ref in zip(rows, readings["pytorch-fp32"])]
        report["sources"][source] = {
            "hidden_states": {
                "cosine_mean": float(np.mean([m["cosine_mean"] for m in per_text])),
                "cosine_min": float(min(m["cosine_min"] for m in per_text)),
                "relative_error_median": float(np.median([m["relative_error"] for m in per_text])),
                "relative_error_max": float(max(m["relative_error"] for m in per_text)),
            },
        }
        if source in memory:
            report["sources"][source]["gpu_memory_mib"] = memory[source]
        print(f"{source:16s} {json.dumps(report['sources'][source]['hidden_states'])}", flush=True)

    pipe = pipeline(checkpoint)
    for source, rows in readings.items():
        if source == "pytorch-fp32":
            continue   # the picture reference is MOSS-Image's own bf16 reading
        (out / "pictures" / source).mkdir(parents=True, exist_ok=True)
        for i in range(len(PROMPTS)):
            draw(pipe, rows[i], rows[-1], i).save(out / "pictures" / source / f"{i:02d}.png")
        print(f"drew every prompt from {source}", flush=True)
    for source in report["sources"]:
        if source in ("pytorch-fp32", "pytorch-bf16"):
            continue
        scores = [psnr(Image.open(out / "pictures" / source / f"{i:02d}.png"),
                       Image.open(out / "pictures" / "pytorch-bf16" / f"{i:02d}.png")) for i in range(len(PROMPTS))]
        report["sources"][source]["pictures"] = {"psnr_min": round(min(scores), 1),
                                                 "psnr_median": round(float(np.median(scores)), 1),
                                                 "psnr": [round(s, 1) for s in scores]}
        print(f"{source:16s} pictures {json.dumps(report['sources'][source]['pictures'])}", flush=True)

    (out / "encoder-comparison.json").write_text(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()