// LEGACY — frozen. Diagrams move off this code onto the shared layout tree one at a time, built from the Mermaid kit
// (docs/mermaid-diagrams.md); this file goes when the last diagram using it has moved. Read it for what a diagram draws —
// never copy from it, add to it, or use it from code on the shared tree (MermaidDiagramRulesTests).
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// The Mermaid <c>config.cynefin</c> options plus the <c>themeVariables.cynefin</c> domain-background
/// overrides.  Sizes carry sensible in-app defaults; every colour is null unless a theme variable
/// supplied it, in which case it overrides the palette tint for that domain.
/// </summary>
public sealed class CynefinConfig
{
    public double Width   { get; set; } = 520;
    public double Height  { get; set; } = 520;
    public double Padding { get; set; } = 8;

    /// <summary>When true, the renderer prints each domain's short description under its name.</summary>
    public bool ShowDomainDescriptions { get; set; }

    // themeVariables.cynefin domain backgrounds (null ⇒ fall back to the palette series tint).
    public Brush? ComplexBg     { get; set; }
    public Brush? ComplicatedBg { get; set; }
    public Brush? ClearBg       { get; set; }
    public Brush? ChaoticBg     { get; set; }
    public Brush? ConfusionBg   { get; set; }
    public Brush? BoundaryColor { get; set; }
}
