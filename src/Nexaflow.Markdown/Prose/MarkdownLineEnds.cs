using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The line endings in a run of words, as anything showing the words asks about them: an ending inside the words is where a
/// line breaks or reflows, and the one closing the last line has no line after it to reflow into.
/// </summary>
public static class MarkdownLineEnds
{
    /// <summary>
    /// Whether a line ending is the last thing its words hold — closing them rather than breaking them, so nothing is drawn or
    /// written for it: it is the last piece of every run of words it is inside, up to the block holding them.
    /// </summary>
    public static bool Closes(ContentPart part)
    {
        for (var at = part; at.Parent is { } holder; at = holder)
        {
            if (holder.Children.LastOrDefault(child => !child.Derived && child.Length > 0) != at) return false;
            if (holder.Kind is not (Kinds.Words or MarkdownKinds.Emphasis or MarkdownKinds.Strong or MarkdownKinds.Strike
                                    or MarkdownKinds.Mark or MarkdownKinds.Insert or Kinds.Link))
                return true;
        }

        return true;
    }
}
