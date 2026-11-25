using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Interaction;
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
public sealed partial class MarkdownSurface : IDropTarget, IPasteTarget
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

    // ── Something pasted ────────────────────────────────────────────────────

    private IReadOnlyList<string> _pasteKinds = WrittenKinds;

    /// <summary>
    /// The kinds a paste here is handed: words, marked-up words and markdown — none while it cannot be written in. A host that
    /// makes something of more, a picture or a file, widens it and says what that comes to when it answers <see cref="Pasting"/>.
    /// </summary>
    public IReadOnlyList<string> PasteKinds
    {
        get => IsReadOnly ? [] : _pasteKinds;
        set => _pasteKinds = value;
    }

    /// <inheritdoc/>
    public Task PasteAsync(IDragPayload pasted)
    {
        if (!IsReadOnly) PasteTaken(pasted);
        return Task.CompletedTask;
    }

    private static readonly string[] WrittenKinds = [DragKinds.Text, DragKinds.Html, MarkdownClipboard.MarkdownFormat];

    /// <inheritdoc/>
    public IReadOnlyList<string> AcceptedKinds => WrittenKinds;

    /// <summary>Words are written here. Files are not, whatever words come with them: those are the host's, which takes
    /// them as a target of its own around this one.</summary>
    public DropOffer? OfferDrop(DropRequest request)
        => IsReadOnly || request.Payload.CarriesFiles() ? null : new DropOffer(DropEffect.Copy);

    /// <inheritdoc/>
    public Task AcceptDropAsync(DropRequest request)
    {
        DropContent(request.Payload, new Point(request.At.X, request.At.Y));
        return Task.CompletedTask;
    }

    /// <summary>
    /// What was dragged in from somewhere else, written where it was let go — at the end, where that was past the last of
    /// what is written. The host is asked first (<see cref="Dropping"/>) and either says what it comes to in words and in
    /// markdown or deals with it itself, marking it handled having said neither. Words nobody claimed are written as a
    /// paste of them would be read; files nobody claimed are not.
    /// </summary>
    public void DropContent(IDragPayload data, Point pointInEditor)
    {
        if (IsReadOnly) return;

        var asked = new ContentDroppingEventArgs(DroppingEvent, data, pointInEditor);
        RaiseEvent(asked);

        string? words, markdown;
        if (asked.Handled)
        {
            (words, markdown) = (asked.Words, asked.Markdown);
        }
        else
        {
            if (data.CarriesFiles()) return;
            (words, markdown) = (MarkdownClipboard.ReadPlainText(data), MarkdownClipboard.ReadBestMarkdown(data));
        }
        if (string.IsNullOrEmpty(words) && string.IsNullOrEmpty(markdown)) return;

        var size = _shown.Laid.Size;
        var at = Spot(pointInEditor) ?? new Point(size.Width, Math.Max(0, size.Height - 1));
        _engine.Brought(words ?? string.Empty, markdown ?? string.Empty, at.ToLayout());
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
