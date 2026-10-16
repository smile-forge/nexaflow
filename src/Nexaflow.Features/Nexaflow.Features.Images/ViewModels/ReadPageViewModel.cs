using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Visuals.Text.Pages;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// The picture on screen read as a page of writing (<see cref="IPageReading"/>). Page reading's text finder looks at each
/// picture as it is shown; where it is a page — prose, a form, a receipt, a scan — the toolbar offers Read page, which
/// opens its text in Markdown beside the picture (<see cref="PageReadingPanel"/>), read from the picture as it is shown,
/// edits and all, at its full size. Until page reading is here nothing can tell a page, so every picture offers it, and the
/// panel offers the download.
/// </summary>
public sealed partial class ReadPageViewModel : ObservableObject
{
    /// <summary>The longest side the text finder is shown a picture at: it reads at 960, and a smaller picture costs less to send.</summary>
    public const int LookEdge = 1280;

    private readonly IPageReading? _reading;
    private readonly ImageEditingServices _editing;
    private readonly Func<Action, Task> _runOnUi;
    private readonly Func<string, ImageRecipe, MadeByTools> _made;
    private ImageEditSession? _session;
    private CancellationTokenSource? _looking;

    /// <param name="made">What the picture tools made of a photo for a recipe — what the page is rendered with.</param>
    public ReadPageViewModel(IPageReading? reading, IAIService? ai, IShellServices? shell, ImageEditingServices editing,
                             Func<Action, Task> runOnUi, Func<string, ImageRecipe, MadeByTools> made)
    {
        _reading = reading;
        _editing = editing;
        _runOnUi = runOnUi;
        _made = made;
        Panel = shell is null ? null : new PageReadingPanel(reading, ai, shell);
        if (reading is not null) reading.Changed += (_, _) => _ = runOnUi(() => Look(_session));
    }

    /// <summary>The page's text beside the picture; none where there is no shell to save it through.</summary>
    public PageReadingPanel? Panel { get; }

    /// <summary>The picture on screen is a page of writing, as far as the text finder can tell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRead))]
    private bool _isPage;

    /// <summary>Read page shows: page reading runs here and the picture is a page — or page reading is not here yet, so
    /// nothing can tell.</summary>
    public bool CanRead => Panel is { Available: true } panel && _session is not null && (!panel.Ready || IsPage);

    /// <summary>The picture now on screen: looked at for writing, and the panel closed on the one before.</summary>
    public void Follow(ImageEditSession? session)
    {
        if (ReferenceEquals(session, _session)) return;
        Panel?.CloseCommand.Execute(null);
        Look(session);
    }

    /// <summary>The picture on screen has been decoded: what was looked at before it came had nothing to show, so it is
    /// looked at now.</summary>
    public void PictureDecoded(ImageEditSession session)
    {
        if (ReferenceEquals(session, _session)) Look(session);
    }

    private void Look(ImageEditSession? session)
    {
        _looking?.Cancel();
        _looking = null;
        _session = session;
        IsPage = false;
        OnPropertyChanged(nameof(CanRead));
        if (session is null || _reading is not { State: ToolModelsState.Ready } reading) return;

        var cts = new CancellationTokenSource();
        _looking = cts;
        var recipe = session.Recipe;
        _ = LookAsync(reading, session, recipe, cts.Token);
    }

    private async Task LookAsync(IPageReading reading, ImageEditSession session, ImageRecipe recipe, CancellationToken ct)
    {
        try
        {
            if (await Task.Run(() => Small(session, recipe), ct) is not { } picture) return;
            var found = await reading.FindTextAsync(picture.Rgba, picture.Width, picture.Height, ct);
            await _runOnUi(() =>
            {
                if (!ct.IsCancellationRequested && ReferenceEquals(_session, session)) IsPage = found.IsPage;
            });
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }   // nothing found to say: the picture is not offered as a page
    }

    /// <summary>The picture as shown, at most <see cref="LookEdge"/> on its longest side, as RGBA.</summary>
    private static (byte[] Rgba, int Width, int Height)? Small(ImageEditSession session, ImageRecipe recipe)
    {
        if (session.OutputSize(recipe) is not { } output || output.Width <= 0 || output.Height <= 0) return null;
        double scale = Math.Min(1, (double)LookEdge / Math.Max(output.Width, output.Height));
        using var picture = session.Snapshot(recipe, new RenderRequest(RenderFrame.Output, scale));
        if (picture is null) return null;
        var info = new SKImageInfo(picture.Width, picture.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        return picture.ReadPixels(bitmap.PeekPixels()) ? (bitmap.Bytes, picture.Width, picture.Height) : null;
    }

    /// <summary>Opens the picture's text beside it: kept from before for the same picture and edits, else read now.</summary>
    [RelayCommand]
    private void Read()
    {
        if (Panel is null || _session is not { } session) return;
        string path = session.Path;
        var recipe = session.Recipe;
        Panel.Open(Key(path, recipe), path, ct => Task.Run(() => Page(path, recipe, ct), ct));
    }

    /// <summary>The page as shown, edits and all, at full size, in a PNG file of its own.</summary>
    private IReadOnlyList<string> Page(string path, ImageRecipe recipe, CancellationToken ct)
    {
        var png = new ImageExporter(_editing).Export(path, recipe, ImageContainer.Png, ct, _made(path, recipe));
        string file = Path.Combine(Path.GetTempPath(), $"nexaflow-page-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(file, png);
        return [file];
    }

    /// <summary>What names a picture's text: the file as it is now, and the edits it is shown with.</summary>
    private static string Key(string path, ImageRecipe recipe)
    {
        var file = new FileInfo(path);
        return $"{path}|{(file.Exists ? file.Length : 0)}|{(file.Exists ? file.LastWriteTimeUtc.Ticks : 0)}|{ImageRecipeJson.Serialize(recipe)}";
    }
}
