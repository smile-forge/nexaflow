using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Editing;
using System.Windows.Media;
using Nexaflow.Visuals.Common.Localization;

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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    private int _number;

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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(ClearCutLines), nameof(CrossingCutLines))]
    private IReadOnlyList<PdfThumbnailCut> _cuts = [];

    /// <summary>
    /// What a screen reader says of the page: its number, and — while a split is set up — whether a cut would cross its
    /// artwork, which the sheet otherwise shows only in colour.
    /// </summary>
    public string Name => Cuts.Any(cut => cut.CrossesInk)
        ? Str.Format("Pdf.Pages.PageCrossedFormat", Number)
        : Str.Format("Pdf.Pages.PageFormat", Number);

    /// <summary>The cut lines that cross nothing, as one figure; null when there are none.</summary>
    public Geometry? ClearCutLines => CutLines(crossing: false);

    /// <summary>The cut lines that cross the page's artwork, as one figure; null when there are none.</summary>
    public Geometry? CrossingCutLines => CutLines(crossing: true);

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

    private Geometry? CutLines(bool crossing)
    {
        var lines = Cuts.Where(cut => cut.CrossesInk == crossing).ToList();
        if (lines.Count == 0) return null;

        var figure = new StreamGeometry();
        using (var context = figure.Open())
        {
            foreach (var line in lines)
            {
                context.BeginFigure(new System.Windows.Point(line.X1, line.Y1), isFilled: false, isClosed: false);
                context.LineTo(new System.Windows.Point(line.X2, line.Y2), isStroked: true, isSmoothJoin: false);
            }
        }

        figure.Freeze();
        return figure;
    }
}
