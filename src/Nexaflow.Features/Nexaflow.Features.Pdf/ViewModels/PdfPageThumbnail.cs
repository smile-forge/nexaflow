using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Editing;

namespace Nexaflow.Features.Pdf.ViewModels;

/// <summary>
/// One page in the Pages tab: its picture once drawn, the sheet it is drawn on, its number, and whether it is selected
/// or the page being read. It is the same page however it moves, so its picture and selection move with it.
/// </summary>
public sealed partial class PdfPageThumbnail : ObservableObject
{
    /// <summary>The widest a sheet is drawn, in device-independent pixels.</summary>
    public const double SheetBox = 120;

    /// <summary>The tallest a sheet is drawn.</summary>
    public const double SheetTallest = 160;

    internal PdfPageThumbnail(PdfPageId id)
    {
        Id = id;
    }

    internal PdfPageId Id { get; }

    /// <summary>The page's place in the document, from 1.</summary>
    [ObservableProperty] private int _number;

    /// <summary>The page drawn, once it is.</summary>
    [ObservableProperty] private BitmapSource? _picture;

    [ObservableProperty] private double _sheetWidth = SheetBox;

    [ObservableProperty] private double _sheetHeight = SheetTallest;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Whether this is the page being read.</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>
    /// The lines the page would be cut along while a split is set up, drawn across the sheet — each marked where it crosses
    /// the page's artwork. None otherwise.
    /// </summary>
    [ObservableProperty] private IReadOnlyList<PdfThumbnailCut> _cuts = [];

    /// <summary>
    /// Sizes the sheet to the page's proportions, as large as fits <see cref="SheetBox"/> by <see cref="SheetTallest"/>.
    /// </summary>
    internal void Fit(System.Windows.Size page)
    {
        if (page.Width <= 0 || page.Height <= 0) return;
        var scale = Math.Min(SheetBox / page.Width, SheetTallest / page.Height);
        SheetWidth = page.Width * scale;
        SheetHeight = page.Height * scale;
    }

    /// <summary>Draws <paramref name="cuts"/> across the sheet, those <paramref name="crossing"/> the artwork marked.</summary>
    internal void ShowCuts(IReadOnlyList<PdfPageCut> cuts, Func<PdfPageCut, bool> crossing) =>
        Cuts = cuts.Select(cut => cut.Vertical
                ? new PdfThumbnailCut(cut.At * SheetWidth, 0, cut.At * SheetWidth, SheetHeight, crossing(cut))
                : new PdfThumbnailCut(0, cut.At * SheetHeight, SheetWidth, cut.At * SheetHeight, crossing(cut)))
            .ToList();
}
