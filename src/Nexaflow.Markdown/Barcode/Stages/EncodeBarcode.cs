using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Barcode.Stages;

/// <summary>
/// Reads a <c>barcode</c> block (<see cref="BarcodeBlockReader"/>), encodes its value (<see cref="BarcodeEncoder"/>) and says
/// what is drawn: the block becomes a <see cref="BarcodeBlockNode"/> holding how it is drawn, with the bars
/// (<see cref="BarcodeBarsNode"/>) and every run printed with them (<see cref="BarcodeRunNode"/>) hung under it, and the
/// <c>value:</c> line the bars encode says so (<see cref="BarcodeRoles.Encoded"/>).
///
/// <para>
/// A value is typed into where it is printed, so one that will not encode — or is not written yet — keeps a faint symbol of its
/// kind while the block is written in: the value to put right is under it. Anywhere else, and where nothing of the value is
/// printed, the only place to put it right is the block's source, so the value is what is at fault.
/// </para>
/// </summary>
public sealed class EncodeBarcode(bool writing) : IAstStage
{
    public string Name => "barcode:encode";

    public ContentNode Run(ContentNode tree)
    {
        if (!BarcodeBlockReader.TryRead(tree, out var block, out var field, out var wrong)) return MatrixBlockReader.Blamed(tree, wrong);

        var written = field!.Part(MatrixRoles.Value);
        IReadOnlyList<ContentNode> characters = written is null ? [] : [.. written.Children.Where(child => child.Kind == BarcodeKinds.Character)];
        var value = string.Concat(characters.Select(character => character.Text));

        // A value not yet written, where somebody is writing and it would be printed, is a hole under a faint symbol of its kind:
        // nothing is wrong yet, there is only something still to write.
        var holding = block!.DisplayValue && written?.Children.Any(child => child.Kind == Kinds.Hole) == true;

        BarcodePattern? pattern = null;
        string? refusal = null;

        if (holding) { }
        else if (value.Length == 0) refusal = "A barcode needs a value.";
        else if (BarcodeEncoder.TryEncode(block.Format, value, out var encoded, out var error)) pattern = encoded;
        else refusal = error;

        // Editing passes through values that do not encode on the way to one that does, so where the value is typed into under
        // the bars they stay on the page, faint and struck through. Anywhere else it is put right in the block's source.
        if (refusal is not null && (!writing || !block.DisplayValue || characters.Count == 0))
            return MatrixBlockReader.Blamed(tree, (characters.Count > 0 ? written! : field, refusal));

        var drawn = holding || refusal is not null ? Sample(block.Format) : pattern;

        // Several formats add a check digit or move a group outside the bars, so what is printed comes from the encoded pattern,
        // not the value — except where it will not encode, where there is nothing else. A publication keeps its caption line then.
        var caption = pattern is not null
            ? pattern.Caption
            : block.Format is BarcodeSymbology.Isbn or BarcodeSymbology.Issn or BarcodeSymbology.Ismn
                ? BarcodeTextLayout.CaptionFor(block.Format, value)
                : null;

        var printed = BarcodeTextLayout.Read(characters, pattern?.Text ?? value, pattern?.TextRuns ?? [], caption, drawn?.Width ?? 0);

        List<ContentNode> drawing = drawn is null ? [] : [new BarcodeBarsNode(drawn, pattern is null, AddOn(pattern), Main(pattern, drawn))];
        drawing.AddRange(printed);

        var encoding = AstRewrite.Each(tree, node => ReferenceEquals(node, field) ? node.As(BarcodeRoles.Encoded) : node);
        return new BarcodeBlockNode(encoding.With([.. encoding.Children, .. drawing]), block, refusal);
    }

    /// <summary>A symbol of the format's kind, standing in for a value that will not encode or is not written — or null where even that will not.</summary>
    private static BarcodePattern? Sample(BarcodeSymbology format) =>
        BarcodeEncoder.TryEncode(format, BarcodeEncoder.SampleValue(format), out var sample, out _) ? sample : null;

    /// <summary>The first module of the add-on, or <see cref="int.MaxValue"/> when there is none.</summary>
    private static int AddOn(BarcodePattern? pattern)
    {
        if (pattern is null) return int.MaxValue;

        int first = int.MaxValue;
        foreach (var run in pattern.TextRuns)
            if (run.Placement == BarcodeTextPlacement.Above && run.Modules > 0 && run.StartModule > 0)
                first = Math.Min(first, run.StartModule);

        return first;
    }

    /// <summary>Width of the main symbol in modules — everything before an add-on, or all of what is drawn if none.</summary>
    private static int Main(BarcodePattern? pattern, BarcodePattern drawn)
    {
        int addOn = AddOn(pattern);
        if (pattern is null || addOn == int.MaxValue) return drawn.Width;

        int end = 0;
        foreach (var (start, length) in pattern.InkRuns())
            if (start < addOn) end = Math.Max(end, start + length);

        return end > 0 ? end : drawn.Width;
    }
}
