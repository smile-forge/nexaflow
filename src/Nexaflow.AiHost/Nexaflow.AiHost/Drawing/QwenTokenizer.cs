using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace Nexaflow.AiHost.Drawing;

/// <summary>Qwen's tokenizer, which the image models that read prompts through a Qwen model share.</summary>
internal static class QwenTokenizer
{
    /// <summary>
    /// The tokenizer a tokenizer.json describes: byte-level BPE over its vocabulary and merges, the text NFC-normalised and
    /// split by its pre-tokenizer pattern, special tokens (&lt;|im_start|&gt; …) kept whole.
    /// </summary>
    public static Tokenizer FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var model = root.GetProperty("model");
        var vocabulary = model.GetProperty("vocab").EnumerateObject().Select(p => new KeyValuePair<string, int>(p.Name, p.Value.GetInt32())).ToList();
        var merges = model.GetProperty("merges").EnumerateArray()
                          .Select(m => m.ValueKind == JsonValueKind.Array ? $"{m[0].GetString()} {m[1].GetString()}" : m.GetString()!)
                          .ToList();
        var special = root.GetProperty("added_tokens").EnumerateArray()
                          .ToDictionary(t => t.GetProperty("content").GetString()!, t => t.GetProperty("id").GetInt32());
        var pattern = root.GetProperty("pre_tokenizer").GetProperty("pretokenizers")[0].GetProperty("pattern").GetProperty("Regex").GetString()!;

        return BpeTokenizer.Create(new BpeOptions(vocabulary)
        {
            Merges        = merges,
            ByteLevel     = true,
            SpecialTokens = special,
            PreTokenizer  = new RegexPreTokenizer(new Regex(pattern, RegexOptions.Compiled), special),
            Normalizer    = new NfcNormalizer(),
        });
    }

    private sealed class NfcNormalizer : Normalizer
    {
        public override string Normalize(string original) => original.Normalize(NormalizationForm.FormC);
        public override string Normalize(ReadOnlySpan<char> original) => original.ToString().Normalize(NormalizationForm.FormC);
    }
}
