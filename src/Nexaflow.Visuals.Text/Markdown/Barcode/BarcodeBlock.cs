using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode;
using Nexaflow.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>Where the human-readable text sits under the bars.</summary>
public enum BarcodeTextAlign { Left, Center, Right }

/// <summary>
/// One <c>barcode</c> block as <see cref="BarcodeBlockReader"/> read it: what to encode, and how to draw it.
///
/// <para>
/// The value is kept as the piece of the tree it was written as rather than as encoded, because it is the thing the reader edits
/// in place. What goes <em>under</em> the bars is the encoder's business — several of these formats add a check digit, and the
/// label on a real package shows it.
/// </para>
/// </summary>
public sealed class BarcodeBlock
{
    public required BarcodeSymbology Format { get; init; }

    /// <summary>The <c>value:</c> line the value is written on.</summary>
    public required ContentPart Field { get; init; }

    /// <summary>The value as it is written, one piece per character — or null where nothing follows the colon.</summary>
    public ContentPart? Written => Field.Part(MatrixRoles.Value);

    /// <summary>The value, as the author wrote it and as they edit it.</summary>
    public string Value => string.Concat(Characters.Select(character => character.Text));

    /// <summary>Each character of the value, in order: what a character printed from it stands for.</summary>
    public IReadOnlyList<ContentPart> Characters =>
        Written is { } written ? [.. written.Children.Where(part => part.Kind == BarcodeKinds.Character)] : [];

    /// <summary>The hole standing where a value not yet written goes, where the block is being written in — or null.</summary>
    public ContentPart? Hole => Written?.Children.FirstOrDefault(part => part.Kind == Kinds.Hole);

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
