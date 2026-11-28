using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Syntax;
using Nexaflow.Visuals.Text.Markdown.Code;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// Colours a stretch of code as its reading's syntax says (<see cref="CodeSyntax"/>): what the grammar calls each part of it, found in
/// all of the reading, so a string or a comment running in from the stretch before, or on past this one, is coloured as the one thing
/// it is.
///
/// <para>
/// Slow, so the engine finds it away from the thread that lays content out (<see cref="ISlowStage"/>) and keeps it by the version of
/// the reading and where the stretch stands in it. Until it lands, the stretch is coloured as it was last found, moved along by every
/// change since.
/// </para>
/// </summary>
/// <param name="syntax">The reading's syntax, at the version the stretch is of.</param>
/// <param name="at">Where the stretch starts among the reading's own characters.</param>
internal sealed class CodeHighlights(CodeSyntax syntax, int at) : ISlowStage
{
    public string Name => "code:highlights";

    public object? Basis { get; } = (syntax, at);

    public object? TakesTurnsWith => syntax.TakesTurnsWith;

    public object? Find(ContentNode tree) => syntax.Find(at, at + tree.Width);

    public ContentNode Apply(ContentNode tree, object found) => this.Coloured(tree, ((CodeFound)found).Spans);

    public ContentNode Meanwhile(ContentNode tree) => this.Coloured(tree, syntax.Known(at, at + tree.Width));

    /// <summary>
    /// <paramref name="tree"/> cut into lines, each into what <paramref name="spans"/>, standing among the reading's characters, call each
    /// part of it — a line said and coloured as one the reading was cut into before being that line again.
    /// </summary>
    private ContentNode Coloured(ContentNode tree, IReadOnlyList<HighlightSpan> spans) =>
        CodeParser.InLines(tree, [.. spans.Select(span => span with { Start = span.Start - at })], syntax.Lines);
}
