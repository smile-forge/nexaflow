using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.PlainText;

/// <summary>
/// A stretch of plain text cut into its lines, and each line into its words and the spaces between them (<see cref="TextParser.InLines"/>)
/// — first of the stages text is worked over by, so every stage after it finds the words it looks into.
/// </summary>
public sealed class TextIntoLines : IAstStage
{
    public string Name => "text:lines";

    public ContentNode Run(ContentNode tree) => TextParser.InLines(tree);
}
