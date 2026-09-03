using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// Mermaid <c>journey</c> configuration — the front-matter <c>config.journey</c> block (task box
/// geometry, <c>actorColours</c>, <c>sectionFills</c>) plus the <c>themeVariables</c>
/// <c>fillType0…7</c> section palette.  Geometry carries Mermaid's documented defaults; both colour
/// lists are index-ordered and empty ⇒ the active <see cref="MarkdownPalette"/> series bank is used.
/// </summary>
public sealed class JourneyConfig
{
    public double Width        { get; set; } = 150;
    public double Height       { get; set; } = 50;
    public double BoxMargin    { get; set; } = 10;
    public double TaskFontSize { get; set; } = 12;

    /// <summary>Section band fills, from <c>config.journey.sectionFills</c> or <c>themeVariables.fillType0…7</c>.</summary>
    public List<Brush> SectionFills { get; } = [];

    /// <summary>Actor dot/legend colours, from <c>config.journey.actorColours</c>, by first-appearance index.</summary>
    public List<Brush> ActorColours { get; } = [];
}
