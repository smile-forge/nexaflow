using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// What is written to it, said back a line at a time — its number, counting from nought, where it starts among everything written, and what
/// is written on it without its line end — as a document reads lines: a line feed, a return, or the two together end one. Holds only the line
/// being read, so a file of any length is read through it at the cost of its longest line.
/// </summary>
/// <param name="firstLine">The number of the first line written, where what is written starts part way through a document.</param>
/// <param name="firstOffset">Where the first line written starts in that document.</param>
internal sealed class ContentLineSplitter(Action<long, long, string> each, CancellationToken cancel, long firstLine = 0, long firstOffset = 0) : TextWriter
{
    private readonly StringBuilder _line = new();

    /// <summary>How many lines have been said, which is the number of the one being read.</summary>
    private long _number = firstLine;

    /// <summary>Where the line being read starts, and how far everything written reaches.</summary>
    private long _starts = firstOffset, _reached = firstOffset;

    /// <summary>Whether the last character written was a return, which a line feed after it joins.</summary>
    private bool _returned;

    public override Encoding Encoding => Encoding.Unicode;

    public override void Write(char value) => this.Write([value]);

    public override void Write(char[] buffer, int index, int count) => this.Write(buffer.AsSpan(index, count));

    public override void Write(string? value) => this.Write(value.AsSpan());

    public override void Write(ReadOnlySpan<char> buffer)
    {
        cancel.ThrowIfCancellationRequested();

        while (!buffer.IsEmpty)
        {
            if (_returned && buffer[0] == '\n')
            {
                // The line feed of a return's line end: the line was said at the return; the next one starts after this.
                _returned = false;
                _starts = ++_reached;
                buffer = buffer[1..];
                continue;
            }

            _returned = false;

            // A run of a line's own characters at a time, up to the line end that closes it.
            var end = buffer.IndexOfAny('\r', '\n');
            if (end < 0)
            {
                _line.Append(buffer);
                _reached += buffer.Length;
                return;
            }

            _line.Append(buffer[..end]);
            _reached += end + 1;
            _returned = buffer[end] == '\r';
            this.Said();
            _starts = _reached;
            buffer = buffer[(end + 1)..];
        }
    }

    /// <summary>Says the last line, which no line end closed — what is written after the last line end, nothing where that is nothing.</summary>
    public void Ended() => this.Said();

    /// <summary>Says the line read — not once reading is called off, which whoever is told a line may do from the line before.</summary>
    private void Said()
    {
        cancel.ThrowIfCancellationRequested();

        each(_number++, _starts, _line.ToString());
        _line.Clear();
    }
}
