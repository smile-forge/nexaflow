using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a right-click opens: the ribbon of what may be done where it landed — what the pieces under it answer to, what the language
/// drawn there offers (<see cref="IContentLanguage.Ribbon"/>), and what the host adds. This control adds nothing of its own: what may
/// be done in content is the language's to say.
/// </summary>
public sealed partial class NexaTextSurface
{
    private Popup? _ribbon;

    /// <summary>The ribbon a right-click last opened, open or since closed — null before the first.</summary>
    internal Popup? ContextRibbon => _ribbon;

    /// <inheritdoc/>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (e.Handled) return;

        e.Handled = ShowContextRibbon(e.GetPosition(_shown));
    }

    /// <summary>
    /// Shows what may be done at <paramref name="at"/> — a point on the document as drawn — and says whether there was
    /// anything, the caret first put where the press means it (<see cref="ContentEngine.MeantAt"/>).
    /// </summary>
    internal bool ShowContextRibbon(Point at)
    {
        Focus();

        _engine.MeantAt(_shown.InContent(at));

        if (_shown.BuildRibbon(at) is not { } ribbon) return false;

        if (_ribbon is null)
        {
            _ribbon = new Popup
            {
                StaysOpen = false,
                AllowsTransparency = true,
                Placement = PlacementMode.MousePoint,
                PlacementTarget = this,
            };
            _ribbon.Closed += (_, _) => TakeKeyboardBack();
        }

        _ribbon.IsOpen = false;
        _ribbon.Child = ribbon;
        _ribbon.IsOpen = true;

        return true;
    }

    /// <summary>
    /// Gives the keyboard back to the document when the ribbon closes, where the ribbon took it: a control pressed there takes the
    /// keyboard as it is pressed, and what it did — a node put in, words begun beside one — left the caret where the reader writes next.
    /// Where the reader closed it by pressing something else that took the keyboard, that keeps it.
    /// </summary>
    private void TakeKeyboardBack()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        var inRibbon = focused is System.Windows.Media.Visual visual && _ribbon?.Child is { } ribbon && ribbon.IsAncestorOf(visual);
        if (focused is not null && !inRibbon) return;

        Focus();
    }
}
