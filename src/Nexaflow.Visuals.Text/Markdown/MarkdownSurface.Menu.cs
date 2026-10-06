using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a right-click opens: the ribbon of what may be done where it landed — what the pieces under it answer to, what the language
/// drawn there offers (<see cref="IContentLanguage.Offers"/>), and what the host adds. This control adds nothing of its own: what may
/// be done in content is the language's to say.
/// </summary>
public sealed partial class MarkdownSurface
{
    private Popup? _ribbon;

    /// <inheritdoc/>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (e.Handled) return;

        e.Handled = ShowContextRibbon(e.GetPosition(_shown));
    }

    /// <summary>
    /// Shows what may be done at <paramref name="at"/> — a point on the document as drawn — and says whether there was
    /// anything. A press outside what is picked out puts the caret where it landed first, which is what every editor does:
    /// a reader right-clicking a word means that word.
    /// </summary>
    private bool ShowContextRibbon(Point at)
    {
        Focus();

        var zoom = _shown.Zoom;
        var offset = _shown.Laid.Root.OffsetAt(new Point(at.X / zoom, at.Y / zoom));
        var state = _shown.Current;

        if (!state.HasSelection || !state.Selection.Any(range => offset >= range.Start && offset <= range.End))
            _shown.MoveCaretTo(offset);

        if (_shown.BuildRibbon(at) is not { } ribbon) return false;

        _ribbon ??= new Popup
        {
            StaysOpen = false,
            AllowsTransparency = true,
            Placement = PlacementMode.MousePoint,
            PlacementTarget = this,
        };

        _ribbon.IsOpen = false;
        _ribbon.Child = ribbon;
        _ribbon.IsOpen = true;

        return true;
    }
}
