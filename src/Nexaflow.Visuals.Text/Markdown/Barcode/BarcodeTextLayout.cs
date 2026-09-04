using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Visuals.Text.Markdown.Barcode.Encoders;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>
/// How the retail symbologies print their number: which digits go where against the bars, and which
/// bars run down past them.
///
/// <para>
/// This is not decoration. An EAN-13 is recognised by its shape — one digit out on its own to the left,
/// two groups of six sitting in the wells the guard bars leave, the guards themselves dropping past the
/// digits — and printed as one centred string underneath it reads as some other barcode entirely, even
/// though every module is right. The reference images make the point better than any description: the
/// bars matched and nothing else did.
/// </para>
/// <para>
/// Worked out here from the symbology and the encoded text rather than threaded back through each
/// encoder, because it is a property of the format and not of the encoding: every EAN-13 ever printed
/// breaks in the same two places.
/// </para>
/// </summary>
internal static class BarcodeTextLayout
{
    /// <summary>The bar patterns that bound and divide a retail symbol, as (start module, module count).</summary>
    private const int GuardWidth = 3, CentreWidth = 5, DigitWidth = 7;

    /// <summary>
    /// Describes <paramref name="text"/> against a symbol <paramref name="modules"/> wide.
    /// <para>
    /// Returns nothing for the symbologies that really do print their text as one run underneath, which
    /// is every one outside the retail family — the caller then falls back to centring it, and no format
    /// has to be listed here to be drawn correctly.
    /// </para>
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
                // Nothing special. An add-on prints its digits above its bars only when it is standing beside a
                // main symbol, where being above is what separates the two; asked for on its own it is an
                // ordinary little barcode and prints them underneath like any other. Drawn above regardless,
                // they landed on top of the bars and made the symbol unreadable.
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
    /// EAN-13: the first digit outside the bars, then six under each half.
    /// <para>
    /// The digit is outside because there is nothing encoding it — the left half's six symbols carry the
    /// next six digits, and the thirteenth is carried by which parity pattern those six use. There is no
    /// stretch of bar it could sit under, so it sits beside them.
    /// </para>
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
    /// UPC-A: the number system digit outside on the left and the check digit outside on the right, with
    /// five under each half.
    /// <para>
    /// The printed groups are deliberately not the encoded ones. Each half encodes six digits; what is
    /// printed under it is five of them, because the two on the ends are set outside to mark where the
    /// number begins and ends. It is the one place where reading the label and reading the bars give
    /// different groupings of the same twelve digits.
    /// </para>
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
    /// A publication symbol is an EAN-13, optionally with an add-on beside it, under a caption naming
    /// the number it stands for.
    /// <para>
    /// The caption is taken from what the author wrote rather than rebuilt from the thirteen digits,
    /// because the hyphens are not derivable: where they fall depends on which registration group and
    /// which registrant the number belongs to, which is a table nobody should be shipping to print one
    /// line of text. What was typed is already correct.
    /// </para>
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

        return (runs, guards, Caption(symbology, value));
    }

    /// <summary>The caption line: the scheme's name and the number as written, hyphens and all.</summary>
    private static string Caption(BarcodeSymbology symbology, string value)
    {
        string name = symbology switch
        {
            BarcodeSymbology.Isbn => "ISBN",
            BarcodeSymbology.Ismn => "ISMN",
            _                     => "ISSN",
        };

        // Only the number itself. Whatever followed it on the line is the add-on or an issue variant,
        // and both are printed elsewhere on the symbol rather than in the caption.
        string number = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
        return $"{name} {number}";
    }

    // ── Reading the symbol as a tree ───────────────────────────────────────

    /// <summary>
    /// The whole symbol as a tree of parts: the caption, the bars and their guards, and each printed run
    /// of the number, every one of them saying which characters of the value it came from.
    /// <para>
    /// Built on top of <see cref="Describe"/> rather than instead of it, because that is where each
    /// family's structure is already worked out and checked. This only decides, for each run, whether what
    /// is printed <em>is</em> the value or was worked out from it.
    /// </para>
    /// </summary>
    internal static BarcodePart Read(
        string value, string text, int modules,
        IReadOnlyList<BarcodeTextRun> runs,
        IReadOnlyList<(int Start, int Length)> guards,
        string? caption)
    {
        var parts = new List<BarcodePart>();

        if (caption is { Length: > 0 }) parts.Add(ReadCaption(caption, value));

        // The bars stand for the whole value — they are the value, encoded — while the guards inside them
        // stand for none of it, being the format's own punctuation rather than anything anybody typed. So a
        // press on a guard resolves outwards to the bars, and means the number.
        parts.Add(BarcodePart.Spanning(
            BarcodeKind.Bars, BarcodeRole.Bars, 0, value.Length,
            guards.Select(guard => BarcodePart.Leaf(
                BarcodeKind.Guard, BarcodeRole.Guard, string.Empty, 0, 0, guard)),
            (0, modules)));

        // Every format that does not break its number up prints it as one run underneath, which is what
        // the caller would otherwise have to remember to do for it.
        IReadOnlyList<BarcodeTextRun> printed = runs.Count > 0
            ? runs
            : [new BarcodeTextRun(text, 0, modules, BarcodeTextPlacement.Below)];

        // Asked once, of the whole printed number, rather than of each group. The groups are how the number
        // is laid out and not what it is made of: an EAN-13 whose value is missing its check digit prints
        // six digits that happen to match what was typed and six that are one character out of step, and
        // calling the first six editable would be an accident of where the guard bars fall. Either the
        // printed number is the value or it is a rendering of it.
        var verbatim = string.Equals(text, value, StringComparison.Ordinal);

        var at = 0;
        foreach (var run in printed)
        {
            parts.Add(BarcodePart.Read(
                run.Placement == BarcodeTextPlacement.Above ? BarcodeRole.AddOn : BarcodeRole.Label,
                run.Text,
                value,
                verbatim ? at : -1,
                (run.StartModule, run.Modules),
                run.Placement));

            at += run.Text.Length;
        }

        return BarcodePart.Symbol(value, parts);
    }

    /// <summary>
    /// The caption, read as the two different things it is made of: a scheme's name, which is ours and
    /// which nobody typed, and the number as the author wrote it, hyphens and all, which is theirs.
    /// <para>
    /// That split is what makes a publication the one format whose value can be edited where it is drawn.
    /// The digits under the bars are the number with its hyphens taken out and a check digit added, so
    /// they are a rendering; the caption is the value itself.
    /// </para>
    /// </summary>
    private static BarcodePart ReadCaption(string caption, string value)
    {
        var split = caption.IndexOf(' ');
        if (split < 0)
            return BarcodePart.Leaf(BarcodeKind.EncodedText, BarcodeRole.Scheme, caption, 0, value.Length);

        return BarcodePart.Branch(BarcodeKind.Caption, BarcodeRole.Caption,
        [
            BarcodePart.Leaf(BarcodeKind.EncodedText, BarcodeRole.Scheme, caption[..(split + 1)], 0, value.Length),
            BarcodePart.Read(BarcodeRole.Number, caption[(split + 1)..], value, 0),
        ]);
    }
}
