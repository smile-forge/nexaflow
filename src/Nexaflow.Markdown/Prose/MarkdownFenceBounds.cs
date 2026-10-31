using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// Whether a fence still ends where it did once something is written inside what it holds (<see cref="IBodyBounds"/>): only a line
/// that would close it can end it early, so only the lines the change touched are read — a fence of five megabytes answers from the
/// line being typed on.
///
/// <para>
/// <strong>Only what can be vouched for.</strong> A fence of backticks or tildes is closed by a line of the same mark, at least as
/// many, indented by three spaces at most and followed by nothing but spaces — CommonMark's rule, and so the scan's. A fence written
/// in a quote or a list is held round the marks its container puts in front of each line: there a new line would need a mark of
/// its own, and a line of fence marks would close it from past the container's, so a change writing either is left to be read
/// again. Anything else a fence could be — a formula's <c>$$</c> — says nothing, and is read again.
/// </para>
/// </summary>
internal sealed class MarkdownFenceBounds : IBodyBounds
{
    public static MarkdownFenceBounds Instance { get; } = new();

    private MarkdownFenceBounds()
    {
    }

    public bool Holds(ContentNode holder, ContentNode body, string written, string lines)
    {
        if (holder.Part(Roles.Open) is not { } open || Mark(open.Text) is not var (mark, count)) return false;

        // Held round a container's marks: neither a line of its own nor anything that could be a fence's marks.
        if (!body.IsLeaf) return written.AsSpan().IndexOfAny('\n', '\r') < 0 && written.IndexOf(mark) < 0;

        foreach (var line in lines.Split('\n'))
            if (Closes(line.TrimEnd('\r'), mark, count)) return false;

        return true;
    }

    /// <summary>The mark a fence line opens with, and how many of it — or null where it is no fence of backticks or tildes.</summary>
    private static (char Mark, int Count)? Mark(string line)
    {
        var at = 0;
        while (at < line.Length && line[at] == ' ') at++;
        if (at >= line.Length || line[at] is not ('`' or '~')) return null;

        var count = 0;
        while (at + count < line.Length && line[at + count] == line[at]) count++;

        return (line[at], count);
    }

    /// <summary>Whether <paramref name="line"/> closes a fence opened with <paramref name="count"/> of <paramref name="mark"/>.</summary>
    private static bool Closes(string line, char mark, int count)
    {
        var at = 0;
        while (at < line.Length && line[at] == ' ') at++;
        if (at > 3) return false;

        var run = 0;
        while (at + run < line.Length && line[at + run] == mark) run++;

        return run >= count && line.AsSpan(at + run).Trim(" \t").IsEmpty;
    }
}
