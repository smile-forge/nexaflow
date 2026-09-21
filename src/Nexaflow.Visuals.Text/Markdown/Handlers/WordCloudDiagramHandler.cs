using System;
using System.Windows;
using Nexaflow.Visuals.Text.Markdown.WordCloud;

namespace Nexaflow.Visuals.Text.Markdown.Handlers;

/// <summary>
/// Handles <c>wordcloud</c> fenced code blocks — a word and what it counts for on each line, packed into a
/// cloud.
///
/// <para>
/// Registered beside the QR and barcode handlers for the same reason they are: it is not a diagram, but it
/// arrives as one — a fenced block whose info string names a language, rendered to an element in place of
/// its source — and registering it here is what puts it on both markdown surfaces at once.
/// </para>
/// <para>
/// The handler is a seam and nothing more. <see cref="Markdown.WordCloud"/>'s parser reads the block into a
/// tree, and <see cref="WordCloudBuilder"/> packs and lays it out.
/// </para>
/// </summary>
public sealed class WordCloudDiagramHandler : IDiagramHandler
{
    public bool CanHandle(string language) =>
        language.Equals("wordcloud", StringComparison.OrdinalIgnoreCase)
        || language.Equals("word-cloud", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, StyleFormat palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options) =>
        WordCloudBuilder.Element(source, options);
}
