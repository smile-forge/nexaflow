using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode.Encoders;

namespace Nexaflow.Markdown.Barcode;

/// <summary>
/// How the retail symbologies print their number: which digits go where against the bars, and which
/// bars run down past them. Getting this shape wrong (e.g. one centred string) makes a correctly-encoded
/// EAN-13 read as the wrong barcode.
///
/// <para>
/// Computed here from symbology + encoded text rather than in each encoder, since the grouping is a
/// property of the format, not of the encoding — every EAN-13 breaks in the same two places.
/// </para>
/// </summary>
internal static class BarcodeTextLayout
{
    /// <summary>The bar patterns that bound and divide a retail symbol, as (start module, module count).</summary>
    private const int GuardWidth = 3, CentreWidth = 5, DigitWidth = 7;

    /// <summary>
    /// Describes <paramref name="text"/> against a symbol <paramref name="modules"/> wide.
    /// Returns empty for non-retail formats, which just centre their text — no listing needed here.
    /// </summary>
    /// <param name="value">The value as the author wrote it — where a caption's punctuation comes from.</param>
    internal static (IReadOnlyList<BarcodeTextRun> Runs, IReadOnlyList<(int Start, int Length)> Guards, string? Caption)
        Describe(BarcodeSymbology symbology, string value, string text, int modules)
    {
        switch (symbology)
        {
            case BarcodeSymbology.Ean13:
                return (Ean13Runs(text), MainGuards(), null);

            case BarcodeSymbology.Ean8:
                return (Ean8Runs(text), Ean8Guards(), null);

            case BarcodeSymbology.Upc:
                return (UpcRuns(text), MainGuards(), null);

            case BarcodeSymbology.UpcE:
                return (UpcERuns(text), UpcEGuards(), null);

            case BarcodeSymbology.Ean2:
            case BarcodeSymbology.Ean5:
                // An add-on only prints above its bars when beside a main symbol; standalone it's an
                // ordinary barcode — printing above regardless overlapped the digits on the bars.
                return ([], [], null);

            case BarcodeSymbology.Isbn:
            case BarcodeSymbology.Issn:
            case BarcodeSymbology.Ismn:
                return Publication(symbology, value, text, modules);

            default:
                return ([], [], null);
        }
    }

    // ── The retail family ──────────────────────────────────────────────────

    /// <summary>Where the two halves of a thirteen- or twelve-digit symbol sit: 3 · 42 · 5 · 42 · 3.</summary>
    private const int LeftHalf = GuardWidth, RightHalf = GuardWidth + 6 * DigitWidth + CentreWidth;
    private const int HalfWidth = 6 * DigitWidth;

    private static IReadOnlyList<(int Start, int Length)> MainGuards() =>
        [(0, GuardWidth), (LeftHalf + HalfWidth, CentreWidth), (RightHalf + HalfWidth, GuardWidth)];

    /// <summary>
    /// EAN-13: the first digit outside the bars, then six under each half. It's outside because it isn't
    /// separately encoded — it's carried by the parity pattern of the left half's six symbols.
    /// </summary>
    private static IReadOnlyList<BarcodeTextRun> Ean13Runs(string text)
    {
        if (text.Length < 13) return [];

        return
        [
            new BarcodeTextRun(text[..1],     0,         0,         BarcodeTextPlacement.LeftOfBars),
            new BarcodeTextRun(text[1..7],    LeftHalf,  HalfWidth, BarcodeTextPlacement.Below),
            new BarcodeTextRun(text[7..13],   RightHalf, HalfWidth, BarcodeTextPlacement.Below),
        ];
    }

    /// <summary>EAN-8: four under each half, and nothing outside — every digit is encoded.</summary>
    private static IReadOnlyList<BarcodeTextRun> Ean8Runs(string text)
    {
        if (text.Length < 8) return [];

        const int half = 4 * DigitWidth;
        const int right = GuardWidth + half + CentreWidth;

        return
        [
            new BarcodeTextRun(text[..4],  GuardWidth, half, BarcodeTextPlacement.Below),
            new BarcodeTextRun(text[4..8], right,      half, BarcodeTextPlacement.Below),
        ];
    }

    private static IReadOnlyList<(int Start, int Length)> Ean8Guards()
    {
        const int half = 4 * DigitWidth;
        return [(0, GuardWidth), (GuardWidth + half, CentreWidth), (GuardWidth + 2 * half + CentreWidth, GuardWidth)];
    }

    /// <summary>
    /// UPC-A: number system digit outside left, check digit outside right, five digits under each half.
    /// Each half actually encodes six digits — the printed grouping deliberately differs from the encoded one.
    /// </summary>
    private static IReadOnlyList<BarcodeTextRun> UpcRuns(string text)
    {
        if (text.Length < 12) return [];

        return
        [
            new BarcodeTextRun(text[..1],    0,         0,         BarcodeTextPlacement.LeftOfBars),
            new BarcodeTextRun(text[1..6],   LeftHalf,  HalfWidth, BarcodeTextPlacement.Below),
            new BarcodeTextRun(text[6..11],  RightHalf, HalfWidth, BarcodeTextPlacement.Below),
            new BarcodeTextRun(text[11..12], 0,         0,         BarcodeTextPlacement.RightOfBars),
        ];
    }

    /// <summary>UPC-E: number system outside left, the six encoded digits under, check digit outside right.</summary>
    private static IReadOnlyList<BarcodeTextRun> UpcERuns(string text)
    {
        if (text.Length < 8) return [];

        return
        [
            new BarcodeTextRun(text[..1],  0,          0,         BarcodeTextPlacement.LeftOfBars),
            new BarcodeTextRun(text[1..7], GuardWidth, HalfWidth, BarcodeTextPlacement.Below),
            new BarcodeTextRun(text[7..8], 0,          0,         BarcodeTextPlacement.RightOfBars),
        ];
    }

    /// <summary>UPC-E closes with a six-module guard rather than a three-module one.</summary>
    private static IReadOnlyList<(int Start, int Length)> UpcEGuards() =>
        [(0, GuardWidth), (GuardWidth + HalfWidth, 6)];

    // ── Books, journals and printed music ──────────────────────────────────

    /// <summary>
    /// A publication symbol is an EAN-13, optionally with an add-on, under a caption naming the number.
    /// The caption uses the author's own text (not rebuilt from the digits) since where the hyphens fall
    /// depends on registration-group tables not worth shipping just to reprint one line.
    /// </summary>
    private static (IReadOnlyList<BarcodeTextRun>, IReadOnlyList<(int, int)>, string?)
        Publication(BarcodeSymbology symbology, string value, string text, int modules)
    {
        // The main symbol's digits are everything up to the space the add-on was joined on with.
        var main = text.Split(' ', 2);
        var runs = new List<BarcodeTextRun>(Ean13Runs(main[0]));
        var guards = MainGuards();

        const int mainWidth = GuardWidth + 2 * HalfWidth + CentreWidth + GuardWidth;   // 95
        if (main.Length == 2 && modules > mainWidth + PublicationEncoder.AddOnGap)
        {
            int start = mainWidth + PublicationEncoder.AddOnGap;
            runs.Add(new BarcodeTextRun(main[1], start, modules - start, BarcodeTextPlacement.Above));
        }

        return (runs, guards, CaptionFor(symbology, value));
    }

    /// <summary>The caption line: the scheme's name and the number as written, hyphens and all.</summary>
    internal static string CaptionFor(BarcodeSymbology symbology, string value)
    {
        string name = symbology switch
        {
            BarcodeSymbology.Isbn => "ISBN",
            BarcodeSymbology.Ismn => "ISMN",
            _                     => "ISSN",
        };

        // Only the number — an add-on or issue variant that follows it is printed elsewhere on the symbol.
        string number = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
        return $"{name} {number}";
    }

    // ── Reading the symbol's text ──────────────────────────────────────────

    /// <summary>
    /// What is printed with the bars, as the runs a builder sets it from: the caption where there is one, then each run of the
    /// number in the order it is printed, each saying where it goes against the bars. Every printed character that is a character
    /// of the value stands for that character as written (<see cref="BarcodePrintedNode"/>); everything worked out rather than
    /// typed stands for nothing anybody wrote. Builds on <see cref="Describe"/>'s grouping rather than duplicating it.
    /// </summary>
    /// <param name="characters">The value, as the characters it is written with.</param>
    /// <param name="text">What is printed: the value as encoded, or the value where it will not encode.</param>
    /// <param name="modules">How many modules wide the bars are — what a number printed as one run sits under.</param>
    internal static IReadOnlyList<ContentNode> Read(IReadOnlyList<ContentNode> characters, string text,
                                                    IReadOnlyList<BarcodeTextRun> runs, string? caption, int modules)
    {
        var value = string.Concat(characters.Select(character => character.Text));
        var read = new List<ContentNode>();

        if (caption is { Length: > 0 }) read.Add(ReadCaption(caption, value, characters));

        // Formats that don't break up their number print it as one run under the whole of the bars.
        IReadOnlyList<BarcodeTextRun> printed = runs.Count > 0
            ? runs
            : [new BarcodeTextRun(text, 0, modules, BarcodeTextPlacement.Below)];

        // Located once for the whole value, then cut across the printed groups — a group can be part
        // typed and part generated, e.g. EAN-13's last six digits are five typed plus a check digit.
        var window = Window(text, value);

        var at = 0;
        foreach (var run in printed)
        {
            // Runs are cut from the printed text in order but not always edge-to-edge (e.g. the add-on's joining space).
            if (text.IndexOf(run.Text, at, StringComparison.Ordinal) is var found and >= 0) at = found;

            // Falls back to matching one word of the value exactly, for a run like the add-on that prints verbatim.
            var pieces = Pieces(run.Text, at, window.At >= 0 ? window : Word(run.Text, at, value), characters);
            read.Add(new BarcodeRunNode(ContentNode.Branch(pieces is null ? BarcodeKinds.Worked : BarcodeKinds.Group, pieces ?? [], Roles.Derived), run));

            at += run.Text.Length;
        }

        return read;
    }

    /// <summary>
    /// Where the value sits inside a printed string: (index in printed, index in value, length). A
    /// negative index means it isn't there in one piece (e.g. ISBN's hyphens stripped, Pharmacode's
    /// leading zero dropped) — every format either prints the value untouched or with something added to
    /// an end, so a single IndexOf is enough; one that rearranges the value is correctly treated as not found.
    /// </summary>
    private static (int At, int From, int Length) Window(string printed, string value) =>
        value.Length == 0
            ? (-1, 0, 0)
            : (printed.IndexOf(value, StringComparison.Ordinal), 0, value.Length);

    /// <summary>Matches a run against one word of the value verbatim, e.g. a publication's add-on printed as typed.</summary>
    private static (int At, int From, int Length) Word(string run, int at, string value)
    {
        foreach (var (word, from) in Words(value))
            if (word == run) return (at, from, word.Length);

        return (-1, 0, 0);
    }

    /// <summary>The value's words — split where the author put spaces — each with where it begins.</summary>
    private static IEnumerable<(string Word, int From)> Words(string value)
    {
        for (var at = 0; at < value.Length;)
        {
            while (at < value.Length && char.IsWhiteSpace(value[at])) at++;

            var start = at;
            while (at < value.Length && !char.IsWhiteSpace(value[at])) at++;

            if (at > start) yield return (value[start..at], start);
        }
    }

    /// <summary>
    /// The caption: a generated scheme name plus the number as the author wrote it (hyphens and all) — the
    /// one place a publication's number appears as itself, since the digits under the bars are a rendering
    /// of it. Matches only the number, not the whole value, so an add-on after it stays selectable there
    /// instead of being wrongly claimed by the caption.
    /// </summary>
    private static ContentNode ReadCaption(string caption, string value, IReadOnlyList<ContentNode> characters)
    {
        var window = Window(caption, value);

        var number = Words(value).FirstOrDefault();
        if (window.At < 0 && number.Word is { Length: > 0 })
            window = (caption.IndexOf(number.Word, StringComparison.Ordinal), number.From, number.Word.Length);

        var pieces = Pieces(caption, 0, window, characters) ?? [ContentNode.Leaf(BarcodeKinds.Worked, caption, Roles.Derived)];

        return new BarcodeRunNode(ContentNode.Branch(BarcodeKinds.Caption, pieces, Roles.Derived),
                                  new BarcodeTextRun(caption, 0, 0, BarcodeTextPlacement.Above));
    }

    /// <summary>
    /// One printed run cut against the value's window (where a stretch of the value was found in what is printed): characters
    /// inside it stand for the characters of the value they are, and what is outside it — a Codabar start or stop mark, UPC-E's
    /// number system digit, EAN-13's check digit — is worked out. Null where none of the value is in it, so the run is worked out
    /// whole.
    /// </summary>
    /// <param name="run">What this run prints.</param>
    /// <param name="at">Where the run begins in the whole printed number.</param>
    /// <param name="window">
    /// Where a stretch of the value sits in the printed number (<c>At</c>), where it begins in the value
    /// (<c>From</c>), and its length. <c>At</c> is negative when none of the value is in it (e.g. ISBN's
    /// stripped hyphens, Pharmacode's dropped leading zero).
    /// </param>
    private static IReadOnlyList<ContentNode>? Pieces(string run, int at, (int At, int From, int Length) window,
                                                      IReadOnlyList<ContentNode> characters)
    {
        if (run.Length == 0) return [];

        // Where this run overlaps the value, in the printed number's own indices.
        var from = Math.Max(at, window.At);
        var to = Math.Min(at + run.Length, window.At + window.Length);

        if (window.At < 0 || to <= from) return null;

        var pieces = new List<ContentNode>();

        Worked(at, from);

        // One node per character — the smallest thing a caret can stand beside.
        for (var p = from; p < to; p++)
            pieces.Add(new BarcodePrintedNode(characters[window.From + (p - window.At)]));

        Worked(to, at + run.Length);

        return pieces;

        void Worked(int start, int end)
        {
            if (end > start) pieces.Add(ContentNode.Leaf(BarcodeKinds.Worked, run[(start - at)..(end - at)], Roles.Derived));
        }
    }
}
