using Nexaflow.Features.Common;

namespace Nexaflow.Features.Pdf.Theming;

/// <summary>
/// Contributes the PDF reader's own theme tokens (<c>Pdf.Paper</c>, <c>Pdf.Selection</c>, <c>Pdf.FindHit</c>,
/// <c>Pdf.FindCurrent</c>) as low-precedence fallbacks. Reflection-discovered by FeatureManager and merged below the
/// active theme, so the reader looks right in every theme and any theme may retune a <c>Pdf.*</c> key by name.
/// </summary>
public sealed class PdfThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Pdf;component/Theming/PdfTheme.xaml"),
    ];
}
