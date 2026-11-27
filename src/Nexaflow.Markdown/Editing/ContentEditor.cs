using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Markdown.Editing;

/// <summary>Where on the page a line asked for is put (<see cref="ContentEditor.ScrollToLine"/>).</summary>
public enum ShownPlace
{
    /// <summary>Left where it is if it is on the page already; otherwise brought onto it, half way down.</summary>
    Nearest,

    /// <summary>At the top of the page.</summary>
    Top,

    /// <summary>Half way down the page, whether or not it was on it.</summary>
    Centre,
}

/// <summary>
/// What shows a <see cref="ContentEditor"/>: it makes what the editor is asked for — so the editing rules of what the caret is in apply,
/// and what is written is laid out again — and moves its page; and it tells the editor what came of it (<see cref="ContentEditor.Stood"/>,
/// <see cref="ContentEditor.Showed"/>).
/// </summary>
public interface IContentEditorView
{
    /// <summary>Makes <paramref name="next"/> what is shown — written, as a step that can be taken back, where <paramref name="written"/> says so.</summary>
    void Take(EditState next, bool written);

    /// <summary>Moves the page so line <paramref name="line"/>, counting from nought, stands where <paramref name="place"/> says.</summary>
    void ShowLine(int line, ShownPlace place);

    /// <summary>What the content says can be folded, in order: each stretch's start and end — nothing, for content that folds nothing.</summary>
    IReadOnlyList<(int Start, int End)> Foldable();
}

/// <summary>
/// One view of a document: where its caret is, what is picked out, and which lines are on its page — with what a host asks of a view.
///
/// <para>
/// <strong>The engine's, not a control's.</strong> A page's view-model holds this beside the <see cref="EditedDocument"/> it shows, and
/// reads and moves the caret and the selection through it; whatever draws the document — today a WPF control — is its
/// <see cref="IContentEditorView"/>, attached while it is on screen. So the caret a host put somewhere, and the line it asked for, are
/// where it left them whatever draws them, and before anything does.
/// </para>
/// <para>
/// <strong>One way in for every change.</strong> What a host asks — put the caret there, pick that out, write this over it — goes to the
/// view where there is one, which makes it as it makes a key, and hands back what it came to (<see cref="Stood"/>); with no view it is made
/// here. Either way <see cref="CaretMoved"/> and <see cref="SelectionChanged"/> say so once.
/// </para>
/// </summary>
public sealed class ContentEditor : IDisposable
{
    private EditState _state;
    private IContentEditorView? _view;

    /// <summary>A line asked for before anything showed the editor, shown once something does.</summary>
    private (int Line, ShownPlace Place)? _asked;

    /// <summary>Whether this is writing in the document itself, so the change it hears back is its own.</summary>
    private bool _taking;

    /// <summary>A view of <paramref name="document"/> — a document of its own where none is handed over — with the caret at its start.</summary>
    public ContentEditor(EditedDocument? document = null)
    {
        this.Document = document ?? new EditedDocument();
        _state = new EditState(this.Document.Current, 0);

        this.Marks.Add(this.Found);
        this.Folds = new ContentFolds(this.Document);
        this.Document.Changed += this.Elsewhere;
    }

    /// <summary>The document it is a view of.</summary>
    public EditedDocument Document { get; }

    /// <summary>The document as this view stands in it: the version, the caret, what is picked out.</summary>
    public EditState State => _state;

    /// <summary>Where the caret is.</summary>
    public int Caret => _state.Caret;

    /// <summary>What is picked out, in order: nothing, one stretch, or several.</summary>
    public IReadOnlyList<EditRange> Selection => _state.Selection;

    /// <summary>Whether anything is picked out.</summary>
    public bool HasSelection => _state.HasSelection;

    /// <summary>The first and last line on the page, counting from nought — both nought before anything shows it.</summary>
    public (int First, int Last) ShownLines { get; private set; }

    /// <summary>Whether something shows it now.</summary>
    public bool IsShown => _view is not null;

    /// <summary>
    /// The layers of marks laid over this view — what a search found, what a filter leaves out, each level a log line is at — carried
    /// through every change to the document, and drawn by whatever shows it over the stretch it draws.
    /// </summary>
    public ContentMarkLayers Marks { get; } = new();

    /// <summary>What a search turned up in this view, and which is being looked at: one of <see cref="Marks"/>, always there.</summary>
    public FoundMarks Found { get; } = new();

    /// <summary>What this view has folded away, held through every change to the document.</summary>
    public ContentFolds Folds { get; private set; } = null!;

    /// <summary>Raised when the caret moves.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Raised when what is picked out changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when which lines are on the page changes.</summary>
    public event EventHandler? Scrolled;

    /// <summary>What is picked out, joined in order — made only when asked.</summary>
    public string SelectedText() => _state.SelectedText;

    // ── What a host asks ────────────────────────────────────────────────────

    /// <summary>Puts the caret at <paramref name="offset"/>, or stretches what is picked out to it.</summary>
    public void MoveCaretTo(int offset, bool extend = false) =>
        this.Take(extend ? this.Extended(offset) : _state.MoveCaretTo(offset), written: false);

    /// <summary>Picks out <paramref name="length"/> characters from <paramref name="start"/>, the caret at their end.</summary>
    public void Select(int start, int length) => this.Take(_state.Select(start, length), written: false);

    /// <summary>Picks out everything written.</summary>
    public void SelectAll() => this.Select(0, _state.Document.Length);

    /// <summary>Picks out lines <paramref name="first"/> to <paramref name="last"/>, counting from nought, line breaks and all.</summary>
    public void SelectLines(int first, int last)
    {
        var document = _state.Document;
        var (from, to) = (Math.Min(first, last), Math.Max(first, last));
        var start = document.OffsetOf(from);
        var end = to + 1 < document.LineCount ? document.OffsetOf(to + 1) : document.Length;

        this.Select(start, end - start);
    }

    /// <summary>Writes <paramref name="text"/> over what is picked out — at the caret, where nothing is — as one step that can be taken back.</summary>
    public void ReplaceSelection(string text) => this.Take(_state.Write(text), written: true);

    /// <summary>Moves the caret a word along, or back — stretching what is picked out where <paramref name="extend"/> says.</summary>
    public void MoveWord(bool forward, bool extend = false) =>
        this.MoveCaretTo(ContentWordEdges.Next(_state.Document, _state.Caret, forward), extend);

    /// <summary>Takes out the word after the caret, or the one before it — or what is picked out, where anything is.</summary>
    public void DeleteWord(bool forward)
    {
        if (this.Words(forward) is { } next) this.Take(next, written: true);
    }

    /// <summary>
    /// What taking out a word from the caret comes to — what is picked out, where anything is — or null where there is nothing that way.
    /// What a view does for Ctrl+Backspace and Ctrl+Delete too.
    /// </summary>
    public EditState? Words(bool forward)
    {
        if (_state.HasSelection) return _state.Write(string.Empty);

        var to = ContentWordEdges.Next(_state.Document, _state.Caret, forward);
        var (start, end) = (Math.Min(to, _state.Caret), Math.Max(to, _state.Caret));

        return end > start ? _state.Remove(start, end - start) : null;
    }

    /// <summary>Moves the page so line <paramref name="line"/>, counting from nought, stands where <paramref name="place"/> says — once something shows it.</summary>
    public void ScrollToLine(int line, ShownPlace place = ShownPlace.Nearest)
    {
        if (_view is { } view) view.ShowLine(line, place);
        else _asked = (line, place);
    }

    /// <summary>Folds away everything the content says can be folded — nothing, where nothing shows it to say.</summary>
    public void FoldAll() => this.Folds.FoldAll(_view?.Foldable() ?? []);

    /// <summary>Opens everything folded.</summary>
    public void UnfoldAll() => this.Folds.UnfoldAll();

    // ── What shows it says ──────────────────────────────────────────────────

    /// <summary>Shown by <paramref name="view"/> from now on — which is handed any line asked for meanwhile.</summary>
    public void Attach(IContentEditorView view)
    {
        _view = view;

        if (_asked is not var (line, place)) return;

        _asked = null;
        view.ShowLine(line, place);
    }

    /// <summary>No longer shown by <paramref name="view"/>.</summary>
    public void Detach(IContentEditorView view)
    {
        if (ReferenceEquals(_view, view)) _view = null;
    }

    /// <summary>What shows it now stands at <paramref name="state"/>: said to whoever listens where the caret or what is picked out moved.</summary>
    public void Stood(EditState state) => this.Told(this.Stand(state));

    /// <summary>
    /// What shows it stands at <paramref name="state"/> from now on, not said yet — so what shows it can lay itself out by it before anyone listening
    /// hears (<see cref="Told"/>). Hands back where it stood before.
    /// </summary>
    internal EditState Stand(EditState state)
    {
        var before = _state;
        _state = state;
        return before;
    }

    /// <summary>Says to whoever listens where the caret or what is picked out moved since it stood at <paramref name="before"/>.</summary>
    internal void Told(EditState before)
    {
        if (before.Caret != _state.Caret) this.CaretMoved?.Invoke(this, EventArgs.Empty);
        if (!before.Selection.SequenceEqual(_state.Selection)) this.SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What shows it now has lines <paramref name="first"/> to <paramref name="last"/> on its page.</summary>
    public void Showed(int first, int last)
    {
        if (this.ShownLines == (first, last)) return;

        this.ShownLines = (first, last);
        this.Scrolled?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        this.Document.Changed -= this.Elsewhere;
        this.Folds.Dispose();
    }

    // ── Mechanics ───────────────────────────────────────────────────────────

    /// <summary>What is picked out stretched from where it was begun to <paramref name="offset"/>, the caret there.</summary>
    private EditState Extended(int offset)
    {
        var from = !_state.HasSelection ? _state.Caret
                 : _state.Caret == _state.Selection[0].Start ? _state.Selection[^1].End
                 : _state.Selection[0].Start;

        var to = Math.Clamp(offset, 0, _state.Document.Length);
        var picked = _state.Select(Math.Min(from, to), Math.Abs(to - from));

        return picked with { Caret = to };
    }

    /// <summary><paramref name="next"/> made by what shows this, or here where nothing does.</summary>
    private void Take(EditState next, bool written)
    {
        if (_view is { } view)
        {
            view.Take(next, written);
            return;
        }

        if (written && !ReferenceEquals(next.Document, _state.Document))
        {
            _taking = true;

            try
            {
                this.Document.History.Break();
                this.Document.Wrote(_state, next, ContentChangeKind.Replaced);
                this.Document.History.Break();
            }
            finally
            {
                _taking = false;
            }
        }

        this.Stood(next);
    }

    /// <summary>
    /// The document was written in by something other than this — a page, or another view of it: with nothing showing this, the caret and
    /// what is picked out are carried through the change here. Whatever shows it carries them itself, and says where they came to.
    /// </summary>
    private void Elsewhere(object? sender, ContentSourceChange change)
    {
        // The marks over the view stay over what they named, whoever wrote: this view, another, or the page.
        this.Marks.Carry(change);

        if (_view is not null || _taking) return;

        if (change.Kind == ContentChangeKind.Loaded)
        {
            this.Stood(new EditState(change.After.Document, 0));
            return;
        }

        var through = change.Change;
        var picked = _state.Selection
            .Select(range =>
            {
                var start = through.Carried(range.Start);
                return new EditRange(start, Math.Max(0, through.Carried(range.End, leansAfter: true) - start));
            })
            .Where(range => range.Length > 0)
            .ToList();

        this.Stood(new EditState(change.After.Document, through.Carried(_state.Caret, leansAfter: true), picked));
    }
}
