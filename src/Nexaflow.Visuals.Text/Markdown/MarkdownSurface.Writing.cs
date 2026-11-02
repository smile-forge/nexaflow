using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Writing on somebody's behalf: a block the host puts in, something dropped, and the symbol palette's keys.
///
/// <para>
/// <strong>"The formula under the caret" is a question about the tree.</strong> Whether the caret is in a formula is
/// whether a part written as maths holds it — asked of what was laid, which names every part it drew. So a palette key
/// lands in the formula the reader is writing, and says so where there is none rather than typing <c>\frac{}{}</c> into a
/// sentence.
/// </para>
/// </summary>
public sealed partial class MarkdownSurface
{
    /// <summary>Puts the caret at the end of what is written and takes the keyboard — for a note just made, to be written in.</summary>
    public void BeginEdit()
    {
        Focus();
        Keyboard.Focus(this);

        _shown.TakeCaret(_shown.Markdown.Length);
    }

    /// <summary>
    /// Takes the keyboard and puts the caret in the first thing written — for focus arriving without a press, a host opening
    /// straight onto the content. False where nothing is written. Where the caret goes is the engine's; this only asks.
    /// </summary>
    public bool FocusFirstBlock()
    {
        Focus();
        Keyboard.Focus(this);

        return _engine.TakeFirstBlock();
    }

    /// <summary>Reads and draws the content again, for a host that has changed the source underneath it.</summary>
    public void Refresh() => _shown.Refresh();

    /// <summary>
    /// Writes <paramref name="change"/> as though the reader typed it here — more of their writing, joining the step it carries on
    /// from, the caret after it. What another view of the same document hands over: the raw half of a page beside this one. The page
    /// stays where it is, since the reader is looking at the half it was written in.
    /// </summary>
    public void Write(ContentChange change)
    {
        _writingGiven = true;
        try
        {
            _engine.WriteAsGiven(change);
        }
        finally
        {
            _writingGiven = false;
        }
    }

    /// <summary>Whether what another view wrote is being written here, so the caret it leaves is not brought onto the page.</summary>
    private bool _writingGiven;

    /// <summary><paramref name="text"/> written over <paramref name="length"/> characters from <paramref name="start"/>, as <see cref="Write"/> writes it.</summary>
    public void Replace(int start, int length, string text) => Write(ContentChange.Write(start, length, text));

    // ── Blocks from the host ────────────────────────────────────────────────

    // ── Something dropped ───────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (e.Handled) return;

        e.Effects = IsReadOnly ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Handled || IsReadOnly) return;

        DropContent(e.Data, e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>
    /// What was dragged in from somewhere else, written where it was let go. Whoever answers says what it comes to in words and
    /// in markdown — a drop carries whatever the thing dragged put in it, and turning that into something a document can be
    /// written from is the host's, not the content's — or deals with it itself and marks it handled having said neither.
    /// </summary>
    public void DropContent(IDataObject data, Point pointInEditor)
    {
        if (IsReadOnly || Spot(pointInEditor) is not { } at) return;

        var asked = new ContentDroppingEventArgs(DroppingEvent, data, pointInEditor);
        RaiseEvent(asked);

        if (!asked.Handled) return;

        _engine.Brought(asked.Words ?? string.Empty, asked.Markdown ?? string.Empty, at.ToLayout());
    }

    /// <summary>
    /// <paramref name="pointInEditor"/> in the content's own units, or null where it is past the end of what was laid. All this
    /// control does with a point: which piece of the content is under it is the engine's, which laid it.
    /// </summary>
    private Point? Spot(Point pointInEditor)
    {
        var at = TranslatePoint(pointInEditor, _shown);
        var zoom = _shown.Zoom;
        var point = new Point(at.X / zoom, at.Y / zoom);

        return point.Y > _shown.Laid.Size.Height ? null : point;
    }

    // ── The formula the caret is in ─────────────────────────────────────────
}
