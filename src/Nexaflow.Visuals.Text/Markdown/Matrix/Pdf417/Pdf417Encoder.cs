using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

/// <summary>What the encoder is asked for beyond the text.</summary>
public sealed record Pdf417Options
{
    /// <summary>
    /// How much of the symbol may be destroyed and still read: 0–8, spending 2^(level+1) codewords on
    /// parity. Null picks by payload size, which is what the standard recommends and what a reader
    /// expects to find.
    /// </summary>
    public int? ErrorCorrectionLevel { get; init; }

    /// <summary>Data columns, 1–30. Null lets the encoder pick a shape near three-to-one.</summary>
    public int? Columns { get; init; }

    /// <summary>
    /// Drop the right row indicator and the stop pattern down to a single bar. Saves eighteen modules
    /// a row where the symbol will not be damaged at its right edge — a document, not a parcel.
    /// </summary>
    public bool Truncated { get; init; }

    public static readonly Pdf417Options Default = new();
}

/// <summary>A finished PDF417 symbol, and the shape it settled on.</summary>
public sealed class Pdf417Symbol : IModuleMatrix
{
    private readonly bool[] _modules;

    internal Pdf417Symbol(int rows, int columns, int errorLevel, bool truncated, int width, bool[] modules, string compaction)
    {
        Rows                 = rows;
        Columns              = columns;
        ErrorCorrectionLevel = errorLevel;
        Truncated            = truncated;
        Width                = width;
        _modules             = modules;
        Compaction           = compaction;
    }

    /// <summary>Rows of the symbol — each is one module tall here; the renderer draws them taller.</summary>
    public int Rows { get; }

    /// <summary>Data columns, excluding the row indicators.</summary>
    public int Columns { get; }

    public int ErrorCorrectionLevel { get; }
    public bool Truncated { get; }

    /// <summary>The compaction the payload was written in — for the tooltip.</summary>
    public string Compaction { get; }

    public int Width { get; }
    public int Height => Rows;

    public bool this[int x, int y] => _modules[y * Width + x];
}

/// <summary>
/// Encodes text or bytes as a PDF417 symbol — ISO/IEC 15438.
///
/// <para>
/// A stacked symbology rather than a matrix one: each row is an independent line of bars, and what
/// makes the stack readable is that every row carries its own indicators saying which row it is, how
/// many rows and columns the symbol has, and how much parity it holds. A scanner can therefore piece
/// the symbol together from rows read out of order, or in strips, which is why it survives being
/// dragged across a moving parcel.
/// </para>
/// <para>
/// Three parts, none of which knows the others: compact the payload into base-929 codewords, protect
/// the lot with Reed–Solomon over GF(929), and lay the codewords out in rows, choosing the pattern for
/// each from the cluster its row is drawn in.
/// </para>
/// </summary>
public static class Pdf417Encoder
{
    // ── Codewords the standard reserves ────────────────────────────────────

    private const int LatchText    = 900;
    private const int LatchByte    = 901;
    private const int LatchNumeric = 902;
    private const int LatchByte6   = 924;   // for a byte run that is a multiple of six

    private const int MaxCodewords = 929;   // one symbol may carry 928 data + parity, minus the descriptor

    /// <summary>Data columns a symbol may have.</summary>
    public const int MinColumns = 1, MaxColumns = 30;

    /// <summary>Rows a symbol may have.</summary>
    public const int MinRows = 3, MaxRows = 90;

    /// <summary>Error-correction levels.</summary>
    public const int MinErrorLevel = 0, MaxErrorLevel = 8;

    public static bool TryEncode(string text, Pdf417Options options, out Pdf417Symbol? symbol, out string? error)
        => TryEncode(text, Encoding.UTF8.GetBytes(text ?? string.Empty), options, out symbol, out error);

    /// <summary>Encodes <paramref name="bytes"/>, using <paramref name="text"/> where text compaction is shorter.</summary>
    public static bool TryEncode(string? text, byte[] bytes, Pdf417Options options,
                                 out Pdf417Symbol? symbol, out string? error)
    {
        symbol = null;
        error  = null;

        if (options.Columns is { } c && (c < MinColumns || c > MaxColumns))
        {
            error = $"A PDF417 symbol has {MinColumns} to {MaxColumns} data columns; {c} is outside that.";
            return false;
        }

        if (options.ErrorCorrectionLevel is { } e && (e < MinErrorLevel || e > MaxErrorLevel))
        {
            error = $"PDF417 error-correction levels run {MinErrorLevel} to {MaxErrorLevel}; {e} is outside that.";
            return false;
        }

        var data = Compact(text, bytes, out string compaction);

        int level = options.ErrorCorrectionLevel ?? RecommendedLevel(data.Count + 1);
        int parity = 1 << (level + 1);

        if (!TryShape(data.Count + 1, parity, options, out int rows, out int columns, out error)) return false;

        // The descriptor counts itself and the data, never the parity; the rest of the field is padded
        // with the text latch, which a reader passes over.
        int capacity = rows * columns;
        var words = new List<int>(capacity) { 0 };
        words.AddRange(data);
        while (words.Count + parity < capacity) words.Add(LatchText);
        words[0] = words.Count;

        words.AddRange(Parity(words, parity));

        var modules = Layout(words, rows, columns, level, options.Truncated, out int width);
        symbol = new Pdf417Symbol(rows, columns, level, options.Truncated, width, modules, compaction);
        return true;
    }

    /// <summary>
    /// The level the standard recommends for a payload of this size — enough parity to survive ordinary
    /// handling without spending half the symbol on it.
    /// </summary>
    private static int RecommendedLevel(int codewords) =>
        codewords <= 40 ? 2 : codewords <= 160 ? 3 : codewords <= 320 ? 4 : 5;

    /// <summary>Rows and columns that hold the codewords, near the three-to-one shape a reader expects.</summary>
    private static bool TryShape(int dataWords, int parity, Pdf417Options options,
                                 out int rows, out int columns, out string? error)
    {
        error = null;
        int total = dataWords + parity;

        if (options.Columns is { } fixedColumns)
        {
            columns = fixedColumns;
            rows    = (total + columns - 1) / columns;
        }
        else
        {
            // A symbol about three times as wide as it is tall reads well and wastes little; the columns
            // that gives are the square root of a third of the area.
            columns = Math.Clamp((int)Math.Round(Math.Sqrt(total / 3.0)), MinColumns, MaxColumns);
            rows    = (total + columns - 1) / columns;

            while (rows > MaxRows && columns < MaxColumns)
            {
                columns++;
                rows = (total + columns - 1) / columns;
            }
        }

        rows = Math.Max(rows, MinRows);

        if (rows > MaxRows)
        {
            error = $"Too much data for a PDF417 symbol: {total} codewords need {rows} rows where {MaxRows} is the most allowed.";
            return false;
        }

        if (total > MaxCodewords - 1)
        {
            error = $"Too much data for a PDF417 symbol: {total} codewords where {MaxCodewords - 1} is the most one can hold.";
            return false;
        }

        return true;
    }

    // ── Compaction ─────────────────────────────────────────────────────────

    /// <summary>
    /// The payload as base-929 codewords, in whichever compaction is shortest.
    /// <para>
    /// Text carries two characters per codeword and is much the densest for prose; numeric packs
    /// digits denser still; byte carries anything at a cost. The three can be mixed, and a long run of
    /// digits inside text is worth switching for — which is what <see cref="Runs"/> works out.
    /// </para>
    /// </summary>
    private static List<int> Compact(string? text, byte[] bytes, out string compaction)
    {
        // Bytes that are not the text the caller gave us can only go one way.
        if (text is null || Encoding.UTF8.GetByteCount(text) != bytes.Length || !CanText(text))
        {
            compaction = "Byte";
            return ByteWords(bytes, latch: true);
        }

        var words = new List<int>();
        var used  = new HashSet<string>();

        foreach (var (kind, start, length) in Runs(text))
        {
            switch (kind)
            {
                case Run.Numeric:
                    words.Add(LatchNumeric);
                    words.AddRange(NumericWords(text.Substring(start, length)));
                    used.Add("Numeric");
                    break;

                default:
                    // Text is the mode a symbol starts in, so the first run needs no latch.
                    if (words.Count > 0) words.Add(LatchText);
                    words.AddRange(TextWords(text.Substring(start, length)));
                    used.Add("Text");
                    break;
            }
        }

        var asBytes = ByteWords(bytes, latch: true);
        if (asBytes.Count < words.Count)
        {
            compaction = "Byte";
            return asBytes;
        }

        compaction = string.Join("+", used);
        return words;
    }

    private enum Run { Text, Numeric }

    /// <summary>
    /// The payload split into stretches worth compacting the same way. A digit run pays for its latch
    /// once it is long enough to beat carrying the same digits as text, which is thirteen.
    /// </summary>
    private static IEnumerable<(Run Kind, int Start, int Length)> Runs(string text)
    {
        const int worthSwitching = 13;

        int at = 0;
        while (at < text.Length)
        {
            int digits = 0;
            while (at + digits < text.Length && char.IsAsciiDigit(text[at + digits])) digits++;

            if (digits >= worthSwitching)
            {
                yield return (Run.Numeric, at, digits);
                at += digits;
                continue;
            }

            int start = at;
            while (at < text.Length)
            {
                int run = 0;
                while (at + run < text.Length && char.IsAsciiDigit(text[at + run])) run++;
                if (run >= worthSwitching) break;
                at += Math.Max(run, 1);
            }
            yield return (Run.Text, start, at - start);
        }
    }

    /// <summary>Whether every character has a place in one of text compaction's four sub-modes.</summary>
    private static bool CanText(string text)
    {
        foreach (char ch in text) if (TextValue(ch) is null) return false;
        return true;
    }

    private const string MixedSet = "0123456789&\r\t,:#-.$/+%*=^";
    private const string PunctSet = ";<>@[\\]_`~!\r\t,:\n-.$/\"|*()?{}'";

    private enum Sub { Upper = 0, Lower = 1, Mixed = 2, Punct = 3 }

    /// <summary>Which sub-modes a character lives in, and its value in each.</summary>
    private static (Sub Sub, int Value)[]? TextValue(char ch)
    {
        var places = new List<(Sub, int)>();

        if (ch is >= 'A' and <= 'Z') places.Add((Sub.Upper, ch - 'A'));
        if (ch is >= 'a' and <= 'z') places.Add((Sub.Lower, ch - 'a'));
        if (ch == ' ') { places.Add((Sub.Upper, 26)); places.Add((Sub.Lower, 26)); places.Add((Sub.Mixed, 26)); }

        int mixed = MixedSet.IndexOf(ch);
        if (mixed >= 0) places.Add((Sub.Mixed, mixed));

        int punct = PunctSet.IndexOf(ch);
        if (punct >= 0) places.Add((Sub.Punct, punct));

        return places.Count == 0 ? null : places.ToArray();
    }

    /// <summary>
    /// Text compaction: values 0–29 packed two to a codeword, with latches and shifts between the four
    /// sub-modes. A shift borrows one character from another sub-mode and comes straight back, which is
    /// cheaper than latching there and back for a single capital in a word.
    /// </summary>
    private static List<int> TextWords(string text)
    {
        var values = new List<int>();
        var mode   = Sub.Upper;

        foreach (char ch in text)
        {
            var places = TextValue(ch)!;

            // Already reachable without changing mode.
            var here = Array.Find(places, p => p.Sub == mode);
            if (here != default || Array.Exists(places, p => p.Sub == mode))
            {
                values.Add(Array.Find(places, p => p.Sub == mode).Value);
                continue;
            }

            var target = places[0];

            // Punctuation is reached by a shift from anywhere; a single upper-case letter in lower-case
            // text is the other shift the standard gives, and both save a codeword over latching.
            if (target.Sub == Sub.Punct && mode != Sub.Punct)
            {
                values.Add(mode == Sub.Mixed ? 25 : 29);
                if (mode != Sub.Mixed) { /* upper/lower shift-to-punct is a single-character shift */ }
                values.Add(target.Value);
                continue;
            }

            if (target.Sub == Sub.Upper && mode == Sub.Lower)
            {
                values.Add(27);            // shift to upper for one character
                values.Add(target.Value);
                continue;
            }

            values.AddRange(Latch(mode, target.Sub));
            mode = target.Sub;
            values.Add(target.Value);
        }

        // An odd tail is padded with 29, which reads as a shift and carries nothing.
        if (values.Count % 2 == 1) values.Add(29);

        var words = new List<int>(values.Count / 2);
        for (int i = 0; i < values.Count; i += 2) words.Add(values[i] * 30 + values[i + 1]);
        return words;
    }

    /// <summary>The values that move from one sub-mode to another and stay there.</summary>
    private static int[] Latch(Sub from, Sub to) => (from, to) switch
    {
        (Sub.Upper, Sub.Lower) => [27],
        (Sub.Upper, Sub.Mixed) => [28],
        (Sub.Upper, Sub.Punct) => [28, 25],
        (Sub.Lower, Sub.Upper) => [28, 28],
        (Sub.Lower, Sub.Mixed) => [28],
        (Sub.Lower, Sub.Punct) => [28, 25],
        (Sub.Mixed, Sub.Upper) => [28],
        (Sub.Mixed, Sub.Lower) => [27],
        (Sub.Mixed, Sub.Punct) => [25],
        (Sub.Punct, Sub.Upper) => [29],
        (Sub.Punct, Sub.Lower) => [29, 27],
        (Sub.Punct, Sub.Mixed) => [29, 28],
        _                      => [],
    };

    /// <summary>
    /// Byte compaction: five bytes into six codewords, base 256 to base 900. A run whose length is a
    /// multiple of six uses its own latch, which is how a reader knows the last group is whole.
    /// </summary>
    private static List<int> ByteWords(byte[] bytes, bool latch)
    {
        var words = new List<int>();
        if (latch) words.Add(bytes.Length % 6 == 0 && bytes.Length > 0 ? LatchByte6 : LatchByte);

        int at = 0;
        while (bytes.Length - at >= 6)
        {
            long chunk = 0;
            for (int i = 0; i < 6; i++) chunk = (chunk << 8) | bytes[at + i];

            var six = new int[5];
            for (int i = 4; i >= 0; i--) { six[i] = (int)(chunk % 900); chunk /= 900; }
            words.AddRange(six);
            at += 6;
        }

        // A short tail goes one byte to one codeword, which a reader tells apart by what is left.
        for (; at < bytes.Length; at++) words.Add(bytes[at]);
        return words;
    }

    /// <summary>
    /// Numeric compaction: digits in groups of up to forty-four, each read as one big decimal number
    /// with a leading 1 in front of it, then written in base 900.
    /// </summary>
    private static List<int> NumericWords(string digits)
    {
        var words = new List<int>();

        for (int at = 0; at < digits.Length; at += 44)
        {
            string group = digits.Substring(at, Math.Min(44, digits.Length - at));
            var value = BigInteger.Parse("1" + group);

            var chunk = new List<int>();
            while (value > 0) { chunk.Add((int)(value % 900)); value /= 900; }
            chunk.Reverse();
            words.AddRange(chunk);
        }

        return words;
    }

    // ── Parity ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The parity codewords: the negated remainder over GF(929), which is what makes a reader's
    /// syndromes vanish across the data and the parity together.
    /// </summary>
    private static int[] Parity(List<int> words, int count)
    {
        var generator = ReedSolomon.Generator(GaloisField.Pdf417, count, firstRoot: 1);
        return ReedSolomon.NegatedParity(GaloisField.Pdf417, words.ToArray(), generator);
    }

    // ── Layout ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Lays the codewords out row by row, each row framed by indicators that say where it sits and how
    /// the symbol is shaped. The three clusters cycle with the row, and a reader uses the cluster to
    /// tell which row a strip of bars came from.
    /// </summary>
    private static bool[] Layout(List<int> words, int rows, int columns, int level, bool truncated, out int width)
    {
        // start(17) + left(17) + data(17 × columns) + right(17) + stop(18), or without the last two.
        width = 17 * (columns + 2) + (truncated ? 1 : 17 + 18);

        var modules = new bool[width * rows];

        for (int r = 0; r < rows; r++)
        {
            int cluster = Pdf417Codewords.Clusters[r % 3];
            int k       = r / 3;
            int x       = 0;

            Write(modules, r * width + x, Pdf417Codewords.StartPattern, 17); x += 17;

            var (left, right) = Indicators(r, k, cluster, rows, columns, level);
            Write(modules, r * width + x, Pdf417Codewords.Pattern(cluster, left), 17); x += 17;

            for (int c = 0; c < columns; c++)
            {
                int at = r * columns + c;
                int word = at < words.Count ? words[at] : LatchText;
                Write(modules, r * width + x, Pdf417Codewords.Pattern(cluster, word), 17);
                x += 17;
            }

            if (truncated)
            {
                modules[r * width + x] = true;      // the closing bar, and nothing else
                continue;
            }

            Write(modules, r * width + x, Pdf417Codewords.Pattern(cluster, right), 17); x += 17;
            Write(modules, r * width + x, Pdf417Codewords.StopPattern, Pdf417Codewords.StopModuleCount);
        }

        return modules;
    }

    /// <summary>
    /// The row's two indicator codewords. Between them the three clusters carry the row count, the
    /// column count and the error-correction level, each appearing twice over a group of three rows so
    /// a reader that missed a row can still recover the shape.
    /// </summary>
    private static (int Left, int Right) Indicators(int row, int k, int cluster, int rows, int columns, int level)
        => cluster switch
        {
            0 => (30 * k + (rows - 1) / 3,                 30 * k + columns - 1),
            3 => (30 * k + level * 3 + (rows - 1) % 3,     30 * k + (rows - 1) / 3),
            _ => (30 * k + columns - 1,                    30 * k + level * 3 + (rows - 1) % 3),
        };

    /// <summary>Writes a pattern's modules left to right, most significant bit first.</summary>
    private static void Write(bool[] modules, int at, int pattern, int count)
    {
        for (int i = 0; i < count; i++)
            modules[at + i] = ((pattern >> (count - 1 - i)) & 1) == 1;
    }
}
