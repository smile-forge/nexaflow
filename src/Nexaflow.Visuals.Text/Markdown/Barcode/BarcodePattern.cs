using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>Where a run of the human-readable text sits against the bars.</summary>
public enum BarcodeTextPlacement
{
    /// <summary>Under the bars, in the space the guard pattern leaves for it.</summary>
    Below,

    /// <summary>In the quiet zone to the left of the symbol — an EAN-13's first digit.</summary>
    LeftOfBars,

    /// <summary>In the quiet zone to the right — a UPC's check digit.</summary>
    RightOfBars,

    /// <summary>Over the bars, which is where an add-on prints its digits.</summary>
    Above,
}

/// <summary>
/// One run of the human-readable text and the stretch of bars it belongs to.
///
/// <para>
/// The retail symbologies do not print their digits as one string underneath. The number is broken at
/// the guard patterns and each group sits in the gap its half of the symbol leaves for it, with the
/// first digit outside the bars altogether — which is why an EAN-13 printed as one centred run reads as
/// the wrong barcode even when every module is right.
/// </para>
/// </summary>
/// <param name="Text">The digits in this group.</param>
/// <param name="StartModule">The first module the group sits over. Ignored when it sits outside the bars.</param>
/// <param name="Modules">How many modules wide that stretch is.</param>
/// <param name="Placement">Where the group goes.</param>
public readonly record struct BarcodeTextRun(
    string Text, int StartModule, int Modules, BarcodeTextPlacement Placement);

/// <summary>
/// An encoded barcode: the bars themselves, and the text printed under them.
///
/// <para>
/// Every symbology here reduces to the same thing — a row of equal-width modules, each either ink or
/// paper — so one type carries all of them and the renderer never learns what an EAN is. Widths in the
/// block's <c>width:</c> setting scale a module; nothing in here is in pixels.
/// </para>
/// </summary>
public sealed class BarcodePattern
{
    private readonly bool[] _modules;

    internal BarcodePattern(BarcodeSymbology symbology, bool[] modules, string text)
    {
        Symbology = symbology;
        _modules  = modules;
        Text      = text;
    }

    public BarcodeSymbology Symbology { get; }

    /// <summary>How many modules wide the symbol is, quiet zones excluded.</summary>
    public int Width => _modules.Length;

    /// <summary>True where the module is ink.</summary>
    public bool this[int index] => _modules[index];

    /// <summary>
    /// What is printed beneath the bars — the value as encoded, which for the symbologies that compute
    /// one includes the check digit the reader will see on a real label.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// How <see cref="Text"/> is broken up against the bars, or empty when the whole of it simply goes
    /// underneath — which is what every symbology but the retail family wants.
    /// </summary>
    public IReadOnlyList<BarcodeTextRun> TextRuns { get; init; } = [];

    /// <summary>
    /// Stretches of bar that run down past the text row: an EAN's start, centre and end guards. They are
    /// what makes the two halves of the number look like halves, and a scanner uses them to find the
    /// symbol's edges however it is held.
    /// </summary>
    public IReadOnlyList<(int Start, int Length)> Guards { get; init; } = [];

    /// <summary>A line printed above the whole symbol — the <c>ISBN 978-…</c> over a book's barcode.</summary>
    public string? Caption { get; init; }

    /// <summary>
    /// The symbol as it is understood: the caption, the bars and their guards, and each printed run of the
    /// number, every one saying which characters of the value it came from — or, where it was worked out
    /// rather than typed, which characters it was worked out from.
    ///
    /// <para>
    /// <see cref="TextRuns"/>, <see cref="Guards"/> and <see cref="Caption"/> are the same facts flattened
    /// for a renderer that only wants to draw. This is the shape a caret and a selection need, because it
    /// is the only one that says which parts of what is printed an edit could safely be applied to.
    /// </para>
    /// </summary>
    public BarcodePart? Symbol { get; init; }

    /// <summary>The modules as a run-length list of (start, length, isInk), for drawing.</summary>
    public IEnumerable<(int Start, int Length)> InkRuns()
    {
        int index = 0;
        while (index < _modules.Length)
        {
            if (!_modules[index]) { index++; continue; }

            int run = 1;
            while (index + run < _modules.Length && _modules[index + run]) run++;

            yield return (index, run);
            index += run;
        }
    }

    /// <summary>The modules as <c>1</c>/<c>0</c>, for a test to read at a glance.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder(_modules.Length);
        foreach (bool module in _modules) sb.Append(module ? '1' : '0');
        return sb.ToString();
    }

    // ── Building ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a pattern from digit strings of element widths, alternating ink and paper and starting
    /// with ink — the form every one of these symbologies is tabulated in.
    /// </summary>
    internal static bool[] FromWidths(IEnumerable<string> symbols)
    {
        var modules = new List<bool>(128);
        foreach (string symbol in symbols)
        {
            bool ink = true;
            foreach (char width in symbol)
            {
                int count = width - '0';
                for (int i = 0; i < count; i++) modules.Add(ink);
                ink = !ink;
            }
        }
        return [.. modules];
    }

    /// <summary>Builds a pattern from a string of <c>1</c> and <c>0</c> — the form the EAN family is tabulated in.</summary>
    internal static bool[] FromBits(string bits)
    {
        var modules = new bool[bits.Length];
        for (int i = 0; i < bits.Length; i++) modules[i] = bits[i] == '1';
        return modules;
    }
}
