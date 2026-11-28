using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Where the caret goes when nobody has said: where reading content just shown starts, where a right-click means it to be, and keeping it
/// on the page as it moves.
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>
    /// Puts the caret where reading content just shown starts: a field holding one language is written at the end of what it holds; a
    /// document, text shown as a page, and anything only read, is read from its top — unless a page handed over its view of the document,
    /// whose caret stands where the page put it.
    /// </summary>
    internal void CaretWhereReadingStarts()
    {
        if (ViewHandedOver) return;

        var state = this.State;
        Apply(state.MoveCaretTo(_named is null || _readOnly || _wrapsLines ? 0 : state.Document.Length), notify: false);
    }

    /// <summary>
    /// What a right-click at <paramref name="point"/> means for the caret: a press outside what is picked out puts the caret where it landed
    /// first, which is what every editor does — a reader right-clicking a word means that word. A press on something picked out keeps all of
    /// it picked out — a tile among the boxes Ctrl+clicked — though the place a caret would go there may be outside it.
    /// </summary>
    internal void MeantAt(LayoutPoint point)
    {
        var offset = _laid.Root.OffsetAt(point);
        var pressed = _laid.Root.PieceAt(point).Selectable().Sits();

        var onPicked = this.State.Selection.Any(range => (offset >= range.Start && offset <= range.End)
                                                         || (pressed.Length > 0 && pressed.Start >= range.Start && pressed.End <= range.End));
        if (!onPicked) MoveCaretTo(offset);
    }

    /// <summary>
    /// Brings the caret onto the page, wherever an edit or a key left it: a box slid away from it slides to it first, so where it stands on
    /// the page is where it stands once slid; and a view of the content's own moves no further than it must. For content shown whole, hands
    /// back where the caret stands, for whatever holds it to bring onto its screen; null where nothing more is to be done.
    /// </summary>
    internal LayoutRect? RevealCaret()
    {
        var caret = this.State.Caret;

        SlideOnto(caret);

        var at = _laid.Root.CaretRect(caret);
        if (at.IsEmpty) return null;

        if (!Scrolls) return at;

        KeepInView(at);
        return null;
    }

    /// <summary>
    /// Whether this content holds the caret: put at a place in it, or shown where it stood when whatever shows it came to hold the keyboard.
    /// Only content written in holds one; content only read never does.
    /// </summary>
    public bool HasCaret { get; private set; }

    /// <summary>Whether whatever shows this content holds the keyboard, as it last said (<see cref="KeyboardHeld"/>).</summary>
    private bool _keyboard;

    /// <summary>
    /// Whatever shows this content gained the keyboard or lost it. Gaining it shows the caret where it stood, putting it nowhere, so
    /// coming back to content moves nothing; losing it takes the caret away, so only the content the reader is in shows one.
    /// </summary>
    public void KeyboardHeld(bool held)
    {
        _keyboard = held;

        if (!held) ReleaseCaret();
        else if (!HasCaret) ShowCaret();
    }

    /// <summary>
    /// Shows the caret where it already stands, without putting it at a place first: what gaining the keyboard means, and what a host
    /// restoring a state it saved means. A caret put at a place is <see cref="TakeCaret"/>.
    /// </summary>
    public void ShowCaret()
    {
        HasCaret = true;
        CaretTaken?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes the caret away, where this content held it.</summary>
    public void ReleaseCaret()
    {
        if (!HasCaret) return;

        HasCaret = false;
        CaretTaken?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The source changed, said to whoever listens. A change nobody typed here — undone, loaded, replaced, made elsewhere — while this
    /// content holds the keyboard shows the caret where the change left it, so the reader can go on writing from there.
    /// </summary>
    private void RaiseSourceChanged(ContentSourceChange change)
    {
        if (change.Kind != ContentChangeKind.Written && _keyboard) ShowCaret();

        SourceChanged?.Invoke(this, change);
    }
}
