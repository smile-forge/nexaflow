using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Services;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>A click on the photo, in its pixels as it stands: on the thing to keep, or off it.</summary>
public sealed record CutPoint(double X, double Y, bool Include);

/// <summary>
/// The editor's cut-out tool: a thing in the photo picked out by clicks on it and off it, a box drawn round it, or
/// everything a phrase names — by picture selection, on this machine — shown over the photo, and copied or saved cut out
/// of its background. The photo is read for selection once, the first time something is picked from it, and let go when
/// the editor moves to another photo or closes; each pick after that is answered from the one reading. What it is doing,
/// or why it cannot, it says beneath the tool.
/// </summary>
public sealed partial class PictureCutter : ObservableObject
{
    /// <summary>The longest side a selection is shown at: sharp on screen, a fraction of a large photo's pixels.</summary>
    public const int ShownLongSide = 1280;

    private readonly ImageEditingServices _services;
    private readonly IPictureSelection? _selection;
    private readonly IShellServices? _shell;
    private readonly Func<Action, Task> _runOnUi;
    private readonly List<CutPoint> _points = [];
    private string? _photo;
    private Task<Opened>? _opened;
    private CancellationTokenSource? _asking;
    private IReadOnlyList<PictureFind> _finds = [];

    private sealed record Opened(IPictureSelectionSession Session, UprightPixels Photo);

    /// <param name="selection">Picture selection, where the app has it.</param>
    /// <param name="runOnUi">Hops to the UI thread, where this is read.</param>
    public PictureCutter(ImageEditingServices services, IPictureSelection? selection, IShellServices? shell, Func<Action, Task> runOnUi)
    {
        _services = services;
        _selection = selection;
        _shell = shell;
        _runOnUi = runOnUi;
        if (selection is not null) selection.Changed += (_, _) => _ = _runOnUi(Refresh);
        Refresh();
    }

    /// <summary>Whether this machine can pick things out of pictures at all — what the editor's tool shows for.</summary>
    public bool Available => _selection is { State: not ToolModelsState.Unavailable };

    /// <summary>Picture selection is here, ready to pick with.</summary>
    public bool CanPick => _selection is { State: ToolModelsState.Ready };

    /// <summary>The clicks so far, in the photo's pixels as it stands.</summary>
    public IReadOnlyList<CutPoint> Points => _points;

    /// <summary>The box drawn round the thing, in the photo's pixels as it stands; none drawn, null.</summary>
    [ObservableProperty] private PictureBox? _box;

    /// <summary>What to find, in a few words.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private string _phrase = "";

    /// <summary>The phrase whose finds are shown; null where clicks and a box say what is picked.</summary>
    [ObservableProperty] private string? _found;

    /// <summary>What is picked, at most <see cref="ShownLongSide"/> on its longer side, for showing over the photo; its
    /// bounds are in the photo's pixels. Null where nothing is picked yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(SaveCutOutCommand))]
    private PictureMask? _shown;

    /// <summary>Something is picked, to copy or save.</summary>
    public bool HasSelection => Shown?.Bounds is not null;

    /// <summary>What is picked, as a recipe's cut-out — the phrase whose finds are shown, else the clicks and the box — for
    /// removing the photo's background by; null where nothing is picked.</summary>
    public RecipeCutOut? PickedCutOut => !HasSelection ? null
        : Found is { } found ? new RecipeCutOut { Phrase = found }
        : new RecipeCutOut
        {
            Clicks = [.. _points.Select(p => new RecipeCutClick(p.X, p.Y, p.Include))],
            Box = Box is { } b ? new RecipeCutBox(b.Left, b.Top, b.Right, b.Bottom) : null,
        };

    /// <summary>The photo is being read, or a pick made.</summary>
    [ObservableProperty] private bool _isWorking;

    /// <summary>What it says beneath the tool: how to pick, what it is doing, or why it cannot.</summary>
    [ObservableProperty] private string _notice = "";

    /// <summary>The last pick failed; it is tried again when asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction), nameof(ActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    private bool _failed;

    /// <summary>The tool has something to offer: the pick to try again. Picture selection itself is offered over the picture,
    /// with the editor's other models.</summary>
    public bool HasAction => Failed;

    /// <summary>What the tool's action button says.</summary>
    public string? ActionLabel => Failed ? Str.Get("Images.CutOut.Retry") : null;

    /// <summary>The photo the editor is on; another one lets the last go, with everything picked from it.</summary>
    public void Follow(string? photo)
    {
        if (photo == _photo) return;
        Close();
        _photo = photo;
    }

    /// <summary>Lets the photo go: the selection it was read for ends, and nothing is picked.</summary>
    public void Close()
    {
        _asking?.Cancel();
        _asking = null;
        var opened = _opened;
        _opened = null;
        _photo = null;
        if (opened is not null) _ = EndAsync(opened);
        ClearPicks();
    }

    private static async Task EndAsync(Task<Opened> opened)
    {
        try { await (await opened).Session.DisposeAsync(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { }   // never read, or already ended: nothing to let go
    }

    // ── Picking ─────────────────────────────────────────────────────────────

    /// <summary>A click on the photo at (<paramref name="x"/>, <paramref name="y"/>) in its pixels as it stands: on the
    /// thing to keep where <paramref name="include"/>, else off it. Finds a phrase showed give way to it.</summary>
    public void Click(double x, double y, bool include)
    {
        if (Found is not null) ClearPicks();
        _points.Add(new CutPoint(x, y, include));
        OnPropertyChanged(nameof(Points));
        _ = PickAsync(phrase: null);
    }

    /// <summary>A box drawn round the thing, in the photo's pixels as it stands; it takes the place of any drawn before.</summary>
    public void Boxed(PictureBox box)
    {
        if (Found is not null) ClearPicks();
        Box = box;
        _ = PickAsync(phrase: null);
    }

    [RelayCommand(CanExecute = nameof(CanFind))]
    private Task Find()
    {
        ClearPicks();
        return PickAsync(Phrase.Trim());
    }

    private bool CanFind() => Phrase.Trim().Length > 0;

    /// <summary>Picks out everything <paramref name="phrase"/> names, as Find does, and gives what was found: none where
    /// nothing is, or where the pick could not be made — <see cref="Notice"/> then says why.</summary>
    public async Task<IReadOnlyList<PictureFind>> FindNamedAsync(string phrase)
    {
        Phrase = phrase;
        ClearPicks();
        await PickAsync(phrase.Trim());
        return _finds;
    }

    /// <summary>Picks the thing inside <paramref name="box"/>, as a box drawn round it does, and gives whether anything was
    /// picked — not where nothing stands out there, or the pick could not be made; <see cref="Notice"/> then says why.</summary>
    public async Task<bool> BoxedAsync(PictureBox box)
    {
        ClearPicks();
        Box = box;
        await PickAsync(phrase: null);
        return HasSelection;
    }

    [RelayCommand]
    private void Clear()
    {
        _asking?.Cancel();
        ClearPicks();
    }

    [RelayCommand(CanExecute = nameof(HasAction))]
    private Task Act()
    {
        Failed = false;
        _opened = null;   // a selection that failed may have ended; the photo is read again
        return PickAsync(Found);
    }

    private void ClearPicks()
    {
        _points.Clear();
        Box = null;
    Found = null;
        _finds = [];
        Shown = null;
        Failed = false;
        OnPropertyChanged(nameof(Points));
        Refresh();
    }

    /// <summary>Picks what the clicks and box say, or — given a phrase — everything it names; the latest ask wins.</summary>
    private async Task PickAsync(string? phrase)
    {
        _asking?.Cancel();
        var asking = new CancellationTokenSource();
        _asking = asking;
        if (!Ready() || _photo is null) return;
        if (phrase is null && _points.Count == 0 && Box is null)
        {
            Shown = null;
            Refresh();
            return;
        }

        IsWorking = true;
        Notice = Str.Get(_opened is { IsCompletedSuccessfully: true } ? "Images.CutOut.Picking" : "Images.CutOut.Reading");
        try
        {
            var opened = await Open();
            var (width, height) = ShownSize(opened.Photo.Width, opened.Photo.Height);
            PictureMask mask;
            if (phrase is not null)
            {
                var finds = await opened.Session.FindAsync(phrase, [], width, height, asking.Token);
                mask = Union(finds, width, height);
                if (asking.IsCancellationRequested) return;
    Found = phrase;
                _finds = finds;
                Notice = finds.Count == 0 ? Str.Format("Images.CutOut.NothingFound", phrase) : Str.Format("Images.CutOut.FoundFormat", finds.Count, phrase);
            }
            else
            {
                mask = await opened.Session.SelectAsync(Asked(), Box, width, height, asking.Token);
                if (asking.IsCancellationRequested) return;
                Notice = mask.Bounds is null ? Str.Get("Images.CutOut.NothingThere") : Str.Get("Images.CutOut.Picked");
            }
            Shown = mask;
        }
        catch (OperationCanceledException)
        {
            // a later ask took its place
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException)
        {
            if (asking.IsCancellationRequested) return;
            _opened = null;
            Failed = true;
            Notice = Str.Format("Images.CutOut.Failed", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_asking, asking)) IsWorking = false;
        }
    }

    private IReadOnlyList<PicturePoint> Asked() => [.. _points.Select(p => new PicturePoint(p.X, p.Y, p.Include))];

    /// <summary>The selection on the photo, opened the first time something is picked from it: the photo read as it
    /// stands, then handed over.</summary>
    private Task<Opened> Open()
    {
        var photo = _photo!;
        return _opened ??= OpenAsync(photo);
    }

    private async Task<Opened> OpenAsync(string photo)
    {
        var upright = await Task.Run(() => _services.ReadUpright(photo));
        var session = await _selection!.OpenAsync(upright.Rgba, upright.Width, upright.Height, CancellationToken.None);
        return new Opened(session, upright);
    }

    /// <summary>Picture selection is here: else what to do about it is said, and nothing is picked.</summary>
    private bool Ready()
    {
        Refresh();
        return _selection is { State: ToolModelsState.Ready };
    }

    /// <summary>Says where picture selection stands, and how to pick once it is here.</summary>
    private void Refresh()
    {
        
        if (IsWorking || Failed) return;
        Notice = _selection?.State switch
        {
            null or ToolModelsState.Unavailable => Str.Get("Images.CutOut.Unavailable"),
            ToolModelsState.NotDownloaded when _selection!.DownloadFailure is { } failure => Str.Format("Images.CutOut.DownloadStopped", failure),
            ToolModelsState.NotDownloaded => Str.Format("Images.CutOut.NeedsTool", SizeFormatter.FormatBytes(_selection!.DownloadBytes)),
            ToolModelsState.Downloading => Str.Format("Images.CutOut.Downloading", _selection!.DownloadPercent),
            _ when Shown?.Bounds is not null => Notice,
            _ => Str.Get("Images.CutOut.Hint"),
        };
    }

    private static (int Width, int Height) ShownSize(int width, int height)
    {
        double scale = Math.Min(1.0, (double)ShownLongSide / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>Everything found as one mask, its bounds round them all.</summary>
    private static PictureMask Union(IReadOnlyList<PictureFind> finds, int width, int height)
    {
        var pixels = new byte[width * height];
        foreach (var find in finds)
            for (int i = 0; i < pixels.Length; i++) pixels[i] |= find.Mask.Pixels[i];
        PictureBox? bounds = finds.Count == 0 ? null : new PictureBox(finds.Min(f => f.Box.Left), finds.Min(f => f.Box.Top),
                                                                       finds.Max(f => f.Box.Right), finds.Max(f => f.Box.Bottom));
        return new PictureMask(pixels, width, height, bounds);
    }

    // ── Keeping it ──────────────────────────────────────────────────────────

    /// <summary>What is picked, cut out of the photo at its full size — transparent around it, cropped to it — as a PNG.</summary>
    internal async Task<byte[]?> CutOutAsync(CancellationToken ct)
    {
        var opened = await Open();
        var photo = opened.Photo;
        IReadOnlyList<byte[]> masks = Found is { } phrase
            ? [.. (await opened.Session.FindAsync(phrase, [], photo.Width, photo.Height, ct)).Select(f => f.Mask.Pixels)]
            : [(await opened.Session.SelectAsync(Asked(), Box, photo.Width, photo.Height, ct)).Pixels];
        return await Task.Run(() => PictureCutOut.Of(photo.Rgba, photo.Width, photo.Height, masks) is var (rgba, w, h)
            ? Png(rgba, w, h, photo.Space)
            : null, ct);
    }

    private static byte[] Png(byte[] rgba, int width, int height, SKColorSpace space)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul, space));
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Copy()
    {
        try
        {
            if (await CutOutAsync(CancellationToken.None) is { } png)
            {
                PictureClipboard.CopyPng(png);
                _shell?.ShowNotification(Str.Get("Images.CutOut.Copied"));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.ExternalException)
        {
            _shell?.ShowError(Str.Format("Images.CutOut.Failed", ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SaveCutOut()
    {
        if (_shell is null || _photo is null) return;
        var suggested = Path.GetFileNameWithoutExtension(_photo) + Str.Get("Images.CutOut.FileSuffix") + ".png";
        var folder = ImageEditingServices.IsFileBacked(_photo) ? Path.GetDirectoryName(_photo) : null;
        var destination = await _shell.PickSaveFileAsync(suggested, [".png"], folder);
        if (destination is null) return;
        try
        {
            if (await CutOutAsync(CancellationToken.None) is not { } png) return;
            await File.WriteAllBytesAsync(Path.ChangeExtension(destination, ".png"), png);
            _shell.ShowNotification(Str.Format("Images.CutOut.SavedFormat", Path.GetFileName(destination)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _shell.ShowError(Str.Format("Images.CutOut.Failed", ex.Message));
        }
    }
}
