using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// One page of a document as objects: its size, what it draws, the annotations over it, and the text it shows.
/// </summary>
/// <param name="Number">The page's 1-based number in the document it was read from.</param>
/// <param name="Size">The page as shown — its crop box, turned by its rotation — in points.</param>
/// <param name="FromUser">
/// From the page's default user space (PDF's own, y up, before crop box and rotation) to page space — what places a
/// destination or a field the document gives in its own coordinates.
/// </param>
/// <param name="Content">Everything the page's content stream draws, in page space.</param>
/// <param name="Annotations">What sits over the page: links, notes, form widgets, stamps.</param>
/// <param name="Text">
/// The text the page shows, run after run in the order the page sets them, with a space or line break between runs
/// where the layout puts one. Every <see cref="PdfTextNode"/> names its stretch of it, which is how a reader's
/// selection and a search find their way back to the glyphs.
/// </param>
/// <param name="Unsupported">What the page asked for that this reading could not carry, in the order met.</param>
internal sealed record PdfPageNode(
    int Number, Size Size, Matrix FromUser, PdfGroupNode Content, IReadOnlyList<PdfAnnotationNode> Annotations, string Text,
    IReadOnlyList<PdfUnsupported> Unsupported);

/// <summary>
/// A page before its content is read: its number, its size as shown, and the crop box and rotation it is shown by — what
/// a part of it is measured against.
/// </summary>
internal readonly record struct PdfPageSlot(int Number, Size Size, UglyToad.PdfPig.Core.PdfRectangle Crop, int Rotation);

/// <summary>
/// A document as objects: the pages it has, in the order it shows them, and its form fields. A page's content is
/// read only when something asks for it (<see cref="PdfPageReader"/>), so opening a thousand-page document costs
/// its page sizes and nothing more; reordering or dropping pages is a new slot list.
/// </summary>
/// <param name="Pages">The pages, in showing order.</param>
/// <param name="Fields">The interactive form's fields, when the document has a form.</param>
internal sealed record PdfDocumentNode(IReadOnlyList<PdfPageSlot> Pages, IReadOnlyList<PdfFieldNode> Fields);
