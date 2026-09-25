using System;
using System.Collections.Generic;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// Lays an <c>aztec</c> block out: its fields read into an <see cref="AztecBlock"/>, the payload encoded, and the
/// symbol laid from the middle out — the bullseye a scanner finds it by, the ring round it that says how big the
/// symbol is, and in a full-range symbol the reference grid that keeps a large one square — with the data layers
/// as its modules.
/// </summary>
internal sealed class AztecBuilder : MatrixBuilder<AztecSymbol>
{
    /// <summary>The bullseye: nine modules across in a compact symbol, thirteen in a full-range one.</summary>
    public const string Finder = "Finder";

    /// <summary>The ring round the bullseye: its orientation marks, and the mode message giving the layers and data size.</summary>
    public const string ModeMessage = "ModeMessage";

    /// <summary>A full-range symbol's grid: the lines through the middle, and again every sixteen modules out.</summary>
    public const string ReferenceGrid = "ReferenceGrid";

    private const int GridPitch = 16;

    internal AztecBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly)
        : base(reading, state, style, isReadOnly) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    internal static Laid Lay(string source, StyleFormat style, int at = 0) =>
        new AztecBuilder(ContentReading.Of(MatrixParser.Parse(source), at), EditState.For(source), style, isReadOnly: true).Lay();

    protected override Drawn? Encode(ContentPart tree, out (ContentPart Part, string Reason) wrong)
    {
        if (!AztecBlockReader.TryRead(tree, out var block, out wrong)) return null;
        if (!AztecEncoder.TryEncode(block!.Payload, block.Options, out var symbol, out var trouble)) return Refused(tree, trouble, out wrong);

        return new Drawn(symbol!, block.Settings);
    }

    protected override IReadOnlyList<Region> Regions(AztecSymbol symbol)
    {
        int middle = symbol.Size / 2;
        int core = AztecLayout.CoreRadius(symbol.Compact);

        return
        [
            new(Finder, (x, y) => Ring(x, y) < core),
            new(ModeMessage, (x, y) => Ring(x, y) == core),
            new(ReferenceGrid, (x, y) => !symbol.Compact && ((x - middle) % GridPitch == 0 || (y - middle) % GridPitch == 0)),
        ];

        // How many modules out from the middle a module is, square rather than round: the bullseye's rings are squares.
        int Ring(int x, int y) => Math.Max(Math.Abs(x - middle), Math.Abs(y - middle));
    }
}
