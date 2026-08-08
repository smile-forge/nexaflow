using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Makes a rendered diagram object clickable. One implementation of the gesture, shared by every
/// diagram type, so "objects in a diagram can be links" is a property of diagrams generally rather
/// than a trick one renderer happens to support.
/// <para>
/// The affordance is deliberately uniform — hand cursor, tooltip, left-click navigates — because a
/// user who learns that class members are clickable should find that flowchart nodes, entities and
/// states behave the same way.
/// </para>
/// </summary>
public static class DiagramInteraction
{
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
        if (element is null || navigate is null || string.IsNullOrEmpty(href)) return false;

        string target = href;

        // Cursor and ToolTip are FrameworkElement concerns; a bare UIElement still gets the click.
        if (element is FrameworkElement framework)
        {
            framework.Cursor  = Cursors.Hand;
            framework.ToolTip = new TextBlock
            {
                Text          = string.IsNullOrWhiteSpace(tooltip) ? target : tooltip,
                TextAlignment = TextAlignment.Left,
            };
        }

        // Handled so a click on a node inside a text container is not also read as a caret move.
        element.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            navigate(target);
        };
        return true;
    }
}
