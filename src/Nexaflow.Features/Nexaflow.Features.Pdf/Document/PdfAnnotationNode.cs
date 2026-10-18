using System.Windows;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Annotations;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>Where activating a link goes.</summary>
internal abstract record PdfLinkTarget;

/// <summary>A link to a place in the same document.</summary>
/// <param name="PageNumber">The 1-based page number it opens.</param>
/// <param name="Top">
/// The height on that page the link opens at, in the page's default user space (y up), when the link says; null for the
/// page's top. <see cref="PdfPageNode.FromUser"/> takes it to page space once the page is read.
/// </param>
internal sealed record PdfPageTarget(int PageNumber, double? Top) : PdfLinkTarget;

/// <summary>A link out of the document.</summary>
/// <param name="Uri">The address, as the document wrote it.</param>
internal sealed record PdfUriTarget(string Uri) : PdfLinkTarget;

/// <summary>Something that sits over a page rather than in its content: a link, a note, a form widget, a stamp.</summary>
/// <param name="Kind">The annotation's subtype.</param>
/// <param name="Bounds">Its rectangle, in page space.</param>
/// <param name="Appearance">What it draws, in page space, from its normal appearance stream; null when it has none.</param>
/// <param name="Link">Where activating it goes, for a link.</param>
/// <param name="Contents">The text it carries — a note's body, a widget's tooltip.</param>
/// <param name="FieldName">For a form widget, the fully qualified name of the field it shows.</param>
/// <param name="Printable">Whether the document asks for it to be printed.</param>
internal sealed record PdfAnnotationNode(
    AnnotationType Kind, Rect Bounds, PdfGroupNode? Appearance, PdfLinkTarget? Link, string? Contents,
    string? FieldName, bool Printable);

/// <summary>One field of a document's interactive form, as it stands in the file.</summary>
/// <param name="Name">The fully qualified field name — the key a filled-in value is written back under.</param>
/// <param name="Kind">What sort of field it is.</param>
/// <param name="Value">Its current value as text: a text field's contents, a checkbox's state name, a choice.</param>
/// <param name="PageNumber">The 1-based page its widget is on, when the document says.</param>
/// <param name="ReadOnly">Whether the form forbids changing it.</param>
internal sealed record PdfFieldNode(string Name, AcroFieldType Kind, string? Value, int? PageNumber, bool ReadOnly);
