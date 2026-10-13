"""Patch onnxruntime-genai 0.17.1's Whisper builder for checkpoints whose decoder has fewer layers than the encoder.

The builder's WhisperEncoder emits one cross-attention K/V pair per *encoder* layer, reading
model.decoder.layers[i] for each — fine for tiny..large-v3 (encoder_layers == decoder_layers) but large-v3-turbo has
32 encoder layers and 4 decoder layers, so the export dies with "IndexError: index 4 is out of range".
The runtime (src/models/io/cross_kv_cache.cpp) already sizes the cross cache by decoder.num_hidden_layers, so only the
builder needs to count decoder layers for those outputs.

Usage: python -I patch_builder.py   (patches the onnxruntime_genai package of the interpreter that runs it; idempotent)
"""
import pathlib
import onnxruntime_genai

path = pathlib.Path(onnxruntime_genai.__file__).parent / "models" / "builders" / "whisper.py"
src = path.read_text(encoding="utf-8")
if "num_cross_layers" in src:
    print(f"already patched: {path}")
    raise SystemExit(0)

edits = [
    # remember the decoder depth before the encoder overwrites num_hidden_layers
    ("        config.num_hidden_layers = config.encoder_layers\n",
     "        self.num_cross_layers = config.decoder_layers\n        config.num_hidden_layers = config.encoder_layers\n"),
    # encoder outputs: one cross K/V per decoder layer
    ('            "present_key_cross": {i: f"present_key_cross_{i}" for i in range(self.num_layers)},\n'
     '            "present_value_cross": {i: f"present_value_cross_{i}" for i in range(self.num_layers)},\n',
     '            "present_key_cross": {i: f"present_key_cross_{i}" for i in range(self.num_cross_layers)},\n'
     '            "present_value_cross": {i: f"present_value_cross_{i}" for i in range(self.num_cross_layers)},\n'),
    # the projection subgraphs that produce them
    ("        # Add cross-attention KV cache outputs\n        for i in range(self.num_layers):\n",
     "        # Add cross-attention KV cache outputs\n        for i in range(self.num_cross_layers):\n"),
]
for old, new in edits:
    if src.count(old) != 1:
        raise SystemExit(f"patch anchor not found exactly once:\n{old}")
    src = src.replace(old, new)
path.write_text(src, encoding="utf-8")
print(f"patched: {path}")
