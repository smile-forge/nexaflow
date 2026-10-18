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
/// The picture on screen converted to Markdown as a page of writing (<see cref="IPageReading"/>). Page reading's text finder
/// looks at each picture once it is there to look at; where it is a page — prose, a form, a receipt, a scan — the toolbar
/// offers Convert to Markdown, which reads it as it is shown, edits and all, at its full size, and opens the Markdown in the
/// Markdown editor in the right-hand pane (<see cref="PageConversion"/>). Until page reading is here nothing can tell a
/// page, so every picture offers it, and the conversion offers the download.
/// </summary>
public sealed partial class ReadPageViewModel : ObservableObject
{
    /// <summary>The longest side the text finder is shown a picture at: it reads at 960, and a smaller picture costs less to send.</summary>
    public const int LookEdge = 1280;

    private readonly IPageReading? _reading;
    private readonly ImageEditingServices _editing;
    private readonly Func<Action, Task> _runOnUi;
    private readonly Func<string, ImageRecipe, MadeByTools> _made;
    private readonly Func<IPageView?> _host;
    private ImageEditSession? _session;
    private CancellationTokenSource? _looking;

    /// <param name="made">What the picture tools made of a photo for a recipe — what the page is rendered with.</param>
    /// <param name="host">The tab showing the picture, beside which the Markdown opens.</param>
    public ReadPageViewModel(IPageReading? reading, IAIService? ai, IShellServices? shell, ImageEditingServices editing,
                             Func<Action, Task> runOnUi, Func<string, ImageRecipe, MadeByTools> made, Func<IPageView?> host)
    {
        _reading = reading;
        _editing = editing;
        _runOnUi = runOnUi;
        _made = made;
        _host = host;
        Conversion = shell is null ? null : new PageConversion(reading, ai, shell);
        if (reading is not null) reading.Changed += (_, _) => _ = runOnUi(() => Look(_session));
    }

    /// <summary>The picture's conversion to Markdown; none where there is no shell to open it through.</summary>
    public PageConversion? Conversion { get; }

    /// <summary>The picture on screen is a page of writing, as far as the text finder can tell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRead))]
    private bool _isPage;

    /// <summary>Convert to Markdown shows: page reading runs here and the picture is a page — or page reading is not here
    /// yet, so nothing can tell.</summary>
    public bool CanRead => Conversion is { Available: true } conversion && _session is not null && (!conversion.Ready || IsPage);

    /// <summary>The picture now on screen: looked at for writing, and what was said about the one before forgotten.</summary>
    public void Follow(ImageEditSession? session)
    {
        if (ReferenceEquals(session, _session)) return;
        Conversion?.Forget();
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

    /// <summary>Converts the picture to Markdown and opens it in the right-hand pane: kept from before for the same picture
    /// and edits, else read now.</summary>
    [RelayCommand]
    private Task Convert() => Conversion is not null && Source() is { } source ? Conversion.ConvertAsync(source, _host()) : Task.CompletedTask;

    /// <summary>The picture read as a page — kept from before, or read now — or null where it cannot be read now; for the
    /// assistant, which is handed its text. Called on the UI thread.</summary>
    public Task<PagesRead?> ReadAsync(CancellationToken ct)
        => Conversion is not null && Source() is { } source ? Conversion.ReadAsync(source, ct) : Task.FromResult<PagesRead?>(null);

    /// <summary>The picture on screen as pages to read: named by the file and its edits, rendered as shown when read.</summary>
    private PageSource? Source()
    {
        if (_session is not { } session) return null;
        string path = session.Path;
        var recipe = session.Recipe;
        return new PageSource(Key(path, recipe), path, 1, (_, ct) => Task.Run<string?>(() => Page(path, recipe, ct), ct),
                              ImageTabRegistration.StaticPageKind, new Dictionary<string, string> { ["paths"] = path });
    }

    /// <summary>The page as shown, edits and all, at full size, in a PNG file of its own.</summary>
    private string Page(string path, ImageRecipe recipe, CancellationToken ct)
    {
        var png = new ImageExporter(_editing).Export(path, recipe, ImageContainer.Png, ct, _made(path, recipe));
        string file = Path.Combine(Path.GetTempPath(), $"nexaflow-page-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(file, png);
        return file;
    }

    /// <summary>What names a picture's text: the file as it is now, and the edits it is shown with.</summary>
    private static string Key(string path, ImageRecipe recipe)
    {
        var file = new FileInfo(path);
        return $"{path}|{(file.Exists ? file.Length : 0)}|{(file.Exists ? file.LastWriteTimeUtc.Ticks : 0)}|{ImageRecipeJson.Serialize(recipe)}";
    }
}
