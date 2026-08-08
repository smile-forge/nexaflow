using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>Which of a node's two hit regions a target belongs to.</summary>
public enum DiagramTargetKind
{
    /// <summary>The body of the object — goes somewhere, or selects.</summary>
    Activate,
    /// <summary>The expand chip — opens or closes what is behind the node.</summary>
    Expand,
}

/// <summary>What a hit region of a diagram does when it is clicked.</summary>
/// <param name="Invoke">Runs the action; returns true when it was handled.</param>
/// <param name="Tooltip">Tooltip for the region. Null shows nothing.</param>
/// <param name="Kind">Which region this is, so a host can treat a chip differently from a body.</param>
/// <param name="NodeId">The node the region belongs to, when it belongs to one.</param>
public sealed record DiagramTarget(
    Func<bool> Invoke,
    string? Tooltip = null,
    DiagramTargetKind Kind = DiagramTargetKind.Activate,
    string? NodeId = null);

/// <summary>
/// Makes a rendered diagram object clickable. One implementation of the gesture, shared by every
/// diagram type, so "objects in a diagram can be links" is a property of diagrams generally rather
/// than a trick one renderer happens to support.
/// <para>
/// The affordance is deliberately uniform — hand cursor, tooltip, left-click acts — because a user
/// who learns that class members are clickable should find that flowchart nodes, entities and states
/// behave the same way.
/// </para>
/// <para>
/// A node can carry more than one region: its body navigates, its expand chip opens the subtree
/// behind it. So a target is attached <i>per element</i> rather than per node, and is also stamped
/// onto the element as <see cref="TargetProperty"/> — a diagram embedded in a text container never
/// receives mouse events reliably, and its host has to hit-test geometrically and run the target
/// itself. Both paths therefore read the same one description of what a region does.
/// </para>
/// </summary>
public static class DiagramInteraction
{
    /// <summary>The action this element performs when clicked, for a host that must dispatch the
    /// click itself (see <see cref="Invoke"/>).</summary>
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.RegisterAttached("Target", typeof(DiagramTarget), typeof(DiagramInteraction),
            new PropertyMetadata(null));

    public static DiagramTarget? GetTarget(DependencyObject element) =>
        (DiagramTarget?)element.GetValue(TargetProperty);

    public static void SetTarget(DependencyObject element, DiagramTarget? value) =>
        element.SetValue(TargetProperty, value);

    /// <summary>
    /// Attaches navigation to <paramref name="element"/> when it has somewhere to go.
    /// <para>
    /// A null <paramref name="navigate"/> means the host has no in-app handler, in which case
    /// nothing is attached at all: a cursor and an underline that promise a click and then do
    /// nothing are worse than plain text.
    /// </para>
    /// </summary>
    /// <returns>True when the element was made interactive.</returns>
    public static bool Attach(UIElement? element, string? href, string? tooltip, Func<string, bool>? navigate)
    {
        if (navigate is null || string.IsNullOrEmpty(href)) return false;

        string target = href;
        return Attach(element, new DiagramTarget(() => navigate(target),
                                                 string.IsNullOrWhiteSpace(tooltip) ? target : tooltip));
    }

    /// <summary>Attaches an arbitrary action to one hit region.</summary>
    /// <returns>True when the element was made interactive.</returns>
    public static bool Attach(UIElement? element, DiagramTarget? target)
    {
        if (element is null || target is null) return false;

        SetTarget(element, target);

        // Cursor and ToolTip are FrameworkElement concerns; a bare UIElement still gets the click.
        if (element is FrameworkElement framework)
        {
            framework.Cursor = Cursors.Hand;
            if (!string.IsNullOrWhiteSpace(target.Tooltip))
                framework.ToolTip = new TextBlock
                {
                    Text          = target.Tooltip,
                    TextAlignment = TextAlignment.Left,
                };
        }

        // Handled so a click on a node inside a text container is not also read as a caret move.
        element.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            target.Invoke();
        };
        return true;
    }

    /// <summary>
    /// Runs the target owned by <paramref name="hit"/> or by the nearest ancestor up to (and not
    /// past) <paramref name="stopAt"/>. For a host that hit-tests the diagram itself because the
    /// element's own mouse events cannot be trusted.
    /// </summary>
    /// <returns>True when a target was found and handled the click.</returns>
    public static bool Invoke(DependencyObject? hit, DependencyObject? stopAt = null)
        => Find(hit, stopAt)?.Invoke() ?? false;

    /// <summary>The target owned by <paramref name="hit"/> or its nearest ancestor, without running it.</summary>
    public static DiagramTarget? Find(DependencyObject? hit, DependencyObject? stopAt = null)
    {
        for (var d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (GetTarget(d) is { } target) return target;
            if (ReferenceEquals(d, stopAt)) break;
        }
        return null;
    }
}
