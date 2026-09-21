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

    private const string Sample = "Nexaflow";

    private DataMatrixBuilder(ContentReading reading, MarkdownPalette palette, double pixelsPerDip)
        : base(reading, palette, pixelsPerDip) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    public static Laid Build(string source, MarkdownPalette palette, double pixelsPerDip) =>
        new DataMatrixBuilder(ContentReading.Of(MatrixParser.Parse(source)), palette, pixelsPerDip).Lay();

    public static Editing.ContentElement Element(string source, DiagramRenderOptions options) =>
        Host(source, options, Build);

    protected override Drawn? Encode(ContentNode tree, out string? trouble)
    {
        if (!DataMatrixBlockReader.TryRead(tree, out var block, out trouble)) return null;
        if (!DataMatrixEncoder.TryEncode(block!.Payload, block.Options, out var symbol, out trouble)) return null;

        return new Drawn(symbol!, block.Settings);
    }

    protected override Drawn StandIn(MatrixSettings settings) =>
        DataMatrixEncoder.TryEncode(Sample, DataMatrixOptions.Default, out var symbol, out string? error)
            ? new Drawn(symbol!, settings)
            : throw new InvalidOperationException(error);

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
