using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode.Stages;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Barcode;

/// <summary>
/// Reads the body of a <c>barcode</c> block into a tree. It is written as every code block is — a <c>key: value</c> field a
/// line, which <see cref="MatrixParser"/> reads — and its value is then spelled out a character at a time
/// (<see cref="SpellValue"/>), because a barcode prints its value back a character at a time and each character it prints has
/// to be able to say which one of the value it is. For a block somebody is writing in, a value not yet written is a hole
/// (<see cref="HoldValue"/>).
/// </summary>
public static class BarcodeParser
{
    private static readonly AstPipeline Reading = new(new SpellValue());

    private static readonly AstPipeline Writing = Reading.Then(new HoldValue());

    /// <param name="holes">Whether somebody is writing in the block, so a value not yet written wants a hole to be typed into.</param>
    public static ContentNode Parse(string? source, bool holes = false) => (holes ? Writing : Reading).Run(MatrixParser.Parse(source));
}
