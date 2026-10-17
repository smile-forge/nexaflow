using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Markdown.Packaging;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Text.Pages;

/// <summary>
/// Pages to read: what names them — the same key, the same reading, kept from before — the file they come from, their
/// pictures as files (PNG or JPEG) when they are read, deleted once they have been, and the tab showing them, which the
/// Markdown they make leads back to.
/// </summary>
public sealed record PageSource(string Key, string Path, Func<CancellationToken, Task<IReadOnlyList<string>>> Pictures,
                                string OriginKind, IReadOnlyDictionary<string, string> OriginParams);

/// <summary>
/// Pages of writing — a picture of a page, a PDF's scanned pages — converted to Markdown: read by page reading
/// (<see cref="PageReader"/>), or found read before, and opened as a draft in the Markdown editor in the right-hand pane,
/// to be saved where the reader picks — a Markdown Package by default, its figures inside it.
/// <para>
/// A reading is kept, figures and all, under the temp folder by what was read and the models that read it, so the same
/// pages convert again at once and the assistant reads them without reading again. Until page reading is here, what is
/// asked waits for it, and <see cref="Status"/> offers its download; while it reads, it says how far it has got.
/// </para>
/// </summary>
public sealed partial class PageConversion : ObservableObject
{
    /// <summary>What a reading's figures folder is called beside its Markdown, and how its links name it.</summary>
    private const string Figures = "page.figures";

    /// <summary>What each page's own text is kept in beside the reading's Markdown.</summary>
    private const string PagesFile = "pages.json";

    private const string KeptMarkdown = "page.md";

    private readonly IPageReading? _reading;
    private readonly IAIService? _ai;
    private readonly IShellServices _shell;
    private CancellationTokenSource? _cts;
    private (PageSource Source, IPageView? Caller)? _waiting;
    private bool _offering;

    /// <param name="reading">Page reading, where the app has it.</param>
    /// <param name="ai">The workspace's AI, whose image model draws diagrams again; none leaves figures as pictures.</param>
    public PageConversion(IPageReading? reading, IAIService? ai, IShellServices shell)
    {
        _reading = reading;
        _ai = ai;
        _shell = shell;
        if (reading is not null) reading.Changed += (_, _) => _ = shell.RunOnUiAsync(OnToolChanged);
    }

    /// <summary>Whether this machine can read pages at all.</summary>
    public bool Available => _reading is { State: not ToolModelsState.Unavailable };

    /// <summary>Whether page reading is here, so a page can be told from a picture before it is offered.</summary>
    public bool Ready => _reading is { State: ToolModelsState.Ready };

    /// <summary>What the conversion has to say — how far it has got, its download, why it could not — or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    public bool HasStatus => Status is not null;

    /// <summary>Pages are being read.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isReading;

    /// <summary>Page reading is not here, and can be fetched.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool _canDownload;

    /// <summary>Each page's own text, its figures left out, from the last reading read or found; null until one is.</summary>
    public IReadOnlyList<string>? PageTexts { get; private set; }

    /// <summary>Raised, on the UI thread, when pages are read — or found read before — and <see cref="PageTexts"/> is theirs.</summary>
    public event Action? TextRead;

    /// <summary>
    /// Converts <paramref name="source"/>'s pages to Markdown and opens it in the Markdown editor in the right-hand pane of
    /// <paramref name="caller"/>'s window, as a draft offered to be saved beside the source. Until page reading is here it
    /// waits for its download, which <see cref="Status"/> offers. Called on the UI thread.
    /// </summary>
    public async Task ConvertAsync(PageSource source, IPageView? caller)
    {
        var read = await ReadKeptAsync(source, CancellationToken.None);
        if (read is null)
        {
            if (_reading is { State: ToolModelsState.NotDownloaded or ToolModelsState.Downloading }) _waiting = (source, caller);
            return;
        }
        if (read.Value.Read.Markdown.Trim().Length == 0) return;   // Status says nothing was read
        try
        {
            string draft = Draft(source, read.Value.Read, read.Value.Folder);
            OriginBreadcrumbs.Register(draft, source.OriginKind, source.OriginParams, System.IO.Path.GetFileName(source.Path));
            _shell.OpenTab("Markdown",
                           new Dictionary<string, string> { ["path"] = draft, ["draft"] = System.IO.Path.GetDirectoryName(source.Path) ?? "" },
                           caller, inRightPane: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = Str.Format("Text.PageReading.Failed", ex.Message);
        }
    }

    /// <summary>
    /// <paramref name="source"/>'s pages read — found read before, or read now — or null where they cannot be read now:
    /// page reading is not here (<see cref="Status"/> offers it), or the reading failed or was stopped. Called on the UI
    /// thread.
    /// </summary>
    public async Task<PagesRead?> ReadAsync(PageSource source, CancellationToken ct = default)
        => (await ReadKeptAsync(source, ct))?.Read;

    private async Task<(PagesRead Read, string Folder)?> ReadKeptAsync(PageSource source, CancellationToken ct)
    {
        Stop();
        _waiting = null;
        _offering = false;
        CanDownload = false;
        if (_reading is not { State: not ToolModelsState.Unavailable } reading)
        {
            Status = Str.Get("Text.PageReading.Unavailable");
            return null;
        }
        string folder = KeptFolder(source.Key, reading.ModelsVersion);
        if (Kept(folder) is { } kept)
        {
            Found(kept);
            return (kept, folder);
        }
        if (reading.State != ToolModelsState.Ready)
        {
            Offer();
            return null;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _cts = cts;
        IsReading = true;
        Status = Str.Get("Text.PageReading.Preparing");
        IReadOnlyList<string> pictures = [];
        try
        {
            pictures = await source.Pictures(cts.Token);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            var shown = new Shown(so => _ = _shell.RunOnUiAsync(() =>
            {
                if (cts.IsCancellationRequested) return;
                Status = so.DrawingFigure is { } figure
                    ? Str.Format("Text.PageReading.Drawing", figure)
                    : Str.Format("Text.PageReading.Reading", so.Page, so.Pages);
            }));
            var reader = new PageReader(reading, _ai);
            var read = await Task.Run(() => reader.ReadAsync(pictures, System.IO.Path.Combine(folder, Figures), Figures, shown, cts.Token), cts.Token);
            await File.WriteAllTextAsync(System.IO.Path.Combine(folder, PagesFile), JsonSerializer.Serialize(read.Pages), cts.Token);
            await File.WriteAllTextAsync(System.IO.Path.Combine(folder, KeptMarkdown), read.Markdown, cts.Token);
            if (cts.IsCancellationRequested) return null;
            Found(read);
            return (read, folder);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_cts, cts)) Status = null;
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            if (!cts.IsCancellationRequested) Status = Str.Format("Text.PageReading.Failed", ex.Message);
            return null;
        }
        finally
        {
            foreach (var picture in pictures)
                try { File.Delete(picture); } catch (IOException) { }
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
                IsReading = false;
            }
        }
    }

    /// <summary>The reading kept in <paramref name="folder"/>, or null where none is.</summary>
    private static PagesRead? Kept(string folder)
    {
        string markdown = System.IO.Path.Combine(folder, KeptMarkdown);
        if (!File.Exists(markdown)) return null;
        string pages = System.IO.Path.Combine(folder, PagesFile);
        return new PagesRead(File.ReadAllText(markdown),
                             File.Exists(pages) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(pages)) ?? [] : []);
    }

    private void Found(PagesRead read)
    {
        PageTexts = read.Pages;
        Status = read.Markdown.Trim().Length == 0 ? Str.Get("Text.PageReading.Nothing") : null;
        TextRead?.Invoke();
    }

    /// <summary>
    /// The reading as a draft of its own in the temp folder, named after the source — <c>scan.md</c>, its figures in
    /// <c>scan.figures</c> — holding only the figures its text shows, for the Markdown editor to open and the reader to save.
    /// </summary>
    private static string Draft(PageSource source, PagesRead read, string kept)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(source.Path);
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexaflow-pages", "drafts", Guid.NewGuid().ToString("N")[..12]);
        string figures = name + ".figures";
        Directory.CreateDirectory(folder);
        foreach (var figure in MarkdownAttachments.Paths(read.Markdown))
        {
            string from = System.IO.Path.Combine(kept, figure);
            if (!File.Exists(from)) continue;
            string to = System.IO.Path.Combine(folder, figures, System.IO.Path.GetFileName(figure));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: true);
        }
        string draft = System.IO.Path.Combine(folder, name + ".md");
        File.WriteAllText(draft, read.Markdown.Replace("](" + Figures + "/", "](" + Uri.EscapeDataString(figures) + "/", StringComparison.Ordinal));
        return draft;
    }

    /// <summary>Says what page reading needs before pages can be read, and offers its download where it can be fetched.</summary>
    private void Offer()
    {
        _offering = true;
        CanDownload = _reading!.State == ToolModelsState.NotDownloaded;
        Status = _reading.State == ToolModelsState.Downloading
            ? Str.Format("Text.PageReading.Downloading", _reading.DownloadPercent)
            : _reading.DownloadFailure is { } failure ? Str.Format("Text.PageReading.DownloadStopped", failure)
            : Str.Format("Text.PageReading.NeedsTool", SizeFormatter.FormatBytes(_reading.DownloadBytes));
    }

    private void OnToolChanged()
    {
        OnPropertyChanged(nameof(Available));
        OnPropertyChanged(nameof(Ready));
        if (IsReading || !_offering) return;
        if (!Ready)
        {
            if (Available) Offer();
            return;
        }
        // Page reading has arrived: what waited for it goes on; otherwise there is nothing left to say.
        _offering = false;
        CanDownload = false;
        Status = null;
        if (_waiting is { } waiting)
        {
            _waiting = null;
            _ = ConvertAsync(waiting.Source, waiting.Caller);
        }
    }

    /// <summary>Forgets what was asked and what was said about it — the pages it was for are no longer shown.</summary>
    public void Forget()
    {
        Stop();
        _waiting = null;
        _offering = false;
        CanDownload = false;
        Status = null;
        PageTexts = null;
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private void Download() => _reading?.Download();

    /// <summary>Stops the reading under way; what was read of it is not kept.</summary>
    [RelayCommand(CanExecute = nameof(IsReading))]
    private void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        IsReading = false;
    }

    /// <summary>Where the reading of <paramref name="key"/> by these models is kept.</summary>
    private static string KeptFolder(string key, string modelsVersion)
        => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexaflow-pages",
                                  Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key + "\n" + modelsVersion)))[..16]);

    private sealed class Shown(Action<PagesSoFar> show) : IProgress<PagesSoFar>
    {
        public void Report(PagesSoFar value) => show(value);
    }
}
