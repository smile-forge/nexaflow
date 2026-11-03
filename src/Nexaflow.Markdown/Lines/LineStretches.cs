using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Lines;

/// <summary>
/// Content read line by line — code, text shown as typed — read a stretch at a time (<see cref="ContentStretch"/>): once the stretch's
/// length is behind, it stops after the next line end that a line with something on it follows, carrying nothing, since such content
/// has nothing open where a line starts. A stretch that stops says the next carries on from it (<see cref="LineKinds.CarriedOn"/>); a
/// line, or a run of empty lines, longer than the window asks for a longer one.
/// </summary>
public static class LineStretches
{
    /// <summary><paramref name="stretch"/>, read by <paramref name="parse"/> as far as it stops — null for nothing read, where it needs more.</summary>
    public static ContentParse Read(ContentStretch stretch, Func<string?, ContentNode> parse)
    {
        var window = stretch.Window;
        var end = stretch.SoftMaxLength < window.Length ? window.IndexOf('\n', Math.Max(stretch.SoftMaxLength - 1, 0)) : -1;

        // A stretch stops before a line with something written on it, so the next starts where a caret stands before words, as it does
        // in the content read whole — an empty line has nothing for a caret to stand against.
        while (end >= 0 && end + 1 < window.Length && window[end + 1] is '\n' or '\r') end = window.IndexOf('\n', end + 1);

        if (end < 0 || end + 1 == window.Length)
            return stretch.Ends ? ContentParse.Of(parse(window)) : ContentParse.Of(parse(null)) with { NeedsMore = true };

        var stop = end + 1;
        var read = parse(window[..stop]);

        return ContentParse.Of(read.With([.. read.Children, ContentNode.Holding(LineKinds.CarriedOn, Roles.Derived, true)])) with { Stopped = stop };
    }
}
