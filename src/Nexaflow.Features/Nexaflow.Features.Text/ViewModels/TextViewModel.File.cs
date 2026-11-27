using Nexaflow.Markdown.Editing;

namespace Nexaflow.Features.Text.ViewModels;

/// <summary>
/// The file as lines of the whole of it — what going to a line, searching, the assistant's tools and the line numbers all speak in — whether
/// the document is the file, or a window onto it that moves as the reader does.
/// </summary>
public sealed partial class TextViewModel
{
    /// <summary>How many lines the whole file comes to, as it is written now.</summary>
    private long FileLineCount => _window?.LineCount ?? Document.Current.LineCount;

    /// <summary>How many lines of the file stand before the document — none, where the document is the whole file.</summary>
    private long LinesBefore => _window?.LinesBefore ?? 0;

    /// <summary>How many characters the whole file comes to, as it is written now.</summary>
    private long FileLength => _window?.Length ?? Document.Current.Length;

    /// <summary>Where line <paramref name="line"/> of the whole file starts, counting from nought — or null for a line it does not have.</summary>
    private long? FileOffsetOfLine(long line)
    {
        if (_window is { } window) return window.OffsetOfLine(line);

        var document = Document.Current;
        return line < 0 || line >= document.LineCount ? null : document.OffsetOf((int)line);
    }

    /// <summary>The <paramref name="length"/> characters of the whole file from <paramref name="start"/>, as it is written now.</summary>
    private string FileText(long start, int length)
    {
        if (_window is { } window) return window.Text(start, length);

        var document = Document.Current;
        var from = (int)Math.Clamp(start, 0, document.Length);
        return document.Slice(from, Math.Clamp(length, 0, document.Length - from));
    }

    /// <summary>
    /// What hands every line of the whole file, as it is written now, to the action it is given — its number, where it starts and what is
    /// written on it — off this thread if need be: it reads the file as it stood when this was asked, whatever is written or moved meanwhile.
    /// </summary>
    private Action<Action<long, long, string>, CancellationToken> FileLines()
    {
        if (_window is { } window) return window.EachLine;

        var document = Document.Current;

        return (each, cancel) =>
        {
            for (var line = 0; line < document.LineCount; line++)
            {
                cancel.ThrowIfCancellationRequested();

                var (start, end) = document.LineAt(document.OffsetOf(line));
                each(line, start, document.Slice(start, end - start).TrimEnd('\r', '\n'));
            }
        };
    }

    /// <summary>Where in the document <paramref name="fileOffset"/>, an offset in the whole file, stands — the window brought to it first where it is not over it.</summary>
    private int DocumentOffset(long fileOffset)
    {
        if (_window is not { } window) return (int)Math.Clamp(fileOffset, 0, Document.Current.Length);

        window.Reach(fileOffset, fileOffset);
        return (int)Math.Clamp(fileOffset - window.Start, 0, Document.Current.Length);
    }

    /// <summary>Which line of the whole file <paramref name="documentOffset"/> stands on, counting from nought.</summary>
    private long FileLineOf(int documentOffset) =>
        LinesBefore + Document.Current.LineOf(Math.Clamp(documentOffset, 0, Document.Current.Length));

    /// <summary>Puts the caret at the start of line <paramref name="line"/> of the whole file, counting from nought, and the line where <paramref name="place"/> says.</summary>
    private void GoToFileLine(long line, ShownPlace place)
    {
        if (FileOffsetOfLine(Math.Clamp(line, 0, Math.Max(0, FileLineCount - 1))) is not { } at) return;

        var offset = DocumentOffset(at);
        Editor.MoveCaretTo(offset);
        Editor.ScrollToLine(Document.Current.LineOf(offset), place);
    }

    /// <summary>The lines on the page, numbered as lines of the whole file from one: the first's number, and what is written on each.</summary>
    private (long First, IReadOnlyList<string> Lines) VisibleLines()
    {
        var document = Document.Current;
        var (first, last) = Editor.ShownLines;
        (first, last) = (Math.Clamp(first, 0, document.LineCount - 1), Math.Clamp(last, 0, document.LineCount - 1));

        var lines = new List<string>(last - first + 1);
        for (var line = first; line <= last; line++)
        {
            var (start, end) = document.LineAt(document.OffsetOf(line));
            lines.Add(document.Slice(start, end - start).TrimEnd('\r', '\n'));
        }

        return (LinesBefore + first + 1, lines);
    }

    /// <summary>Whether the window is on its way to stand round the reader, so a scroll meanwhile asks for nothing more.</summary>
    private bool _following;

    /// <summary>
    /// Keeps a window onto a file round the reader: where the page comes near either end of it, and the file goes on past that end, the window
    /// is moved to stand round the page — once the page is done being laid — and the page is stood back on the line it stood on, which moving
    /// the window does not move.
    /// </summary>
    private async void FollowReader(object? sender, EventArgs args)
    {
        if (_window is not { } window || _following) return;

        var document = Document.Current;
        var (first, last) = Editor.ShownLines;
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
            if (_disposed || !ReferenceEquals(window, _window)) return;

            var standing = FileLineOf(document.OffsetOf(Math.Min(first, document.LineCount - 1)));
            window.MoveTo(window.Start + top + ((bottom - top) / 2));
            Editor.ScrollToLine((int)(standing - LinesBefore), ShownPlace.Top);
        }
        finally
        {
            _following = false;
        }
    }
}
