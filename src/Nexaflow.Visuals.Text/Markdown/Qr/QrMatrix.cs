namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// A finished QR symbol: the square grid of dark/light modules plus the choices that produced it.
/// Immutable and free of any drawing concern, so the same matrix serves a WPF surface, an export
/// and a test that reads the modules back.
/// </summary>
public sealed class QrMatrix : Matrix.IModuleMatrix
{
    private readonly bool[] _modules;

    internal QrMatrix(int version, QrErrorCorrection ecl, int mask, bool[] modules)
    {
        Version         = version;
        Size            = version * 4 + 17;
        ErrorCorrection = ecl;
        Mask            = mask;
        _modules        = modules;
    }

    /// <summary>1ΓÇô40. Each step up adds four modules to the side.</summary>
    public int Version { get; }

    /// <summary>Modules per side, <c>4 ├ù Version + 17</c>. Excludes the quiet zone, which is the
    /// renderer's margin rather than part of the symbol.</summary>
    public int Size { get; }

    public QrErrorCorrection ErrorCorrection { get; }

    /// <summary>A QR symbol is square, so both are <see cref="Size"/>.</summary>
    public int Width => Size;
    public int Height => Size;

    /// <summary>The mask pattern (0ΓÇô7) chosen for this symbol by the penalty rules.</summary>
    public int Mask { get; }

    /// <summary>True where the module is dark. Origin is the top-left corner.</summary>
    public bool this[int x, int y] => _modules[y * Size + x];
}
