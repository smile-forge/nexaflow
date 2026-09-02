using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

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
