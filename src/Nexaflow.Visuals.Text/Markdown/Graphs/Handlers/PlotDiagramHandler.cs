using System;
using System.Windows;
using Nexaflow.Markdown.Plot;
using Nexaflow.Visuals.Text.Markdown.Plot;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Handles one of the correlation fences — <c>scatter</c>, <c>bubble</c>, <c>heatmap</c> or
/// <c>density2d</c> — a table of values read into marks on a pair of axes.
///
/// <para>
/// One grammar and one builder behind four languages, because they differ in what is drawn rather than in
/// what is written. An instance per fence, as the music handler has one per dialect, because the fence's
/// own name is the one thing the four do not share: it decides the geom and which channel a third column
/// feeds, and a block may override either.
/// </para>
/// <para>
/// Registered beside the word cloud and the 2D codes for the same reason they are: it is not a Mermaid
/// diagram, but it arrives as one — a fenced block whose info string names a language, rendered to an
/// element in place of its source — and registering it here is what puts it on both markdown surfaces at
/// once.
/// </para>
/// </summary>
public sealed class PlotDiagramHandler(PlotFence fence) : IDiagramHandler
{
    public bool CanHandle(string language) => PlotFences.Named(language) == fence;

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => this.Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options) =>
        PlotBuilder.Element(source, fence, options);
}
