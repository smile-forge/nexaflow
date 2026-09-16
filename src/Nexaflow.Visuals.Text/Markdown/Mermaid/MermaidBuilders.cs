using System;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Pie;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Radar;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Venn;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Xy;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// The diagrams drawn on the shared layout tree, and the builder each is drawn by.
///
/// <para>
/// What <c>MermaidDiagramHandler</c> asks first: a diagram named here is shown in an element it can be selected and written
/// in, and never reaches a drawing made any other way. A diagram is named here once its grammar is named in
/// <see cref="MermaidDiagrams.Grammar"/> — the two lists are the same list, which the tests hold them to.
/// </para>
/// </summary>
internal static class MermaidBuilders
{
    /// <summary>Lays a block's source out for a palette, a pixel density, a width, and whether somebody is writing in it.</summary>
    public delegate Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing);

    /// <summary>The builder a diagram is drawn by, or null for one not drawn on the shared tree.</summary>
    public static Build? For(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Pie => PieBuilder.Build,
        MermaidDiagram.Venn => VennBuilder.Build,
        MermaidDiagram.Radar => RadarBuilder.Build,
        MermaidDiagram.XyChart => XyBuilder.Build,
        _ => null,
    };

    /// <summary>The element a block is shown and written in, or null where its diagram is not drawn on the shared tree.</summary>
    public static Editing.ContentElement? Element(string source, MermaidDiagram diagram, DiagramRenderOptions options) =>
        For(diagram) is { } build ? MermaidBuilder.Host(source, options, (state, palette, pixelsPerDip, room, writing) => build(state, palette, pixelsPerDip, room, writing), readOnly: false) : null;

    /// <summary>Lays a block out as its header names, with no caret in it — or null where its diagram is not drawn on the shared tree.</summary>
    public static Laid? Lay(string source, MarkdownPalette palette, double pixelsPerDip = 1, double room = double.PositiveInfinity, bool writing = false) =>
        For(MermaidBlock.Read(source).Diagram)?.Invoke(EditState.For(source), palette, pixelsPerDip, room, writing);
}
