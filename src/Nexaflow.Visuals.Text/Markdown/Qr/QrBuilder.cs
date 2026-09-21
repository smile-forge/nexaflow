using System.Collections.Generic;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// Lays a <c>qr</c> block out: its fields read into a <see cref="QrBlock"/>, the payload encoded, and the symbol
/// laid as the parts a scanner finds it by — a finder in three of its corners and the timing lines that run
/// between them — with everything else as its modules.
/// </summary>
internal sealed class QrBuilder : MatrixBuilder<QrMatrix>
{
    /// <summary>A seven-module square in a corner: top left, top right and bottom left.</summary>
    public const string Finder = "Finder";

    /// <summary>The alternating line along row six or column six, between two finders, that gives the module pitch.</summary>
    public const string Timing = "Timing";

    private const int FinderSize = 7;
    private const int TimingLine = 6;

    /// <summary>What a stand-in symbol says. Anything short enough for the smallest version will do.</summary>
    private const string Sample = "Nexaflow";

    internal QrBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly)
        : base(reading, state, style, isReadOnly) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    internal static Laid Lay(string source, StyleFormat style) =>
        new QrBuilder(ContentReading.Of(MatrixParser.Parse(source)), EditState.For(source), style, isReadOnly: true).Lay();

    public static Editing.ContentElement Element(string source, DiagramRenderOptions options) =>
        Host(source, options, static (r, s, f, o) => new QrBuilder(r, s, f, o));

    protected override Drawn? Encode(ContentNode tree, out string? trouble)
    {
        if (!QrBlockReader.TryRead(tree, out var block, out trouble)) return null;
        if (!QrEncoder.TryEncode(block!.Payload, block.ErrorCorrection, out var matrix, out trouble)) return null;

        return new Drawn(matrix!, block.Settings);
    }

    protected override Drawn StandIn(MatrixSettings settings) =>
        new(QrEncoder.Encode(Sample, QrErrorCorrection.Medium), settings);

    protected override IReadOnlyList<Region> Regions(QrMatrix symbol)
    {
        int far = symbol.Size - FinderSize;

        // A timing line runs between the separators that ring two finders, so it starts a module past one
        // finder's edge and stops a module short of the next.
        return
        [
            new(Finder, (x, y) => x < FinderSize && y < FinderSize),
            new(Finder, (x, y) => x >= far && y < FinderSize),
            new(Finder, (x, y) => x < FinderSize && y >= far),
            new(Timing, (x, y) => y == TimingLine && x > FinderSize && x < far - 1),
            new(Timing, (x, y) => x == TimingLine && y > FinderSize && y < far - 1),
        ];
    }
}
