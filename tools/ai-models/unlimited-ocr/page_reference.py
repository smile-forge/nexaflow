"""The makers' own reading of each sample page — Unlimited-OCR through their transformers code, as trained (the decoder
seeing the prompt and the last 128 tokens it wrote) and as they decode (greedy, no 35-token run repeated within the last
128) — which is what the AI host's reading is held to.

    page_reference.py <the makers' model folder> <sample pages> <out folder>

Writes <page>.md, the model's own text with its layout markers, for every page."""
import sys
import time
from pathlib import Path

import torch
from transformers import AutoModel, AutoTokenizer

model_dir, pages, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
out.mkdir(parents=True, exist_ok=True)
tokenizer = AutoTokenizer.from_pretrained(model_dir, trust_remote_code=True)
model = AutoModel.from_pretrained(model_dir, trust_remote_code=True, use_safetensors=True,
                                  torch_dtype=torch.bfloat16).eval().cuda()
for page in sorted(pages.glob("*.png")):
    started = time.perf_counter()
    text = model.infer(tokenizer, prompt="<image>document parsing.", image_file=str(page), output_path=str(out / "scratch"),
                       base_size=1024, image_size=640, crop_mode=True, max_length=32768,
                       no_repeat_ngram_size=35, ngram_window=128, eval_mode=True)
    (out / f"{page.stem}.md").write_text(text, encoding="utf-8")
    print(f"{page.stem}: {len(tokenizer.encode(text))} tokens in {time.perf_counter() - started:.1f} s", flush=True)
