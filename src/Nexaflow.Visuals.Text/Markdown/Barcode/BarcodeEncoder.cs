using Nexaflow.Visuals.Text.Markdown.Barcode.Encoders;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>
/// Turns a value into bars, for whichever symbology was asked for.
///
/// <para>
/// The one place that knows the whole family. Each symbology's rules live in its own encoder — they have
/// almost nothing in common beyond producing modules — and this only routes, so adding a format is a new
/// encoder and a line here.
/// </para>
/// <para>
/// WPF-free on purpose: a barcode is a row of modules long before it is a picture, and keeping the
/// encoding testable without a UI thread is what lets every symbology be read back and checked.
/// </para>
/// </summary>
public static class BarcodeEncoder
{
    /// <summary>
    /// Encodes <paramref name="value"/>, or explains why it cannot be — which for a barcode is nearly
    /// always the same thing: this format does not carry those characters, or not that many of them.
    /// </summary>
    public static bool TryEncode(BarcodeSymbology symbology, string value,
                                 out BarcodePattern? pattern, out string? error)
    {
        pattern = null;
        value ??= string.Empty;

        bool[]? modules;
        string? text = value;

        bool ok = symbology switch
        {
            BarcodeSymbology.Code128  => Code128Encoder.TryEncode(value, Code128Encoder.Subset.Auto, out modules, out error),
            BarcodeSymbology.Code128A => Code128Encoder.TryEncode(value, Code128Encoder.Subset.A, out modules, out error),
            BarcodeSymbology.Code128B => Code128Encoder.TryEncode(value, Code128Encoder.Subset.B, out modules, out error),
            BarcodeSymbology.Code128C => Code128Encoder.TryEncode(value, Code128Encoder.Subset.C, out modules, out error),

            BarcodeSymbology.Ean13 => EanEncoder.TryEncodeEan13(value, out modules, out text, out error),
            BarcodeSymbology.Ean8  => EanEncoder.TryEncodeEan8(value, out modules, out text, out error),
            BarcodeSymbology.Ean5  => EanEncoder.TryEncodeEan5(value, out modules, out text, out error),
            BarcodeSymbology.Ean2  => EanEncoder.TryEncodeEan2(value, out modules, out text, out error),
            BarcodeSymbology.Upc   => EanEncoder.TryEncodeUpc(value, out modules, out text, out error),
            BarcodeSymbology.UpcE  => EanEncoder.TryEncodeUpcE(value, out modules, out text, out error),

            BarcodeSymbology.Code39 => WidthEncoders.TryEncodeCode39(value, out modules, out text, out error),
            BarcodeSymbology.Itf    => WidthEncoders.TryEncodeItf(value, out modules, out text, out error),
            BarcodeSymbology.Itf14  => WidthEncoders.TryEncodeItf14(value, out modules, out text, out error),

            BarcodeSymbology.Msi     => WidthEncoders.TryEncodeMsi(value, false, false, false, out modules, out text, out error),
            BarcodeSymbology.Msi10   => WidthEncoders.TryEncodeMsi(value, true,  false, false, out modules, out text, out error),
            BarcodeSymbology.Msi11   => WidthEncoders.TryEncodeMsi(value, false, true,  false, out modules, out text, out error),
            BarcodeSymbology.Msi1010 => WidthEncoders.TryEncodeMsi(value, true,  false, true,  out modules, out text, out error),
            BarcodeSymbology.Msi1110 => WidthEncoders.TryEncodeMsi(value, false, true,  true,  out modules, out text, out error),

            BarcodeSymbology.Isbn or BarcodeSymbology.Issn or BarcodeSymbology.Ismn
                => PublicationEncoder.TryEncode(symbology, value, out modules, out text, out error),

            BarcodeSymbology.Pharmacode => WidthEncoders.TryEncodePharmacode(value, out modules, out text, out error),
            _                           => WidthEncoders.TryEncodeCodabar(value, out modules, out text, out error),
        };

        if (!ok) return false;

        var (runs, guards, caption) =
            BarcodeTextLayout.Describe(symbology, value, text ?? value, modules!.Length);

        pattern = new BarcodePattern(symbology, modules!, text ?? value)
        {
            TextRuns = runs,
            Guards   = guards,
            Caption  = caption,
            Symbol   = BarcodeTextLayout.Read(value, text ?? value, runs, caption),
        };
        error   = null;
        return true;
    }

    /// <summary>Parses a <c>format:</c> name, case-insensitively. The spec's names, plus the obvious spellings.</summary>
    public static bool TryParseSymbology(string name, out BarcodeSymbology symbology)
    {
        symbology = BarcodeSymbology.Code128;

        switch (name.Trim().ToUpperInvariant().Replace("-", string.Empty).Replace("_", string.Empty))
        {
            case "CODE128":   symbology = BarcodeSymbology.Code128;    return true;
            case "CODE128A":  symbology = BarcodeSymbology.Code128A;   return true;
            case "CODE128B":  symbology = BarcodeSymbology.Code128B;   return true;
            case "CODE128C":  symbology = BarcodeSymbology.Code128C;   return true;
            case "EAN13":     symbology = BarcodeSymbology.Ean13;      return true;
            case "EAN8":      symbology = BarcodeSymbology.Ean8;       return true;
            case "EAN5":      symbology = BarcodeSymbology.Ean5;       return true;
            case "EAN2":      symbology = BarcodeSymbology.Ean2;       return true;
            case "UPC":
            case "UPCA":      symbology = BarcodeSymbology.Upc;        return true;
            case "UPCE":      symbology = BarcodeSymbology.UpcE;       return true;
            case "CODE39":    symbology = BarcodeSymbology.Code39;     return true;
            case "ITF":       symbology = BarcodeSymbology.Itf;        return true;
            case "ITF14":     symbology = BarcodeSymbology.Itf14;      return true;
            case "MSI":       symbology = BarcodeSymbology.Msi;        return true;
            case "MSI10":     symbology = BarcodeSymbology.Msi10;      return true;
            case "MSI11":     symbology = BarcodeSymbology.Msi11;      return true;
            case "MSI1010":   symbology = BarcodeSymbology.Msi1010;    return true;
            case "MSI1110":   symbology = BarcodeSymbology.Msi1110;    return true;
            case "PHARMACODE": symbology = BarcodeSymbology.Pharmacode; return true;
            case "CODABAR":   symbology = BarcodeSymbology.Codabar;    return true;
            case "ISBN":      symbology = BarcodeSymbology.Isbn;       return true;
            case "ISSN":      symbology = BarcodeSymbology.Issn;       return true;
            case "ISMN":      symbology = BarcodeSymbology.Ismn;       return true;
            default:          return false;
        }
    }

    /// <summary>Every format name a block may use, in the order the documentation lists them.</summary>
    public static readonly string[] FormatNames =
    [
        "CODE128", "CODE128A", "CODE128B", "CODE128C",
        "EAN13", "EAN8", "EAN5", "EAN2", "UPC", "UPCE",
        "CODE39", "ITF", "ITF14",
        "MSI", "MSI10", "MSI11", "MSI1010", "MSI1110",
        "pharmacode", "codabar",
        "ISBN", "ISSN", "ISMN",
    ];


    /// <summary>
    /// A value this format is guaranteed to carry.
    /// <para>
    /// Used to draw a stand-in when the real value will not encode: a barcode-shaped absence reads as "this
    /// is a barcode, and it is wrong", where an empty gap reads as a rendering fault. Each is the example
    /// the block syntax documents, so what is drawn is the shape the format really takes.
    /// </para>
    /// </summary>
    public static string SampleValue(BarcodeSymbology symbology) => symbology switch
    {
    BarcodeSymbology.Code128C => "12345678",
    BarcodeSymbology.Ean13 => "5901234123457",
    BarcodeSymbology.Ean8 => "96385074",
    BarcodeSymbology.Ean5 => "12345",
    BarcodeSymbology.Ean2 => "12",
    BarcodeSymbology.Upc => "036000291452",
    BarcodeSymbology.UpcE => "01234565",
    BarcodeSymbology.Itf => "12345678",
    BarcodeSymbology.Itf14 => "1234567890123",
    BarcodeSymbology.Msi or BarcodeSymbology.Msi10 or BarcodeSymbology.Msi11
    or BarcodeSymbology.Msi1010 or BarcodeSymbology.Msi1110 => "1234567",
    BarcodeSymbology.Pharmacode => "1234",
    BarcodeSymbology.Codabar => "A12345B",
    BarcodeSymbology.Isbn => "978-1-56581-231-4",
    BarcodeSymbology.Issn => "0311-175X",
    BarcodeSymbology.Ismn => "979-0-2605-3211-3",
    _ => "BARCODE",
    };
}
