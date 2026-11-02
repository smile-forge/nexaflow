using Nexaflow.Features.Images.ViewModels;
using System.Printing;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// Print, as the viewer and Artist both offer it: a toggle opening the print options, and from them the printer chosen and the
/// pages drawn from the picture at full size. Its DataContext is the page's <see cref="PicturePrinting"/>.
/// </summary>
public partial class PicturePrintView : UserControl
{
    public PicturePrintView() => InitializeComponent();

    private async void PrintGo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PicturePrinting printing) return;
        printing.IsOpen = false;
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        dialog.PrintTicket.PageOrientation = printing.PrintsLandscape ? PageOrientation.Landscape : PageOrientation.Portrait;
        if (dialog.ShowDialog() != true) return;
        try
        {
            double printerDpi = dialog.PrintTicket.PageResolution?.X ?? 600;
            if (await printing.PaginatorAsync(new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight), printerDpi) is not { } paginator) return;
            dialog.PrintDocument(paginator, printing.Title);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            printing.Failed(ex.Message);
        }
    }
}
