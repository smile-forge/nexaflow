using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// SMuFL (Standard Music Font Layout) glyph codepoints for the bundled Bravura font, plus a small
/// drawing helper. The font is loaded once from disk (copied beside the assembly as <c>MusicFonts\</c>);
/// if it can't be found <see cref="Available"/> is false and the engraver falls back to plain geometry,
/// so rendering degrades instead of crashing.
///
/// SMuFL convention: a glyph's origin sits on its <em>baseline</em> at the reference staff position
/// (a notehead's baseline is its vertical centre, a clef's is its reference line), and one em = four
/// staff spaces. <see cref="Draw"/> places a glyph so its baseline lands on the supplied point.
/// </summary>
internal static class Smufl
{
    // ── Codepoints ──────────────────────────────────────────────────────────
    public const int GClef = 0xE050;
    public const int FClef = 0xE062;
    public const int CClef = 0xE05C;

    public const int NoteheadWhole = 0xE0A2;
    public const int NoteheadHalf  = 0xE0A3;
    public const int NoteheadBlack = 0xE0A4;

    public const int Flag8thUp    = 0xE240, Flag8thDown  = 0xE241;
    public const int Flag16thUp   = 0xE242, Flag16thDown = 0xE243;
    public const int Flag32ndUp   = 0xE244, Flag32ndDown = 0xE245;

    public const int AccidentalFlat        = 0xE260;
    public const int AccidentalNatural     = 0xE261;
    public const int AccidentalSharp       = 0xE262;
    public const int AccidentalDoubleSharp = 0xE263;
    public const int AccidentalDoubleFlat  = 0xE264;

    public const int RestWhole   = 0xE4E3;
    public const int RestHalf    = 0xE4E4;
    public const int RestQuarter = 0xE4E5;
    public const int Rest8th     = 0xE4E6;
    public const int Rest16th    = 0xE4E7;

    public const int TimeSig0 = 0xE080; // 0..9 are contiguous: TimeSig0 + digit
    public const int AugmentationDot = 0xE1E7;

    // ── Font loading ────────────────────────────────────────────────────────

    private static readonly FontFamily? _font = LoadFont();

    /// <summary>True when the Bravura font resolved and glyphs can be drawn.</summary>
    public static bool Available => _font is not null;

    private static FontFamily? LoadFont()
    {
        foreach (var dir in CandidateFontDirs())
        {
            try
            {
                if (!File.Exists(Path.Combine(dir, "Bravura.otf"))) continue;
                // Base URI must be the folder (trailing slash); "./#Bravura" selects the family by name.
                var baseUri = new Uri(dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar);
                var family  = new FontFamily(baseUri, "./#Bravura");
                // Force realisation so a bad file fails here (→ next candidate), not mid-render.
                if (family.GetTypefaces().Count > 0) return family;
            }
            catch { /* try next candidate */ }
        }
        return null;
    }

    private static string[] CandidateFontDirs()
    {
        string? asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return
        [
            Path.Combine(AppContext.BaseDirectory, "MusicFonts"),
            asmDir is null ? "" : Path.Combine(asmDir, "MusicFonts"),
        ];
    }

    private static Typeface? _typeface;
    private static Typeface? Face => _typeface ??= _font is null
        ? null
        : new Typeface(_font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // ── Drawing ─────────────────────────────────────────────────────────────

    /// <summary>Draws a SMuFL glyph so its baseline sits at <paramref name="baseline"/>. <paramref name="staffSpace"/>
    /// is the staff-space size in px (em = 4 × staffSpace). Returns the glyph advance width in px.</summary>
    public static double Draw(DrawingContext dc, int codepoint, Point baseline, double staffSpace,
        Brush brush, double pixelsPerDip)
    {
        var face = Face;
        if (face is null) return 0;
        double emSize = 4.0 * staffSpace;
        var ft = new FormattedText(
            char.ConvertFromUtf32(codepoint), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            face, emSize, brush, pixelsPerDip);
        // FormattedText positions by top-left; offset up by the baseline (ascent) so the glyph baseline lands on y.
        dc.DrawText(ft, new Point(baseline.X, baseline.Y - ft.Baseline));
        return ft.WidthIncludingTrailingWhitespace;
    }

    /// <summary>Advance width (px) of a glyph, for layout without drawing.</summary>
    public static double Advance(int codepoint, double staffSpace, double pixelsPerDip)
    {
        var face = Face;
        if (face is null) return staffSpace * 1.2;
        var ft = new FormattedText(
            char.ConvertFromUtf32(codepoint), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            face, 4.0 * staffSpace, Brushes.Black, pixelsPerDip);
        return ft.WidthIncludingTrailingWhitespace;
    }
}
