using Microsoft.ML.Tokenizers;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>Qwen's tokenizer, which the image models that read prompts through a Qwen model share.</summary>
internal static class QwenTokenizer
{
    /// <summary>
    /// The tokenizer a tokenizer.json describes: byte-level BPE over its vocabulary and merges, the text NFC-normalised and
    /// split by its pre-tokenizer pattern, special tokens (&lt;|im_start|&gt; …) kept whole.
    /// </summary>
    public static Tokenizer FromJson(string json) => ByteLevelBpe.FromJson(json);
}
