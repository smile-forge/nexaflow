using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace Nexaflow.AiHost.Selecting;

/// <summary>
/// SAM 3.1's tokenizer — CLIP's, as the makers' <c>SimpleTokenizer</c> runs it: the phrase cleaned (<see cref="ClipText"/>),
/// split into runs of letters, single digits and runs of other marks, each run byte-pair encoded over the makers'
/// vocabulary with <c>&lt;/w&gt;</c> ending its last piece, between the start and end tokens, cut to
/// <see cref="ContextLength"/> and padded with 0.
/// </summary>
internal sealed class ClipTokenizer
{
    /// <summary>The makers' vocabulary file, beside the text encoder's graph.</summary>
    public const string VocabularyFile = "bpe_simple_vocab_16e6.txt.gz";

    /// <summary>How many tokens the text encoder reads.</summary>
    public const int ContextLength = 32;

    public const int StartToken = 49406;
    public const int EndToken = 49407;

    private const string Start = "<start_of_text>", End = "<end_of_text>";
    private readonly Tokenizer _bpe;

    private ClipTokenizer(Tokenizer bpe) => _bpe = bpe;

    /// <summary>The tokenizer the makers' gzipped merges describe: the 256 byte symbols, each again ending a word, the
    /// first 48,894 merges, and the start and end tokens — 49,408 in all.</summary>
    public static ClipTokenizer Load(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var lines = reader.ReadToEnd().Split('\n');
        var merges = lines.Skip(1).Take(49152 - 256 - 2).ToList();

        var symbols = ByteSymbols();
        var vocabulary = new List<string>(EndToken + 1);
        vocabulary.AddRange(symbols);
        vocabulary.AddRange(symbols.Select(s => s + "</w>"));
        vocabulary.AddRange(merges.Select(m => m.Replace(" ", "", StringComparison.Ordinal)));
        vocabulary.Add(Start);
        vocabulary.Add(End);
        if (vocabulary.Count != EndToken + 1) throw new InvalidDataException($"{path} is not SAM 3.1's vocabulary: it makes {vocabulary.Count} tokens.");

        var special = new Dictionary<string, int> { [Start] = StartToken, [End] = EndToken };
        // the makers' pattern, case-blind: the special tokens whole, English contractions, runs of letters, single
        // digits, runs of anything else that is not a space
        var pattern = new Regex(@"<start_of_text>|<end_of_text>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
                                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
        return new ClipTokenizer(BpeTokenizer.Create(new BpeOptions(vocabulary.Select((t, i) => new KeyValuePair<string, int>(t, i)))
        {
            Merges          = merges,
            ByteLevel       = true,
            EndOfWordSuffix = "</w>",
            SpecialTokens   = special,
            PreTokenizer    = new RegexPreTokenizer(pattern, special),
        }));
    }

    /// <summary>A phrase's <see cref="ContextLength"/> tokens: start, the phrase, end — the last of a phrase too long
    /// for them made the end — then 0.</summary>
    public long[] Tokens(string phrase)
    {
        var ids = _bpe.EncodeToIds(ClipText.Clean(phrase), considerPreTokenization: true, considerNormalization: false);
        var tokens = new long[ContextLength];
        tokens[0] = StartToken;
        int n = Math.Min(ids.Count, ContextLength - 2);
        for (int i = 0; i < n; i++) tokens[1 + i] = ids[i];
        tokens[Math.Min(1 + ids.Count, ContextLength - 1)] = EndToken;
        return tokens;
    }

    /// <summary>CLIP's <c>bytes_to_unicode</c>: each byte as a character that is neither space nor control — the
    /// printable Latin-1 ones as themselves, the rest moved past 255 in order.</summary>
    private static List<string> ByteSymbols()
    {
        var printable = Enumerable.Range('!', '~' - '!' + 1).Concat(Enumerable.Range('¡', '¬' - '¡' + 1)).Concat(Enumerable.Range('®', 'ÿ' - '®' + 1)).ToList();
        var bytes = new List<int>(printable);
        var chars = new List<int>(printable);
        int next = 0;
        for (int b = 0; b < 256; b++)
        {
            if (printable.Contains(b)) continue;
            bytes.Add(b);
            chars.Add(256 + next++);
        }
        return [.. chars.Select(c => ((char)c).ToString())];
    }
}

/// <summary>
/// The makers' cleaning of a phrase before it is tokenized, as their tokenizer's <c>lower</c> cleaning does it:
/// <c>ftfy.fix_text</c>, HTML entities unescaped twice, runs of whitespace made one space, the ends trimmed, lower case.
/// Of ftfy's fixes, those that change text a person or an assistant writes are made — HTML entities, terminal colour
/// codes, C1 controls read as Windows-1252, Latin ligatures, full- and half-width forms, curly quotes, line breaks, other
/// control characters, NFC — and its repair of mojibake is not: that undoes text decoded with the wrong code page, which a
/// phrase handed over as a .NET string has not been.
/// </summary>
internal static partial class ClipText
{
    public static string Clean(string text)
    {
        text = FixText(text);
        text = WebUtility.HtmlDecode(WebUtility.HtmlDecode(text)).Trim();
        return Whitespace().Replace(text, " ").Trim().ToLowerInvariant();
    }

    /// <summary>ftfy's <c>fix_text</c> with its defaults, short of mojibake repair: each fix in turn until none changes
    /// anything, then NFC.</summary>
    internal static string FixText(string text)
    {
        bool html = !text.Contains('<');   // ftfy leaves entities alone in what looks like HTML
        while (true)
        {
            string before = text;
            if (html) text = WebUtility.HtmlDecode(text);
            text = TerminalEscape().Replace(text, "");
            text = C1Control().Replace(text, m => Windows1252[m.Value[0] - 0x80].ToString());
            text = Replace(text, Ligatures);
            text = Replace(text, Widths.Value);
            text = SingleQuote().Replace(text, "'");
            text = DoubleQuote().Replace(text, "\"");
            text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
            text = LineBreak().Replace(text, "\n");
            text = string.Concat(text.Where(c => !IsControl(c)));
            if (text == before) break;
        }
        return text.Normalize(NormalizationForm.FormC);
    }

    private static string Replace(string text, IReadOnlyDictionary<char, string> map)
    {
        if (!text.Any(map.ContainsKey)) return text;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text) sb.Append(map.TryGetValue(c, out var s) ? s : c.ToString());
        return sb.ToString();
    }

    /// <summary>ftfy's <c>remove_control_chars</c>: C0 controls but tab and the line breaks, DEL, the deprecated
    /// format controls, the byte-order mark and the interlinear annotation marks.</summary>
    private static bool IsControl(char c) => c is (>= '\u0000' and <= '\u0008') or '\u000B' or (>= '\u000E' and <= '\u001F') or '\u007F'
                                               or (>= '\u206A' and <= '\u206F') or '\uFEFF' or (>= '\uFFF9' and <= '\uFFFC');

    /// <summary>ftfy's <c>fix_latin_ligatures</c>.</summary>
    private static readonly Dictionary<char, string> Ligatures = new()
    {
        ['Ĳ'] = "IJ", ['ĳ'] = "ij", ['ŉ'] = "ʼn", ['Ǳ'] = "DZ", ['ǲ'] = "Dz", ['ǳ'] = "dz", ['Ǆ'] = "DŽ", ['ǅ'] = "Dž", ['ǆ'] = "dž",
        ['Ǉ'] = "LJ", ['ǈ'] = "Lj", ['ǉ'] = "lj", ['Ǌ'] = "NJ", ['ǋ'] = "Nj", ['ǌ'] = "nj", ['ﬀ'] = "ff", ['ﬁ'] = "fi", ['ﬂ'] = "fl",
        ['ﬃ'] = "ffi", ['ﬄ'] = "ffl", ['ﬅ'] = "ſt", ['ﬆ'] = "st",
    };

    /// <summary>ftfy's <c>fix_character_width</c>: the ideographic space, and each full- or half-width form NFKC makes
    /// something else of, as NFKC makes it.</summary>
    private static readonly Lazy<Dictionary<char, string>> Widths = new(() =>
    {
        var map = new Dictionary<char, string> { ['\u3000'] = " " };
        for (int i = 0xFF01; i < 0xFFF0; i++)
        {
            string c = ((char)i).ToString(), alternate = c.Normalize(NormalizationForm.FormKC);
            if (alternate != c) map[(char)i] = alternate;
        }
        return map;
    });

    /// <summary>Windows-1252 for the bytes 0x80–0x9F, ftfy's "sloppy" way: a byte it leaves undefined stays itself.</summary>
    private static readonly char[] Windows1252 =
        "\u20AC\u0081\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\u008D\u017D\u008F\u0090\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\u009D\u017E\u0178".ToCharArray();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\x1b\[((?:\d|;)*)([a-zA-Z])")]
    private static partial Regex TerminalEscape();

    [GeneratedRegex(@"[\x80-\x9f]")]
    private static partial Regex C1Control();

    [GeneratedRegex(@"[\u02BC\u2018-\u201B]")]
    private static partial Regex SingleQuote();

    [GeneratedRegex(@"[\u201C-\u201F]")]
    private static partial Regex DoubleQuote();

    [GeneratedRegex(@"[\r\u2028\u2029\u0085]")]
    private static partial Regex LineBreak();
}
