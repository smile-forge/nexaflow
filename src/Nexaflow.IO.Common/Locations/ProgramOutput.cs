using System.Text;

namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// What a program run on a location's host writes, as its backend receives it: chunks of stdout and stderr cut anywhere —
/// inside a line, inside a character — decoded as UTF-8 and reported a line at a time as each line ends, a line's
/// <c>\r</c> left off. What every backend's <see cref="ILocationShell.RunProgramAsync"/> gathers its output with, so each
/// reports the same lines. Fed from one reader at a time.
/// </summary>
public sealed class ProgramOutput(IProgress<string>? lines)
{
    private readonly WrittenText _output = new();
    private readonly WrittenText _errors = new();

    /// <summary>Takes in the next chunk of stdout, or of stderr when <paramref name="isError"/>.</summary>
    public void Add(bool isError, ReadOnlySpan<byte> chunk) => (isError ? _errors : _output).Add(chunk, lines);

    /// <summary>How the program ended, with all it wrote; a last line it did not end is reported now.</summary>
    public ProgramExit Finish(int exitCode) => new(exitCode, _output.Finish(lines), _errors.Finish(lines));

    /// <summary>One of the program's streams, decoded so far, and where its unreported line starts.</summary>
    private sealed class WrittenText
    {
        private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
        private readonly StringBuilder _text = new();
        private int _lineStart;

        public void Add(ReadOnlySpan<byte> chunk, IProgress<string>? lines)
        {
            Decode(chunk, flush: false);
            ReportEnded(lines);
        }

        public string Finish(IProgress<string>? lines)
        {
            Decode([], flush: true);
            ReportEnded(lines);
            if (_lineStart < _text.Length) lines?.Report(Line(_text.Length));
            _lineStart = _text.Length;
            return _text.ToString();
        }

        private void Decode(ReadOnlySpan<byte> chunk, bool flush)
        {
            Span<char> decoded = new char[_decoder.GetCharCount(chunk, flush)];
            _decoder.GetChars(chunk, decoded, flush);
            _text.Append(decoded);
        }

        private void ReportEnded(IProgress<string>? lines)
        {
            for (int at = _lineStart; at < _text.Length; at++)
            {
                if (_text[at] != '\n') continue;
                lines?.Report(Line(at));
                _lineStart = at + 1;
            }
        }

        private string Line(int end) => _text.ToString(_lineStart, end - _lineStart).TrimEnd('\r');
    }
}
