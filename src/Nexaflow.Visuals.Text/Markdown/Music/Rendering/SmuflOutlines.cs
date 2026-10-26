using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Nexaflow.IO.Common;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// The bundled Bravura font's glyphs as WPF draws them — filled outlines, their advances and their ink — for the codepoints
/// <see cref="Nexaflow.Markdown.Scores.Rendering.Smufl"/> names. The font is loaded once from disk (copied beside the assembly as
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
internal static class SmuflOutlines
{
    // ── Font loading ────────────────────────────────────────────────────────

    private static readonly FontFamily? _font = LoadFont();

    /// <summary>True when the Bravura font resolved and glyphs can be drawn.</summary>
    public static bool Available => _font is not null;

    [LocalDiskOnly("the Bravura font shipped beside the app")]
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

    /// <summary>The Bravura face the outlines are cut from, or null where the font could not be found.</summary>
    internal static Typeface? Face => _face;

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

    /// <summary>
    /// A glyph's outline, already placed so its SMuFL baseline sits at <paramref name="baseline"/>, or null
    /// where the font is missing.
    /// <para>
    /// The same shape <see cref="Draw"/> paints, handed over instead of painted. A builder that records what
    /// it drew on the layout tree needs the geometry rather than the side effect — painting and asking are
    /// then the same walk over the same tree, and a single note can be repainted on its own.
    /// </para>
    /// </summary>
    public static Geometry? Outline(int codepoint, Point baseline, double staffSpace, double scale = 1.0)
    {
        if (Get(codepoint, 4.0 * staffSpace * scale) is not { } shape) return null;

        // Frozen, because the cached outline is shared by every note that draws this glyph and a transform
        // group hung off it would be a second thing to keep in step. A copy placed once is cheaper than a
        // transform pushed on every paint.
        var placed = shape.Outline.Clone();
        placed.Transform = new TranslateTransform(baseline.X, baseline.Y);
        placed.Freeze();
        return placed;
    }

    /// <summary>Advance width (px) of a glyph, for layout without drawing.</summary>
    public static double Advance(int codepoint, double staffSpace, double scale = 1.0) =>
        Get(codepoint, 4.0 * staffSpace * scale)?.Advance ?? staffSpace * 1.2;

    /// <summary>Ink bounds of a glyph relative to its baseline origin — how far it actually reaches. A mark
    /// stacked above the staff is positioned by this, not by the advance.</summary>
    public static Rect Ink(int codepoint, double staffSpace, double scale = 1.0) =>
        Get(codepoint, 4.0 * staffSpace * scale)?.Ink ?? Rect.Empty;
}
