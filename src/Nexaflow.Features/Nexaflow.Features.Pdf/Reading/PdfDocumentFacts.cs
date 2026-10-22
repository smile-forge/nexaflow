using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// What a document's catalog says about it that decides whether, and how, it can be written again.
/// </summary>
internal static class PdfDocumentFacts
{
    /// <summary>
    /// Whether the document carries an XFA form, which lays out the whole document itself and cannot follow its pages.
    /// </summary>
    public static bool HasXfa(PdfDocument document) => Form(document)?.ContainsKey(NameToken.Xfa) == true;

    /// <summary>
    /// Whether the document is signed: its form says signatures exist, which a document written anew invalidates.
    /// </summary>
    public static bool IsSigned(PdfDocument document) =>
        Form(document) is { } form
        && form.TryGet(NameToken.SigFlags, document.Structure.TokenScanner, out NumericToken? flags)
        && (flags.Int & 1) != 0;

    private static DictionaryToken? Form(PdfDocument document) =>
        document.Structure.Catalog.CatalogDictionary.TryGet(NameToken.AcroForm, document.Structure.TokenScanner, out DictionaryToken? form)
            ? form
            : null;
}
