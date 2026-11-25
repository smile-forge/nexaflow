using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nexaflow.Services.Initiatives.Cli.Daemon;

/// <summary>
/// One command's output, line by line, each line either part of what the command concluded or detail behind it — an edit's
/// diff, the errors past the first few, the whole of a failing test's trace. A command writes its conclusions to
/// <c>Console.Out</c> as ever, and its detail to <see cref="Detail"/>, which it reaches as <see cref="RequestScope.Detail"/>.
/// <para>
/// What is printed is decided once the command is done (<see cref="Render"/>): a short answer whole; a long one with each
/// run of detail folded into one line naming the number all of it is kept under, so the caller reads what the command
/// concluded and asks for the rest only when it needs it — <c>nfi more @7</c> — without the work being done again.
/// Filtering a command's output down to size cut exactly what the next call then asked for.
/// </para>
/// <para>
/// A live answer (<see cref="AnswerWriter(TextWriter)"/>) is one that takes minutes — a test run: its conclusions go out
/// as they are written, and its detail is only kept (<see cref="FinishLive"/>).
/// </para>
/// </summary>
internal sealed class AnswerWriter : TextWriter
{
    /// <summary>An answer no longer than this is printed whole, detail and all.</summary>
    internal const int WholeChars = 6_000;

    /// <summary>A run of detail no longer than this is printed rather than folded: the line naming it would be as long.</summary>
    internal const int FoldedRun = 3;

    private readonly List<(string Text, bool Detail)> _lines = [];
    private readonly StringBuilder _raw  = new();
    private readonly StringBuilder _open = new();
    private readonly TextWriter? _live;
    private readonly object _gate = new();
    private bool _started;
    private bool _openDetail;

    /// <summary>An answer printed once the command is done.</summary>
    public AnswerWriter() : this(null) { }

    /// <summary>An answer whose conclusions go to <paramref name="live"/> as they are written, when it is not null.</summary>
    public AnswerWriter(TextWriter? live)
    {
        _live  = live;
        Detail = new DetailWriter(this);
    }

    /// <summary>Where the command writes its detail.</summary>
    public TextWriter Detail { get; }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value) => Append(value.ToString(), detail: false);

    public override void Write(string? value) => Append(value, detail: false);

    public override void Write(char[] buffer, int index, int count) => Append(new string(buffer, index, count), detail: false);

    /// <summary>Everything written, conclusions and detail, exactly as it was written.</summary>
    public override string ToString()
    {
        lock (_gate) return _raw.ToString();
    }

    /// <summary>
    /// What is printed. The whole answer when it is no longer than <see cref="WholeChars"/>, or holds no detail, or
    /// <paramref name="keep"/> has nowhere to keep it — detail nobody can get back is detail lost. Otherwise what the
    /// command concluded, with each run of detail longer than <see cref="FoldedRun"/> lines folded into one line naming
    /// the number <paramref name="keep"/> kept every line under.
    /// </summary>
    public string Render(Func<IReadOnlyList<string>, int?> keep)
    {
        var (raw, lines) = Finish();
        if (raw.Length <= WholeChars || !lines.Any(l => l.Detail) || keep([.. lines.Select(l => l.Text)]) is not { } number)
            return raw;

        var sb     = new StringBuilder();
        var hinted = false;
        for (var i = 0; i < lines.Count;)
        {
            if (!lines[i].Detail)
            {
                sb.Append(lines[i++].Text).Append(NewLine);
                continue;
            }

            var end = i;
            while (end < lines.Count && lines[end].Detail) end++;
            if (end - i <= FoldedRun)
                for (; i < end; i++) sb.Append(lines[i].Text).Append(NewLine);
            else
            {
                sb.Append(Folded(end - i, number, hinted)).Append(NewLine);
                hinted = true;
                i      = end;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Ends a live answer, whose conclusions have been printed already: what is left to print is one line naming the number
    /// <paramref name="keep"/> kept the whole answer under; or, with nowhere to keep it, the detail itself; or nothing,
    /// when there was no detail.
    /// </summary>
    public string FinishLive(Func<IReadOnlyList<string>, int?> keep)
    {
        var (_, lines) = Finish();
        var detail = lines.Where(l => l.Detail).Select(l => l.Text).ToList();
        if (detail.Count == 0) return "";
        if (keep([.. lines.Select(l => l.Text)]) is { } number) return Folded(detail.Count, number, hinted: false) + NewLine;
        return string.Concat(detail.Select(d => d + NewLine));
    }

    private static string Folded(int count, int number, bool hinted) =>
        $"… {count} line(s) kept: nfi more @{number}" + (hinted ? "" : " reads them, --grep <regex> searches them");

    /// <summary>Closes a line left open, and hands back the text and the lines.</summary>
    private (string Raw, List<(string Text, bool Detail)> Lines) Finish()
    {
        lock (_gate)
        {
            if (_started) EndLine();
            return (_raw.ToString(), [.. _lines]);
        }
    }

    /// <summary>
    /// Adds <paramref name="text"/> to the answer. A line is detail only when every part of it was written as detail — a
    /// conclusion that happens to be written in pieces stays a conclusion.
    /// </summary>
    private void Append(string? text, bool detail)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_gate)
        {
            _raw.Append(text);
            var start = 0;
            while (true)
            {
                var newline = text.IndexOf('\n', start);
                var part    = newline < 0 ? text.AsSpan(start) : text.AsSpan(start, newline - start);
                if (part.Length > 0 || newline >= 0)
                {
                    _openDetail = _started ? _openDetail && detail : detail;
                    _started    = true;
                    _open.Append(part);
                }
                if (newline < 0) break;
                EndLine();
                start = newline + 1;
            }
        }
    }

    private void EndLine()
    {
        var line = _open.ToString().TrimEnd('\r');
        _lines.Add((line, _openDetail));
        if (_live is not null && !_openDetail) _live.WriteLine(line);
        _open.Clear();
        _started    = false;
        _openDetail = false;
    }

    /// <summary>The <see cref="Detail"/> face of an answer: what it writes is the answer's detail.</summary>
    private sealed class DetailWriter(AnswerWriter answer) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => answer.Append(value.ToString(), detail: true);

        public override void Write(string? value) => answer.Append(value, detail: true);

        public override void Write(char[] buffer, int index, int count) => answer.Append(new string(buffer, index, count), detail: true);
    }
}
