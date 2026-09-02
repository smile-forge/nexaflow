namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>Where the human-readable text sits under the bars.</summary>
public enum BarcodeTextAlign { Left, Center, Right }

/// <summary>
/// One parsed <c>barcode</c> block: what to encode, and how to draw it.
///
/// <para>
/// The <see cref="Value"/> is kept as written rather than as encoded, because it is the thing the reader
/// edits in place. What goes <em>under</em> the bars is the encoder's business — several of these
/// formats add a check digit, and the label on a real package shows it.
/// </para>
/// </summary>
public sealed class BarcodeBlock
{
    public required BarcodeSymbology Format { get; init; }

    /// <summary>The value as the author wrote it, and as they edit it.</summary>
    public required string Value { get; init; }

    /// <summary>Where in the block source the value sits, so an edit can be spliced back into it.</summary>
    public int ValueStart { get; init; }

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

    /// <summary>The same block with a different value — what an in-place edit produces.</summary>
    public BarcodeBlock With(string value) => new()
    {
        Format       = Format,
        Value        = value,
        ValueStart   = ValueStart,
        BarWidth     = BarWidth,
        BarHeight    = BarHeight,
        DisplayValue = DisplayValue,
        FontSize     = FontSize,
        TextAlign    = TextAlign,
        LineColor    = LineColor,
        Background   = Background,
        Margin       = Margin,
    };

    /// <summary>
    /// The same block with the value's offset rebased onto a larger source — how a parser handed only a
    /// fence's content reports a position an editing host can splice against.
    /// </summary>
    public BarcodeBlock At(int valueStart) => new()
    {
        Format       = Format,
        Value        = Value,
        ValueStart   = valueStart,
        BarWidth     = BarWidth,
        BarHeight    = BarHeight,
        DisplayValue = DisplayValue,
        FontSize     = FontSize,
        TextAlign    = TextAlign,
        LineColor    = LineColor,
        Background   = Background,
        Margin       = Margin,
    };
}
