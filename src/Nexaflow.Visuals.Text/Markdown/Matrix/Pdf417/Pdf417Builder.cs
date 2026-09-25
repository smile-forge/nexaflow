using System;
using System.Collections.Generic;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

/// <summary>
/// Lays a <c>pdf417</c> block out: its fields read into a <see cref="Pdf417Block"/>, the payload encoded, and the
/// symbol laid as the columns every row of it is made of — the start pattern, the left row indicator, the data
/// codewords, the right row indicator and the stop pattern. A truncated symbol has no right indicator and a stop
/// one module wide.
///
/// <para>
/// The one code whose rows are drawn taller than its modules are wide, by the block's <c>rowHeight:</c>.
/// </para>
/// </summary>
internal sealed class Pdf417Builder : MatrixBuilder<Pdf417Symbol>
{
    public const string Start = "Start";

    /// <summary>The codeword at the left of every row that says which row it is, and how many there are.</summary>
    public const string LeftRowIndicator = "LeftRowIndicator";

    public const string Codewords = "Codewords";

    public const string RightRowIndicator = "RightRowIndicator";

    public const string Stop = "Stop";

    /// <summary>How many modules wide one codeword is — and the start pattern, and each row indicator.</summary>
    private const int Codeword = 17;

    internal Pdf417Builder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : base(reading, state, style, isReadOnly, nesting) { }

    protected override Drawn? Encode(ContentPart tree, out (ContentPart Part, string Reason) wrong)
    {
        if (!Pdf417BlockReader.TryRead(tree, out var block, out wrong)) return null;
        if (!Pdf417Encoder.TryEncode(block!.Payload, block.Options, out var symbol, out var trouble)) return Refused(tree, trouble, out wrong);

        return new Drawn(symbol!, block.Settings, block.RowHeight);
    }

    protected override IReadOnlyList<Region> Regions(Pdf417Symbol symbol)
    {
        int data = Codeword * 2;
        int right = data + Codeword * symbol.Columns;

        return
        [
            new(Start, (x, _) => x < Codeword),
            new(LeftRowIndicator, (x, _) => x < data),
            new(Codewords, (x, _) => x < right),
            new(RightRowIndicator, (x, _) => !symbol.Truncated && x < right + Codeword),
            new(Stop, (_, _) => true),
        ];
    }
}
