using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Encapsulates a complete diagram pipeline (parse → layout → render) for one
/// or more related diagram languages.
///
/// Implement this interface to add a new diagram type; register the instance in
/// <see cref="Nexaflow.Visuals.Text.Markdown.DiagramRenderer"/>.  The renderer
/// knows nothing about diagram internals — all content decisions live here.
/// </summary>
public interface IDiagramHandler
{
    /// <summary>Returns true when this handler owns <paramref name="language"/>.</summary>
    bool CanHandle(string language);

    /// <summary>
    /// Parse <paramref name="source"/> and return a rendered WPF element, themed with
    /// <paramref name="palette"/>. Must not throw; return an informative fallback element on failure.
    /// </summary>
    /// <param name="onNavigate">Optional click hook for navigable diagram elements (e.g. class-diagram member
    /// rows that carry a link). Handlers without clickable elements ignore it.</param>
    FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null);
}
