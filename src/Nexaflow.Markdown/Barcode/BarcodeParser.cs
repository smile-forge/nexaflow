using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode.Stages;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Barcode;

/// <summary>
/// Reads the body of a <c>barcode</c> block into a tree. It is written as every code block is — a <c>key: value</c> field a
/// line, which <see cref="MatrixParser"/> reads — and its value is then spelled out a character at a time
/// (<see cref="SpellValue"/>), because a barcode prints its value back a character at a time and each character it prints has
/// to be able to say which one of the value it is.
/// </summary>
public static class BarcodeParser
{
    private static readonly AstPipeline Stages = new(new SpellValue());

    public static ContentNode Parse(string? source) => Stages.Run(MatrixParser.Parse(source));
}
