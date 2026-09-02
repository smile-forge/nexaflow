using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// Mermaid <c>timeline</c> configuration — the front-matter <c>config.timeline</c> block
/// (<c>disableMulticolor</c>, <c>padding</c>) and the global <c>themeVariables</c> colour slots
/// <c>cScale0…11</c> (period/section fills) and <c>cScaleLabel0…11</c> (their label colours).
/// The slots are kept by index — <c>cScale2</c> alone leaves slots 0 and 1 on the palette — so a
/// label colour always pairs with the fill it was written for.  Null ⇒ fall back to the active
/// <see cref="MarkdownPalette"/>.
/// </summary>
public sealed class TimelineConfig
{
    /// <summary>When true every period/section uses the first colour slot instead of cycling.</summary>
    public bool DisableMulticolor { get; set; }

    /// <summary>Inner padding of period/event boxes (Mermaid's <c>padding</c>).</summary>
    public double Padding { get; set; } = 8;

    public Dictionary<int, Brush> Scale      { get; } = [];
    public Dictionary<int, Brush> ScaleLabel { get; } = [];

    public Brush? ScaleAt(int index)      => Scale.GetValueOrDefault(index);
    public Brush? ScaleLabelAt(int index) => ScaleLabel.GetValueOrDefault(index);
}
