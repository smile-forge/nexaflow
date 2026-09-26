using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Barcode;

/// <summary>Where the human-readable text sits under the bars.</summary>
public enum BarcodeTextAlign { Left, Center, Right }

/// <summary>
/// How one <c>barcode</c> block is drawn, as <see cref="BarcodeBlockReader"/> read it: which format, and the settings.
///
/// <para>
/// The value is not here. It is the characters written, which the reader edits in place, and what is printed from it is the
/// stage's to say (<see cref="Stages.EncodeBarcode"/>) — several of these formats add a check digit, and the label on a real
/// package shows it.
/// </para>
/// </summary>
public sealed class BarcodeBlock
{
    public required BarcodeSymbology Format { get; init; }

    /// <summary>How wide one module is drawn, in device-independent pixels.</summary>
    public double BarWidth { get; init; } = DefaultBarWidth;

    /// <summary>How tall the bars are drawn, excluding any text beneath them.</summary>
    public double BarHeight { get; init; } = DefaultBarHeight;

    /// <summary>Whether the value is printed under the bars.</summary>
    public bool DisplayValue { get; init; } = true;

    public double FontSize { get; init; } = DefaultFontSize;

    public BarcodeTextAlign TextAlign { get; init; } = BarcodeTextAlign.Center;

    /// <summary>Bar colour, or null to take the palette's.</summary>
    public HexColor? LineColor { get; init; }

    /// <summary>Background colour, or null to take the palette's.</summary>
    public HexColor? Background { get; init; }

    /// <summary>The quiet zone drawn around the symbol, in pixels.</summary>
    public double Margin { get; init; } = DefaultMargin;

    // The defaults are JsBarcode's. The option names in the block syntax are that library's API verbatim,
    // so an author arriving from it will expect the same picture from the same settings.
    public const double DefaultBarWidth  = 2;
    public const double DefaultBarHeight = 100;
    public const double DefaultFontSize  = 20;
    public const double DefaultMargin    = 10;

    // A module under half a pixel cannot be drawn, and one over 20 makes a symbol wider than any page.
    public const double MinBarWidth  = 0.5,  MaxBarWidth  = 20;
    public const double MinBarHeight = 4,    MaxBarHeight = 1000;
    public const double MinFontSize  = 4,    MaxFontSize  = 200;
    public const double MaxMargin    = 200;
}
