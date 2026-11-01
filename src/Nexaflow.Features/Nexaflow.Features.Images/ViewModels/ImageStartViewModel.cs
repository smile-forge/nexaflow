using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.OpenRaster;
using Nexaflow.Visuals.Common.Drafts;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>A layered picture opened here lately, for the start screen to open again.</summary>
/// <param name="Path">Its file.</param>
/// <param name="Name">What it is shown as.</param>
/// <param name="Where">Its folder, or that it is not saved yet.</param>
/// <param name="IsDraft">A canvas made here and not saved yet: it opens as the draft it is.</param>
public sealed record RecentCanvas(string Path, string Name, string Where, bool IsDraft);

/// <summary>What an Images tab shows from its start screen on, for its page to follow: its pictures, how, and — a draft —
/// where the draft's Save as starts.</summary>
public sealed record ShownPictures(IReadOnlyList<string> Paths, ImageViewMode Mode, bool IsDraft, string? DraftFolder);

/// <summary>
/// New picture — what an Images tab holding no picture shows: a blank canvas of a size and background set up here, made as a
/// draft <c>.ora</c> and opened for editing; a picture drawn by the workspace's image model into such a canvas as its first
/// layer; the picture on the clipboard as a canvas of its own; pictures opened from disk; and the canvases opened here lately.
/// Whatever it opens, the tab shows from then on.
/// </summary>
public sealed partial class ImageStartViewModel : ObservableObject, IDisposable
{
    /// <summary>How many canvases are remembered.</summary>
    internal const int RecentKept = 8;

    private readonly ImageViewModel _viewer;
    private readonly IShellServices? _shell;
    private readonly IAIService? _ai;
    private readonly ImageEditingServices _editing;
    private readonly ImagesConfig _config;

    internal ImageStartViewModel(ImageViewModel viewer, IShellServices? shell, IAIService? ai, ImageEditingServices editing, ImagesConfig config)
    {
        _viewer  = viewer;
        _shell   = shell;
        _ai      = ai;
        _editing = editing;
        _config  = config;
        Setup.PropertyChanged += OnSetupChanged;
        if (ai is not null)
        {
            Generation = new GenerateImageOptions(ai, DraftLocation.Root);
            Generation.PropertyChanged += OnGenerationChanged;
            PreferCanvasShape();
        }
    }

    /// <summary>The canvas's size and background.</summary>
    public CanvasSetup Setup { get; } = new();

    /// <summary>What a picture drawn into the canvas is drawn with; null where the tab has no AI.</summary>
    public GenerateImageOptions? Generation { get; }

    /// <summary>Whether a picture can be drawn: a model draws now, or one of Nexaflow's own waits on its download. Until then the
    /// start screen says why, and offers the workspace's AI settings.</summary>
    public bool OffersGeneration => _ai is not null && GenerateImageOptions.Offered(_ai);

    /// <summary>The canvases opened here lately that are still there, newest first.</summary>
    public ObservableCollection<RecentCanvas> Recent { get; } = [];

    /// <summary>The clipboard holds a picture, as of the last look.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
    private bool _clipboardHoldsPicture;

    /// <summary>A canvas is being made.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(GenerateCommand), nameof(PasteCommand))]
    private bool _isMaking;

    /// <summary>Looks again at what may have changed while the screen was away: the clipboard, the canvases still there, the models.</summary>
    public async Task RefreshAsync()
    {
        ClipboardHoldsPicture = PictureClipboard.HoldsPicture();
        OnPropertyChanged(nameof(OffersGeneration));
        var recent = new List<RecentCanvas>();
        foreach (var path in _config.RecentCanvases)
        {
            if (!await _editing.FileSystem.ExistsAsync(path)) continue;
            bool draft = IsDraft(path);
            recent.Add(new RecentCanvas(path, Path.GetFileName(path),
                                        draft ? Str.Get("Images.Start.NotSaved") : Path.GetDirectoryName(path) ?? string.Empty, draft));
        }
        Recent.Clear();
        foreach (var canvas in recent) Recent.Add(canvas);
    }

    private static bool IsDraft(string path) => path.StartsWith(DraftLocation.Root, StringComparison.OrdinalIgnoreCase);

    /// <summary>Remembers a layered picture opened here, newest first, for every tab's start screen.</summary>
    internal void Remember(string path)
    {
        var recent = _config.RecentCanvases;
        recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, path);
        if (recent.Count > RecentKept) recent.RemoveRange(RecentKept, recent.Count - RecentKept);
        _shell?.SaveFeatureConfig(_config);
    }

    /// <summary>Where a draft made here offers to be saved first: the user's Pictures.</summary>
    internal static string DraftFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    private void OnSetupChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CanvasSetup.IsValid):
                CreateCommand.NotifyCanExecuteChanged();
                GenerateCommand.NotifyCanExecuteChanged();
                break;
            case nameof(CanvasSetup.PixelWidth) or nameof(CanvasSetup.PixelHeight):
                PreferCanvasShape();
                break;
        }
    }

    private void OnGenerationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GenerateImageOptions.CanCreate)) GenerateCommand.NotifyCanExecuteChanged();
    }

    // The picture is drawn in the proportions nearest the canvas's.
    private void PreferCanvasShape()
    {
        if (Generation is not null && Setup.PixelWidth is { } width && Setup.PixelHeight is { } height && width > 0 && height > 0)
            Generation.Prefer(width, height);
    }

    /// <summary>Create: the blank canvas set up, made as a draft and opened for editing.</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task Create()
    {
        if (Setup.Make(Str.Format("Images.Layers.PixelName", 1)) is not { } canvas) return;
        await OpenAsync(canvas);
    }

    private bool CanCreate() => !IsMaking && Setup.IsValid;

    /// <summary>Generate: the picture set up drawn as one of the workspace's jobs, the blank canvas opened meanwhile, the picture
    /// its first layer — beneath the empty one — once it arrives.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task Generate()
    {
        if (Setup.Make(Str.Format("Images.Layers.PixelName", 1)) is not { } canvas || _viewer.Pictures.Draw(Generation!) is not var (job, name)) return;
        if (await OpenAsync(canvas) is { } path) _viewer.Pictures.Follow(job, name, path, above: null);
        else job.Stop();
    }

    private bool CanGenerate() => !IsMaking && Setup.IsValid && Generation?.CanCreate == true;

    /// <summary>Paste from clipboard: a canvas the clipboard picture's size, the picture its one layer.</summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task Paste()
    {
        if (PictureClipboard.ReadPng() is not { } png) return;
        using var bitmap = SKBitmap.Decode(png);
        if (bitmap is null || bitmap.Width > LayeredPicture.MaxSide || bitmap.Height > LayeredPicture.MaxSide) return;
        var canvas = LayeredPicture.Blank(bitmap.Width, bitmap.Height, LayerBackground.Transparent, Str.Get("Images.Layers.PastedName"));
        canvas = canvas.WithLayer(canvas.ActiveId!.Value, l => ((PixelLayer)l) with { Pixels = TiledPixels.FromBitmap(bitmap, 0, 0) });
        await OpenAsync(canvas);
    }

    private bool CanPaste() => !IsMaking && ClipboardHoldsPicture;

    /// <summary><paramref name="canvas"/> written as a draft and opened for editing; gives the draft, or null where it could not be written.</summary>
    private async Task<string?> OpenAsync(LayeredPicture canvas)
    {
        var path = DraftLocation.NewFile(Str.Get("Images.Canvas.DraftName") + OpenRasterPackage.Extension);
        IsMaking = true;
        try
        {
            await _editing.WriteLayeredAsync(canvas, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _shell?.ShowError(Str.Format("Images.Canvas.FailedFormat", Path.GetFileName(path), ex.Message));
            return null;
        }
        finally
        {
            IsMaking = false;
        }
        _viewer.OpenCanvas(path, draft: true);
        return path;
    }

    /// <summary>Open picture: pictures from disk, shown here.</summary>
    [RelayCommand]
    private async Task OpenPicture()
    {
        if (_shell is null) return;
        var paths = await _shell.PickOpenFilesAsync([.. ImageFileTypes.Extensions]);
        if (paths.Count > 0) _viewer.ShowPictures(paths, ImageViewMode.Carousel);
    }

    /// <summary>A canvas opened here lately, opened for editing again — a draft as the draft it is.</summary>
    [RelayCommand]
    private void OpenRecent(RecentCanvas? canvas)
    {
        if (canvas is not null) _viewer.OpenCanvas(canvas.Path, canvas.IsDraft);
    }

    /// <summary>The workspace's AI settings, where it draws no pictures.</summary>
    [RelayCommand]
    private void OpenAiSettings() => _shell?.OpenWorkspaceConfig(AiAbilitiesConfigName);

    /// <summary>The workspace setting that says which models draw pictures.</summary>
    private const string AiAbilitiesConfigName = "ai-abilities";

    public void Dispose()
    {
        Setup.PropertyChanged -= OnSetupChanged;
        if (Generation is null) return;
        Generation.PropertyChanged -= OnGenerationChanged;
        Generation.Dispose();
    }
}
