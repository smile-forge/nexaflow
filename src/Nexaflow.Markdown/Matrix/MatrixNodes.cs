using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Matrix;

/// <summary>
/// A 2D-code block as its stage leaves it: what a picture of the symbol its fields encode to needs, and nothing else — which
/// modules are dark, which of the parts the code is made of each belongs to, and how big and in what colours it is drawn. It
/// prints as the fields written.
/// </summary>
internal sealed class MatrixSymbolNode : ContentNode
{
    private readonly int[] _owners;

    private MatrixSymbolNode(ContentNode written, IModuleMatrix modules, MatrixSettings settings, double rowHeight,
                             IReadOnlyList<string> parts, int[] owners)
        : base(written)
    {
        this.Modules = modules;
        this.Settings = settings;
        this.RowHeight = rowHeight;
        this.Parts = parts;
        _owners = owners;
    }

    /// <summary>
    /// The block <paramref name="written"/> as the symbol <paramref name="modules"/>, each module belonging to the first of
    /// <paramref name="regions"/> that holds it.
    /// </summary>
    internal static MatrixSymbolNode Of(ContentNode written, IModuleMatrix modules, MatrixSettings settings, double rowHeight,
                                        IReadOnlyList<MatrixRegion> regions)
    {
        var owners = new int[modules.Width * modules.Height];

        for (var y = 0; y < modules.Height; y++)
            for (var x = 0; x < modules.Width; x++)
            {
                var held = regions.Count;
                for (var r = 0; r < regions.Count; r++)
                    if (regions[r].Holds(x, y)) { held = r; break; }
                owners[y * modules.Width + x] = held;
            }

        return new MatrixSymbolNode(written, modules, settings, rowHeight, [.. regions.Select(region => region.Kind)], owners);
    }

    /// <summary>Which modules are dark.</summary>
    public IModuleMatrix Modules { get; }

    /// <summary>How it is drawn — cell size, quiet zone, colours.</summary>
    public MatrixSettings Settings { get; }

    /// <summary>
    /// A module's height as a multiple of its width: one for a true matrix, more for a stacked code whose rows are drawn taller
    /// than they are wide.
    /// </summary>
    public double RowHeight { get; }

    /// <summary>The parts the symbol is made of — a QR code's finders, an Aztec code's bullseye — in the order they claim modules.</summary>
    public IReadOnlyList<string> Parts { get; }

    /// <summary>Which of <see cref="Parts"/> the module at (<paramref name="x"/>, <paramref name="y"/>) is in, or past the last where it is in none.</summary>
    public int Owner(int x, int y) => _owners[y * this.Modules.Width + x];

    protected override ContentNode Reshaped(ContentNode shape) =>
        new MatrixSymbolNode(shape, this.Modules, this.Settings, this.RowHeight, this.Parts, _owners);
}

/// <summary>One of the parts a symbol is made of: what it is called, and which modules are its.</summary>
internal readonly record struct MatrixRegion(string Kind, Func<int, int, bool> Holds);
