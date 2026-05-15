namespace Nexaflow.Features.Logs.Parsing;

public static class LogParserRegistry
{
    private static readonly List<ILogParser> Parsers =
    [
        new JsonLogParser(),
        new RawTextLogParser(),
    ];

    /// <summary>
    /// Selects the highest-confidence parser for the given file header and first line.
    /// Always returns at least the <see cref="RawTextLogParser"/> fallback.
    /// </summary>
    public static ILogParser SelectParser(ReadOnlySpan<byte> headerBytes, string firstLine)
    {
        ILogParser best       = Parsers[^1]; // RawText fallback
        float      bestScore  = 0f;

        foreach (var parser in Parsers)
        {
            var score = parser.Confidence(headerBytes, firstLine);
            if (score > bestScore) { bestScore = score; best = parser; }
        }

        return best;
    }
}
