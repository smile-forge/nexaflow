using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A rendered block element (hosted in the editor / selectable <see cref="System.Windows.Controls.RichTextBox"/>
/// via a <see cref="System.Windows.Documents.BlockUIContainer"/>) that owns its own click/drag input.
///
/// Inside a RichTextBox, mouse events never reach an embedded element reliably (the text container attributes
/// them to the container, the document, or even a neighbouring paragraph — and capture is contested), so the
/// HOST drives the whole gesture: on mouse-down it locates the element with a geometric visual hit-test and
/// calls <see cref="BeginPointerSelect"/>, then forwards moves to <see cref="ExtendPointerSelect"/> and the
/// button-up to <see cref="EndPointerSelect"/>. A single click is thus owned by the element (e.g. the music
/// score's note/measure selection) rather than selecting/activating the whole block; double-click stays with
/// the host (source-edit mode).
/// </summary>
public interface IInteractiveBlock
{
    /// <summary>Starts a click/drag selection at <paramref name="pointInElement"/> (element coordinates).</summary>
    void BeginPointerSelect(Point pointInElement);

    /// <summary>Continues the drag at <paramref name="pointInElement"/>. No-op before a begin.</summary>
    void ExtendPointerSelect(Point pointInElement);

    /// <summary>Ends the gesture (mouse released).</summary>
    void EndPointerSelect();

    /// <summary>Clears this block's selection (used by the coordinator when another block or a text click
    /// takes the selection).</summary>
    void ClearSelection();
}

/// <summary>
/// Enforces a single active selection across interactive blocks on a page: when one block takes the
/// selection (or a plain text click happens) any other selected block is cleared. A process-wide singleton —
/// selection is a one-at-a-time affair, so a click in one score/document clears the rest.
/// </summary>
public static class InteractiveSelection
{
    private static IInteractiveBlock? _owner;

    /// <summary>Marks <paramref name="block"/> as the sole selection owner, clearing any previous one.</summary>
    public static void Own(IInteractiveBlock block)
    {
        if (_owner is not null && !ReferenceEquals(_owner, block)) _owner.ClearSelection();
        _owner = block;
    }

    /// <summary>Drops <paramref name="block"/> as owner if it currently is (called when it self-clears).</summary>
    public static void Release(IInteractiveBlock block)
    {
        if (ReferenceEquals(_owner, block)) _owner = null;
    }

    /// <summary>Clears the current owner's selection (called on a plain text/background click).</summary>
    public static void ClearActive()
    {
        var o = _owner;
        _owner = null;
        o?.ClearSelection();
    }
}
