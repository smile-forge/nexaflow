using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.Models;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Editing.Paged;
using Nexaflow.Visuals.Text.Pages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace Nexaflow.Features.Pdf.ViewModels;

/// <summary>
/// Drives the PDF reader tab: the document's pages, read as objects and laid on the shared layout tree for the paged
/// host to show; the document's properties and table of contents in the side panel; finding text; and a tool surface
/// that lets the AI read the text, look at a page, and pull out a figure.
/// <para>
/// The document is a <see cref="PdfDocumentSession"/>: the file and the pages in their order, each source opened once
/// for the tab's lifetime and read behind its own lock.
/// </para>
/// </summary>
public sealed partial class PdfViewModel : ObservableObject, IPageViewModel, IDisposable
{
    /// <summary>Characters of page text a single read returns unless the caller asks for fewer.</summary>
    private const int DefaultReadChars = 40_000;

    /// <summary>Pages a read covers when the caller names a start but no end.</summary>
    private const int DefaultPageSpan = 10;

    /// <summary>
    /// Fraction of the page an image must cover for the page to count as "this page IS this picture" — the
    /// signature of a scan. Below it, an embedded image is an illustration on a page that also has text.
    /// </summary>
    private const double ScannedPageCoverage = 0.90;

    /// <summary>How wide, in pixels, a page is drawn when the AI asks to look at it.</summary>
    private const int PagePictureWidth = 1600;

    /// <summary>How many pixels a page is drawn at for page reading, per point: 200 dots an inch.</summary>
    private const double ReadingResolution = 200.0 / 72;

    /// <summary>The longest side, in pixels, a page is drawn at for page reading, however large the page.</summary>
    private const int ReadingSide = 2400;

    private readonly IShellServices _shell;
    private readonly PdfConfig _config;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _path;          // as opened — may be virtual (inside an archive)
    private readonly string? _tempCopy;     // materialised copy to delete on dispose, if we made one

    private readonly PdfDocumentSession _session;
    private bool _disposed;

    /// <summary>The document as a real file — what the default PDF application is handed.</summary>
    public Uri FileUri { get; }

    public string FileName { get; }

    // ── Panel state ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isMetadataLoading = true;

    /// <summary>What the panel says while it has nothing else to show — loading, or why it never will.</summary>
    [ObservableProperty] private string _panelStatus = Str.Get("Pdf.Panel.Reading");

    [ObservableProperty] private bool _isPanelOpen = true;

    public ObservableCollection<PdfInfoRow>     Properties { get; } = [];
    public ObservableCollection<PdfOutlineItem> Contents   { get; } = [];

    [ObservableProperty] private bool _hasProperties;
    [ObservableProperty] private bool _hasContents;

    /// <summary>The document's scanned pages, by number: no text on them, one picture over each.</summary>
    private IReadOnlyList<int> _scans = [];

    // The pages the reading last asked for, and the pages of the reading whose texts the conversion holds — the whole
    // document's, or its scans' — so each text is known by its page.
    private IReadOnlyList<int> _askedPages = [];
    private IReadOnlyList<int> _readPages = [];

    /// <summary>The document has scanned pages, and this machine can read them — the notice above it says so.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsScansNotice))]
    private bool _hasScans;

    /// <summary>The reader has put the scans notice away for this document.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsScansNotice))]
    private bool _scansNoticeDismissed;

    /// <summary>
    /// The notice above the document that its scanned pages have no text, and can be converted to Markdown — put away
    /// while the conversion's own banner says how it is getting on, so the offer and its progress are never shown together.
    /// </summary>
    public bool ShowsScansNotice => HasScans && !ScansNoticeDismissed && !Conversion.HasStatus;

    /// <summary>The tab showing the document, set by its view: what opens beside it opens in its window.</summary>
    public IPageView? Host { get; set; }

    /// <summary>How many of the document's pages are scans, for the notice above it.</summary>
    [ObservableProperty] private string _scansSummary = string.Empty;

    /// <summary>The scanned pages converted to Markdown, opened in the right-hand pane; their text for the assistant too.</summary>
    public PageConversion Conversion { get; }

    /// <summary>
    /// The contents row the user last picked. Kept so the panel keeps showing where they are in the document
    /// instead of losing the place the moment the click finishes — a table of contents that forgets what you
    /// just chose makes you re-find it on every jump.
    /// </summary>
    [ObservableProperty] private PdfOutlineItem? _selectedOutlineItem;

    /// <summary>Shown in the Contents tab when the document simply has no outline — the common case.</summary>
    public string ContentsEmptyMessage => IsMetadataLoading
        ? Str.Get("Pdf.Panel.Reading")
        : Str.Get("Pdf.Panel.NoContents");

    // ── Pages ─────────────────────────────────────────────────────────────

    /// <summary>The document's pages, once its page slots are read; what the paged host shows.</summary>
    [ObservableProperty] private IPagedLayoutSource? _pages;

    /// <summary>How many pages the document has; zero until it is read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageCountText))]
    private int _pageCount;

    /// <summary>"of N", beside the page being read.</summary>
    public string PageCountText => Str.Format("Pdf.Toolbar.PageCountFormat", PageCount);

    /// <summary>The page being read, 1-based, as the paged host reports it.</summary>
    [ObservableProperty] private int _currentPage = 1;

    /// <summary>True when the document could not be read at all — damaged past recovery, or locked with a password.</summary>
    [ObservableProperty] private bool _isUnreadable;

    [ObservableProperty] private string _failureMessage = string.Empty;

    private PdfPageSource? _source;

    // ── Finding text ──────────────────────────────────────────────────────

    /// <summary>What the reader is looking for.</summary>
    [ObservableProperty] private string _findText = string.Empty;

    /// <summary>Where a search stands: searching, how many hits, or none.</summary>
    [ObservableProperty] private string _findStatus = string.Empty;

    /// <summary>Every hit of the last search, page by page in reading order.</summary>
    public IReadOnlyList<PageRange> FoundRanges { get; private set; } = [];

    /// <summary>The hit being looked at, or -1.</summary>
    [ObservableProperty] private int _findIndex = -1;

    private string _foundFor = string.Empty;
    private CancellationTokenSource? _finding;

    // ── View-supplied hooks ───────────────────────────────────────────────

    /// <summary>
    /// Set by the view: scrolls the paged host to a 1-based page, optionally to a position measured down from the top
    /// of it in points.
    /// </summary>
    public Action<int, double?>? NavigateToPage { get; set; }

    /// <param name="reading">Page reading, which reads the document's scanned pages into Markdown, where the app has it.</param>
    /// <param name="ai">The workspace's AI, whose image model draws the scanned pages' diagrams again.</param>
    public PdfViewModel(string path, IShellServices shell, PdfConfig config, IPageReading? reading = null, IAIService? ai = null)
    {
        _path    = path;
        _shell   = shell;
        _config  = config;
        FileName = Path.GetFileName(path);
        Conversion = new PageConversion(reading, ai, shell);
        Conversion.TextRead += () => _readPages = _askedPages;
        Conversion.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageConversion.HasStatus)) OnPropertyChanged(nameof(ShowsScansNotice));
        };

        // A PDF inside an archive is virtual and the default PDF application can't open it; materialise a real copy
        // and remember to delete it. A plain on-disk file passes straight through and gets no temp copy.
        var real = RealizeLocalFile(path);
        _tempCopy = string.Equals(real, path, StringComparison.OrdinalIgnoreCase) ? null : real;
        FileUri   = new Uri(real);
        _session  = new PdfDocumentSession(path);
    }

    private static string RealizeLocalFile(string filePath)
    {
        try { return VirtualFileSystem.Instance.MaterializeFile(filePath); }
        catch { return filePath; }
    }

    // ── Panel load ────────────────────────────────────────────────────────

    /// <summary>The document's own metadata once read — what <see cref="GetContext"/> describes, independent of the translated property rows.</summary>
    private PdfDocumentInfo? _info;

    /// <summary>
    /// Reads the document's page slots, for the pages to be shown, and everything cheap about it for the panel. Runs
    /// entirely off the UI thread; the pages appear as soon as their slots are read, before the panel is filled.
    /// </summary>
    /// <param name="paper">What pages are laid on, from the theme.</param>
    public async Task LoadAsync(System.Windows.Media.Brush paper)
    {
        var opened = false;
        try
        {
            opened = await _session.OpenAsync(_cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch { /* falls through to the unreadable state */ }

        if (_disposed) return;

        if (!opened)
        {
            IsUnreadable   = true;
            FailureMessage = Str.Get("Pdf.Fallback.Unreadable");
        }
        else
        {
            _source   = new PdfPageSource(_session.Arrangement, _session.Opened(), paper);
            PageCount = _source.PageCount;
            Pages     = _source;
        }

        var oversize = OversizeMessage();
        if (oversize is not null)
        {
            PanelStatus       = oversize;
            IsMetadataLoading = false;
            OnPropertyChanged(nameof(ContentsEmptyMessage));
            return;
        }

        PdfDocumentInfo? info = null;
        try
        {
            info = await WithDocumentAsync(doc => PdfMetadataReader.Read(doc, _cts.Token), _cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch { /* falls through to the unreadable message */ }

        if (_disposed) return;

        if (info is null)
        {
            PanelStatus = Str.Get("Pdf.Panel.Unreadable");
            IsMetadataLoading = false;
            OnPropertyChanged(nameof(ContentsEmptyMessage));
            return;
        }

        _info = info;
        foreach (var row in BuildPropertyRows(info)) Properties.Add(row);
        foreach (var entry in info.Outline)
            Contents.Add(new PdfOutlineItem(entry.Title, entry.Level, entry.PageNumber, entry.OffsetFromTop)
            {
                CanJump = entry.PageNumber is not null && _source is not null,
            });

        HasProperties     = Properties.Count > 0;
        HasContents       = Contents.Count > 0;
        PanelStatus       = string.Empty;
        IsMetadataLoading = false;
        OnPropertyChanged(nameof(ContentsEmptyMessage));
        await FindScansAsync();
    }

    /// <summary>Finds the document's scanned pages, once its metadata is shown: a page at a time, so a long document's
    /// search waits behind whatever else reads it.</summary>
    private async Task FindScansAsync()
    {
        if (!Conversion.Available) return;
        try
        {
            _scans = await WithDocumentAsync(doc => PdfScans.Find(doc, ScannedPageCoverage, _cts.Token), _cts.Token) ?? [];
        }
        catch (OperationCanceledException) { return; }
        catch { return; }   // a document whose pages will not parse has no scans to offer
        if (_disposed || _scans.Count == 0) return;
        ScansSummary = Str.Format("Pdf.Scans.Notice", _scans.Count, _info?.PageCount ?? _scans.Count);
        HasScans = true;
    }

    /// <summary>Converts the whole document to Markdown with page reading — every page, whether its text is its own or a scan's.</summary>
    [RelayCommand]
    private Task ConvertDocument() =>
        PageCount == 0 ? Task.CompletedTask : Conversion.ConvertAsync(PagesToRead(Enumerable.Range(1, PageCount).ToList(), "pages"), Host);

    [RelayCommand]
    private void DismissScansNotice() => ScansNoticeDismissed = true;

    /// <summary>
    /// The pages numbered <paramref name="numbers"/>, to read: each drawn as the reader draws it when the reading reaches it.
    /// They are named by this file as it now stands and by <paramref name="what"/> of it they are — the whole document, or
    /// its scans — so a reading kept for them is found again.
    /// </summary>
    private PageSource PagesToRead(IReadOnlyList<int> numbers, string what)
    {
        var file = new FileInfo(_path);
        string key = $"{_path}|{(file.Exists ? file.Length : 0)}|{(file.Exists ? file.LastWriteTimeUtc.Ticks : 0)}|{what}";
        _askedPages = numbers;
        return new PageSource(key, _path, numbers.Count, (at, ct) => PagePictureAsync(numbers[at], ct),
            PdfTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = _path });
    }

    /// <summary>
    /// Page <paramref name="number"/> as page reading reads it, written to a temporary PNG: drawn as the reader draws it —
    /// its colours through their profiles, its scans however they are encoded, masked and turned as the page is shown — at
    /// <see cref="ReadingResolution"/>, or a scanned page at its own resolution where that is finer, its longest side held to
    /// <see cref="ReadingSide"/>. Null where the page cannot be drawn.
    /// </summary>
    private async Task<string?> PagePictureAsync(int number, CancellationToken ct)
    {
        if (_source is not { } source || number < 1 || number > source.PageCount) return null;

        var size = source.PageSize(number - 1);
        var points = new System.Windows.Size(size.Width / PdfPageLayout.DipsPerPoint, size.Height / PdfPageLayout.DipsPerPoint);
        var perPoint = ReadingResolution;
        if (_scans.Contains(number) && await WithDocumentAsync(doc => PdfScans.ScanResolution(doc, number), ct) is { } scanned)
            perPoint = Math.Max(perPoint, scanned);
        perPoint = Math.Min(perPoint, ReadingSide / Math.Max(points.Width, points.Height));

        byte[]? png;
        try
        {
            png = await source.PictureAsync(number - 1, Math.Max(1, (int)Math.Round(points.Width * perPoint)), ct);
        }
        catch (InvalidOperationException)
        {
            return null;   // the document is no longer readable
        }
        if (png is not { Length: > 0 }) return null;

        string picture = Path.Combine(Path.GetTempPath(), $"nexaflow-page-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(picture, png, ct);
        return picture;
    }

    /// <summary>
    /// One row per fact the document actually states. Empty fields are omitted rather than rendered as a
    /// column of dashes — a panel of blanks reads as broken, and the absence is not information.
    /// </summary>
    private IEnumerable<PdfInfoRow> BuildPropertyRows(PdfDocumentInfo info)
    {
        yield return new PdfInfoRow(Str.Get("Pdf.Property.File"), FileName);

        var size = TryFileSize();
        if (size is long bytes) yield return new PdfInfoRow(Str.Get("Pdf.Property.Size"), SizeFormatter.FormatBytes(bytes));

        yield return new PdfInfoRow(Str.Get("Pdf.Property.Pages"), info.PageCount.ToString());
        yield return new PdfInfoRow(Str.Get("Pdf.Property.PdfVersion"), info.PdfVersion);

        if (info.IsEncrypted)
            // Worth saying plainly: the document IS encrypted, and it opened anyway because it only carries
            // an owner password (a "no copying" flag any reader ignores). Claiming it isn't protected would
            // be wrong; claiming we broke a password would be worse.
            yield return new PdfInfoRow(Str.Get("Pdf.Property.Protection"), Str.Get("Pdf.Property.Protected"));

        if (info.HasForm)
            yield return new PdfInfoRow(Str.Get("Pdf.Property.Form"), info.FormFieldCount == 1 ? Str.Format("Pdf.Property.FieldsOneFormat", info.FormFieldCount) : Str.Format("Pdf.Property.FieldsManyFormat", info.FormFieldCount));

        if (info.Title      is { } t) yield return new PdfInfoRow(Str.Get("Pdf.Property.Title"), t);
        if (info.Author     is { } a) yield return new PdfInfoRow(Str.Get("Pdf.Property.Author"), a);
        if (info.Subject    is { } s) yield return new PdfInfoRow(Str.Get("Pdf.Property.Subject"), s);
        if (info.Keywords   is { } k) yield return new PdfInfoRow(Str.Get("Pdf.Property.Keywords"), k);
        if (info.Creator    is { } c) yield return new PdfInfoRow(Str.Get("Pdf.Property.Creator"), c);
        if (info.Producer   is { } p) yield return new PdfInfoRow(Str.Get("Pdf.Property.Producer"), p);
        if (info.CreationDate is { } cd) yield return new PdfInfoRow(Str.Get("Pdf.Property.Created"), cd);
        if (info.ModifiedDate is { } md) yield return new PdfInfoRow(Str.Get("Pdf.Property.Modified"), md);
    }

    private long? TryFileSize()
    {
        try
        {
            var fi = new FileInfo(_tempCopy ?? _path);
            return fi.Exists ? fi.Length : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Declines to parse a document past the viewer's own ceiling. Deliberately not the search ceiling: a
    /// sweep is sequential, so one huge file there stalls every candidate behind it, whereas a document the
    /// user just double-clicked has nothing queued behind it and deserves a far more generous limit.
    /// </summary>
    private string? OversizeMessage()
    {
        var size = TryFileSize();
        if (size is not long bytes) return null;

        var max = (long)_config.ViewerMaxFileSizeMb * 1024 * 1024;
        return bytes > max
            ? Str.Format("Pdf.Panel.OversizeFormat", SizeFormatter.FormatBytes(bytes), _config.ViewerMaxFileSizeMb)
            : null;
    }

    // ── Document access ───────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="read"/> against the open document on a background thread, opening it first if
    /// needed. Returns default when the document can't be opened at all — every caller has to be able to
    /// say so honestly rather than reporting an empty document.
    /// </summary>
    private Task<T?> WithDocumentAsync<T>(Func<PdfDocument, T> read, CancellationToken ct) =>
        ReadScopeAsync(scope => read(scope.Document), ct);

    /// <summary>
    /// Runs <paramref name="read"/> against the document as it now stands, off the UI thread and under its lock; default
    /// when it cannot be opened at all.
    /// </summary>
    private async Task<T?> ReadScopeAsync<T>(Func<PdfDocumentScope, T> read, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        return await _session.Document.ReadAsync(read, linked.Token).ConfigureAwait(false);
    }

    // ── Page navigation ───────────────────────────────────────────────────

    /// <summary>Scrolls the reader to a 1-based page, optionally to a position that many points down it.</summary>
    /// <returns>False when the page is not one of the document's, or nothing is showing it.</returns>
    public bool GoToPage(int page, double? offsetFromTop = null)
    {
        if (NavigateToPage is null || page < 1 || page > PageCount) return false;
        NavigateToPage(page, offsetFromTop);
        return true;
    }

    /// <summary>Follows a link to a place in this document: its page, at the height it names when that page has been read.</summary>
    internal bool GoToTarget(PdfPageTarget target) =>
        GoToPage(target.PageNumber, _source?.OffsetFromTop(_session.Base.Id, target));

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    /// <summary>Everything the Properties tab shows, as plain lines — the panel's most-wanted action.</summary>
    [RelayCommand]
    private void CopyAllProperties()
    {
        if (Properties.Count == 0) return;
        try { System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, Properties)); }
        catch { _shell.ShowError(Str.Get("Pdf.Error.Clipboard")); }
    }

    /// <summary>Copies the whole table of contents, indentation and page numbers included.</summary>
    [RelayCommand]
    private void CopyOutline()
    {
        if (Contents.Count == 0) return;
        var text = string.Join(Environment.NewLine,
            Contents.Select(c => new string(' ', c.Level * 2) + c.CopyWithPage));
        try { System.Windows.Clipboard.SetText(text); }
        catch { _shell.ShowError("Couldn't copy to the clipboard."); }
    }

    [RelayCommand]
    private void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { System.Windows.Clipboard.SetText(text); }
        catch { _shell.ShowError("Couldn't copy to the clipboard."); }
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    /// <summary>Ready once the panel load has finished — succeeded <em>or</em> failed. Waiting forever on a
    /// document that will never parse would block the send with no prospect of unblocking.</summary>
    public bool IsContextReady => !IsMetadataLoading;

    public string GetContext()
    {
        var sb = new StringBuilder();
        sb.Append($"PDF reader: '{FileName}'");

        if (_info is { } info)
        {
            sb.Append($", {info.PageCount} page(s)");
            if (info.Title is { } title) sb.Append($", titled \"{title}\"");
            if (info.Author is { } author) sb.Append($", by {author}");
        }

        sb.Append('.');

        if (PageCount > 0) sb.Append($" The reader is showing page {CurrentPage} of {PageCount}.");

        sb.Append(Contents.Count > 0
            ? $" It has a table of contents with {Contents.Count} entries."
            : " It has no table of contents.");

        if (!string.IsNullOrEmpty(PanelStatus))
            sb.Append($" ({PanelStatus})");

        sb.Append(" The document's text is not included here — use the pdf_ tools to read it.");
        return sb.ToString();
    }

    /// <summary>
    /// The boundary these tools act within is this one document. Per-path rather than per-feature so two PDF
    /// tabs pinned into one conversation stay distinguishable instead of collapsing first-wins.
    /// </summary>
    public string? GetSecurityContext() => $"pdf:{_path}";

    /// <summary>The document this tab shows.</summary>
    public ContextGrant GetContextGrant() => ContextGrant.OfFile(_path);

    /// <summary>A single local document the user opened themselves — tightly scoped by construction.</summary>
    public ContextSecurityRisk GetContextSecurityRisk() => ContextSecurityRisk.Low;

    public string? GetAiSystemPromptGuidance()
        => "This is a PDF reader. The document's text is NOT in your context — call pdf_read_text with a page "
         + "range to read it. A scanned PDF has no text at all: when pdf_read_text returns nothing for a page, "
         + "call pdf_page_image and read the picture instead. pdf_outline gives the table of contents with page "
         + "numbers, so you can go straight to the section that matters rather than reading the whole document. "
         + "Page text comes back in reading order, so a multi-column page reads down each column in turn - "
         + "but an unusual layout can still defeat that, so if a page reads like nonsense, look at its image.";

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "pdf_get_info",
            "Report what this PDF says about itself: page count, PDF version, whether it's protected or "
            + "carries a form, and its title/author/subject/keywords/creator/producer/dates. Cheap — call it "
            + "first to know what you're dealing with.",
            [],
            ToolSafety.SafeOperation,
            (_, ct) => GetInfoToolAsync(ct),
            parallelizable: true),

        new DelegateClientTool(
            "pdf_outline",
            "Return the document's table of contents — each entry's title, its nesting level, and the page it "
            + "starts on. Use it to jump straight to the relevant section instead of reading every page.",
            [],
            ToolSafety.SafeOperation,
            (_, ct) => GetOutlineToolAsync(ct),
            parallelizable: true),

        new DelegateClientTool(
            "pdf_read_text",
            "Read the text of a range of pages, with an explicit marker before each page. This is how you read "
            + "the document — its text is not in your context. If a page comes back with no text it is probably "
            + "a scan; call pdf_page_image for that page instead.",
            [
                new ClientToolParameter("page_from", "First page to read, 1-based. Defaults to 1.", Required: false, Type: "number"),
                new ClientToolParameter("page_to",   "Last page to read, 1-based, inclusive. Defaults to nine pages after page_from.", Required: false, Type: "number"),
                new ClientToolParameter("max_chars", "Cap on characters returned. Defaults to 40000.", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            ReadTextToolAsync,
            parallelizable: false),

        new DelegateClientTool(
            "pdf_find_text",
            "Search the whole document for a phrase and report which pages it appears on, with a short snippet "
            + "around each hit. Far cheaper than paging through a long document to find where something is "
            + "discussed. Finds nothing in a scanned PDF, which has no text to search.",
            [
                new ClientToolParameter("query",    "The text to look for. Case-insensitive."),
                new ClientToolParameter("max_hits", "Most hits to report. Defaults to 20.", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            FindTextToolAsync,
            parallelizable: false),

        new DelegateClientTool(
            "pdf_read_scans",
            "Read the document's scanned pages — pages that are pictures, with no text in them — with page reading on "
            + "this machine, and return their text as Markdown, page by page: columns as one text, formulas as LaTeX, "
            + "tables. A few seconds a page; once read, pdf_read_text and pdf_find_text include their text too.",
            [],
            ToolSafety.SafeOperation,
            ReadScansToolAsync,
            parallelizable: false),

        new DelegateClientTool(
            "pdf_list_images",
            "List the images embedded in the document — page, position on the page, pixel size, format, byte "
            + "size, and whether it repeats an earlier one. Returns no image data, so it's the cheap way to "
            + "decide which image is worth fetching with pdf_get_image.",
            [new ClientToolParameter("page", "Restrict to one page, 1-based. Omit for the whole document.", Required: false, Type: "number")],
            ToolSafety.SafeOperation,
            ListImagesToolAsync,
            parallelizable: false),

        new DelegateClientTool(
            "pdf_get_image",
            "Fetch one embedded image so you can look at it — a figure, chart, photo or diagram on a page.",
            [
                new ClientToolParameter("page",  "Page the image is on, 1-based.", Type: "number"),
                new ClientToolParameter("index", "Which image on that page, 1-based. Defaults to the first.", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            GetImageToolAsync,
            parallelizable: false) { Needs = [AiAbility.ImageRecognition] },

        new DelegateClientTool(
            "pdf_page_image",
            "Look at a whole page as a picture. Use this for a scanned page (one that pdf_read_text returns no "
            + "text for), and for any page whose layout, chart or table you need to see rather than read.",
            [new ClientToolParameter("page", "Page to look at, 1-based.", Type: "number")],
            ToolSafety.SafeOperation,
            PageImageToolAsync,
            parallelizable: false) { Needs = [AiAbility.ImageRecognition] },

        new DelegateClientTool(
            "pdf_view_page",
            "Scroll the on-screen reader to a page so the user is looking at what you're talking about. This "
            + "changes what they see; it does not return the page's content.",
            [new ClientToolParameter("page", "Page to show, 1-based.", Type: "number")],
            ToolSafety.SafeOperation,
            ViewPageToolAsync,
            parallelizable: false),
    ];

    // ── Tools ─────────────────────────────────────────────────────────────

    private async Task<ToolResult> GetInfoToolAsync(CancellationToken ct)
    {
        var info = await WithDocumentAsync(doc => PdfMetadataReader.Read(doc, ct), ct);
        if (info is null) return Unreadable();

        var sb = new StringBuilder();
        sb.AppendLine($"{FileName} — {info.PageCount} page(s), PDF {info.PdfVersion}.");
        if (info.IsEncrypted) sb.AppendLine("Protected (opened read-only).");
        if (info.HasForm)     sb.AppendLine($"Carries a form with {info.FormFieldCount} field(s).");

        Append(sb, "Title", info.Title);
        Append(sb, "Author", info.Author);
        Append(sb, "Subject", info.Subject);
        Append(sb, "Keywords", info.Keywords);
        Append(sb, "Creator", info.Creator);
        Append(sb, "Producer", info.Producer);
        Append(sb, "Created", info.CreationDate);
        Append(sb, "Modified", info.ModifiedDate);

        sb.Append(info.Outline.Count > 0
            ? $"Has a table of contents with {info.Outline.Count} entries (call pdf_outline)."
            : "Has no table of contents.");

        return ToolResult.Ok($"Read {FileName}'s properties.", sb.ToString());

        static void Append(StringBuilder sb, string label, string? value)
        {
            if (value is not null) sb.AppendLine($"{label}: {value}");
        }
    }

    private async Task<ToolResult> GetOutlineToolAsync(CancellationToken ct)
    {
        var outline = await WithDocumentAsync(doc => PdfMetadataReader.ReadOutline(doc, ct), ct);
        if (outline is null) return Unreadable();
        if (outline.Count == 0)
            return ToolResult.Ok("No table of contents.",
                "This document has no table of contents. Use pdf_read_text to read it page by page.");

        var sb = new StringBuilder();
        sb.AppendLine($"Table of contents for {FileName}:");
        foreach (var entry in outline)
        {
            sb.Append(new string(' ', entry.Level * 2)).Append(entry.Title);
            if (entry.PageNumber is int p) sb.Append($"  — p.{p}");
            sb.AppendLine();
        }

        return ToolResult.Ok($"Read the table of contents ({outline.Count} entries).", sb.ToString());
    }

    private async Task<ToolResult> ReadTextToolAsync(JsonObject args, CancellationToken ct)
    {
        // Nullable on purpose: an unconstrained T? is still int for a value type, so a failed open would
        // otherwise arrive as a perfectly plausible zero-page document.
        var pageCount = await WithDocumentAsync(doc => (int?)doc.NumberOfPages, ct);
        if (pageCount is not int total) return Unreadable();

        var from = ReadInt(args, "page_from") ?? 1;
        var to   = ReadInt(args, "page_to")   ?? from + DefaultPageSpan - 1;
        var cap  = Math.Max(1, ReadInt(args, "max_chars") ?? DefaultReadChars);

        // An out-of-range page is an error, not a silent clamp: quietly reading page 1 when the model asked
        // for page 500 would have it draw conclusions about the wrong part of the document.
        if (from < 1 || from > total)
            return ToolResult.Error($"Page {from} doesn't exist — this document has {total} page(s).");
        if (to < from)
            return ToolResult.Error($"page_to ({to}) is before page_from ({from}).");

        to = Math.Min(to, total);

        var pages = await WithDocumentAsync(
            doc => PdfTextReader.ReadPages(doc, from, to, cap * 2L, PdfReadingOrder.Layout, ct).ToList(), ct);
        if (pages is null) return Unreadable();

        var sb        = new StringBuilder();
        var truncated = false;
        var lastRead  = from - 1;
        var anyText   = false;

        foreach (var page in pages)
        {
            lastRead = page.PageNumber;
            sb.AppendLine($"--- page {page.PageNumber} ---");
            if (string.IsNullOrWhiteSpace(page.Text) && ScanText(page.PageNumber) is { } read)
            {
                anyText = true;
                sb.AppendLine("(a scan: its text as page reading read it)");
                sb.AppendLine(read.Trim());
            }
            else if (string.IsNullOrWhiteSpace(page.Text))
                sb.AppendLine(_scans.Contains(page.PageNumber) && Conversion.Available
                    ? "(no text on this page — it is a scan; read it with pdf_read_scans, or look at it with pdf_page_image)"
                    : "(no text on this page — it may be a scan; try pdf_page_image)");
            else
            {
                anyText = true;
                sb.AppendLine(page.Text.Trim());
            }
            if (page.Truncated) { truncated = true; break; }
        }

        if (truncated)
            sb.AppendLine($"[Stopped at the {cap}-character limit part-way through page {lastRead}. "
                        + $"Call pdf_read_text again from page {lastRead} for the rest.]");
        else if (to < total)
            sb.AppendLine($"[Pages {from}–{to} of {total}. Call again from page {to + 1} to continue.]");

        var summary = anyText
            ? $"Read pages {from}–{lastRead} of {FileName}."
            : $"Pages {from}–{lastRead} of {FileName} have no text (probably scanned).";
        return ToolResult.Ok(summary, sb.ToString());
    }

    private async Task<ToolResult> FindTextToolAsync(JsonObject args, CancellationToken ct)
    {
        var query = args["query"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(query)) return ToolResult.Error("No search text was given.");

        var maxHits = Math.Max(1, ReadInt(args, "max_hits") ?? 20);

        var hits = await WithDocumentAsync(doc => FindHits(doc, query, maxHits, ct), ct);
        if (hits is null) return Unreadable();
        hits = [.. hits, .. ScanHits(query, maxHits - hits.Count)];

        if (hits.Count == 0)
            return ToolResult.Ok($"No match for \"{query}\".",
                $"\"{query}\" doesn't appear in this document's text. If it's a scanned PDF there is no text "
                + "to search — look at the pages with pdf_page_image instead.");

        var sb = new StringBuilder();
        sb.AppendLine($"\"{query}\" appears on {hits.Select(h => h.Page).Distinct().Count()} page(s):");
        foreach (var (page, snippet) in hits) sb.AppendLine($"p.{page}: …{snippet}…");
        if (hits.Count >= maxHits) sb.AppendLine($"[Stopped after {maxHits} hits; there may be more.]");

        return ToolResult.Ok($"Found \"{query}\" in {FileName}.", sb.ToString());
    }

    /// <summary>The text page reading read on page <paramref name="page"/>, in the reading the conversion holds; null where it holds none of it.</summary>
    private string? ScanText(int page)
    {
        if (Conversion.PageTexts is not { } texts) return null;
        for (int at = 0; at < _readPages.Count && at < texts.Count; at++)
            if (_readPages[at] == page) return texts[at];
        return null;
    }

    /// <summary>Where <paramref name="query"/> stands in the scanned pages' read text, each with a little around it.</summary>
    private IEnumerable<(int Page, string Snippet)> ScanHits(string query, int most)
    {
        foreach (int page in _scans)
        {
            if (most <= 0 || ScanText(page) is not { } text) continue;
            for (int at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase); at >= 0 && most > 0;
                 at = text.IndexOf(query, at + query.Length, StringComparison.OrdinalIgnoreCase), most--)
            {
                int from = Math.Max(0, at - 40), to = Math.Min(text.Length, at + query.Length + 40);
                yield return (page, text[from..to].ReplaceLineEndings(" "));
            }
        }
    }

    /// <summary>Reads the scanned pages with page reading — or finds them read before — and gives their text, page by page.</summary>
    private async Task<ToolResult> ReadScansToolAsync(JsonObject args, CancellationToken ct)
    {
        if (_scans.Count == 0) return ToolResult.Ok($"{FileName} has no scanned pages.", "Every page has text of its own: read it with pdf_read_text.");
        if (!Conversion.Available) return ToolResult.Error("Page reading cannot run on this machine.");
        if (await _shell.RunOnUiAsync(() => Conversion.ReadAsync(PagesToRead(_scans, "scans"), ct)) is not { Pages: var texts })
            return ToolResult.Error(Conversion.Status ?? "The scanned pages could not be read.");

        var sb = new StringBuilder();
        for (int i = 0; i < _scans.Count && i < texts.Count; i++)
        {
            sb.AppendLine($"--- page {_scans[i]} ---");
            sb.AppendLine(texts[i].Trim().Length > 0 ? texts[i].Trim() : "(nothing read)");
        }
        return ToolResult.Ok($"Read {_scans.Count} scanned page(s) of {FileName}.", sb.ToString());
    }

    /// <summary>Page-by-page scan with a small window of context around each hit.</summary>
    private static List<(int Page, string Snippet)> FindHits(
        PdfDocument document, string query, int maxHits, CancellationToken ct)
    {
        const int Context = 80;
        var hits = new List<(int, string)>();

        // Content-stream order on purpose. This walks every page of the document to locate a phrase, and
        // layout analysis would put a clustering pass on all of them; finding WHICH pages mention something
        // does not need them read in order. The model then calls pdf_read_text on those pages, which does.
        foreach (var page in PdfTextReader.ReadPages(
            document, 1, document.NumberOfPages, long.MaxValue, PdfReadingOrder.ContentStream, ct))
        {
            var text  = page.Text;
            var start = 0;
            while (hits.Count < maxHits)
            {
                var at = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                if (at < 0) break;

                var s = Math.Max(0, at - Context);
                var e = Math.Min(text.Length, at + query.Length + Context);
                hits.Add((page.PageNumber, text[s..e].Replace('\n', ' ').Trim()));
                start = at + query.Length;
            }
            if (hits.Count >= maxHits) break;
        }

        return hits;
    }

    private async Task<ToolResult> ListImagesToolAsync(JsonObject args, CancellationToken ct)
    {
        var page = ReadInt(args, "page");

        var all = await WithDocumentAsync(doc => PdfImageReader.Inventory(doc, ct).ToList(), ct);
        if (all is null) return Unreadable();

        var rows = page is int p ? all.Where(i => i.PageNumber == p).ToList() : all;
        var where = page is int q ? $"page {q}" : "this document";

        if (rows.Count == 0)
            return ToolResult.Ok($"No images on {where}.", $"There are no embedded images on {where}.");

        var sb = new StringBuilder();
        sb.AppendLine($"{rows.Count} image(s) on {where}, {rows.Count(r => !r.IsRepeat)} distinct:");
        foreach (var r in rows)
            sb.AppendLine($"p.{r.PageNumber} #{r.IndexOnPage}: {r.WidthInSamples}×{r.HeightInSamples} "
                        + $"{r.Extension.TrimStart('.')}, {SizeFormatter.FormatBytes(r.ByteLength)}, "
                        + $"covers ~{r.PageCoverage:P0} of the page{(r.IsRepeat ? ", repeat of an earlier image" : "")}");

        return ToolResult.Ok($"Listed {rows.Count} image(s) on {where}.", sb.ToString());
    }

    private async Task<ToolResult> GetImageToolAsync(JsonObject args, CancellationToken ct)
    {
        if (ReadInt(args, "page") is not int page)
            return ToolResult.Error("No page number was given.");
        var index = Math.Max(1, ReadInt(args, "index") ?? 1);

        var images = await WithDocumentAsync(doc => PdfImageReader.ReadPage(doc, page, ct), ct);
        if (images is null) return Unreadable();

        if (images.Count == 0)
            return ToolResult.Error($"Page {page} has no embedded image. "
                                  + "If you want to see how the page looks, call pdf_page_image.");
        if (index > images.Count)
            return ToolResult.Error($"Page {page} has {images.Count} image(s); there is no #{index}.");

        var image = images[index - 1];
        var label = $"{FileName} page {page}, image {index}";
        return ToolResult.Ok(
            $"Fetched image {index} from page {page}.",
            $"Image {index} of {images.Count} on page {page} of {FileName} is attached "
            + $"({SizeFormatter.FormatBytes(image.Bytes.Length)}, {image.Extension.TrimStart('.')}).")
            with { Images = [new ContextImage(image.Bytes.ToArray(), MimeFor(image.Extension), label)] };
    }

    /// <summary>
    /// A whole page as a picture, by whichever route actually works for that page.
    /// <para>
    /// A scanned page <em>is</em> one full-page image, so lifting it straight out of the PDF is free, needs
    /// no browser, and gives the source resolution rather than however many pixels the viewport happened to
    /// have. Anything else — vector text, a chart, a form — has no such image, and the only way to see it is
    /// to photograph what the renderer drew.
    /// </para>
    /// </summary>
    private async Task<ToolResult> PageImageToolAsync(JsonObject args, CancellationToken ct)
    {
        if (ReadInt(args, "page") is not int page)
            return ToolResult.Error("No page number was given.");

        var embedded = await WithDocumentAsync(doc => ReadPageWithCoverage(doc, page, ct), ct);

        if (embedded is { Count: 1 } && embedded[0].Coverage >= ScannedPageCoverage)
        {
            var only = embedded[0];
            return ToolResult.Ok(
                $"Read page {page} as a scanned image.",
                $"Page {page} of {FileName} is a single full-page image — a scan, with no text to extract. "
                + "It is attached for you to read.")
                with { Images = [new ContextImage(only.Data.Bytes.ToArray(), MimeFor(only.Data.Extension),
                                                  $"{FileName} page {page}")] };
        }

        if (_source is not null && page >= 1 && page <= PageCount)
        {
            var png = await _source.PictureAsync(page - 1, PagePictureWidth, ct);
            if (png is { Length: > 0 })
                return ToolResult.Ok(
                    $"Drew page {page} as the reader shows it.",
                    $"A picture of page {page} of {FileName}, drawn as the reader shows it, is attached.")
                    with { Images = [new ContextImage(png, "image/png", $"{FileName} page {page}")] };
        }

        if (embedded is { Count: > 0 })
        {
            var take = embedded.Take(3).ToList();
            return ToolResult.Ok(
                $"Returned {take.Count} image(s) embedded on page {page}.",
                $"The page couldn't be drawn, so here is what page {page} of {FileName} embeds "
                + $"({take.Count} of {embedded.Count} image(s)) — this is what the page contains, not how it looks.")
                with
            {
                Images = [.. take.Select((e, i) => new ContextImage(
                    e.Data.Bytes.ToArray(), MimeFor(e.Data.Extension), $"{FileName} page {page}, image {i + 1}"))],
            };
        }

        return ToolResult.Error(
            $"Page {page} has no embedded image and couldn't be drawn. "
            + "Try pdf_read_text for this page instead.");
    }

    private static List<(PdfImageData Data, double Coverage)> ReadPageWithCoverage(
        PdfDocument document, int pageNumber, CancellationToken ct)
    {
        var images = PdfImageReader.ReadPage(document, pageNumber, ct);
        if (images.Count == 0) return [];

        double pageArea;
        try
        {
            var page = document.GetPage(pageNumber);
            pageArea = page.Width * page.Height;
        }
        catch { pageArea = 0; }

        // Coverage needs the live IPdfImage geometry, which ReadPage doesn't carry. Re-walk the page's images
        // in the same order to line them up — the page is already parsed and cached, so this is cheap.
        var geometry = new List<double>();
        try
        {
            foreach (var image in document.GetPage(pageNumber).GetImages())
                geometry.Add(PdfImageReader.PageCoverage(image, pageArea));
        }
        catch { }

        var result = new List<(PdfImageData, double)>();
        foreach (var data in images)
        {
            var coverage = data.IndexOnPage - 1 < geometry.Count ? geometry[data.IndexOnPage - 1] : 0;

            // A page that yields exactly one image and no words at all is a scan even when its geometry is
            // unreadable — the fallback exists because a degenerate bounding box must not send a scanned
            // document down the "photograph the renderer" path, which needs a live browser.
            if (coverage == 0 && images.Count == 1 && PageHasNoWords(document, pageNumber))
                coverage = 1.0;

            result.Add((data, coverage));
        }
        return result;
    }

    private static bool PageHasNoWords(PdfDocument document, int pageNumber)
    {
        try
        {
            foreach (var _ in document.GetPage(pageNumber).GetWords()) return false;
            return true;
        }
        catch { return false; }
    }

    private Task<ToolResult> ViewPageToolAsync(JsonObject args, CancellationToken ct)
    {
        if (ReadInt(args, "page") is not int page)
            return Task.FromResult(ToolResult.Error("No page number was given."));

        if (NavigateToPage is null)
            return Task.FromResult(ToolResult.Error("The on-screen reader isn't showing the document, so it can't be moved."));

        return Task.FromResult(GoToPage(page)
            ? ToolResult.Ok($"Showed page {page}.", $"The reader is now showing page {page} of {FileName}.")
            : ToolResult.Error($"{FileName} has no page {page}; it has {PageCount}."));
    }

    private ToolResult Unreadable()
        => ToolResult.Error($"Nexaflow couldn't read {FileName} — it may be damaged, or protected with a "
                          + "password. The on-screen view may still render it.");

    private static int? ReadInt(JsonObject args, string name)
    {
        if (!args.TryGetPropertyValue(name, out var node) || node is null) return null;
        try { return (int)node.GetValue<double>(); }
        catch
        {
            return int.TryParse(node.ToString(), out var parsed) ? parsed : null;
        }
    }

    private static string MimeFor(string extension) => extension switch
    {
        ".jpg" => "image/jpeg",
        ".jp2" => "image/jp2",
        _      => "image/png",
    };

    // ── Teardown ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { }
        _session.Dispose();
        _cts.Dispose();

        // Only a copy we made ourselves — never the user's file.
        if (_tempCopy is not null)
            try { File.Delete(_tempCopy); } catch { }
    }

    // ── Finding text ──────────────────────────────────────────────────────

    /// <summary>The next hit of <see cref="FindText"/>, searching the document first when the text changed.</summary>
    [RelayCommand]
    private Task FindNextAsync() => FindStepAsync(forward: true);

    /// <summary>The previous hit of <see cref="FindText"/>, searching the document first when the text changed.</summary>
    [RelayCommand]
    private Task FindPreviousAsync() => FindStepAsync(forward: false);

    private async Task FindStepAsync(bool forward)
    {
        var query = FindText.Trim();
        if (query.Length == 0 || _source is null)
        {
            ClearFind();
            return;
        }

        if (!string.Equals(query, _foundFor, StringComparison.Ordinal))
        {
            await FindAllAsync(query);
            return;
        }

        if (FoundRanges.Count == 0) return;
        FindIndex = (FindIndex + (forward ? 1 : -1) + FoundRanges.Count) % FoundRanges.Count;
        FindStatus = Str.Format("Pdf.Find.ResultFormat", FindIndex + 1, FoundRanges.Count);
    }

    /// <summary>
    /// Every hit of <paramref name="query"/>, page by page from the page being read round to the one before it, so the
    /// first hit shown is the nearest ahead. A page's text is the text the reader selects, so a hit is a stretch of it the
    /// reader can wash. Hits are shown as each page is searched; a new search abandons this one.
    /// </summary>
    private async Task FindAllAsync(string query)
    {
        _finding?.Cancel();
        var finding = _finding = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _foundFor = query;
        FoundRanges = [];
        FindIndex = -1;
        OnPropertyChanged(nameof(FoundRanges));
        FindStatus = Str.Get("Pdf.Find.Searching");

        var hits = new List<PageRange>();
        var start = Math.Clamp(CurrentPage - 1, 0, Math.Max(0, PageCount - 1));
        try
        {
            for (var step = 0; step < PageCount; step++)
            {
                var index = (start + step) % PageCount;
                var text = await _source!.TextAsync(index, finding.Token);
                for (var at = text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
                     at >= 0;
                     at = text.IndexOf(query, at + query.Length, StringComparison.CurrentCultureIgnoreCase))
                    hits.Add(new PageRange(index, at, query.Length));

                if (hits.Count > 0 && FindIndex < 0)
                {
                    Publish(hits, 0);
                    FindStatus = Str.Format("Pdf.Find.ResultFormat", 1, hits.Count);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (finding != _finding) return;
        Publish(hits, hits.Count == 0 ? -1 : Math.Max(FindIndex, 0));
        FindStatus = hits.Count == 0
            ? Str.Get("Pdf.Find.None")
            : Str.Format("Pdf.Find.ResultFormat", FindIndex + 1, hits.Count);
    }

    private void Publish(List<PageRange> hits, int index)
    {
        // Hits are kept in reading order, which is not the order they were found in when the search started part-way.
        FoundRanges = [.. hits.OrderBy(static h => h.Page).ThenBy(static h => h.Start)];
        OnPropertyChanged(nameof(FoundRanges));
        FindIndex = index < 0 ? -1 : FirstFrom(CurrentPage - 1, index);
    }

    /// <summary>The hit to show first: the first on or after the page being read.</summary>
    private int FirstFrom(int page, int fallback)
    {
        for (var i = 0; i < FoundRanges.Count; i++)
            if (FoundRanges[i].Page >= page) return i;
        return Math.Min(fallback, FoundRanges.Count - 1);
    }

    /// <summary>Drops the search and its hits.</summary>
    [RelayCommand]
    private void ClearFind()
    {
        _finding?.Cancel();
        _foundFor = string.Empty;
        FoundRanges = [];
        OnPropertyChanged(nameof(FoundRanges));
        FindIndex = -1;
        FindStatus = string.Empty;
    }

    // ── Printing ──────────────────────────────────────────────────────────

    /// <summary>
    /// The document's pages for a printer whose sheets print <paramref name="sheet"/> (device-independent pixels); a print
    /// still going when the tab closes is abandoned with it.
    /// </summary>
    internal System.Windows.Documents.DocumentPaginator? Paginator(System.Windows.Size sheet) =>
        _source is null ? null : new PdfPaginator(_source, sheet, _cts.Token);
}
