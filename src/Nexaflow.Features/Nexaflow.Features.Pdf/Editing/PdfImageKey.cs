using System.Windows;
using UglyToad.PdfPig.Core;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>One image of the document being edited: the image XObject <paramref name="Image"/> of source <paramref name="Source"/>.</summary>
internal readonly record struct PdfImageKey(PdfSourceId Source, IndirectReference Image);

/// <summary>An image a page draws, and where: its bounds on the page as shown, in points.</summary>
internal sealed record PdfPlacedImage(PdfImageKey Key, Rect Bounds);
