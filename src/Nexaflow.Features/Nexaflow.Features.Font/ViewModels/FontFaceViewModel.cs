using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Font.ViewModels;

/// <summary>
/// One face (typeface) within a font family — e.g. Regular, Bold, Italic — flattened from its
/// <see cref="GlyphTypeface"/>. Supplies the chip label shown in the details panel and the per-face
/// "Style &amp; metrics" rows; the family-level Identity/Legal/Technical rows come from the
/// representative face (see <see cref="FontItemViewModel"/>).
/// </summary>
public sealed partial class FontFaceViewModel : ObservableObject
{
    public Typeface Typeface { get; }
    public GlyphTypeface Glyph { get; }

    /// <summary>True when this is the font's currently-selected face (drives the chip's accent).</summary>
    [ObservableProperty] private bool _isSelected;

    public FontFaceViewModel(Typeface typeface, GlyphTypeface glyph)
    {
        Typeface = typeface;
        Glyph = glyph;
    }

    /// <summary>True for a Normal/Normal/Normal face — the family's representative for identity fields.</summary>
    public bool IsRegular =>
        Glyph.Style == FontStyles.Normal &&
        Glyph.Weight == FontWeights.Normal &&
        Glyph.Stretch == FontStretches.Normal;

    public string FaceName => FontNames.Pick(Glyph.FaceNames) ?? "Regular";

    /// <summary>Chip caption in the Faces strip.</summary>
    public string ChipLabel => FaceName;

    // Bindable render attributes so the chip can preview the face in its own weight/style.
    public FontWeight Weight => Glyph.Weight;
    public FontStyle Style => Glyph.Style;
    public FontStretch Stretch => Glyph.Stretch;

    public IReadOnlyList<DetailRow> MetricRows => BuildMetricRows();

    public string? SampleText => FontNames.Pick(Glyph.SampleTexts);

    private List<DetailRow> BuildMetricRows()
    {
        var rows = new List<DetailRow> { DetailRow.Header("Style & metrics") };
        Add(rows, "Weight", DescribeWeight(Glyph.Weight));
        Add(rows, "Style", Glyph.Style.ToString());
        Add(rows, "Stretch", Glyph.Stretch.ToString());
        Add(rows, "Symbol font", Glyph.Symbol ? "Yes" : "No");
        Add(rows, "Glyphs", Glyph.GlyphCount.ToString(CultureInfo.InvariantCulture));
        Add(rows, "Mapped characters", Glyph.CharacterToGlyphMap.Count.ToString(CultureInfo.InvariantCulture));
        Add(rows, "Cap height", Em(Glyph.CapsHeight));
        Add(rows, "x-height", Em(Glyph.XHeight));
        Add(rows, "Height", Em(Glyph.Height));
        Add(rows, "Baseline", Em(Glyph.Baseline));
        Add(rows, "Underline", $"{Em(Glyph.UnderlinePosition)} @ {Em(Glyph.UnderlineThickness)}");
        Add(rows, "Strikethrough", $"{Em(Glyph.StrikethroughPosition)} @ {Em(Glyph.StrikethroughThickness)}");
        return rows;
    }

    private static void Add(List<DetailRow> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) rows.Add(new DetailRow(label, value!));
    }

    private static string Em(double v) => v.ToString("0.###", CultureInfo.InvariantCulture) + " em";

    private static string DescribeWeight(FontWeight w) =>
        w == FontWeights.Normal ? "Regular (400)" : $"{w} ({w.ToOpenTypeWeight()})";
}
