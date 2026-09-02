namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>
/// The barcode symbologies a <c>barcode</c> block can ask for — the <c>format:</c> setting.
///
/// <para>
/// They are not variations on one another. Each fixes its own alphabet, its own width rules and its own
/// check digit, and the differences are why a retailer's scanner reads one and not the next. The names
/// match the <see href="https://markdown.org/tools/diagrams/barcode/">block syntax</see>.
/// </para>
/// </summary>
public enum BarcodeSymbology
{
    /// <summary>Any ASCII, switching between subsets to keep the symbol short. The general-purpose choice.</summary>
    Code128,

    /// <summary>Code 128 pinned to subset A — upper case, digits and control characters.</summary>
    Code128A,

    /// <summary>Code 128 pinned to subset B — the printable ASCII range, both cases.</summary>
    Code128B,

    /// <summary>Code 128 pinned to subset C — digits only, two per symbol, so an even count.</summary>
    Code128C,

    /// <summary>Retail article number, 12 digits plus a computed check digit.</summary>
    Ean13,

    /// <summary>The short article number, 7 digits plus a computed check digit.</summary>
    Ean8,

    /// <summary>Five-digit add-on, printed beside an EAN to carry a price.</summary>
    Ean5,

    /// <summary>Two-digit add-on, for an issue number.</summary>
    Ean2,

    /// <summary>North American retail code, 11 digits plus a computed check digit.</summary>
    Upc,

    /// <summary>The zero-suppressed UPC for small packages: 6 digits and a check digit.</summary>
    UpcE,

    /// <summary>Upper case, digits and <c>- . $ / + %</c> and space. Widely readable, not dense.</summary>
    Code39,

    /// <summary>Interleaved 2 of 5 — digits in pairs, so an even count.</summary>
    Itf,

    /// <summary>The shipping-container form of ITF: 13 digits plus a computed check digit.</summary>
    Itf14,

    /// <summary>Modified Plessey, digits, no check digit.</summary>
    Msi,

    /// <summary>MSI with one mod-10 check digit.</summary>
    Msi10,

    /// <summary>MSI with one mod-11 check digit.</summary>
    Msi11,

    /// <summary>MSI with two mod-10 check digits.</summary>
    Msi1010,

    /// <summary>MSI with a mod-11 check digit followed by a mod-10.</summary>
    Msi1110,

    /// <summary>Pharmacode — a whole number from 3 to 131070, read right to left.</summary>
    Pharmacode,

    /// <summary>Codabar — digits and <c>- $ : / . +</c>, wrapped in a start/stop letter A–D.</summary>
    Codabar,
}
