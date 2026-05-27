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
    /// Parse <paramref name="source"/> and return a rendered WPF element.
    /// Must not throw; return an informative fallback element on failure.
    /// </summary>
    FrameworkElement Render(string source);
}
