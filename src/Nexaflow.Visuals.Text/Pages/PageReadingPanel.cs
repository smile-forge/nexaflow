using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Text.Pages;

/// <summary>
/// The text of pages, read into Markdown, in a panel beside what shows them — a picture of a page, a PDF's scanned pages.
/// Opened on what to read, it shows the reading kept from before for the same pages, or reads them once page reading is
/// here, the document filling in as each block is read; until then it offers the download. The reading is kept, figures
/// and all, under the temp folder, so the same pages open on their text again; it can be copied, or saved as Markdown
/// with its figures beside it.
/// </summary>
public sealed partial class PageReadingPanel : ObservableObject
{
    /// <summary>What a reading's figures folder is called beside its Markdown, and how its links name it.</summary>
    private const string Figures = "page.figures";

    /// <summary>What each page's own text is kept in beside the reading's Markdown.</summary>
    private const string PagesFile = "pages.json";

    private readonly IPageReading? _reading;
    private readonly IAIService? _ai;
    private readonly IShellServices _shell;
    private Func<CancellationToken, Task<IReadOnlyList<string>>>? _pages;
    private string _key = "";
    private string _name = "";
    private CancellationTokenSource? _cts;

    private TaskCompletionSource _done = Finished();

    /// <param name="reading">Page reading, where the app has it.</param>
    /// <param name="ai">The workspace's AI, whose image model draws diagrams again; none leaves figures as pictures.</param>
    public PageReadingPanel(IPageReading? reading, IAIService? ai, IShellServices shell)
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

    [ObservableProperty] private bool _isOpen;

    /// <summary>The pages' text, as much of it as is read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(SaveCommand))]
    private string _markdown = "";

    public bool HasText => Markdown.Length > 0;

    /// <summary>Each page's text on its own, its figures left out, once the pages are read; null until then.</summary>
    public IReadOnlyList<string>? PageTexts { get; private set; }

    /// <summary>Completes when what the panel was last opened on is read, found read before, or cannot be read now —
    /// page reading not here, or the reading failed or stopped. <see cref="PageTexts"/> says which.</summary>
    public Task Done => _done.Task;

    private static TaskCompletionSource Finished()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }

    /// <summary>Raised when the pages are read — or found read before — and <see cref="PageTexts"/> is theirs.</summary>
    public event Action? TextRead;

    /// <summary>Where the reading's figures are found from: the folder its Markdown is kept in.</summary>
    [ObservableProperty] private string? _folder;

    /// <summary>What the panel says above the text; null when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    public bool HasStatus => Status is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isReading;

    /// <summary>Page reading is not here, and can be fetched.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool _canDownload;

    /// <summary>
    /// Opens the panel on pages: <paramref name="key"/> names them — the same key, the same text, so a reading kept from
    /// before is shown — <paramref name="name"/> is what a save offers to call them, and <paramref name="pages"/> gives
    /// their pictures as files, PNG or JPEG, when they are read; the panel deletes them once it has.
    /// </summary>
    public void Open(string key, string name, Func<CancellationToken, Task<IReadOnlyList<string>>> pages)
    {
        Stop();
        _done.TrySetResult();
        _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _key = key;
        _name = name;
        _pages = pages;
        Markdown = "";
        PageTexts = null;
        Folder = null;
        IsOpen = true;
        Start();
    }

    private void Start()
    {
        CanDownload = false;
        if (_reading is null || _reading.State == ToolModelsState.Unavailable)
        {
            Status = Str.Get("Text.PageReading.Unavailable");
            _done.TrySetResult();
            return;
        }
        string folder = KeptFolder(_key, _reading.ModelsVersion);
        string kept = Path.Combine(folder, "page.md");
        if (File.Exists(kept))
        {
            Folder = folder;
            Markdown = File.ReadAllText(kept);
            PageTexts = File.Exists(Path.Combine(folder, PagesFile))
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(folder, PagesFile))) ?? []
                : [];
            TextRead?.Invoke();
            Status = Markdown.Length == 0 ? Str.Get("Text.PageReading.Nothing") : null;
            _done.TrySetResult();
            return;
        }
        if (_reading.State != ToolModelsState.Ready)
        {
            CanDownload = _reading.State == ToolModelsState.NotDownloaded;
            Status = _reading.State == ToolModelsState.Downloading
                ? Str.Format("Text.PageReading.Downloading", _reading.DownloadPercent)
                : Str.Format("Text.PageReading.NeedsTool", SizeFormatter.FormatBytes(_reading.DownloadBytes));
            _done.TrySetResult();
            return;
        }
        _ = ReadAsync(folder, _done);
    }

    private async Task ReadAsync(string folder, TaskCompletionSource done)
    {
        var cts = new CancellationTokenSource();
        _cts = cts;
        IsReading = true;
        Status = Str.Get("Text.PageReading.Preparing");
        IReadOnlyList<string> pages = [];
        try
        {
            pages = await _pages!(cts.Token);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            Folder = folder;
            var shown = new Shown(so => _ = _shell.RunOnUiAsync(() =>
            {
                if (cts.IsCancellationRequested) return;
                Markdown = so.Markdown;
                Status = Str.Format("Text.PageReading.Reading", so.Page, so.Pages);
            }));
            var reader = new PageReader(_reading!, _ai);
            var read = await Task.Run(() => reader.ReadAsync(pages, Path.Combine(folder, Figures), Figures, shown, cts.Token), cts.Token);
            await File.WriteAllTextAsync(Path.Combine(folder, PagesFile), JsonSerializer.Serialize(read.Pages), cts.Token);
            await File.WriteAllTextAsync(Path.Combine(folder, "page.md"), read.Markdown, cts.Token);
            if (cts.IsCancellationRequested) return;
            Markdown = read.Markdown;
            PageTexts = read.Pages;
            TextRead?.Invoke();
            Status = read.Markdown.Trim().Length == 0 ? Str.Get("Text.PageReading.Nothing") : null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            if (!cts.IsCancellationRequested) Status = Str.Format("Text.PageReading.Failed", ex.Message);
        }
        finally
        {
            foreach (var page in pages)
                try { File.Delete(page); } catch (IOException) { }
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
                IsReading = false;
            }
            done.TrySetResult();
        }
    }

    private void OnToolChanged()
    {
        OnPropertyChanged(nameof(Available));
        OnPropertyChanged(nameof(Ready));
        if (IsOpen && !IsReading && Markdown.Length == 0) Start();
    }

    private void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        IsReading = false;
    }

    /// <summary>Where the reading of <paramref name="key"/> by these models is kept.</summary>
    private static string KeptFolder(string key, string modelsVersion)
        => Path.Combine(Path.GetTempPath(), "nexaflow-pages",
                        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key + "\n" + modelsVersion)))[..16]);

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private void Download() => _reading?.Download();

    [RelayCommand(CanExecute = nameof(HasText))]
    private void Copy() => Clipboard.SetText(Markdown);

    private bool CanSave => HasText && !IsReading;

    /// <summary>Saves the text as Markdown where the user picks, its figures in a folder of the same name beside it.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (await _shell.PickSaveFileAsync(_name + ".md", [".md"]) is not { } target) return;
        try
        {
            string figures = Path.GetFileNameWithoutExtension(target) + ".figures";
            string from = Path.Combine(Folder!, Figures);
            bool any = Directory.Exists(from) && Directory.EnumerateFiles(from).Any();
            await File.WriteAllTextAsync(target, any ? Markdown.Replace("](" + Figures + "/", "](" + figures + "/") : Markdown);
            if (!any) return;
            string into = Path.Combine(Path.GetDirectoryName(target)!, figures);
            Directory.CreateDirectory(into);
            foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(into, Path.GetFileName(file)), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _shell.ShowError(Str.Format("Text.PageReading.SaveFailed", ex.Message));
        }
    }

    [RelayCommand]
    private void Close()
    {
        Stop();
        IsOpen = false;
    }

    private sealed class Shown(Action<PagesSoFar> show) : IProgress<PagesSoFar>
    {
        public void Report(PagesSoFar value) => show(value);
    }
}
