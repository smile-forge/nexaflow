using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Rtf.ClientTools;
using Nexaflow.Features.Rtf.Reading;
using Nexaflow.IO.Common;
using Nexaflow.Markdown.Rtf;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Features.Rtf.ViewModels;

/// <summary>
/// The rich-text page's view-model. Reads the document off the UI thread as the RTF it is, which the surface draws with the
/// shared renderer's RTF language, and beside it the words that RTF draws — what the page is searched in and what the
/// assistant reads. A change to the file on disk is read again, so the page shows the document as it now stands.
/// </summary>
public sealed partial class RtfViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly IShellServices _shell;

    /// <summary>The runtime's file system the document is read through — on disk, in an archive or on a location.</summary>
    private readonly IVirtualFileSystem _vfs;

    private IFileWatch? _watch;
    private bool _disposed;

    public RtfViewModel(string filePath, IShellServices shell, IVirtualFileSystem vfs)
    {
        FilePath = filePath;
        _shell = shell;
        _vfs   = vfs;
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>The language the surface reads the document in.</summary>
    public string Language => RtfParser.Language;

    /// <summary>Where a link written relative to the document leads from.</summary>
    public string? BaseDirectory => Path.GetDirectoryName(FilePath) is { Length: > 0 } folder ? folder : null;

    /// <summary>This tab's zoom over the shell's text size; the document's own sizes are in proportion to it.</summary>
    public TextZoom Zoom { get; } = new();

    /// <summary>The document as RTF, exactly as the file holds it — what the surface draws.</summary>
    [ObservableProperty] private string _document = string.Empty;

    /// <summary>The words the document draws, a paragraph to a line and a table's cells a tab apart.</summary>
    [ObservableProperty] private string _words = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoaded;

    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private int _wordCount;
    [ObservableProperty] private int _paragraphCount;
    [ObservableProperty] private string _fileSizeText = string.Empty;

    // ── Loading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the document, then watches its file. Safe to call from the view's Loaded handler — the continuation runs on
    /// the UI thread — and a second call does nothing.
    /// </summary>
    public async Task LoadAsync()
    {
        if (IsLoaded) return;

        try
        {
            await ReadAsync().ConfigureAwait(true);
        }
        finally
        {
            IsLoaded = true;   // releases the AI send gate even on failure
        }

        await WatchAsync();
    }

    /// <summary>Reads the document again, as it now stands on disk. A search shown over the old one is dropped with it.</summary>
    internal async Task ReloadAsync()
    {
        if (IsSearchActive) ClearSearch();
        await ReadAsync().ConfigureAwait(true);
    }

    private async Task ReadAsync()
    {
        try
        {
            var read = await Task.Run(() => ReadDocumentAsync(FilePath)).ConfigureAwait(true);

            FileSizeText = SizeFormatter.FormatBytes(read.Bytes);

            if (read.Words is null)
            {
                Fail(Str.Get("Rtf.Error.NotRtf"));
                return;
            }

            HasError = false;
            ErrorMessage = string.Empty;
            Document = read.Source;
            Words = read.Words;
            WordCount = Counted(read.Words);
            ParagraphCount = read.Words.Length == 0 ? 0 : read.Words.Count(c => c == '\n') + 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail(Str.Format("Rtf.Error.UnreadableFormat", e.Message));
        }
    }

    /// <summary>What the file holds, and the words it draws — none when it is not RTF at all.</summary>
    private readonly record struct Reading(string Source, string? Words, long Bytes);

    private async Task<Reading> ReadDocumentAsync(string path)
    {
        var source = await RtfDocumentFile.ReadAsync(_vfs, path);
        return new Reading(source, RtfDocumentFile.IsRtf(source) ? RtfParser.Text(source) : null, source.Length);
    }

    private void Fail(string message)
    {
        HasError = true;
        ErrorMessage = message;
    }

    /// <summary>Words, as the footer counts them: runs of anything that is not white space.</summary>
    private static int Counted(string text)
    {
        var words = 0;
        var inWord = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) inWord = false;
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }

        return words;
    }

    // ── The file on disk ──────────────────────────────────────────────────────

    private async Task WatchAsync()
    {
        if (_disposed || !await _vfs.ExistsAsync(FilePath)) return;

        _watch = _shell.WatchFile(FilePath, OnFileChanged);
    }

    private async void OnFileChanged()
    {
        if (_disposed) return;

        await ReloadAsync();
    }

    /// <summary>
    /// Whether the page is on screen. While it is not, changes to the file are held and coalesced, and read once it is back.
    /// </summary>
    internal void OnScreen(bool shown)
    {
        if (_watch is not null) _watch.Enabled = shown;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Follows a link out of the document: the shell routes it to whichever feature claims it — a web page to the browser,
    /// a path to the viewer for that kind of file — and nothing claiming it leaves it to open the way any other link does.
    /// </summary>
    public bool FollowLink(string url) => !string.IsNullOrWhiteSpace(url) && _shell.HandleObject(url);

    // ── IPageViewModel ────────────────────────────────────────────────────────

    public bool IsContextReady => IsLoaded;

    public string GetContext()
    {
        if (!IsLoaded) return $"Rich-text document \"{FileName}\": still loading…";
        if (HasError) return $"Rich-text document \"{FileName}\" at '{FilePath}': could not be shown — {ErrorMessage}";

        return $"Rich-text (RTF) document \"{FileName}\" at '{FilePath}', shown as it draws and read-only: " +
               $"{WordCount:N0} word(s) in {ParagraphCount:N0} paragraph(s), {FileSizeText}. " +
               "Its words are not in your context — read them with rtf_read_text.";
    }

    public IReadOnlyList<IClientTool> GetClientTools() => RtfTools.For(this);

    public string? GetSecurityContext() => FilePath;

    /// <summary>The document this tab shows.</summary>
    public ContextGrant GetContextGrant() => ContextGrant.OfFile(FilePath);

    public IContext? GetContextObject()
    {
        if (BaseDirectory is not { } folder) return null;

        return new FileSystemContext
        {
            RootPath = folder,
            CurrentPath = folder,
            SelectedItems = [FilePath],
        };
    }

    public string? GetAiSystemPromptGuidance() =>
        "This page shows a rich-text (RTF) document as it is drawn, read-only. Read its words with rtf_read_text, a range " +
        "of paragraphs at a time for a long document, and search it to find where something is.";

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _watch?.Dispose();
        _watch = null;
    }
}
