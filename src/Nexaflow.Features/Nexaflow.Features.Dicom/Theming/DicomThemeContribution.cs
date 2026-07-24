using Nexaflow.Features.Common;

namespace Nexaflow.Features.Dicom.Theming;

/// <summary>
/// Contributes the DICOM viewer's own theme tokens (<c>Dicom.Viewport</c>, <c>Dicom.OverlayText</c>,
/// <c>Dicom.AnnotationBrush</c>) as low-precedence fallbacks. Reflection-discovered by FeatureManager and
/// merged below the active theme, so the viewer looks right in every theme and any theme may retune a
/// <c>Dicom.*</c> key by name — no Core⇄feature reference either way.
/// </summary>
public sealed class DicomThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Dicom;component/Theming/DicomTheme.xaml"),
    ];
}
