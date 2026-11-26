using System;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The keys: each one taken as the one thing it means, which the language the caret is in then makes whatever it makes of —
/// and what was written, so it can be taken back.
///
/// <para>
/// <strong>Nothing here knows what a key does to the document.</strong> Enter is a settle, Backspace is a backspace; whether
/// that starts a list item, a pie slice or nothing at all is the language's to say, which is why a formula and a sentence
/// written on the same page answer the same key differently without this code having a case for either.
/// </para>
/// <para>
/// <strong>The history is the engine's.</strong> An edit is whatever a key came to in the language it landed in, made as one change
/// to the document — so a step back is the change that undoes it, made and read again (<see cref="EditHistory"/>). An edit to a
/// formula and an edit to a paragraph are the same thing to take back.
/// </para>
/// </summary>
public sealed partial class ContentEngine : IContentEditorView
{
    /// <summary>
    /// The document this shows and writes in, with what was written so it can be taken back — the page's, where the page handed one
    /// over (<see cref="EditedDocument"/>), and otherwise one of its own, made the first time it is asked for.
    /// </summary>
    private EditedDocument? _edited;

    /// <summary>Whether this is writing in the document itself, so the change it hears back is its own and not to be taken up.</summary>
    private bool _taking;

    /// <summary>The content as it stood after the last thing written, which is where the next step starts from.</summary>
    private EditState _last = EditState.For(string.Empty);

    /// <summary>Whether the last key was a space already written, so the text it also arrives as is passed over.</summary>
    private bool _spaced;

    private bool _holdsWritten;

    /// <summary>
    /// What the reader did, done. True where it meant something here; false leaves it to whatever else wants it — Tab to the
    /// next field, say.
    /// </summary>
    public bool Input(ContentInput input) => input switch
    {
        ContentKey key => Pressed(key.Key, key.Modifiers),
        ContentText text => Typed(text.Text),
        _ => Pointed(input),
    };

    /// <summary>Raised when the source changed — written, taken back, written again, or put there by whatever shows it.</summary>
    public event EventHandler<ContentSourceChange>? SourceChanged;



    /// <summary>What a key means here, done.</summary>
    private bool Pressed(Key key, ModifierKeys modifiers)
    {
        _spaced = false;

        var shift = (modifiers & ModifierKeys.Shift) != 0;
        var ctrl = (modifiers & ModifierKeys.Control) != 0;

        if (ctrl) return Commanded(key, shift);

        switch (key)
        {
            case Key.Left or Key.Right:
                MoveCaret(forward: key == Key.Right, extend: shift);
                return true;

            case Key.Up or Key.Down:
                MoveCaretVertically(up: key == Key.Up, extend: shift);
                return true;

            case Key.Home or Key.End:
                Along(end: key == Key.End, extend: shift);
                return true;

            // What is picked out goes first; then a block shown as written is drawn again, keeping what was written in it.
            case Key.Escape:
                if (!_state.HasSelection) return LetGo();
                ClearSelection();
                return true;

            // A step up or down of whatever the content makes one of — an octave, in a tune — and failing that a page of
            // whatever shows it. Above the question of writing, because paging a document nobody may write in still pages it.
            case Key.PageUp or Key.PageDown:
                return Stepped(key == Key.PageUp) || this.Events?.OnPage(key == Key.PageUp) == true;
        }

        if (_readOnly) return false;

        switch (key)
        {
            case Key.Back:
                Backspace();
                return true;

            // Shift+Delete and Shift+Insert: the older keys for cutting and pasting.
            case Key.Delete when shift:
                return Cut();

            case Key.Delete:
                Delete();
                return true;

            // A line broken inside what is being written — the language's to say how.
            case Key.Enter when shift:
                Break();
                return true;

            case Key.Enter or Key.Space:
                Settle(key == Key.Enter ? "\n" : " ");
                _spaced = key == Key.Space;
                return true;

            // On to the next place to write in, where the language says; otherwise through the holes a construct left while any
            // remain; otherwise Tab is the window's, to move on with.
            case Key.Tab:
                return Tabbed(forward: !shift) || SelectNextPlaceholder(forward: !shift);

            // Something new where the caret is, where the language has something to put there.
            case Key.Insert when !shift:
                return Inserted();

            case Key.Insert:
                return Paste();

            default:
                return false;
        }
    }

    /// <summary>A step up or down where the language it landed in makes one, and false where it does not.</summary>
    private bool Stepped(bool up)
    {
        if (_readOnly) return false;
        if (Edited(up ? EditKind.Raising : EditKind.Lowering, string.Empty, Landing) is not { } stepped) return false;

        Apply(stepped, notify: true);
        return true;
    }

    /// <summary>What a key held with Ctrl means.</summary>
    private bool Commanded(Key key, bool shift)
    {
        switch (key)
        {
            case Key.A:
                SelectAll();
                return true;

            case Key.Home or Key.End:
                MoveCaretTo(key == Key.End ? _state.Document.Length : 0, extend: shift);
                return true;

            // A word along, or back: to the next start of a word, a run of punctuation, or a line.
            case Key.Left or Key.Right:
                MoveCaretTo(ContentWordEdges.Next(_state.Document, _state.Caret, forward: key == Key.Right), extend: shift);
                return true;

            // Above the question of writing: what is only being looked at can still be picked out and copied.
            case Key.C or Key.Insert:
                return Copied();
        }

        if (_readOnly) return false;

        switch (key)
        {
            case Key.Z when shift:
            case Key.Y:
                Redo();
                return true;

            case Key.Z:
                Undo();
                return true;

            // Strong and emphasised are the language's to write, where it has a way to say them.
            case Key.B or Key.I:
                return Marked(key == Key.B ? EditKind.Emboldening : EditKind.Emphasising);

            // A word taken out, after the caret or before it.
            case Key.Back or Key.Delete:
                return Worded(forward: key == Key.Delete);

            case Key.X:
                return Cut();

            case Key.V:
                return Paste();

            default:
                return false;
        }
    }

    /// <summary>What the language the caret is in makes of <paramref name="kind"/> — and false, leaving the key to whatever else wants it, where it makes nothing.</summary>
    private bool Marked(EditKind kind)
    {
        if (Edited(kind, string.Empty, Landing) is not { } marked) return false;

        Apply(marked, notify: true);
        return true;
    }

    /// <summary>The word after the caret taken out, or the one before it — what is picked out, where anything is.</summary>
    private bool Worded(bool forward)
    {
        if (this.Editor.Words(forward) is not { } taken) return true;

        // A word at a time is a step at a time to take back, never joined to the typing either side of it.
        this.History.Break();
        Apply(taken, notify: true);
        this.History.Break();

        return true;
    }

    /// <summary>Characters typed, written one at a time as the language they land in takes each.</summary>
    private bool Typed(string text)
    {
        if (_readOnly || string.IsNullOrEmpty(text)) return false;

        // A space was already written by the key that made it, as a settle — the character it also arrives as is not a second one.
        if (_spaced && text == " ") { _spaced = false; return true; }

        foreach (var character in text)
        {
            // Control characters arrive as text too — a backspace, an escape — and are keys, handled as keys.
            if (char.IsControl(character)) continue;

            Write(character.ToString());
        }

        return true;
    }

    /// <summary>
    /// Picks out everything written. Exactly that, rather than grown out to the constructs it covers, which in content shown
    /// inside another would reach its delimiters too.
    /// </summary>
    internal void SelectAll() => Apply(_state.Select(0, _state.Document.Length), notify: false);

    /// <summary>Home and End: the caret to the start or the end of the row it stands on (<see cref="LayoutQuery.RowEdge"/>).</summary>
    private void Along(bool end, bool extend)
    {
        if (_laid.Root.RowEdge(_state.Caret, end) is { } edge) MoveCaretTo(edge, extend);
    }

    /// <summary>
    /// Draws again whatever is shown as written — except where the whole of it is held so (<see cref="HoldsWritten"/>), which
    /// is not the reader's to let go of. False where nothing was shown.
    /// </summary>
    private bool LetGo()
    {
        if (_state.Raw is null || _holdsWritten) return false;

        Apply(_state.Settled(), notify: false);
        return true;
    }

    // ── Shown as written ────────────────────────────────────────────────────

    /// <summary>
    /// Whether the whole content is held shown as the characters written rather than what they draw — for when the drawing
    /// itself is the trouble, a formula that will not set. Everything else about writing in it carries on as it was.
    /// </summary>
    internal bool HoldsWritten
    {
        get => _holdsWritten;
        set
        {
            if (_holdsWritten == value) return;

            _holdsWritten = value;

            // Let go of only what this held open: a command being spelled is the writer's, and stays shown as they spell it.
            Apply(value || _state.Raw != Whole(_state) ? _state : _state with { Raw = null }, notify: false);
        }
    }

    /// <summary>
    /// Whether a line of the content shown too long for the room carries on in rows beneath (<see cref="LayoutHost.WrapsLines"/>): text
    /// shown as typed, rather than laid as its language lays it. What was laid the other way is laid again.
    /// </summary>
    internal bool WrapsLines
    {
        get => _wrapsLines;
        set
        {
            if (_wrapsLines == value) return;

            _wrapsLines = value;
            Forget();
        }
    }

    private bool _wrapsLines;

    /// <summary><paramref name="next"/>, held shown as written where the whole content is.</summary>
    private EditState Held(EditState next) => _holdsWritten ? next with { Raw = Whole(next) } : next;

    private static RawZone Whole(EditState state) => new(0, state.Document.Length);

    // ── What was written ────────────────────────────────────────────────────

    /// <summary>What was written, which a host with buttons for undo and redo asks whether either can be done.</summary>
    internal EditHistory History => this.EditedDocument.History;

    /// <summary>
    /// The document this shows and writes in. Handed over by the page that holds it, it is taken up as it stands — shown afresh, as
    /// a document loaded is — and a change the page makes to it later is taken up as it comes (<see cref="Elsewhere"/>).
    /// </summary>
    internal EditedDocument EditedDocument
    {
        get => _edited ?? this.Holding(new EditedDocument());
        set
        {
            if (ReferenceEquals(value, _edited)) return;

            if (_edited is not null) _edited.Changed -= this.Elsewhere;

            var before = _state;
            this.Holding(value);
            this.Unadopted(value);

            this.Apply(new EditState(value.Current, 0), notify: false);
            _last = _state;

            SourceChanged?.Invoke(this, new ContentSourceChange(before, _state, ContentChangeKind.Loaded));
        }
    }

    private EditedDocument Holding(EditedDocument edited)
    {
        _edited = edited;
        edited.Changed += this.Elsewhere;

        return edited;
    }

    /// <summary>The view of the document this shows: a page's, where it handed one over (<see cref="Editor"/>), and otherwise one made here.</summary>
    private ContentEditor? _editor;

    /// <summary>Whether the view of the document was made here, and so is let go of here.</summary>
    private bool _ownsEditor;

    /// <summary>Raised when the view of the document asks for a line to be brought onto the page — what moves the page is whatever shows the content.</summary>
    internal event EventHandler<(int Line, ShownPlace Place)>? LineAsked;

    /// <summary>Raised when what is marked over the view of the document has changed, so it is drawn again.</summary>
    internal event EventHandler? Remarked;

    private void OnRemarked(object? sender, EventArgs args) => this.Remarked?.Invoke(this, EventArgs.Empty);

    /// <summary>Something was folded away or opened in the view of the document: laid again, without what is folded.</summary>
    private void OnFolded(object? sender, EventArgs args) => this.Relay();

    /// <summary>
    /// What the content says can be folded — a code reading's own syntax, as last parsed and carried here — in order; nothing for content that
    /// folds nothing, or before anything is laid.
    /// </summary>
    internal IReadOnlyList<(int Start, int End)> Foldable() =>
        _shown?.LaidSyntax is Code.CodeSyntax syntax ? syntax.Folds() : [];

    IReadOnlyList<(int Start, int End)> IContentEditorView.Foldable() => this.Foldable();

    /// <summary>
    /// The view of the document this shows — where the caret is, what is picked out, which lines are on the page — which a page holds to read
    /// and move them. Handed over, it is taken up with the document it is a view of; otherwise one is made the first time it is asked for.
    /// </summary>
    internal ContentEditor Editor
    {
        get
        {
            if (_editor is { } editor && ReferenceEquals(editor.Document, this.EditedDocument)) return editor;

            this.Adopt(new ContentEditor(this.EditedDocument), owned: true);
            return _editor!;
        }
        set
        {
            if (ReferenceEquals(value, _editor)) return;

            // Where the page put the caret before anything showed its view, it stays.
            var stood = value.State;

            this.EditedDocument = value.Document;
            this.Adopt(value, owned: false);

            if (ReferenceEquals(stood.Document, _state.Document)) this.Apply(stood, notify: false);
        }
    }

    /// <summary>The view of the document this shows, where one has been asked for or handed over — null where nothing has.</summary>
    internal ContentEditor? EditorIfAny => _editor;

    private void Adopt(ContentEditor editor, bool owned)
    {
        this.Unadopted(null);

        (_editor, _ownsEditor) = (editor, owned);
        editor.Attach(this);
        editor.Marks.Changed += this.OnRemarked;
        editor.Folds.Changed += this.OnFolded;
        editor.Stood(_state);
    }

    /// <summary>Lets go of the view of the document, where it is not a view of <paramref name="document"/>.</summary>
    private void Unadopted(EditedDocument? document)
    {
        if (_editor is not { } editor || ReferenceEquals(editor.Document, document)) return;

        editor.Detach(this);
        editor.Marks.Changed -= this.OnRemarked;
        editor.Folds.Changed -= this.OnFolded;
        if (_ownsEditor) editor.Dispose();

        _editor = null;
    }

    void IContentEditorView.Take(EditState next, bool written)
    {
        if (written) this.Replace(next);
        else this.Apply(next, notify: false);
    }

    void IContentEditorView.ShowLine(int line, ShownPlace place) => this.LineAsked?.Invoke(this, (line, place));

    /// <summary>
    /// The page wrote in the document, or loaded another into it: what it wrote is taken up, with the caret carried through the
    /// change, and said again as this engine's change, so whatever shows the content hears it as it hears everything else.
    /// </summary>
    private void Elsewhere(object? sender, ContentSourceChange change)
    {
        if (_taking) return;

        var before = _state;
        var caret = change.Kind == ContentChangeKind.Loaded ? 0 : change.Change.Carried(_state.Caret, leansAfter: true);

        this.Apply(new EditState(change.After.Document, caret), notify: false);
        _last = _state;

        SourceChanged?.Invoke(this, new ContentSourceChange(before, _state, change.Kind));
    }

    /// <summary><paramref name="write"/>, done to the document as this engine's own writing, so the change it raises is not taken up again.</summary>
    private T Taking<T>(Func<T> write)
    {
        _taking = true;
        try { return write(); }
        finally { _taking = false; }
    }

    /// <summary>Takes back the last thing written.</summary>
    internal void Undo() => Back(this.Taking(() => this.EditedDocument.Undo(_state)), ContentChangeKind.Undone);

    /// <summary>Writes again what was last taken back.</summary>
    internal void Redo() => Back(this.Taking(() => this.EditedDocument.Redo(_state)), ContentChangeKind.Redone);

    private void Back(EditState? state, ContentChangeKind kind)
    {
        if (state is null) return;

        var before = _state;
        Apply(state, notify: false);
        _last = _state;

        SourceChanged?.Invoke(this, new ContentSourceChange(before, _state, kind));
    }

    /// <summary>
    /// Writes <paramref name="next"/> as though it had been typed — one step to take back — for an edit made on the reader's
    /// behalf: something dropped or pasted, a block put in by the host.
    /// </summary>
    internal void Replace(EditState next)
    {
        if (_readOnly || ReferenceEquals(next.Document, _state.Document)) return;

        var before = _state;

        this.History.Break();
        Apply(next, notify: false);
        Recorded(ContentChangeKind.Replaced);
        this.History.Break();

        SourceChanged?.Invoke(this, new ContentSourceChange(before, _state, ContentChangeKind.Replaced));
    }

    /// <summary>What is here now is where the next step starts from, whatever was written before it.</summary>
    internal void Begin()
    {
        this.History.Break();
        _last = _state;
    }

    /// <summary>What was just written, remembered as a step — or as more of the step it carries on from — and said to the document's holder.</summary>
    private void Recorded(ContentChangeKind kind = ContentChangeKind.Written)
    {
        this.Taking(() =>
        {
            this.EditedDocument.Wrote(_last, _state, kind);
            return true;
        });

        _last = _state;
    }

    /// <summary>
    /// The caret was put somewhere, or something was picked out, with nothing written: whatever is written next is a step of
    /// its own, and starts from here — so taking it back puts the caret back where the reader had put it.
    /// </summary>
    private void Stayed()
    {
        if (ReferenceEquals(_state, _last) || !ReferenceEquals(_state.Document, _last.Document)) return;

        _last = _state;
        this.History.Break();
    }
}
