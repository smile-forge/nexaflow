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
internal sealed class ContentLineSplitter(Action<long, long, string> each, CancellationToken cancel) : TextWriter
{
    private readonly StringBuilder _line = new();

    /// <summary>How many lines have been said, which is the number of the one being read.</summary>
    private long _number;

    /// <summary>Where the line being read starts, and how far everything written reaches.</summary>
    private long _starts, _reached;

    /// <summary>Whether the last character written was a return, which a line feed after it joins.</summary>
    private bool _returned;

    public override Encoding Encoding => Encoding.Unicode;

    public override void Write(char value) => this.Write([value]);

    public override void Write(char[] buffer, int index, int count) => this.Write(buffer.AsSpan(index, count));

    public override void Write(string? value) => this.Write(value.AsSpan());

    public override void Write(ReadOnlySpan<char> buffer)
    {
        cancel.ThrowIfCancellationRequested();

        foreach (var character in buffer)
        {
            _reached++;

            if (_returned && character == '\n')
            {
                // The line feed of a return's line end: the line was said at the return; the next one starts after this.
                _returned = false;
                _starts = _reached;
                continue;
            }

            _returned = character == '\r';

            if (character is '\n' or '\r')
            {
                this.Said();
                _starts = _reached;
                continue;
            }

            _line.Append(character);
        }
    }

    /// <summary>Says the last line, which no line end closed — what is written after the last line end, nothing where that is nothing.</summary>
    public void Ended() => this.Said();

    private void Said()
    {
        each(_number++, _starts, _line.ToString());
        _line.Clear();
    }
}
