namespace Nexaflow.Visuals.Text.Markdown.Matrix;

/// <summary>
/// A finished two-dimensional symbol: a rectangular grid of dark and light modules.
///
/// <para>
/// Every matrix symbology reduces to this — QR, Data Matrix, Aztec, and the stacked PDF417 once its
/// rows are laid out — so one renderer draws all of them and never learns what a finder pattern is.
/// Rectangular rather than square because Data Matrix has rectangular sizes; a square symbol is the
/// case where the two are equal.
/// </para>
/// <para>
/// Free of any drawing concern, so the same matrix serves a WPF surface, an export and a test that
/// reads the modules back.
/// </para>
/// </summary>
public interface IModuleMatrix
{
    /// <summary>Modules across. Excludes the quiet zone, which is the renderer's margin rather than part of the symbol.</summary>
    int Width { get; }

    /// <summary>Modules down.</summary>
    int Height { get; }

    /// <summary>True where the module is dark. Origin is the top-left corner.</summary>
    bool this[int x, int y] { get; }
}
