using System;
using System.Collections.Generic;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

/// <summary>
/// Lays a <c>datamatrix</c> block out: its fields read into a <see cref="DataMatrixBlock"/>, the payload encoded,
/// and the symbol laid as the border every data region wears — the solid L a scanner finds it by, and the
/// alternating clock track opposite that gives the module pitch — with the data inside as its modules.
///
/// <para>
/// A large symbol is several regions side by side and one above another, each with a border of its own, so the
/// borders between regions are finder and clock too.
/// </para>
/// </summary>
internal sealed class DataMatrixBuilder : MatrixBuilder<DataMatrixSymbol>
{
    /// <summary>The solid left column and bottom row of a region.</summary>
    public const string Finder = "Finder";

    /// <summary>The alternating top row and right column of a region.</summary>
    public const string Clock = "Clock";

    internal DataMatrixBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : base(reading, state, style, isReadOnly, nesting) { }

    protected override Drawn? Encode(ContentPart tree, out (ContentPart Part, string Reason) wrong)
    {
        if (!DataMatrixBlockReader.TryRead(tree, out var block, out wrong)) return null;
        if (!DataMatrixEncoder.TryEncode(block!.Payload, block.Options, out var symbol, out var trouble)) return Refused(tree, trouble, out wrong);

        return new Drawn(symbol!, block.Settings);
    }

    protected override IReadOnlyList<Region> Regions(DataMatrixSymbol symbol)
    {
        // A region is its data with a module of border on every side.
        int down = symbol.Size.RegionRows + 2;
        int across = symbol.Size.RegionColumns + 2;

        return
        [
            new(Finder, (x, y) => x % across == 0 || y % down == down - 1),
            new(Clock, (x, y) => y % down == 0 || x % across == across - 1),
        ];
    }
}
