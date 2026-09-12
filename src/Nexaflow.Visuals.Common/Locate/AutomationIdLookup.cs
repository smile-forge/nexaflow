using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Locate;

/// <summary>
/// Finds the control on screen that carries an <c>AutomationProperties.AutomationId</c> — in-process, by walking the
/// visual tree and reading the attached property, so there is no UI Automation round trip and nothing a release build
/// lacks. Only what a reader could see counts: a collapsed or hidden branch is skipped whole, and an element with no
/// size is passed over.
/// </summary>
public static class AutomationIdLookup
{
    /// <summary>
    /// The first shown element under <paramref name="root"/> whose AutomationId is <paramref name="id"/>, in tree order —
    /// except that one inside <paramref name="searchLast"/> is chosen only when nothing outside it matches (help pointing
    /// at "the search box" means the page's, not its own). Null when none is on show.
    /// </summary>
    public static FrameworkElement? Find(DependencyObject root, string id, DependencyObject? searchLast = null)
    {
        FrameworkElement? inLast = null;
        var pending = new Stack<(DependencyObject Node, bool InLast)>();
        pending.Push((root, ReferenceEquals(root, searchLast)));

        while (pending.Count > 0)
        {
            var (node, isInLast) = pending.Pop();
            if (node is UIElement { Visibility: not Visibility.Visible }) continue;

            if (node is FrameworkElement element && IsSized(element) && AutomationProperties.GetAutomationId(element) == id)
            {
                if (!isInLast) return element;
                inLast ??= element;
            }

            if (node is not (Visual or System.Windows.Media.Media3D.Visual3D)) continue;
            for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--)   // reversed, so they pop in order
            {
                var child = VisualTreeHelper.GetChild(node, i);
                pending.Push((child, isInLast || ReferenceEquals(child, searchLast)));
            }
        }
        return inLast;
    }

    /// <summary>Whether <paramref name="element"/> is still on show under <paramref name="root"/>: in its tree, with
    /// every element between them visible, and a size of its own.</summary>
    public static bool IsShown(FrameworkElement element, DependencyObject root)
    {
        if (!IsSized(element)) return false;
        for (DependencyObject? node = element; node is not null; node = Parent(node))
        {
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
            if (ReferenceEquals(node, root)) return true;
        }
        return false;   // left the tree
    }

    private static bool IsSized(FrameworkElement element) => element.ActualWidth > 0 && element.ActualHeight > 0;

    private static DependencyObject? Parent(DependencyObject node)
        => node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : null;
}
