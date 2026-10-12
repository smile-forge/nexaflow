---
license: mit
base_model: openai/whisper-large-v3-turbo
library_name: onnxruntime-genai
pipeline_tag: automatic-speech-recognition
tags:
  - onnx
  - onnxruntime-genai
  - whisper
language:
  - en
  - fr
  - de
---

# Whisper large-v3-turbo for onnxruntime-genai

[openai/whisper-large-v3-turbo](https://huggingface.co/openai/whisper-large-v3-turbo) (revision `41f01f3f`), exported
for [onnxruntime-genai](https://github.com/microsoft/onnxruntime-genai) 0.17.1 with its own model builder. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), which uses it to transcribe each finished spoken turn; usable by
anything that runs onnxruntime-genai.

| Folder | Precision | For | Size |
|---|---|---|---|
| `cuda/` | fp16 | the CUDA execution provider | 1.6 GB |
| `cpu/` | int8 (embeddings fp32) | the CPU | 1.5 GB |

Both produced identical transcripts on every clip they were checked on.

## How it was built

onnxruntime-genai 0.17.1's builder fails on large-v3-turbo: it emits one cross-attention K/V output per *encoder*
layer, reading the decoder layer of the same index, and turbo has 32 encoder layers but 4 decoder layers. The runtime
already sizes the cross cache by the decoder's depth, so a three-line patch to the builder is all it takes. Both are in
this repository: `patch_builder.py`, the patch, and `Build-WhisperTurbo.ps1`, which pins every version, applies the
patch and runs both builds — rerunning it reproduces these files byte for byte.

## Using it

- **Force the language** in the decoder prompt when it is known —
  `<|startoftranscript|><|de|><|transcribe|><|notimestamps|>`. Left to detect it, short German answers are misheard
  ("Nein." as "9.").
- **On CUDA, set `sdpa_kernel` to `1`** (`config.SetProviderOption("cuda", "sdpa_kernel", "1")`) and transcribe once
  at load. Without it, the first time each output length is reached costs about 150 ms.
- The encoder reads a 30 s window; transcribe longer audio in pieces.

On an RTX PRO 6000, warm: 17 ms for a one-word utterance, 23 ms for 3 s, 52–59 ms for 8 s; 3.3 GB of VRAM. On the CPU,
about 1.5 s per utterance.

## Licence

MIT, as the original model: Copyright (c) 2022 OpenAI. The export changes the format, not the weights.
