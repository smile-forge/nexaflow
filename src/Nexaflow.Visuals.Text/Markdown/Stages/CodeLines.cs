using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Code;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Markdown.Code;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// Cuts a stretch of code at its line ends, where nothing has coloured it into lines already (<see cref="CodeHighlights"/>): each line a
/// part of the stretch's own (<see cref="CodeKinds.Line"/>) — the block a keystroke lays again, every other line that reads as it did set
/// down as it was laid — holding its characters as written and the line end closing it (<see cref="CodeKinds.LineEnd"/>).
///
/// <para>
/// A stage, because where one line stops and the next starts is a fact about the characters that the builder would otherwise
/// have to find again by looking at them — and a line found by what it says is found in the wrong place wherever the same line
/// is written twice.
/// </para>
/// </summary>
public sealed class CodeLines : IAstStage
{
    public string Name => "code:lines";

    public ContentNode Run(ContentNode tree) => CodeParser.InLines(tree, []);
}

