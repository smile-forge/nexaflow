namespace Nexaflow.Features.Pdf.ViewModels;

/// <summary>
/// A cut line drawn on a thumbnail's sheet, from (<see cref="X1"/>, <see cref="Y1"/>) to (<see cref="X2"/>,
/// <see cref="Y2"/>) in device-independent pixels, and whether the page's artwork crosses it.
/// </summary>
public sealed record PdfThumbnailCut(double X1, double Y1, double X2, double Y2, bool CrossesInk);
