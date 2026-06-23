namespace Nexaflow.Syntax;

/// <summary>A coloured range produced by tree-sitter: a char offset/length (UTF-16, matching editor
/// document offsets) and the highlight capture name (e.g. <c>keyword</c>, <c>string</c>, <c>comment</c>).</summary>
public readonly record struct HighlightSpan(int Start, int Length, string Capture);

/// <summary>A collapsible range (char offsets, UTF-16) for editor code folding — a multi-line block-like
/// node from the parse tree.</summary>
public readonly record struct FoldRange(int Start, int End);
