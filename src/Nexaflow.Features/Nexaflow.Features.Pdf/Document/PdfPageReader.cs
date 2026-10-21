using System;
using System.Collections.Generic;
using System.Threading;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms;
using UglyToad.PdfPig.AcroForms.Fields;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// Reads an open document as objects: its page slots up front, each page's content when it is asked for.
/// <para>
/// Not thread-safe, as the document under it is not: every call is made under the lock that guards the document.
/// </para>
/// </summary>
internal sealed class PdfPageReader
{
    /// <summary>Field flag bit 1: the field may not be changed.</summary>
    private const uint ReadOnlyFlag = 1;

    private readonly PdfDocument _document;
    private readonly PdfReadingCaches _caches;

    /// <param name="document">The document, opened with <paramref name="options"/>.</param>
    /// <param name="options">The options it was opened with, whose caches its pages share.</param>
    public PdfPageReader(PdfDocument document, PdfReadingOptions options)
    {
        _document = document;
        _caches = options.Caches;
        _caches.OptionalContent = PdfOptionalContent.Read(
            document.Structure.Catalog.CatalogDictionary, document.Structure.TokenScanner);

        document.AddPageFactory<PdfPageSlot, PdfPageSlotFactory>();
        document.AddPageFactory<PdfPageNode, PdfPageFactory>();
    }

    /// <summary>How many pages the document has.</summary>
    public int PageCount => _document.NumberOfPages;

    /// <summary>The document's pages as slots, in order, and its form's fields — nothing of any page's content.</summary>
    public PdfDocumentNode ReadDocument(CancellationToken ct)
    {
        var slots = new PdfPageSlot[_document.NumberOfPages];
        for (var i = 0; i < slots.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            slots[i] = _document.GetPage<PdfPageSlot>(i + 1);
        }

        return new PdfDocumentNode(slots, Fields());
    }

    /// <summary>Page <paramref name="number"/>'s slot alone: its size, crop box and rotation.</summary>
    public PdfPageSlot Slot(int number) => _document.GetPage<PdfPageSlot>(number);

    /// <summary>
    /// Page <paramref name="number"/> (1-based) read in full — or only its <paramref name="part"/>, read as though the
    /// part were the page's crop box: its size, its place, and what lies outside it all follow.
    /// </summary>
    public PdfPageNode ReadPage(int number, CancellationToken ct, PdfPagePart? part = null)
    {
        ct.ThrowIfCancellationRequested();
        _caches.Cancellation = ct;
        _caches.ReadingPart = part;
        try
        {
            return _document.GetPage<PdfPageNode>(number);
        }
        finally
        {
            _caches.Cancellation = CancellationToken.None;
            _caches.ReadingPart = null;
        }
    }

    private IReadOnlyList<PdfFieldNode> Fields()
    {
        AcroForm? form;
        try
        {
            if (!_document.TryGetForm(out form)) return [];
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ObjectDisposedException or OutOfMemoryException))
        {
            // A broken form leaves the pages readable; the fields are what is lost.
            return [];
        }

        var fields = new List<PdfFieldNode>();
        foreach (var field in form.Fields) Gather(field, null, fields);
        return fields;
    }

    private static void Gather(AcroFieldBase field, string? parent, List<PdfFieldNode> into)
    {
        var partial = field.Information.PartialName;
        var name = string.IsNullOrEmpty(partial) ? parent : parent is null ? partial : $"{parent}.{partial}";

        if (field is AcroNonTerminalField branch)
        {
            foreach (var child in branch.Children) Gather(child, name, into);
            return;
        }

        if (name is null) return;
        into.Add(new PdfFieldNode(name, field.FieldType, Value(field), field.PageNumber,
            (field.FieldFlags & ReadOnlyFlag) != 0));
    }

    private static string? Value(AcroFieldBase field) => field switch
    {
        AcroTextField text         => text.Value,
        AcroCheckboxField box      => box.CurrentValue?.Data,
        AcroRadioButtonField radio => radio.CurrentValue?.Data,
        AcroComboBoxField combo    => string.Join(", ", combo.SelectedOptions),
        AcroListBoxField list      => string.Join(", ", list.SelectedOptions),
        _                          => null,
    };
}
