using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// What a view of a document has folded away: stretches whose lines after the first are hidden — a method's body, a comment block — each
/// held by anchors, so a fold stays over what it folded however the document is written in around it (<see cref="ContentEditor.Folds"/>).
///
/// <para>
/// <strong>A fold hides whole lines.</strong> The line its start is on stays, with a mark saying what is folded behind it; every line
/// after it up to the one its end is on is hidden. So a stretch on one line has nothing to fold, and a caret, a found match or a jump that
/// lands on a hidden line opens what hid it (<see cref="Reveal"/>). A fold whose characters were all taken out is let go of.
/// </para>
/// </summary>
public sealed class ContentFolds : IDisposable
{
    private readonly EditedDocument _document;
    private readonly List<(ContentAnchor Start, ContentAnchor End)> _folds = [];

    internal ContentFolds(EditedDocument document) => _document = document;

    /// <summary>Raised when something is folded away or opened again.</summary>
    public event EventHandler? Changed;

    /// <summary>What is folded, where it now stands, in order.</summary>
    public IReadOnlyList<(int Start, int End)> Ranges
    {
        get
        {
            this.Pruned();
            return [.. _folds.Select(fold => (Start: fold.Start.Offset, End: fold.End.Offset)).OrderBy(range => range.Start)];
        }
    }

    /// <summary>Whether a fold starting at <paramref name="start"/> is folded away.</summary>
    public bool IsFolded(int start)
    {
        this.Pruned();
        return _folds.Any(fold => fold.Start.Offset == start);
    }

    /// <summary>Whether a fold hides the line <paramref name="offset"/> is on.</summary>
    public bool Hides(int offset)
    {
        this.Pruned();

        var document = _document.Current;
        return _folds.Any(fold => Hides(document, fold.Start.Offset, fold.End.Offset, offset));
    }

    /// <summary>Folds away the lines after the one <paramref name="start"/> is on, up to the one <paramref name="end"/> is on — false where there are none.</summary>
    public bool Fold(int start, int end)
    {
        if (!this.Folded(start, end)) return false;

        this.Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Opens the fold starting at <paramref name="start"/> — false where none does.</summary>
    public bool Unfold(int start)
    {
        if (!this.Opened(fold => fold.Start.Offset == start)) return false;

        this.Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Folds the stretch away, or opens it where it is folded.</summary>
    public bool Toggle(int start, int end) => this.IsFolded(start) ? this.Unfold(start) : this.Fold(start, end);

    /// <summary>Opens every fold hiding the line <paramref name="offset"/> is on — what a caret, a match or a jump landing there does.</summary>
    public bool Reveal(int offset)
    {
        var document = _document.Current;
        if (!this.Opened(fold => Hides(document, fold.Start.Offset, fold.End.Offset, offset))) return false;

        this.Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Folds away every one of <paramref name="offered"/> not folded already — the stretches the content says can be folded.</summary>
    public void FoldAll(IEnumerable<(int Start, int End)> offered)
    {
        var any = false;
        foreach (var (start, end) in offered) any |= this.Folded(start, end);

        if (any) this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens everything folded.</summary>
    public void UnfoldAll()
    {
        if (!this.Opened(_ => true)) return;

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whether a fold from <paramref name="start"/> to <paramref name="end"/> hides the line <paramref name="offset"/> is on in
    /// <paramref name="document"/>: a line after the one its start is on, up to and with the one its end is on.
    /// </summary>
    public static bool Hides(ContentDocument document, int start, int end, int offset)
    {
        var (head, last) = (document.LineOf(start), document.LineOf(end));
        if (last <= head) return false;

        var line = document.LineOf(offset);
        return line > head && line <= last;
    }

    public void Dispose()
    {
        foreach (var (start, end) in _folds)
        {
            start.Dispose();
            end.Dispose();
        }

        _folds.Clear();
    }

    private bool Folded(int start, int end)
    {
        var document = _document.Current;
        if (end <= start || document.LineOf(end) <= document.LineOf(start) || this.IsFolded(start)) return false;

        _folds.Add((_document.Anchor(start), _document.Anchor(end, leansAfter: true)));
        return true;
    }

    private bool Opened(Func<(ContentAnchor Start, ContentAnchor End), bool> which)
    {
        var any = false;

        for (var index = _folds.Count - 1; index >= 0; index--)
        {
            if (!which(_folds[index])) continue;

            _folds[index].Start.Dispose();
            _folds[index].End.Dispose();
            _folds.RemoveAt(index);
            any = true;
        }

        return any;
    }

    /// <summary>Lets go of every fold whose characters were all taken out, or whose lines came to be one.</summary>
    private void Pruned()
    {
        var document = _document.Current;
        this.Opened(fold => fold.End.Offset <= fold.Start.Offset || document.LineOf(fold.End.Offset) <= document.LineOf(fold.Start.Offset));
    }
}
