namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>A line standing on a surface's page (<see cref="MarkdownSurface.LinesShown"/>): its number, and where its rows stand.</summary>
/// <param name="Number">Which line of the document it is, counting from one.</param>
/// <param name="Top">Where its first row starts.</param>
/// <param name="Height">How tall its rows come to: one row, or as many as it wrapped onto.</param>
public readonly record struct ShownLine(int Number, double Top, double Height);
