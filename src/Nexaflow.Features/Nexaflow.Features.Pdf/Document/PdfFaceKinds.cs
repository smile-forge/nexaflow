using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.PdfFonts;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>The kind of installed face that stands in for a font a document names without carrying it.</summary>
internal enum PdfFaceKind
{
    Sans,
    Serif,
    Fixed,
}

/// <summary>
/// What kind of face a font asks for: fixed pitch or serif by its descriptor's flags (9.8.2), or by its name where it has
/// no descriptor or its flags say neither — so a font a document names but neither carries nor finds installed, Melior
/// or a house face, is drawn in an installed face of its own kind rather than a sans for everything.
/// </summary>
internal static partial class PdfFaceKinds
{
    private static readonly NameToken FontDescriptor = NameToken.Create("FontDescriptor");
    private static readonly NameToken DescendantFonts = NameToken.Create("DescendantFonts");
    private static readonly NameToken Flags = NameToken.Create("Flags");

    /// <summary>The kind <paramref name="font"/> asks for, its dictionary read through <paramref name="scanner"/>.</summary>
    public static PdfFaceKind Of(IFont font, IPdfTokenScanner scanner) =>
        Of(DescriptorFlags(font, scanner), font.Name?.Data ?? font.Details?.Name);

    /// <summary>The kind a font of these descriptor <paramref name="flags"/> — null where it has none — and this
    /// <paramref name="name"/> asks for; either saying fixed pitch or serif is enough.</summary>
    public static PdfFaceKind Of(FontDescriptorFlags? flags, string? name)
    {
        name ??= string.Empty;
        if (flags?.HasFlag(FontDescriptorFlags.FixedPitch) == true || FixedName().IsMatch(name)) return PdfFaceKind.Fixed;
        if (flags?.HasFlag(FontDescriptorFlags.Serif) == true || SerifName().IsMatch(name)) return PdfFaceKind.Serif;
        return PdfFaceKind.Sans;
    }

    /// <summary>The flags of the font's descriptor — a composite font's, its descendant's (9.7.4) — or null where its
    /// dictionary or its descriptor cannot be found.</summary>
    private static FontDescriptorFlags? DescriptorFlags(IFont font, IPdfTokenScanner scanner)
    {
        if (font.Details?.FontDictionaryReference is not { } reference ||
            !DirectObjectFinder.TryGet(new IndirectReferenceToken(reference), scanner, out DictionaryToken? dictionary))
            return null;

        if (dictionary.TryGet(DescendantFonts, scanner, out ArrayToken? descendants) && descendants.Length > 0 &&
            DirectObjectFinder.TryGet(descendants.Data[0], scanner, out DictionaryToken? descendant))
            dictionary = descendant;

        return dictionary.TryGet(FontDescriptor, scanner, out DictionaryToken? descriptor) &&
               descriptor.TryGet(Flags, scanner, out NumericToken? flags)
            ? (FontDescriptorFlags)flags.Int
            : null;
    }

    /// <summary>A font name that says serif — Latin or CJK: Times, Roman, Mincho, Song, Ming, Batang.</summary>
    [GeneratedRegex("(?i)serif|times|roman|garamond|georgia|minion|mincho|song|ming|batang|myeongjo")]
    private static partial Regex SerifName();

    /// <summary>A font name that says fixed pitch: Courier, a monospace, a typewriter face.</summary>
    [GeneratedRegex("(?i)courier|mono|consol|typewriter|fixed")]
    private static partial Regex FixedName();
}
