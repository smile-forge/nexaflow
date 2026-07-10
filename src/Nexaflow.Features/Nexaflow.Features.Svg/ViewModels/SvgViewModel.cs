using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Svg.ClientTools;
using Nexaflow.Features.Svg.Loaders;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.Svg.ViewModels;

/// <summary>
/// The SVG viewer page's view-model. Loads a vector file through <see cref="SvgLoader"/> off the UI thread,
/// exposes the frozen <see cref="Artifact"/> the view binds to (crisp at any zoom), the metadata the footer
/// shows, and a couple of read-only AI tools. Unlike the 3D viewer there's no live-control bridge — the
/// rendered form is a self-contained frozen drawing, so the view just displays it and the AI's render tool
/// rasterizes it directly.
/// </summary>
public sealed partial class SvgViewModel : ObservableObject, IPageViewModel
{
    /// <summary>Files larger than this aren't rendered — a pathological SVG can expand into an enormous
    /// visual tree. Cosmetic metadata still loads; the view shows the guard message.</summary>
    private const long MaxRenderBytes = 64L * 1024 * 1024;

    private readonly SvgLoader _loader;

    public SvgViewModel(string filePath, SvgLoader? loader = null)
    {
        FilePath = filePath;
        _loader = loader ?? new SvgLoader();
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>The loaded, frozen vector artwork the view binds an <c>Image</c> to. Null until loaded / on error.</summary>
    [ObservableProperty] private DrawingImage? _artifact;

    /// <summary>Natural size of the artwork in device-independent pixels — the view fits/centres to it.</summary>
    public double NaturalWidth { get; private set; }
    public double NaturalHeight { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoaded;

    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;

    [ObservableProperty] private string _dimensionsText = string.Empty;
    [ObservableProperty] private string _viewBoxText = string.Empty;
    [ObservableProperty] private bool _hasViewBox;
    [ObservableProperty] private int _elementCount;
    [ObservableProperty] private string _fileSizeText = string.Empty;

    /// <summary>Whether the transparency checkerboard shows behind the art (else a solid theme background).
    /// The toolbar toggle binds this; a view-side concern kept here so the toggle is testable.</summary>
    [ObservableProperty] private bool _showCheckerboard = true;

    /// <summary>Reads + renders the SVG off the UI thread, then publishes the artwork and footer metadata.
    /// Safe to call from the view's Loaded handler — the continuation runs on the UI thread.</summary>
    public async Task LoadAsync()
    {
        if (IsLoaded) return;

        try
        {
            long size = 0;
            try { size = new FileInfo(FilePath).Length; } catch { /* size is cosmetic */ }
            FileSizeText = SizeFormatter.FormatBytes(size);

            if (size > MaxRenderBytes)
            {
                Fail($"This SVG is too large to render safely ({SizeFormatter.FormatBytes(size)}).");
                return;
            }

            var loaded = await Task.Run(() => _loader.Load(FilePath)).ConfigureAwait(true);

            Artifact = loaded.Image;
            NaturalWidth = loaded.Bounds.Width;
            NaturalHeight = loaded.Bounds.Height;
            ElementCount = loaded.ElementCount;

            var w = loaded.Width ?? Fmt(loaded.Bounds.Width);
            var h = loaded.Height ?? Fmt(loaded.Bounds.Height);
            DimensionsText = $"{w} × {h}";
            ViewBoxText = loaded.ViewBox ?? string.Empty;
            HasViewBox = !string.IsNullOrWhiteSpace(loaded.ViewBox);

            if (loaded.Bounds.IsEmpty || loaded.Bounds.Width <= 0 || loaded.Bounds.Height <= 0)
                Fail("The file loaded but contained nothing to render.");
        }
        catch (Exception ex)
        {
            Fail($"Couldn't open this SVG: {ex.Message}");
        }
        finally
        {
            IsLoaded = true; // releases the AI send gate even on failure
        }
    }

    private void Fail(string message)
    {
        HasError = true;
        ErrorMessage = message;
    }

    private static string Fmt(double value) =>
        value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Multi-line summary for the AI's <c>get_svg_info</c> tool.</summary>
    public string DescribeInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{FileName} — SVG vector image");
        sb.AppendLine($"Size: {DimensionsText}.");
        if (HasViewBox) sb.AppendLine($"viewBox: {ViewBoxText}.");
        sb.AppendLine($"Drawable elements: {ElementCount:N0}.");
        sb.Append($"File size: {FileSizeText}.");
        return sb.ToString();
    }

    // ── IPageViewModel ──────────────────────────────────────────────────────────────────────────
    public bool IsContextReady => IsLoaded;

    public string GetContext()
    {
        if (!IsLoaded) return $"SVG \"{FileName}\": still loading…";
        if (HasError) return $"SVG \"{FileName}\": failed to load — {ErrorMessage}";

        var vb = HasViewBox ? $", viewBox {ViewBoxText}" : "";
        return $"SVG image \"{FileName}\": {DimensionsText}{vb}, {ElementCount:N0} drawable element(s), {FileSizeText}. " +
               "Vector art rendered to a WPF drawing.";
    }

    public IReadOnlyList<IClientTool> GetClientTools() => SvgTools.For(this);

    public string? GetSecurityContext() => FilePath;

    public string? GetAiSystemPromptGuidance() =>
        "This is a static SVG (vector image) viewer. Use render_svg_image to see the artwork and get_svg_info " +
        "for its dimensions, viewBox and element count.";
}
