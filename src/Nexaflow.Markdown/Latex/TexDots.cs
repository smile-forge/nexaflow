namespace Nexaflow.Markdown.Latex;

/// <summary>
/// A row of dots set across a table's columns in place of entries left unwritten — <c>\hdotsfor[spacing]{n}</c>.
/// </summary>
/// <param name="Columns">How many columns it stands across.</param>
/// <param name="Spacing">How far apart the dots are, against the usual.</param>
public sealed record TexDots(int Columns, double Spacing);
