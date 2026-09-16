// LEGACY — frozen. Diagrams move off this code onto the shared layout tree one at a time, built from the Mermaid kit
// (docs/mermaid-diagrams.md); this file goes when the last diagram using it has moved. Read it for what a diagram draws —
// never copy from it, add to it, or use it from code on the shared tree (MermaidDiagramRulesTests).
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Canvas placement sugar — sets <c>Canvas.Left/Top</c> and returns the element fluently, so a
/// child can be positioned in the same expression that creates it.
///
/// <see cref="At{T}"/> and <see cref="Place{T}"/> are the same call under two names: the renderers
/// grew two identical copies of this helper independently, and keeping both names means neither set
/// of call sites had to churn when they were merged into one file.
/// </summary>
internal static class CanvasPlacementExtensions
{
    /// <summary>Positions <paramref name="el"/> on its canvas and returns it.</summary>
    internal static T Place<T>(this T el, double left, double top) where T : UIElement
    {
        Canvas.SetLeft(el, left);
        Canvas.SetTop(el, top);
        return el;
    }

    /// <summary>Positions <paramref name="el"/> on its canvas and returns it. Alias of <see cref="Place{T}"/>.</summary>
    internal static T At<T>(this T el, double left, double top) where T : UIElement
    {
        Canvas.SetLeft(el, left);
        Canvas.SetTop(el, top);
        return el;
    }
}
