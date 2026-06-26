using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// The Mermaid <c>config.venn</c> configuration plus the <c>venn1…venn8</c> theme-variable circle palette.
/// Sizes carry Mermaid's documented defaults, tuned smaller for the in-app surface.  <see cref="SetPalette"/>
/// is the parsed <c>venn1…8</c> bank (empty ⇒ use the active <see cref="MarkdownPalette"/> series colours).
/// </summary>
public sealed class VennConfig
{
    public double Width   { get; set; } = 540;   // Mermaid default 800
    public double Height  { get; set; } = 380;   // Mermaid default 450
    public double Padding { get; set; } = 8;
    public bool   UseDebugLayout { get; set; }
    public bool   UseMaxWidth    { get; set; } = true;

    /// <summary>Circle fill opacity (Mermaid hard-codes 0.1; tuned up for legibility on the app surfaces).</summary>
    public double FillOpacity { get; set; } = 0.28;

    /// <summary>Circle colours parsed from <c>venn1…venn8</c> theme variables (index-ordered). Empty ⇒ use the
    /// palette's <see cref="MarkdownPalette.Series"/> bank.</summary>
    public List<Brush> SetPalette { get; } = [];
}
