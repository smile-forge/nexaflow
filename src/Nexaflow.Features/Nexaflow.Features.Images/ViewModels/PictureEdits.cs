using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Generative edits of the picture by the workspace's Image Editing model, each set up where its kind of edit lives: a fill of
/// what is selected with Select's other ways with a selection, an expansion where the crop frame reaches past a photo's edge
/// in Reshape — or a layered picture's canvas grows in Layers — and a change of the picture or a layer in Adjust. Each sends
/// the picture as it is shown — and, for a fill or an expansion, a mask of what is drawn again — as one of the workspace's
/// background jobs, which passes the picture check like every picture made; a photo is made a layered picture first, and
/// what comes back arrives as a new layer laid exactly over what it was made from, shown only where it was drawn: the
/// original stays beneath, so nothing is lost.
/// </summary>
public sealed partial class PictureEdits : ObservableObject
{
    /// <summary>The least context, in pixels, a fill is shown on each side of its selection.</summary>
    private const int ContextMargin = 64;

    /// <summary>How far an expansion's new area is drawn over the picture it grows from, blended into it so the two meet
    /// with no seam.</summary>
    private const int ExpandOverlap = 24;

    /// <summary>What an expansion asks the model for where it is told nothing — in English, which the models read best,
    /// whatever language the app is shown in.</summary>
    public const string ContinuePrompt = "Continue the picture beyond its edges, as it would naturally go on.";

    /// <summary>What the model is shown where the picture is transparent: a neutral grey, which nothing in it suggests.</summary>
    private static readonly SKColor Unpainted = new(128, 128, 128);

    private readonly ArtistViewModel _viewer;
    private readonly IShellServices? _shell;
    private readonly IAIService? _ai;

    internal PictureEdits(ArtistViewModel viewer, IShellServices? shell, IAIService? ai)
    {
        _viewer = viewer;
        _shell  = shell;
        _ai     = ai;
    }

    /// <summary>What the workspace's Image Editing model offers; null where it has none.</summary>
    private PictureChoices? Editing => _ai?.EditsPictures;

    /// <summary>Whether a layer or the picture can be changed by an instruction.</summary>
    public bool OffersChange => Editing is { Sources: > 0 };

    /// <summary>Whether the selection can be drawn again and the canvas expanded: the model draws again under a mask.</summary>
    public bool OffersFill => Editing is { Masks: true };

    /// <summary>Whether any generative edit is offered.</summary>
    public bool OffersEditing => OffersChange || OffersFill;

    /// <summary>Whether a photo's crop frame can reach past its edge to generate what lies beyond: a photo, not a layered
    /// picture, whose canvas grows in Layers instead.</summary>
    public bool OffersExpansionFromCrop => OffersFill && !_viewer.IsLayered;

    // ── Fill, in Select ──────────────────────────────────────────────────────

    /// <summary>Generate fill being set up: what to draw in the selection asked for beneath Select's other ways with it.</summary>
    [ObservableProperty]
    private bool _isAskingFill;

    /// <summary>What to draw in the selection.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateFillCommand))]
    private string _fillPrompt = string.Empty;

    [RelayCommand(CanExecute = nameof(CanAskFill))]
    private void AskFill()
    {
        FillPrompt = string.Empty;
        IsAskingFill = true;
    }

    private bool CanAskFill() => OffersFill && _viewer.Editor.HoldsSelection;

    [RelayCommand]
    private void CancelFill() => IsAskingFill = false;

    /// <summary>The selection drawn again from what was asked; the prompt closes once it is begun.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerateFill))]
    private async Task GenerateFillAsync()
    {
        if (Reported(await FillAsync(FillPrompt))) IsAskingFill = false;
    }

    private bool CanGenerateFill() => !string.IsNullOrWhiteSpace(FillPrompt);

    // ── Expand, in Reshape and Layers ────────────────────────────────────────

    /// <summary>What the new area shows; blank, the picture is carried on as it would go.</summary>
    [ObservableProperty]
    private string _expandPrompt = string.Empty;

    /// <summary>The new area the photo's crop frame reaches past its edge generated, the frame put back on the picture.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerateExpansion))]
    private async Task GenerateExpansionAsync()
    {
        if (_viewer.Editor.NewArea is not { } area) return;
        if (!Reported(await ExpandAsync(area.Left, area.Top, area.Right, area.Bottom, ExpandPrompt))) return;
        _viewer.Editor.ForgetNewArea();
        ExpandPrompt = string.Empty;
    }

    private bool CanGenerateExpansion() => OffersExpansionFromCrop && _viewer.Editor.NewArea is not null;

    /// <summary>Canvas size: what the canvas gains generated, rather than left transparent.</summary>
    [ObservableProperty]
    private bool _generatesCanvasBorder;

    /// <summary>The layered picture's canvas grown to the size and anchor its Canvas size asks, and what it gains generated.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerateCanvas))]
    private async Task GenerateCanvasAsync()
    {
        var layers = _viewer.Layers;
        if (_viewer.CurrentDocument is not { } document) return;
        var (dx, dy) = LayersViewModel.AnchorOffset(layers.CanvasAnchor, document.Picture.Width, document.Picture.Height, layers.CanvasWidth, layers.CanvasHeight);
        if (Reported(await ResizeCanvasAsync(layers.CanvasWidth, layers.CanvasHeight, dx, dy, ExpandPrompt))) ExpandPrompt = string.Empty;
    }

    private bool CanGenerateCanvas() => OffersFill && _viewer.Layers.ResizeCanvasCommand.CanExecute(null);

    // ── Change, in Adjust ────────────────────────────────────────────────────

    /// <summary>What to change.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateChangeCommand))]
    private string _changePrompt = string.Empty;

    /// <summary>Change: the whole picture as shown, rather than the active layer alone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveLayerOnly))]
    private bool _wholePicture = true;

    /// <summary>Change: the active layer alone — the other of <see cref="WholePicture"/>'s two ways, a layered picture's only.</summary>
    public bool ActiveLayerOnly
    {
        get => !WholePicture;
        set => WholePicture = !value;
    }

    /// <summary>The picture, or a layered picture's active layer, changed as asked; the prompt is cleared once it is begun.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerateChange))]
    private async Task GenerateChangeAsync()
    {
        if (Reported(await ChangeAsync(ChangePrompt, WholePicture || !_viewer.IsLayered))) ChangePrompt = string.Empty;
    }

    private bool CanGenerateChange() => OffersChange && !string.IsNullOrWhiteSpace(ChangePrompt);

    /// <summary>Why an edit was not begun, shown; true where it was.</summary>
    private bool Reported(string? problem)
    {
        if (problem is null) return true;
        _shell?.ShowError(problem);
        return false;
    }

    /// <summary>The picture shown, what is selected on it, or its crop frame changed: what can be generated may have.</summary>
    internal void Refresh()
    {
        AskFillCommand.NotifyCanExecuteChanged();
        GenerateExpansionCommand.NotifyCanExecuteChanged();
        GenerateCanvasCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(OffersExpansionFromCrop));
        if (IsAskingFill && !_viewer.Editor.HoldsSelection) IsAskingFill = false;
    }

    // ── The edits ───────────────────────────────────────────────────────────

    /// <summary>
    /// The selection drawn again from <paramref name="prompt"/>: the picture as shown around it — its bounds and as much
    /// again of what lies about them — sent with the selection as the mask, and what comes back a new layer over that area,
    /// masked by the selection, above the active layer. A photo is made a layered picture first, what is selected on it
    /// carried over. Returns why it was not begun; null once it is.
    /// </summary>
    public async Task<string?> FillAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return Str.Get("Images.Edits.NoPrompt");
        if (!OffersFill) return Str.Get("Images.Edits.NoFill");
        if (await _viewer.Editor.LayeredKeepingSelectionAsync() is not { } document) return Str.Get("Images.Edits.NotReady");
        var picture = document.Picture;
        if (picture.Selection.IsEmpty) return Str.Get("Images.Edits.NoSelection");

        var bounds = picture.Selection.Bounds;
        int margin = Math.Max(ContextMargin, Math.Max(bounds.Width, bounds.Height) / 2);
        var area = SKRectI.Intersect(SKRectI.Inflate(bounds, margin, margin), new SKRectI(0, 0, picture.Width, picture.Height));
        if (await ShownAsync(document, picture, area) is not { } png) return Str.Get("Images.Layers.WaitForTools");
        return Send(document, png, area, prompt.Trim(), picture.Selection, maskSent: true);
    }

    /// <summary>
    /// The canvas grown by the given pixels on each side — one step of its history — and its new area generated from
    /// <paramref name="prompt"/>, or as the picture would go on where none is given. A photo is made a layered picture first.
    /// Returns why it was not begun; null once it is.
    /// </summary>
    public async Task<string?> ExpandAsync(int left, int top, int right, int bottom, string? prompt)
    {
        if (!OffersFill) return Str.Get("Images.Edits.NoFill");
        if (left < 0 || top < 0 || right < 0 || bottom < 0 || left + top + right + bottom == 0) return Str.Get("Images.Edits.NoExpansion");
        if (_viewer.MakeLayered() is not { } document) return Str.Get("Images.Edits.NotReady");
        var before = document.Picture;
        return await GrowAsync(document, before.Width + left + right, before.Height + top + bottom, left, top, prompt);
    }

    /// <summary>
    /// The layered picture's canvas made <paramref name="width"/> × <paramref name="height"/>, what it held moved by
    /// (<paramref name="dx"/>, <paramref name="dy"/>) as Canvas size's anchor places it, and what it gains generated from
    /// <paramref name="prompt"/>. Returns why it was not begun; null once it is.
    /// </summary>
    public async Task<string?> ResizeCanvasAsync(int width, int height, int dx, int dy, string? prompt)
    {
        if (!OffersFill) return Str.Get("Images.Edits.NoFill");
        if (_viewer.CurrentDocument is not { } document) return Str.Get("Images.Edits.NotReady");
        return await GrowAsync(document, width, height, dx, dy, prompt);
    }

    /// <summary>
    /// The canvas resized — one step of its history — with what it held moved by (<paramref name="dx"/>,
    /// <paramref name="dy"/>), and the area it gains generated: the whole canvas sent with that area as the mask, and what
    /// comes back a new layer above the active one, shown in the new area and blended a little way over the picture where
    /// the two meet.
    /// </summary>
    private async Task<string?> GrowAsync(LayeredSession document, int width, int height, int dx, int dy, string? prompt)
    {
        if (width > LayeredPicture.MaxSide || height > LayeredPicture.MaxSide) return Str.Format("Images.Edits.TooLargeFormat", LayeredPicture.MaxSide);
        if (width < 1 || height < 1) return Str.Get("Images.Edits.NoExpansion");
        var before = document.Picture;
        var area = new SKRectI(0, 0, width, height);
        var kept = SKRectI.Intersect(new SKRectI(dx, dy, dx + before.Width, dy + before.Height), area);
        if (kept == area) return Str.Get("Images.Edits.NoExpansion");

        document.Commit(p => LayerEdits.Resized(p, width, height, dx, dy));
        var picture = document.Picture;
        // The picture's sides that meet the new area are drawn over a little, to blend; those at the canvas's edge are kept.
        int overlap = ExpandOverlap / 2;
        var inner = new SKRectI(kept.Left + (kept.Left > 0 ? overlap : 0), kept.Top + (kept.Top > 0 ? overlap : 0),
                                kept.Right - (kept.Right < width ? overlap : 0), kept.Bottom - (kept.Bottom < height ? overlap : 0));
        var border = TiledCoverage.Covered(width, height);
        if (inner.Width > 0 && inner.Height > 0) border = border.Written(inner, new byte[inner.Width * inner.Height]);
        border = border.Feathered(overlap);

        if (await ShownAsync(document, picture, area) is not { } png) return Str.Get("Images.Layers.WaitForTools");
        var asked = string.IsNullOrWhiteSpace(prompt) ? ContinuePrompt : prompt.Trim();
        return Send(document, png, area, asked, border, maskSent: true);
    }

    /// <summary>
    /// The whole picture as shown, or the active layer alone, changed by <paramref name="instruction"/>: what comes back a new
    /// layer over what was sent, above the active layer — a layer's change shown only where that layer is. A photo is made a
    /// layered picture first. Returns why it was not begun; null once it is.
    /// </summary>
    public async Task<string?> ChangeAsync(string instruction, bool wholePicture)
    {
        if (string.IsNullOrWhiteSpace(instruction)) return Str.Get("Images.Edits.NoPrompt");
        if (!OffersChange) return Str.Get("Images.Edits.NoChange");
        if (_viewer.MakeLayered() is not { } document) return Str.Get("Images.Edits.NotReady");
        var picture = document.Picture;
        if (wholePicture)
        {
            var whole = new SKRectI(0, 0, picture.Width, picture.Height);
            if (await ShownAsync(document, picture, whole) is not { } png) return Str.Get("Images.Layers.WaitForTools");
            return Send(document, png, whole, instruction.Trim(), null, maskSent: false);
        }

        if (picture.Active is not { } layer) return Str.Get("Images.Edits.NoLayer");
        var alone = picture with { Layers = [layer with { Visible = true, Opacity = 1, Blend = LayerBlend.Normal, ClipsToBelow = false, Mask = null }] };
        using var renderer = await document.RendererForAsync(alone);
        if (renderer is null) return Str.Get("Images.Layers.WaitForTools");
        var pixels = await Task.Run(renderer.Flattened);
        var shape = pixels.Alpha(picture.Width, picture.Height);
        if (shape.IsEmpty) return Str.Get("Images.Edits.EmptyLayer");
        var area = shape.Bounds;
        var drawn = await Task.Run(() =>
        {
            using var bitmap = pixels.ToBitmap(area);
            using var image = SKImage.FromBitmap(bitmap);
            return OpaquePng(image, new SKRectI(0, 0, area.Width, area.Height));
        });
        return Send(document, drawn, area, instruction.Trim(), shape, maskSent: false);
    }

    /// <summary><paramref name="picture"/> over <paramref name="area"/> as it is shown, its background beneath, as the model
    /// reads it; null while a picture layer waits for the picture tools.</summary>
    private static async Task<byte[]?> ShownAsync(LayeredSession document, LayeredPicture picture, SKRectI area)
    {
        using var renderer = await document.RendererForAsync(picture);
        if (renderer is null) return null;
        return await Task.Run(() =>
        {
            using var image = renderer.Composite(1);
            return OpaquePng(image, area);
        });
    }

    /// <summary>
    /// <paramref name="png"/>, the picture over <paramref name="area"/>, sent to the editing model with
    /// <paramref name="prompt"/>, and with <paramref name="shown"/> as the mask of what is drawn again where
    /// <paramref name="maskSent"/>; what comes back follows as a layer over <paramref name="area"/>, shown where
    /// <paramref name="shown"/> covers, above the active layer. Returns why it was not begun; null once it is.
    /// </summary>
    [LocalDiskOnly("what an edit sends the model, written beside the picture it makes in a draft folder of the temp folder, which picture making reads from this disk")]
    private string? Send(LayeredSession document, byte[] png, SKRectI area, string prompt, TiledCoverage? shown, bool maskSent)
    {
        if (_ai is null) return Str.Get("Images.Edits.NoChange");
        var made   = DraftLocation.NewFile(Str.Get("Images.Canvas.GeneratedName") + ".png");
        var folder = Path.GetDirectoryName(made)!;
        var source = Path.Combine(folder, "source.png");
        File.WriteAllBytes(source, png);
        string? mask = null;
        if (maskSent && shown is not null)
        {
            mask = Path.Combine(folder, "mask.png");
            File.WriteAllBytes(mask, MaskPng(shown.Read(area), area.Width, area.Height));
        }

        PictureJob job;
        try
        {
            job = _ai.EditPicture(new PictureEditAsked(prompt, [source], made) { Mask = mask, OpensWhenMade = false });
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
        var picture = document.Picture;
        _viewer.Pictures.Follow(job, CanvasPictures.NameOf(prompt), document.Path, picture.ActiveId,
                                new EditedPlace(picture.Width, picture.Height, area, shown));
        return null;
    }

    /// <summary>An area of a rendered picture as the model reads it: an opaque 8-bit sRGB PNG, its transparency laid on
    /// <see cref="Unpainted"/>.</summary>
    internal static byte[] OpaquePng(SKImage image, SKRectI area)
    {
        using var flat = new SKBitmap(new SKImageInfo(area.Width, area.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        using (var canvas = new SKCanvas(flat))
        {
            canvas.Clear(Unpainted);
            canvas.DrawImage(image, SKRect.Create(area.Left, area.Top, area.Width, area.Height), SKRect.Create(area.Width, area.Height), SKSamplingOptions.Default);
        }
        using var encoded = SKImage.FromBitmap(flat);
        using var png = encoded.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    /// <summary>A mask as the model reads it: a PNG whose transparent part is what is drawn again — where
    /// <paramref name="covered"/>, a byte a pixel, is 255.</summary>
    internal static byte[] MaskPng(ReadOnlySpan<byte> covered, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var pixels = bitmap.GetPixelSpan();
        for (int i = 0; i < covered.Length; i++) pixels[i * 4 + 3] = (byte)(255 - covered[i]);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
