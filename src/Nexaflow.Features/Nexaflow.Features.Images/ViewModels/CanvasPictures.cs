using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Drafts;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// A picture on its way into a layered picture as a layer of its own: being drawn by the workspace's image model, shown in the
/// layers panel with what the drawing is doing and its Stop until it arrives.
/// </summary>
public sealed partial class PendingPicture : ObservableObject
{
    internal PendingPicture(string name, PictureJob job, string canvas, Guid? above, EditedPlace? place = null)
    {
        Name   = name;
        Job    = job;
        Canvas = canvas;
        Above  = above;
        Place  = place;
    }

    /// <summary>Where an edit's picture lies once it arrives; null for a picture fitted to the canvas.</summary>
    internal EditedPlace? Place { get; }

    /// <summary>The layer's name once it arrives: what was asked for, in its first words.</summary>
    public string Name { get; }

    /// <summary>The drawing: its progress and its Stop.</summary>
    public PictureJob Job { get; }

    /// <summary>The layered picture it goes into, by its file.</summary>
    internal string Canvas { get; }

    /// <summary>The layer it goes above; null for beneath every layer.</summary>
    internal Guid? Above { get; }

    /// <summary>The file it was saved as, once drawn; null while it is drawn.</summary>
    internal string? Arrived { get; set; }

    /// <summary>Stops the drawing; nothing is added.</summary>
    [RelayCommand]
    private void Stop() => Job.Stop();
}

/// <summary>
/// Where an AI edit's picture lies on its canvas once it arrives: laid exactly over <paramref name="Over"/> — stretched
/// each way as its model kept that area's proportions to within a few pixels — and shown only where
/// <paramref name="Shown"/> covers, on a canvas still <paramref name="CanvasWidth"/> × <paramref name="CanvasHeight"/>.
/// </summary>
/// <param name="Shown">What of it is shown, over the whole canvas; null to show all of it.</param>
internal sealed record EditedPlace(int CanvasWidth, int CanvasHeight, SKRectI Over, TiledCoverage? Shown);

/// <summary>
/// Pictures brought into a layered picture as picture layers, each fitted to the canvas: files dropped on the canvas — a photo
/// made a layered picture first — and pictures drawn by the workspace's image model, from the start screen or by Generate
/// layer, followed while they are drawn and added where they were asked for once they arrive.
/// </summary>
public sealed partial class CanvasPictures : ObservableObject, IDisposable
{
    /// <summary>How many words of what was asked for name a drawn layer.</summary>
    private const int NameWords = 5;

    private readonly ArtistViewModel _viewer;
    private readonly IShellServices? _shell;
    private readonly IAIService? _ai;
    private readonly ImageEditingServices _editing;

    internal CanvasPictures(ArtistViewModel viewer, IShellServices? shell, IAIService? ai, ImageEditingServices editing)
    {
        _viewer  = viewer;
        _shell   = shell;
        _ai      = ai;
        _editing = editing;
        Pending.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPending));
    }

    /// <summary>The pictures being drawn for a layered picture, oldest first.</summary>
    public ObservableCollection<PendingPicture> Pending { get; } = [];

    public bool HasPending => Pending.Count > 0;

    // ── Files ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pictures dropped on the canvas, each a picture layer above the active one, fitted to the canvas and centred — the photo
    /// made a layered picture first, where it is not one. What is no picture is passed over.
    /// </summary>
    public async Task AddFilesAsync(IReadOnlyList<string> paths)
    {
        var pictures = paths.Where(ImageFileTypes.IsImage).ToList();
        if (pictures.Count == 0 || _viewer.MakeLayered() is not { } document) return;
        await AddAsync(document, [.. pictures.Select(p => (p, Path.GetFileNameWithoutExtension(p)))], document.Picture.ActiveId);
    }

    /// <summary>The pictures at <paramref name="pictures"/>' paths as picture layers of <paramref name="document"/> under their
    /// names, above <paramref name="above"/> — beneath every layer where null — the last of them active: one step of its history.
    /// Each is fitted to the canvas and centred, or, an edit's, laid where <paramref name="place"/> says.</summary>
    private async Task AddAsync(LayeredSession document, IReadOnlyList<(string Path, string Name)> pictures, Guid? above, EditedPlace? place = null)
    {
        var layers = new List<PictureLayer>();
        foreach (var (path, name) in pictures)
        {
            SKSize size;
            try
            {
                using var decoded = await _editing.DecodeAsync(path, maxLongEdge: 64);
                size = decoded.SourceSize;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                _shell?.ShowError(Str.Format("Images.Layers.AddFailedFormat", Path.GetFileName(path), ex.Message));
                continue;
            }
            var picture = document.Picture;
            layers.Add(place is { } at && (at.CanvasWidth, at.CanvasHeight) == (picture.Width, picture.Height)
                ? Laid(new PictureSource(path, null), size, at, name)
                : LayerEdits.Fitted(new PictureSource(path, null), size, picture.Width, picture.Height, name));
        }
        if (layers.Count == 0) return;
        document.Commit(p =>
        {
            var placed = p;
            var below = above;
            foreach (var layer in layers)
            {
                placed = below is null ? placed with { Layers = placed.Layers.Insert(0, layer), ActiveId = layer.Id, EditsMask = false } : placed.Inserted(layer, below);
                below = layer.Id;
            }
            return placed;
        });
    }

    /// <summary>An edit's picture as a layer laid exactly over the area it was made from, masked to what of it is shown.</summary>
    private static PictureLayer Laid(PictureSource source, SKSize size, EditedPlace place, string name) => new()
    {
        Name      = name,
        Source    = source,
        Placement = new LayerPlacement(place.Over.Width / (double)size.Width, 0, place.Over.Left, 0, place.Over.Height / (double)size.Height, place.Over.Top),
        Mask      = place.Shown is { } shown ? new LayerMask(shown) : null,
    };

    // ── Drawn by the image model ─────────────────────────────────────────────

    /// <summary>Whether the workspace draws pictures now, or will once a model of Nexaflow's own downloads.</summary>
    public bool OffersGeneration => _ai is not null && GenerateImageOptions.Offered(_ai);

    /// <summary>What Generate layer draws: the prompt and what the model offers, its proportions those nearest the canvas's.</summary>
    public GenerateImageOptions? Options => _options ??= _ai is null ? null : new GenerateImageOptions(_ai, DraftLocation.Root);

    private GenerateImageOptions? _options;

    /// <summary>Generate layer is being set up.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateLayerCommand))]
    private bool _isAsking;

    /// <summary>Opens Generate layer, its proportions those nearest the canvas's.</summary>
    [RelayCommand]
    private void AskForLayer()
    {
        if (_viewer.CurrentDocument is not { } document || Options is not { } options) return;
        options.Prefer(document.Picture.Width, document.Picture.Height);
        options.PropertyChanged -= OnOptionsChanged;
        options.PropertyChanged += OnOptionsChanged;
        IsAsking = true;
    }

    [RelayCommand]
    private void CancelAsking() => IsAsking = false;

    private void OnOptionsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GenerateImageOptions.CanCreate)) GenerateLayerCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Generate layer: the picture set up drawn as one of the workspace's jobs, into a new layer above the active one once it arrives.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerateLayer))]
    private void GenerateLayer()
    {
        if (_viewer.CurrentDocument is not { } document || Draw(Options!) is not var (job, name)) return;
        Follow(job, name, document.Path, document.Picture.ActiveId);
        IsAsking = false;
    }

    private bool CanGenerateLayer() => IsAsking && Options?.CanCreate == true;

    /// <summary>
    /// The picture <paramref name="options"/> set up drawn as one of the workspace's jobs into a draft, not opened — for whoever
    /// asked to take in — with the name it is to be known by; null where it could not be begun, the user told why.
    /// </summary>
    internal (PictureJob Job, string Name)? Draw(GenerateImageOptions options)
    {
        if (_ai is null || options.Asked(DraftLocation.NewFile(Str.Get("Images.Canvas.GeneratedName") + ".png")) is not { } asked) return null;
        try
        {
            return (_ai.MakePicture(asked with { OpensWhenMade = false }), NameOf(asked.Prompt));
        }
        catch (InvalidOperationException ex)
        {
            _shell?.ShowError(ex.Message);
            return null;
        }
    }

    /// <summary>The name a layer drawn from <paramref name="prompt"/> is known by: its first words.</summary>
    internal static string NameOf(string prompt)
    {
        var words = prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= NameWords ? string.Join(' ', words) : string.Join(' ', words.Take(NameWords)) + "…";
    }

    /// <summary>
    /// Follows <paramref name="job"/> in the layers panel while it draws, and once it arrives adds it to the layered picture at
    /// <paramref name="canvas"/> above <paramref name="above"/> — beneath every layer where null — as soon as that picture is
    /// open here, fitted to the canvas or, an edit's, laid where <paramref name="place"/> says; stopped, withheld or failed, it goes.
    /// </summary>
    internal void Follow(PictureJob job, string name, string canvas, Guid? above, EditedPlace? place = null)
    {
        var pending = new PendingPicture(name, job, canvas, above, place);
        Pending.Add(pending);
        _ = ArriveAsync(pending);
    }

    private async Task ArriveAsync(PendingPicture pending)
    {
        var made = await pending.Job.Made;
        if (made is null)
        {
            Pending.Remove(pending);
            return;
        }
        pending.Arrived = made;
        await PlaceAsync(pending);
    }

    /// <summary>A layered picture is shown: what arrived for it while it was not is added now.</summary>
    internal void DocumentShown()
    {
        foreach (var pending in Pending.Where(p => p.Arrived is not null).ToList()) _ = PlaceAsync(pending);
    }

    private async Task PlaceAsync(PendingPicture pending)
    {
        if (pending.Arrived is not { } arrived || _viewer.CurrentDocument is not { } document
            || !string.Equals(document.Path, pending.Canvas, StringComparison.OrdinalIgnoreCase)) return;
        Pending.Remove(pending);
        var above = pending.Above is { } id && document.Picture.Find(id) is null ? document.Picture.ActiveId : pending.Above;
        await AddAsync(document, [(arrived, pending.Name)], above, pending.Place);
    }

    public void Dispose()
    {
        if (_options is null) return;
        _options.PropertyChanged -= OnOptionsChanged;
        _options.Dispose();
    }
}
