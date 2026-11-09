using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// The byte-level BPE tokenizer a Hugging Face <c>tokenizer.json</c> describes — Qwen's, Whisper's: its vocabulary and merges,
/// every added token kept whole, the text NFC-normalised where it says so and split by its pre-tokenizer — a Split pattern, or
/// GPT-2's own for a ByteLevel one that splits by regex.
/// </summary>
internal static class ByteLevelBpe
{
    /// <summary>The pattern a ByteLevel pre-tokenizer with <c>use_regex</c> splits by: GPT-2's.</summary>
    private const string Gpt2Pattern = @"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+";

    /// <exception cref="InvalidDataException">The file describes a normaliser or a pre-tokenizer this reader does not know.</exception>
    public static BpeTokenizer FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var model = root.GetProperty("model");
        var vocabulary = model.GetProperty("vocab").EnumerateObject().Select(p => new KeyValuePair<string, int>(p.Name, p.Value.GetInt32())).ToList();
        var merges = model.GetProperty("merges").EnumerateArray()
                          .Select(m => m.ValueKind == JsonValueKind.Array ? $"{m[0].GetString()} {m[1].GetString()}" : m.GetString()!)
                          .ToList();
        var added = root.GetProperty("added_tokens").EnumerateArray()
                        .ToDictionary(t => t.GetProperty("content").GetString()!, t => t.GetProperty("id").GetInt32());

        return BpeTokenizer.Create(new BpeOptions(vocabulary)
        {
            Merges        = merges,
            ByteLevel     = true,
            SpecialTokens = added,
            PreTokenizer  = new RegexPreTokenizer(new Regex(SplitPattern(root), RegexOptions.Compiled), added),
            Normalizer    = NormalizerOf(root),
        });
    }

    private static string SplitPattern(JsonElement root)
    {
        var pre = root.GetProperty("pre_tokenizer");
        return pre.GetProperty("type").GetString() switch
        {
            "Sequence"  => pre.GetProperty("pretokenizers")[0].GetProperty("pattern").GetProperty("Regex").GetString()!,
            "ByteLevel" when pre.TryGetProperty("use_regex", out var regex) && regex.GetBoolean() => Gpt2Pattern,
            var other   => throw new InvalidDataException($"A tokenizer.json pre-tokenizer of type {other} is not one this host reads."),
        };
    }

    private static Normalizer? NormalizerOf(JsonElement root)
        => !root.TryGetProperty("normalizer", out var normalizer) || normalizer.ValueKind == JsonValueKind.Null ? null
           : normalizer.GetProperty("type").GetString() is "NFC" ? new NfcNormalizer()
           : throw new InvalidDataException($"A tokenizer.json normalizer of type {normalizer.GetProperty("type").GetString()} is not one this host reads.");

    private sealed class NfcNormalizer : Normalizer
    {
        public override string Normalize(string original) => original.Normalize(NormalizationForm.FormC);
        public override string Normalize(ReadOnlySpan<char> original) => original.ToString().Normalize(NormalizationForm.FormC);
    }
}
