namespace Nexaflow.Markdown.Layout;

/// <summary>
/// A line a builder laid (<see cref="Laid.Lines"/>): where it starts in the content, and where its rows start and how tall they come to,
/// from the top of what was laid.
/// </summary>
/// <param name="Start">Where the line starts, in the offsets of the content as it read when it was laid.</param>
/// <param name="Top">Where its first row starts, from the top of what was laid.</param>
/// <param name="Height">How tall its rows come to: one row, or as many as it wrapped onto.</param>
public readonly record struct LaidLine(int Start, double Top, double Height);
