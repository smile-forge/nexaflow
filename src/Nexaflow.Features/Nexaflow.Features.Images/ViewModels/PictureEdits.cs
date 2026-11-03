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

/// <summary>Which AI edit of a layered picture is being set up.</summary>
public enum PictureEditKind
{
    /// <summary>The selection drawn again from a prompt.</summary>
    Fill,

    /// <summary>The canvas grown and its new border drawn.</summary>
    Expand,

    /// <summary>A layer or the whole picture changed by an instruction.</summary>
    Change,
}

/// <summary>
/// AI edits of a layered picture by the workspace's Image Editing model: the selection drawn again from a prompt (Fill), the
/// canvas grown and its new border drawn (Expand), and a layer or the whole picture changed by an instruction (Change). Each
/// sends the picture as it is shown — and, for Fill and Expand, a mask of what is drawn again — as one of the workspace's
/// background jobs, which passes the picture check like every picture made, and what comes back arrives as a new layer laid
/// exactly over what it was made from, shown only where it was drawn: the original stays beneath, so nothing is lost.
/// </summary>
public sealed partial class PictureEdits : ObservableObject
{
    /// <summary>The least context, in pixels, a fill is shown on each side of its selection.</summary>
    private const int ContextMargin = 64;

    /// <summary>How far an expansion's new border is drawn over the picture it grows from, blended into it so the two meet
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

    /// <summary>Whether any AI edit is offered.</summary>
    public bool OffersEditing => OffersChange || OffersFill;

    /// <summary>The edit being set up; null when none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking), nameof(IsAskingExpand), nameof(IsAskingChange), nameof(PromptLabel))]
    [NotifyCanExecuteChangedFor(nameof(GoCommand))]
    private PictureEditKind? _asking;

    public bool IsAsking => Asking is not null;

    public bool IsAskingExpand => Asking == PictureEditKind.Expand;

    public bool IsAskingChange => Asking == PictureEditKind.Change;

    /// <summary>What to draw in the selection, what to draw in the new border, or the instruction.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoCommand))]
    private string _prompt = string.Empty;

    /// <summary>What the prompt is asked for as, for the edit being set up.</summary>
    public string PromptLabel => Asking switch
    {
        PictureEditKind.Fill   => Str.Get("Images.Edits.FillPrompt"),
        PictureEditKind.Expand => Str.Get("Images.Edits.ExpandPromptLabel"),
        _                      => Str.Get("Images.Edits.ChangePrompt"),
    };

    /// <summary>Change: the whole picture as shown, rather than the active layer alone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveLayerOnly))]
    private bool _wholePicture = true;

    /// <summary>Change: the active layer alone — the other of <see cref="WholePicture"/>'s two ways.</summary>
    public bool ActiveLayerOnly
    {
        get => !WholePicture;
        set => WholePicture = !value;
    }

    /// <summary>Expand: how many pixels the canvas grows by on each side.</summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GoCommand))] private int _expandLeft = 256;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GoCommand))] private int _expandTop;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GoCommand))] private int _expandRight = 256;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GoCommand))] private int _expandBottom;

    /// <summary>Whether the layered picture shown has a selection to fill.</summary>
    private bool HasSelection => _viewer.CurrentDocument?.Picture.Selection.IsEmpty == false;

    [RelayCommand(CanExecute = nameof(CanAskFill))]
    private void AskFill() => Ask(PictureEditKind.Fill);

    private bool CanAskFill() => OffersFill && HasSelection;

    [RelayCommand(CanExecute = nameof(OffersFill))]
    private void AskExpand() => Ask(PictureEditKind.Expand);

    [RelayCommand(CanExecute = nameof(OffersChange))]
    private void AskChange() => Ask(PictureEditKind.Change);

    private void Ask(PictureEditKind kind)
    {
        Prompt = string.Empty;
        Asking = kind;
    }

    [RelayCommand]
    private void Cancel() => Asking = null;

    /// <summary>The edit set up, begun; the panel closes once it is.</summary>
    [RelayCommand(CanExecute = nameof(CanGo))]
    private async Task GoAsync()
    {
        var problem = Asking switch
        {
            PictureEditKind.Fill   => await FillAsync(Prompt),
            PictureEditKind.Expand => await ExpandAsync(ExpandLeft, ExpandTop, ExpandRight, ExpandBottom, Prompt),
            PictureEditKind.Change => await ChangeAsync(Prompt, WholePicture),
            _                      => null,
        };
        if (problem is not null) _shell?.ShowError(problem);
        else Asking = null;
    }

    private bool CanGo() => Asking switch
    {
        PictureEditKind.Expand => ExpandLeft >= 0 && ExpandTop >= 0 && ExpandRight >= 0 && ExpandBottom >= 0
                                  && ExpandLeft + ExpandTop + ExpandRight + ExpandBottom > 0,
        null                   => false,
        _                      => !string.IsNullOrWhiteSpace(Prompt),
    };

    /// <summary>The layered picture shown changed: whether its selection can be filled may have.</summary>
    internal void Refresh()
    {
        AskFillCommand.NotifyCanExecuteChanged();
        if (Asking == PictureEditKind.Fill && !HasSelection) Asking = null;
    }

    // ── The edits ───────────────────────────────────────────────────────────

    /// <summary>
    /// The selection drawn again from <paramref name="prompt"/>: the picture as shown around it — its bounds and as much
    /// again of what lies about them — sent with the selection as the mask, and what comes back a new layer over that area,
    /// masked by the selection, above the active layer. The photo is made a layered picture first, its selection kept.
    /// Returns why it was not begun; null once it is.
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
    /// The canvas grown by the given pixels on each side — one step of its history — and its new border drawn from
    /// <paramref name="prompt"/>, or as the picture would go on where none is given: the whole canvas sent with the border as
    /// the mask, and what comes back a new layer above the active one, shown in the border and blended a little way over the
    /// picture it grew from. Returns why it was not begun; null once it is.
    /// </summary>
    public async Task<string?> ExpandAsync(int left, int top, int right, int bottom, string? prompt)
    {
        if (!OffersFill) return Str.Get("Images.Edits.NoFill");
        if (left < 0 || top < 0 || right < 0 || bottom < 0 || left + top + right + bottom == 0) return Str.Get("Images.Edits.NoExpansion");
        if (_viewer.MakeLayered() is not { } document) return Str.Get("Images.Edits.NotReady");
        var before = document.Picture;
        int width = before.Width + left + right, height = before.Height + top + bottom;
        if (width > LayeredPicture.MaxSide || height > LayeredPicture.MaxSide)
            return Str.Format("Images.Edits.TooLargeFormat", LayeredPicture.MaxSide);

        document.Commit(p => LayerEdits.Resized(p, width, height, left, top));
        var picture = document.Picture;
        var area = new SKRectI(0, 0, width, height);
        var inner = SKRectI.Inflate(new SKRectI(left, top, left + before.Width, top + before.Height), -ExpandOverlap / 2, -ExpandOverlap / 2);
        var border = TiledCoverage.Covered(width, height);
        if (inner.Width > 0 && inner.Height > 0) border = border.Written(inner, new byte[inner.Width * inner.Height]);
        border = border.Feathered(ExpandOverlap / 2.0);

        if (await ShownAsync(document, picture, area) is not { } png) return Str.Get("Images.Layers.WaitForTools");
        var asked = string.IsNullOrWhiteSpace(prompt) ? ContinuePrompt : prompt.Trim();
        return Send(document, png, area, asked, border, maskSent: true);
    }

    /// <summary>
    /// The whole picture as shown, or the active layer alone, changed by <paramref name="instruction"/>: what comes back a new
    /// layer over what was sent, above the active layer — a layer's change shown only where that layer is. Returns why it
    /// was not begun; null once it is.
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
