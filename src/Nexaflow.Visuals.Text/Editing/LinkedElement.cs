using System;
using System.Windows;
using System.Windows.Input;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Content whose pieces may lead somewhere, and which follows them (<see cref="LayoutLink"/>). A press on a link means
/// the link rather than a place to put the caret, and the pointer is a hand over one saying where it leads.
///
/// Separate from <see cref="ContentElement"/> because almost no content draws a link: what does pays for the following,
/// and everything else is built and pressed exactly as it was.
/// </summary>
/// <param name="navigate">What follows a link, and says whether it did — null where nothing here follows one.</param>
public class LinkedElement(string source, MarkdownPalette palette, IContent content, Func<string, bool>? navigate)
    : ContentElement(source, palette, content)
{
    /// <inheritdoc/>
    protected override Cursor Pointing(Point at)
    {
        if (Leads(at) is not { } link) return base.Pointing(at);

        ToolTip = link.Tip ?? link.Href;
        return Cursors.Hand;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (navigate is not null && Leads(Unscaled(e.GetPosition(this))) is { } link && navigate(link.Href))
        {
            e.Handled = true;
            return;
        }

        base.OnMouseLeftButtonDown(e);
    }

    /// <summary>Where the piece under a point leads: the innermost one that leads anywhere, or null where none does.</summary>
    private LayoutLink? Leads(Point at)
    {
        LayoutLink? found = null;

        foreach (var (piece, where) in Laid.Tree.Root.Placed())
            if (piece.Link is { } link && where.Contains(at))
                found = link;

        return found;
    }
}
