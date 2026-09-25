using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode.Stages;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Barcode;

/// <summary>
/// Reads the body of a <c>barcode</c> block into a tree. It is written as every code block is — a <c>key: value</c> field a
/// line, which <see cref="MatrixParser"/> reads — and its value is then spelled out a character at a time
/// (<see cref="SpellValue"/>), because a barcode prints its value back a character at a time and each character it prints has
/// to be able to say which one of the value it is. For a block somebody is writing in, a value not yet written is a hole,
/// which is a stage's to say (<see cref="Stages"/>).
/// </summary>
public static class BarcodeParser
{
    private static readonly SpellValue Spelling = new();

    public static ContentNode Parse(string? source) => Spelling.Run(MatrixParser.Parse(source));

    /// <summary>What a block is worked over by once parsed: for a block somebody is writing in, a hole where its value is still to be written.</summary>
    public static IReadOnlyList<IAstStage> Stages(bool holes) => holes ? [new HoldValue()] : [];
}