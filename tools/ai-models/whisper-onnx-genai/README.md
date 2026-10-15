---
license: mit
base_model:
  - openai/whisper-small
  - openai/whisper-small.en
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

# Whisper small and small.en for onnxruntime-genai

[openai/whisper-small](https://huggingface.co/openai/whisper-small) (revision `973afd24`) and
[openai/whisper-small.en](https://huggingface.co/openai/whisper-small.en) (revision `e8727524`), exported for
[onnxruntime-genai](https://github.com/microsoft/onnxruntime-genai) 0.17.1 with its own model builder. Built for
[Nexaflow](https://github.com/smile-forge/nexaflow), which uses them to transcribe each finished spoken turn; usable by
anything that runs onnxruntime-genai.

| Folder | Model | Precision | For | Size |
|---|---|---|---|---|
| `small/cuda/` | small, multilingual | fp16 | the CUDA execution provider | 0.53 GB |
| `small/cpu/` | small, multilingual | int8 (embeddings fp32) | the CPU | 0.47 GB |
| `small.en/cuda/` | small, English only | fp16 | the CUDA execution provider | 0.53 GB |
| `small.en/cpu/` | small, English only | int8 (embeddings fp32) | the CPU | 0.47 GB |

Every build passed Nexaflow's spoken-turn checks on its runtime — a sentence heard whole as one turn, a thinking pause
that does not end it, a one-word answer, dictation phrase by phrase, silence that is no turn — and the multilingual
build heard French and German sentences back.

## How it was built

`Build-Whisper.ps1`, in this repository, pins every version and runs the four builds with onnxruntime-genai's builder
unchanged — rerunning it reproduces these files byte for byte.

## Using it

- **Force the language** in the decoder prompt of the multilingual model when it is known —
  `<|startoftranscript|><|de|><|transcribe|><|notimestamps|>`. Left to detect it, short answers are misheard.
- **The English-only model takes no language or task** — `<|startoftranscript|><|notimestamps|>`.
- **On CUDA, set `sdpa_kernel` to `1`** (`config.SetProviderOption("cuda", "sdpa_kernel", "1")`) and transcribe once
  at load.
- The encoder reads a 30 s window; transcribe longer audio in pieces.

## Licence

MIT, as the original models: Copyright (c) 2022 OpenAI. The export changes the format, not the weights.
