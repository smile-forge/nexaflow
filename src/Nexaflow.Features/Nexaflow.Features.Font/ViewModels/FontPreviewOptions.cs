using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Font.ViewModels;

/// <summary>
/// The shared preview settings for a Font page — the sample text, size and bold/italic/underline
/// toggles — held once and attached to every <see cref="FontItemViewModel"/> so the top-bar controls
/// and every compare row stay in sync. Each item combines these with its own selected face to render.
/// </summary>
public sealed partial class FontPreviewOptions : ObservableObject
{
    /// <summary>Alphabet + digits + common symbols shown small under each preview.</summary>
    public string SpecimenText { get; } =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz 0123456789  &@#$%^*()[]{}/\\.,;:!?—“”";

    [ObservableProperty] private string _previewText = "The quick brown fox jumps over the lazy dog";
    [ObservableProperty] private double _previewSizePt = 16;
    [ObservableProperty] private bool _isBold;
    [ObservableProperty] private bool _isItalic;
    [ObservableProperty] private bool _isUnderline;

    /// <summary>WPF FontSize is in DIPs (1/96"); the sliders are in points, so convert (pt × 4/3).</summary>
    public double PreviewSizeDip => PreviewSizePt * 4.0 / 3.0;

    /// <summary>The small specimen line sits one point above the old 8pt so it stays legible.</summary>
    public double SpecimenSizeDip => 9 * 4.0 / 3.0;

    partial void OnPreviewSizePtChanged(double value) => OnPropertyChanged(nameof(PreviewSizeDip));
}
