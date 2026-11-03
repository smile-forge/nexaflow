namespace Nexaflow.Markdown.Lines;

/// <summary>What a piece of content read and laid line by line is — code, text shown as typed — whatever language it is written in.</summary>
public static class LineKinds
{
    /// <summary>Where one line ends and the next begins: a line feed, and the return before it where there is one.</summary>
    public const string LineEnd = "line-end";

    /// <summary>
    /// Hung on a stretch read a stretch at a time that the next stretch carries on from: the line after its last line end is the next
    /// stretch's first, so it is not laid as a line of this one.
    /// </summary>
    public const string CarriedOn = "carried-on";
}
