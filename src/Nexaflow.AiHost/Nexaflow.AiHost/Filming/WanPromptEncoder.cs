using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Filming;

/// <summary>
/// Reads a prompt the way Wan2.2 does: cleaned as its pipeline cleans text (<see cref="Clean"/>), tokenised by UMT5's
/// SentencePiece model (its spiece.model, beside the graph) with the closing &lt;/s&gt;, at most 512 tokens, and run
/// through UMT5-XXL's encoder — its rows then zero to 512, as Wan pads them.
/// </summary>
internal sealed partial class WanPromptEncoder(ModelSpec spec, InferenceSession session, SentencePieceTokenizer tokenizer) : IHostModel
{
    /// <summary>The rows the denoiser reads: the longest prompt, the rest zero.</summary>
    public const int Rows = 512;

    /// <summary>How wide each row is.</summary>
    public const int Width = 4096;

    public ModelSpec Spec => spec;

    /// <summary><paramref name="prompt"/>'s tokens: cleaned, tokenised, cut to <see cref="Rows"/> with its &lt;/s&gt; kept last.</summary>
    public long[] Tokenize(string prompt)
    {
        var ids = tokenizer.EncodeToIds(Clean(prompt), addBeginningOfSentence: false, addEndOfSentence: true);
        if (ids.Count > Rows) ids = [.. ids.Take(Rows - 1), tokenizer.EndOfSentenceId];
        return [.. ids.Select(id => (long)id)];
    }

    /// <summary>The prompt as the denoiser reads it, [512, 4096] row-major: the encoder's rows, then zeros.</summary>
    public float[] Encode(long[] ids)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(ids, [1, ids.Length]);
        using var run = GraphSessions.Releasing(Spec);
        using var outputs = session.Run(run, ["input_ids"], [input], session.OutputNames);
        var text = new float[Rows * Width];
        outputs[0].GetTensorDataAsSpan<float>().CopyTo(text);
        return text;
    }

    /// <summary>
    /// Text as Wan's pipeline cleans it before it is tokenised — ftfy's fixes, HTML unescaped twice, whitespace run
    /// together — those of ftfy's fixes that can apply to text that arrives as Unicode: terminal escapes and control
    /// characters removed, HTML entities unescaped, ligatures spelt out, full-width characters made narrow, curly quotes
    /// made straight, then NFC. ftfy's other fixes repair text decoded in the wrong encoding, which a prompt never is.
    /// </summary>
    public static string Clean(string text)
    {
        // ftfy unescapes HTML only where the text has no tags; Wan's pipeline then unescapes it twice more.
        if (!text.Contains('<')) text = WebUtility.HtmlDecode(text);
        text = TerminalEscape().Replace(text, "");
        var fixedText = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            int c = rune.Value;
            if (IsControl(c)) continue;
            if (Ligature(c) is { } spelt) fixedText.Append(spelt);
            else if (c == 0x3000) fixedText.Append(' ');
            else if (c is >= 0xFF01 and < 0xFFF0) fixedText.Append(rune.ToString().Normalize(NormalizationForm.FormKC));
            else if (c is 0x02BC or (>= 0x2018 and <= 0x201B)) fixedText.Append('\'');
            else if (c is >= 0x201C and <= 0x201F) fixedText.Append('"');
            else fixedText.Append(rune.ToString());
        }
        text = fixedText.ToString().Normalize(NormalizationForm.FormC);
        text = WebUtility.HtmlDecode(WebUtility.HtmlDecode(text)).Trim();
        return Whitespace().Replace(text, " ").Trim();
    }

    /// <summary>The characters ftfy removes: C0 controls but tab, line feed, form feed and carriage return; DEL;
    /// deprecated format characters; the byte-order mark; interlinear annotation and object replacement marks.</summary>
    private static bool IsControl(int c) => c is (>= 0x00 and <= 0x08) or 0x0B or (>= 0x0E and <= 0x1F) or 0x7F
                                                or (>= 0x206A and <= 0x206F) or 0xFEFF or (>= 0xFFF9 and <= 0xFFFC);

    /// <summary>The ligatures ftfy spells out.</summary>
    private static string? Ligature(int c) => c switch
    {
        0x0132 => "IJ", 0x0133 => "ij", 0x0149 => "ʼn",
        0x01C4 => "DŽ", 0x01C5 => "Dž", 0x01C6 => "dž", 0x01C7 => "LJ", 0x01C8 => "Lj", 0x01C9 => "lj",
        0x01CA => "NJ", 0x01CB => "Nj", 0x01CC => "nj", 0x01F1 => "DZ", 0x01F2 => "Dz", 0x01F3 => "dz",
        0xFB00 => "ff", 0xFB01 => "fi", 0xFB02 => "fl", 0xFB03 => "ffi", 0xFB04 => "ffl", 0xFB05 => "ſt", 0xFB06 => "st",
        _ => null,
    };

    [GeneratedRegex(@"\x1B\[((?:\d|;)*)([a-zA-Z])")]
    private static partial Regex TerminalEscape();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public void Dispose() => session.Dispose();
}

internal sealed class WanPromptEncoderRunner : IModelRunner
{
    public string Id => "wan-prompt-encoder";

    public IHostModel Load(ModelSpec spec)
    {
        string modelPath = Path.Combine(spec.Folder, "spiece.model");
        if (!File.Exists(modelPath)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id} has no spiece.model in {spec.Folder}.");
        SentencePieceTokenizer tokenizer;
        using (var stream = File.OpenRead(modelPath))
            tokenizer = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: true);
        var encoder = new WanPromptEncoder(spec, GraphSessions.Open(spec), tokenizer);
        try
        {
            encoder.Encode(encoder.Tokenize("warm up"));   // CUDA tunes its kernels here, not on somebody's first clip
            return encoder;
        }
        catch
        {
            encoder.Dispose();
            throw;
        }
    }
}
