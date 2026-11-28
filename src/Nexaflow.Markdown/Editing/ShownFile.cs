using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// The whole file a page shows, in its own lines and offsets — whether the page's document is the file, or a window onto one too long for a
/// document (<see cref="EditedDocument.Window"/>) — and that window kept round the reader. Going to a line, searching, line numbers and an
/// assistant's tools all speak in the whole file's lines through it, so none of them need know whether there is a window.
/// </summary>
public sealed class ShownFile : IDisposable
{
    private readonly ContentEditor _editor;

    /// <summary>Whether the window is on its way to stand round the reader, so a scroll meanwhile asks for nothing more.</summary>
    private bool _following;

    private bool _disposed;

    /// <summary>The file <paramref name="editor"/>'s document shows, with its window kept round where the editor's view stands.</summary>
    public ShownFile(ContentEditor editor)
    {
        _editor = editor;
        editor.Scrolled += this.FollowReader;
    }

    /// <summary>The view the file is shown in: its caret, and where its page stands.</summary>
    public ContentEditor Editor => _editor;

    /// <summary>The page's document: the file, or a window onto it.</summary>
    public EditedDocument Document => _editor.Document;

    /// <summary>How many lines the whole file comes to, as it is written now.</summary>
    public long LineCount => this.Document.Window?.LineCount ?? this.Document.Current.LineCount;

    /// <summary>How many lines of the file stand before the document — none, where the document is the whole file.</summary>
    public long LinesBefore => this.Document.Window?.LinesBefore ?? 0;

    /// <summary>How many characters the whole file comes to, as it is written now.</summary>
    public long Length => this.Document.Window?.Length ?? this.Document.Current.Length;

    /// <summary>Where line <paramref name="line"/> of the whole file starts, counting from nought — or null for a line it does not have.</summary>
    public long? OffsetOfLine(long line)
    {
        if (this.Document.Window is { } window) return window.OffsetOfLine(line);

        var document = this.Document.Current;
        return line < 0 || line >= document.LineCount ? null : document.OffsetOf((int)line);
    }

    /// <summary>The <paramref name="length"/> characters of the whole file from <paramref name="start"/>, as it is written now.</summary>
    public string Text(long start, int length)
    {
        if (this.Document.Window is { } window) return window.Text(start, length);

        var document = this.Document.Current;
        var from = (int)Math.Clamp(start, 0, document.Length);
        return document.Slice(from, Math.Clamp(length, 0, document.Length - from));
    }

    /// <summary>What is written on line <paramref name="line"/> of the whole file, counting from nought, without its line end.</summary>
    public string LineText(long line)
    {
        if (this.OffsetOfLine(line) is not { } start) return string.Empty;

        var end = this.OffsetOfLine(line + 1) ?? this.Length;
        return this.Text(start, (int)Math.Min(int.MaxValue, end - start)).TrimEnd('\r', '\n');
    }

    /// <summary>
    /// What is written on up to <paramref name="count"/> lines of the whole file from line <paramref name="first"/>, counting from nought, each
    /// without its line end — read as one stretch, so a run of lines costs one look for where it starts.
    /// </summary>
    public IReadOnlyList<string> LineTexts(long first, int count)
    {
        first = Math.Max(0, first);
        count = (int)Math.Clamp(this.LineCount - first, 0, Math.Max(0, count));
        if (count == 0 || this.OffsetOfLine(first) is not { } start) return [];

        var end = this.OffsetOfLine(first + count) ?? this.Length;
        var text = this.Text(start, (int)Math.Min(int.MaxValue, end - start));
        var lines = new List<string>(count);

        for (var (at, from) = (0, 0); lines.Count < count; at++)
        {
            if (at < text.Length && text[at] is not ('\n' or '\r')) continue;

            lines.Add(text[from..Math.Min(at, text.Length)]);
            if (at >= text.Length) break;

            if (at < text.Length - 1 && text[at] == '\r' && text[at + 1] == '\n') at++;
            from = at + 1;
        }

        return lines;
    }

    /// <summary>
    /// What hands every line of the whole file, as it is written now, to the action it is given — its number, where it starts and what is
    /// written on it — off this thread if need be: it reads the file as it stood when this was asked, whatever is written or moved meanwhile.
    /// </summary>
    public Action<Action<long, long, string>, CancellationToken> Lines()
    {
        if (this.Document.Window is { } window) return window.Lines();

        var document = this.Document.Current;

        return (each, cancel) => document.LinesFrom(0, (line, start, text) =>
        {
            each(line, start, text);
            return true;
        }, cancel);
    }

    /// <summary>Where in the document <paramref name="fileOffset"/>, an offset in the whole file, stands — the window brought to it first where it is not over it.</summary>
    public int DocumentOffset(long fileOffset)
    {
        if (this.Document.Window is not { } window) return (int)Math.Clamp(fileOffset, 0, this.Document.Current.Length);

        window.Reach(fileOffset, fileOffset);
        return (int)Math.Clamp(fileOffset - window.Start, 0, this.Document.Current.Length);
    }

    /// <summary>Where in the document <paramref name="fileOffset"/>, an offset in the whole file, stands — or null, where the document does not hold it; nothing is moved.</summary>
    public int? HeldOffset(long fileOffset)
    {
        var at = fileOffset - (this.Document.Window?.Start ?? 0);
        return fileOffset < 0 || at < 0 || at > this.Document.Current.Length ? null : (int)at;
    }

    /// <summary>Which line of the whole file <paramref name="documentOffset"/> stands on, counting from nought.</summary>
    public long LineOf(int documentOffset) =>
        this.LinesBefore + this.Document.Current.LineOf(Math.Clamp(documentOffset, 0, this.Document.Current.Length));

    /// <summary>Puts the caret at the start of line <paramref name="line"/> of the whole file, counting from nought, and the line where <paramref name="place"/> says.</summary>
    public void GoToLine(long line, ShownPlace place)
    {
        if (this.OffsetOfLine(Math.Clamp(line, 0, Math.Max(0, this.LineCount - 1))) is not { } at) return;

        var offset = this.DocumentOffset(at);
        _editor.MoveCaretTo(offset);
        _editor.ScrollToLine(this.Document.Current.LineOf(offset), place);
    }

    /// <summary>The lines on the page, numbered as lines of the whole file from one: the first's number, and what is written on each.</summary>
    public (long First, IReadOnlyList<string> Lines) ShownLines()
    {
        var document = this.Document.Current;
        var (first, last) = _editor.ShownLines;
        (first, last) = (Math.Clamp(first, 0, document.LineCount - 1), Math.Clamp(last, 0, document.LineCount - 1));

        var lines = new List<string>(last - first + 1);
        for (var line = first; line <= last; line++)
        {
            var (start, end) = document.LineAt(document.OffsetOf(line));
            lines.Add(document.Slice(start, end - start).TrimEnd('\r', '\n'));
        }

        return (this.LinesBefore + first + 1, lines);
    }

    public void Dispose()
    {
        _disposed = true;
        _editor.Scrolled -= this.FollowReader;
    }

    /// <summary>
    /// Keeps a window onto a file round the reader: where the page comes near either end of it, and the file goes on past that end, the window
    /// is moved to stand round the page — once the page is done being laid — and the page is stood back on the line it stood on, which moving
    /// the window does not move.
    /// </summary>
    private async void FollowReader(object? sender, EventArgs args)
    {
        if (this.Document.Window is not { } window || _following) return;

        var document = this.Document.Current;
        var (first, last) = _editor.ShownLines;
        var (top, bottom) = (document.OffsetOf(Math.Min(first, document.LineCount - 1)), document.OffsetOf(Math.Min(last, document.LineCount - 1)));
        var near = window.Size / 8;

        var nearStart = top < near && window.Start > 0;
        var nearEnd = bottom > document.Length - near && window.Start + document.Length < window.Length;
        if (!nearStart && !nearEnd) return;

        _following = true;

        try
        {
            // Not while the page is being laid, which is what said where it stands.
            await Task.Yield();
            if (_disposed || !ReferenceEquals(window, this.Document.Window)) return;

            var standing = this.LineOf(document.OffsetOf(Math.Min(first, document.LineCount - 1)));
            window.MoveTo(window.Start + top + ((bottom - top) / 2));
            _editor.ScrollToLine((int)(standing - this.LinesBefore), ShownPlace.Top);
        }
        finally
        {
            _following = false;
        }
    }
}
