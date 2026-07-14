using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// SMuFL (Standard Music Font Layout) glyph codepoints for the bundled Bravura font, plus the drawing helper
/// every glyph goes through. The font is loaded once from disk (copied beside the assembly as
/// <c>MusicFonts\</c>); if it can't be found <see cref="Available"/> is false and the engraver falls back to
/// plain geometry, so rendering degrades instead of crashing.
///
/// SMuFL convention: a glyph's origin sits on its <em>baseline</em> at the reference staff position (a note
/// head's baseline is its vertical centre, a clef's is its reference line), and one em = four staff spaces.
/// <see cref="Draw"/> places a glyph so its baseline lands on the supplied point.
///
/// Glyphs are drawn as <em>filled outlines</em>, not as text. WPF's text pipeline gamma-corrects and
/// contrast-enhances glyph coverage — the right call for prose at 11pt, but it visibly fattens the thin strokes
/// of a music font, which is why accidentals and clefs came out heavier than a reference engraving. Filling the
/// outline reproduces the designed weight exactly. Outlines are cached per (glyph, em size) because a score
/// repaints on every selection change.
/// </summary>
internal static class Smufl
{
    // ── Codepoints ──────────────────────────────────────────────────────────
    public const int GClef = 0xE050;
    public const int FClef = 0xE062;
    public const int CClef = 0xE05C;

    public const int NoteheadDoubleWhole = 0xE0A0;
    public const int NoteheadWhole       = 0xE0A2;
    public const int NoteheadHalf        = 0xE0A3;
    public const int NoteheadBlack       = 0xE0A4;

    public const int Flag8thUp    = 0xE240, Flag8thDown  = 0xE241;
    public const int Flag16thUp   = 0xE242, Flag16thDown = 0xE243;
    public const int Flag32ndUp   = 0xE244, Flag32ndDown = 0xE245;
    public const int Flag64thUp   = 0xE246, Flag64thDown = 0xE247;

    public const int AccidentalFlat        = 0xE260;
    public const int AccidentalNatural     = 0xE261;
    public const int AccidentalSharp       = 0xE262;
    public const int AccidentalDoubleSharp = 0xE263;
    public const int AccidentalDoubleFlat  = 0xE264;

    public const int RestDoubleWhole = 0xE4E2;
    public const int RestWhole       = 0xE4E3;
    public const int RestHalf        = 0xE4E4;
    public const int RestQuarter     = 0xE4E5;
    public const int Rest8th         = 0xE4E6;
    public const int Rest16th        = 0xE4E7;
    public const int Rest32nd        = 0xE4E8;

    public const int TimeSig0 = 0xE080;          // 0..9 are contiguous: TimeSig0 + digit
    public const int AugmentationDot = 0xE1E7;

    // Articulations. Each has an "Above" and a "Below" form; the Below glyph is the next codepoint up.
    public const int ArticAccentAbove   = 0xE4A0;
    public const int ArticStaccatoAbove = 0xE4A2;
    public const int ArticTenutoAbove   = 0xE4A4;
    public const int ArticMarcatoAbove  = 0xE4AC;

    public const int FermataAbove         = 0xE4C0;
    public const int OrnamentTrill        = 0xE566;
    public const int OrnamentTurn         = 0xE567;
    public const int OrnamentMordent      = 0xE56C;
    public const int OrnamentLowerMordent = 0xE56D;

    public const int StringsDownBow = 0xE610;
    public const int StringsUpBow   = 0xE612;

    public const int Segno = 0xE047;
    public const int Coda  = 0xE048;

    /// <summary>Tuplet digits — a dedicated set, narrower than the time-signature figures.</summary>
    public const int TupletDigit0 = 0xE880;

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

    private static readonly Typeface? _face = _font is null
        ? null
        : new Typeface(_font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static Typeface? Face => _face;

    // ── Outline cache ───────────────────────────────────────────────────────

    private sealed record Shape(Geometry Outline, double Advance, Rect Ink);

    // Concurrent, and the geometries are frozen: a score can be measured or painted from any thread (WPF UI
    // threads are per-window, and the tests run several at once), so the cache must not be a plain Dictionary.
    private static readonly ConcurrentDictionary<(int Cp, double Em), Shape?> _shapes = new();

    private static Shape? Get(int codepoint, double emSize) =>
        _shapes.GetOrAdd((codepoint, Math.Round(emSize, 2)), static key =>
        {
            if (Face is not { } face || key.Em <= 0) return null;
            var ft = new FormattedText(
                char.ConvertFromUtf32(key.Cp), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                face, key.Em, Brushes.Black, 1.0);
            // BuildGeometry lays the glyph out from the top-left of its line box; shift it up by the ascent so
            // the geometry's origin becomes the SMuFL baseline, which is what every caller positions against.
            var geo = ft.BuildGeometry(new Point(0, -ft.Baseline));
            geo.Freeze();
            return new Shape(geo, ft.WidthIncludingTrailingWhitespace, geo.Bounds);
        });

    // ── Drawing ─────────────────────────────────────────────────────────────

    /// <summary>Draws a glyph so its baseline sits at <paramref name="baseline"/>. <paramref name="staffSpace"/>
    /// is the staff-space size in px (em = 4 × staffSpace); <paramref name="scale"/> shrinks it for grace notes
    /// and cue-size marks. Returns the glyph's advance width.</summary>
    public static double Draw(DrawingContext dc, int codepoint, Point baseline, double staffSpace, Brush brush,
        double scale = 1.0)
    {
        var shape = Get(codepoint, 4.0 * staffSpace * scale);
        if (shape is null) return 0;
        dc.PushTransform(new TranslateTransform(baseline.X, baseline.Y));
        dc.DrawGeometry(brush, null, shape.Outline);
        dc.Pop();
        return shape.Advance;
    }

    /// <summary>Advance width (px) of a glyph, for layout without drawing.</summary>
    public static double Advance(int codepoint, double staffSpace, double scale = 1.0) =>
        Get(codepoint, 4.0 * staffSpace * scale)?.Advance ?? staffSpace * 1.2;

    /// <summary>Ink bounds of a glyph relative to its baseline origin — how far it actually reaches. A mark
    /// stacked above the staff is positioned by this, not by the advance.</summary>
    public static Rect Ink(int codepoint, double staffSpace, double scale = 1.0) =>
        Get(codepoint, 4.0 * staffSpace * scale)?.Ink ?? Rect.Empty;
}
