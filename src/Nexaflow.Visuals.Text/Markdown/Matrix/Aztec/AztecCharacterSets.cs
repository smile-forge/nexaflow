using System;
using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// The five character sets an Aztec message is written in. Four are five bits wide and one — digits —
/// is four, which is what makes a run of numbers cheap and why the encoder has to decide when a run is
/// long enough to be worth switching for.
/// </summary>
internal enum AztecCharacterSet
{
    Upper,
    Lower,
    Mixed,
    Punct,
    Digit,
}

/// <summary>
/// What each five- or four-bit code means in each of Aztec's character sets, and what it costs to get
/// from one set to another.
///
/// <para>
/// This is the whole vocabulary of the symbology's text encoding, kept apart from the encoder that
/// searches it. The tables are the standard's; the two things derived from them here are the shortest
/// latch route between any pair of sets — <c>Digit</c> reaches <c>Lower</c> only through <c>Upper</c>,
/// and the cheap way there is not the obvious one — and the reverse lookups the search needs. Deriving
/// those rather than writing them out is deliberate: a hand-copied latch table is a silent one-bit tax
/// on every symbol that takes the wrong route.
/// </para>
/// </summary>
internal static class AztecCharacterSets
{
    /// <summary>A code and the width it is written in — the width belongs to the set it is issued from.</summary>
    internal readonly record struct Code(int Value, int Width);

    /// <summary>Punctuation shift: the next character comes from <see cref="AztecCharacterSet.Punct"/>.</summary>
    internal const int PunctShift = 0;

    /// <summary>Byte shift: a run of raw bytes follows, then the set in force resumes.</summary>
    internal const int ByteShift = 31;

    /// <summary>FLG(n) — <see cref="AztecCharacterSet.Punct"/> code zero, the escape that carries FNC1 and ECI.</summary>
    internal const int Flg = 0;

    /// <summary>The longest run a single byte shift can carry: thirty-one under the short count, 2078 under the long one.</summary>
    internal const int MaxByteRun = 31 + 2047;

    /// <summary>Bits per code. Digits are four; everything else is five.</summary>
    internal static int Width(AztecCharacterSet set) => set == AztecCharacterSet.Digit ? 4 : 5;

    // ── The character tables ───────────────────────────────────────────────
    //
    // One entry per code, in code order. A null is a control code — a latch, a shift, or FLG — named
    // by the constants above and by the latch table below rather than by a character.

    private const string? Control = null;

    private static readonly string?[] UpperCodes =
    [
        Control, " ",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        Control, Control, Control, Control,      // L/L, M/L, D/L, B/S
    ];

    private static readonly string?[] LowerCodes =
    [
        Control, " ",
        "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m",
        "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
        Control, Control, Control, Control,      // U/S, M/L, D/L, B/S
    ];

    // The control characters Mixed can name are ^A to ^M and ^[ to ^_. The gap — ^N to ^Z — has no
    // code in any set, so anything containing one can only be carried by a byte shift.
    private static readonly string?[] MixedCodes =
    [
        Control, " ",
        "\u0001", "\u0002", "\u0003", "\u0004", "\u0005", "\u0006", "\u0007",
        "\u0008", "\u0009", "\u000A", "\u000B", "\u000C", "\u000D",
        "\u001B", "\u001C", "\u001D", "\u001E", "\u001F",
        "@", "\\", "^", "_", "`", "|", "~", "\u007F",
        Control, Control, Control, Control,      // L/L, U/L, P/L, B/S
    ];

    // Codes one to five carry a line break or two characters at once, which is what makes prose
    // cheap: a sentence's ". " costs one code rather than two.
    private static readonly string?[] PunctCodes =
    [
        Control, "\r", "\r\n", ". ", ", ", ": ",
        "!", "\"", "#", "$", "%", "&", "'", "(", ")", "*", "+", ",", "-", ".", "/",
        ":", ";", "<", "=", ">", "?", "[", "]", "{", "}",
        Control,                                 // U/L
    ];

    private static readonly string?[] DigitCodes =
    [
        Control, " ",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
        ",", ".",
        Control, Control,                        // U/L, U/S
    ];

    private static readonly string?[][] Codes =
        [UpperCodes, LowerCodes, MixedCodes, PunctCodes, DigitCodes];

    // ── Reverse lookups ────────────────────────────────────────────────────

    private static readonly Dictionary<char, int>[] SingleByCharacter = BuildSingles();
    private static readonly Dictionary<(char First, char Second), int> PairsInPunct = BuildPairs();

    private static Dictionary<char, int>[] BuildSingles()
    {
        var maps = new Dictionary<char, int>[Codes.Length];
        for (int set = 0; set < Codes.Length; set++)
        {
            maps[set] = [];
            for (int code = 0; code < Codes[set].Length; code++)
                if (Codes[set][code] is { Length: 1 } text) maps[set][text[0]] = code;
        }
        return maps;
    }

    private static Dictionary<(char, char), int> BuildPairs()
    {
        var map = new Dictionary<(char, char), int>();
        for (int code = 0; code < PunctCodes.Length; code++)
            if (PunctCodes[code] is { Length: 2 } text) map[(text[0], text[1])] = code;
        return map;
    }

    /// <summary>The code for <paramref name="character"/> in <paramref name="set"/>, or null when it has none.</summary>
    internal static int? Single(AztecCharacterSet set, char character) =>
        SingleByCharacter[(int)set].TryGetValue(character, out int code) ? code : null;

    /// <summary>The Punct code that carries both characters at once — CR LF, ". ", ", " or ": ".</summary>
    internal static int? Pair(char first, char second) =>
        PairsInPunct.TryGetValue((first, second), out int code) ? code : null;

    /// <summary>What <paramref name="code"/> decodes to in <paramref name="set"/>, or null for a control code.</summary>
    internal static string? Text(AztecCharacterSet set, int code)
    {
        var table = Codes[(int)set];
        return code >= 0 && code < table.Length ? table[code] : null;
    }

    // ── Latching and shifting ──────────────────────────────────────────────

    /// <summary>
    /// The latches the standard gives directly. Everything else is a route through these, which is why
    /// the table below is computed rather than written down.
    /// </summary>
    private static readonly (AztecCharacterSet From, AztecCharacterSet To, int Code)[] DirectLatches =
    [
        (AztecCharacterSet.Upper, AztecCharacterSet.Lower, 28),
        (AztecCharacterSet.Upper, AztecCharacterSet.Mixed, 29),
        (AztecCharacterSet.Upper, AztecCharacterSet.Digit, 30),
        (AztecCharacterSet.Lower, AztecCharacterSet.Mixed, 29),
        (AztecCharacterSet.Lower, AztecCharacterSet.Digit, 30),
        (AztecCharacterSet.Mixed, AztecCharacterSet.Lower, 28),
        (AztecCharacterSet.Mixed, AztecCharacterSet.Upper, 29),
        (AztecCharacterSet.Mixed, AztecCharacterSet.Punct, 30),
        (AztecCharacterSet.Punct, AztecCharacterSet.Upper, 31),
        (AztecCharacterSet.Digit, AztecCharacterSet.Upper, 14),
    ];

    /// <summary>
    /// The single-character shifts: a code that borrows one character from another set and then gives
    /// the set in force straight back. Punct is reachable from everywhere; Upper only from Lower and
    /// Digit — Mixed has to latch, which is why a lone capital among symbols is expensive.
    /// </summary>
    private static readonly (AztecCharacterSet From, AztecCharacterSet To, int Code)[] DirectShifts =
    [
        (AztecCharacterSet.Upper, AztecCharacterSet.Punct, PunctShift),
        (AztecCharacterSet.Lower, AztecCharacterSet.Punct, PunctShift),
        (AztecCharacterSet.Mixed, AztecCharacterSet.Punct, PunctShift),
        (AztecCharacterSet.Digit, AztecCharacterSet.Punct, PunctShift),
        (AztecCharacterSet.Lower, AztecCharacterSet.Upper, 28),
        (AztecCharacterSet.Digit, AztecCharacterSet.Upper, 15),
    ];

    private static readonly Code[]?[,] LatchRoutes = BuildLatchRoutes();

    /// <summary>
    /// The cheapest sequence of latch codes from one set to another, by shortest path over the direct
    /// latches. An empty route means the sets are the same; there is no unreachable pair, because every
    /// set can reach Upper and Upper can reach everything.
    /// </summary>
    private static Code[]?[,] BuildLatchRoutes()
    {
        int n = Codes.Length;
        var routes = new Code[]?[n, n];
        var cost   = new int[n, n];

        for (int from = 0; from < n; from++)
            for (int to = 0; to < n; to++)
            {
                cost[from, to]   = from == to ? 0 : Unreachable;
                routes[from, to] = from == to ? [] : null;
            }

        foreach (var (from, to, code) in DirectLatches)
        {
            int bits = Width(from);
            if (bits >= cost[(int)from, (int)to]) continue;
            cost[(int)from, (int)to]   = bits;
            routes[(int)from, (int)to] = [new Code(code, bits)];
        }

        // Floyd–Warshall. Five nodes, so computing this costs nothing, while mistyping the transitive
        // routes by hand costs every affected symbol a few bits — the kind of defect nothing reports.
        for (int via = 0; via < n; via++)
            for (int from = 0; from < n; from++)
                for (int to = 0; to < n; to++)
                {
                    if (routes[from, via] is null || routes[via, to] is null) continue;
                    int through = cost[from, via] + cost[via, to];
                    if (through >= cost[from, to]) continue;
                    cost[from, to]   = through;
                    routes[from, to] = [.. routes[from, via]!, .. routes[via, to]!];
                }

        return routes;
    }

    private const int Unreachable = int.MaxValue / 4;

    /// <summary>The latch codes that move from one set to another; empty when they are the same.</summary>
    internal static Code[] Latch(AztecCharacterSet from, AztecCharacterSet to) =>
        LatchRoutes[(int)from, (int)to]
        ?? throw new InvalidOperationException($"No latch route from {from} to {to}.");

    /// <summary>The bits the latch route from one set to another costs.</summary>
    internal static int LatchCost(AztecCharacterSet from, AztecCharacterSet to)
    {
        int bits = 0;
        foreach (var code in Latch(from, to)) bits += code.Width;
        return bits;
    }

    /// <summary>The code that shifts one character out of <paramref name="from"/> into <paramref name="to"/>, if there is one.</summary>
    internal static Code? Shift(AztecCharacterSet from, AztecCharacterSet to)
    {
        foreach (var (f, t, code) in DirectShifts)
            if (f == from && t == to) return new Code(code, Width(from));
        return null;
    }

    /// <summary>Whether a byte shift can be issued from <paramref name="set"/>. Punct and Digit have no B/S code.</summary>
    internal static bool CanByteShift(AztecCharacterSet set) =>
        set is AztecCharacterSet.Upper or AztecCharacterSet.Lower or AztecCharacterSet.Mixed;

    /// <summary>Every set, for walking the encoder's states.</summary>
    internal static readonly AztecCharacterSet[] All =
        [AztecCharacterSet.Upper, AztecCharacterSet.Lower, AztecCharacterSet.Mixed,
         AztecCharacterSet.Punct, AztecCharacterSet.Digit];
}
