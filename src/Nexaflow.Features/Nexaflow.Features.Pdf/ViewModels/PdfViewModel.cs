using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Pdf.Models;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
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
/// Drives the PDF reader tab. The page itself is rendered by the shared browser surface (Edge renders PDFs
/// natively, toolbar and all); everything this view-model adds is what the renderer can't tell anyone —
/// the document's properties and table of contents in the side panel, and a tool surface that lets the AI
/// actually read the text, look at a page, and pull out a figure.
/// <para>
/// The document is opened once for the tab's lifetime and every reader serialised behind one lock: PdfPig's
/// <see cref="PdfDocument"/> is not thread-safe, and re-parsing the cross-reference table per question is
/// the difference between a fast answer and a slow one on a large report.
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

    private readonly IShellServices _shell;
    private readonly PdfConfig _config;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _docLock = new(1, 1);
    private readonly Timer _idleTimer;

    private readonly string _path;          // as opened — may be virtual (inside an archive)
    private readonly string? _tempCopy;     // materialised copy to delete on dispose, if we made one

    private PdfDocumentScope? _scope;
    private bool _scopeFailed;              // TryOpen already said no; don't keep retrying per tool call
    private DateTime _lastUsedUtc = DateTime.UtcNow;
    private bool _disposed;

    /// <summary>The file:// URI the renderer navigates to.</summary>
    public Uri FileUri { get; }

    public string FileName { get; }

    // ── Panel state ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isMetadataLoading = true;

    /// <summary>What the panel says while it has nothing else to show — loading, or why it never will.</summary>
    [ObservableProperty] private string _panelStatus = "Reading document…";

    [ObservableProperty] private bool _isPanelOpen = true;

    public ObservableCollection<PdfInfoRow>     Properties { get; } = [];
    public ObservableCollection<PdfOutlineItem> Contents   { get; } = [];

    [ObservableProperty] private bool _hasProperties;
    [ObservableProperty] private bool _hasContents;

    /// <summary>
    /// The contents row the user last picked. Kept so the panel keeps showing where they are in the document
    /// instead of losing the place the moment the click finishes — a table of contents that forgets what you
    /// just chose makes you re-find it on every jump.
    /// </summary>
    [ObservableProperty] private PdfOutlineItem? _selectedOutlineItem;

    /// <summary>Shown in the Contents tab when the document simply has no outline — the common case.</summary>
    public string ContentsEmptyMessage => IsMetadataLoading
        ? "Reading document…"
        : "This PDF has no table of contents.";

    /// <summary>
    /// Whether the renderer honours "go to page N". Probed on the first jump: some builds of the embedded
    /// PDF viewer ignore a page fragment entirely, and a table of contents whose rows do nothing when
    /// clicked is worse than one that plainly isn't clickable.
    /// </summary>
    [ObservableProperty] private bool _canNavigateToPage = true;

    /// <summary>True while a shell overlay covers the page; the native browser HWND has to hide for it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SurfaceVisible))]
    private bool _isCovered;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SurfaceVisible))]
    private bool _surfaceAvailable = true;

    public bool SurfaceVisible => SurfaceAvailable && !IsCovered;

    [ObservableProperty] private string _failureMessage = string.Empty;
    [ObservableProperty] private bool   _runtimeMissing;

    // ── View-supplied hooks ───────────────────────────────────────────────

    /// <summary>
    /// Set by the view: moves the rendered view to a 1-based page, optionally to a position measured down
    /// from the top of it. False = the renderer wouldn't move.
    /// </summary>
    public Func<int, double?, CancellationToken, Task<bool>>? NavigateToPageAsync { get; set; }

    /// <summary>Set by the view: captures what the renderer is currently showing, as a PNG.</summary>
    public Func<CancellationToken, Task<byte[]?>>? CapturePageAsync { get; set; }

    public PdfViewModel(string path, IShellServices shell, PdfConfig config)
    {
        _path    = path;
        _shell   = shell;
        _config  = config;
        FileName = Path.GetFileName(path);

        // A PDF inside an archive is virtual and the renderer can't load it; materialise a real copy and
        // remember to delete it. A plain on-disk file passes straight through and gets no temp copy.
        var real = RealizeLocalFile(path);
        _tempCopy = string.Equals(real, path, StringComparison.OrdinalIgnoreCase) ? null : real;
        FileUri   = new Uri(real);

        // Thread-pool timer, not a DispatcherTimer: closing an idle document is background housekeeping and
        // must not depend on (or touch) the UI thread.
        _idleTimer = new Timer(_ => CloseDocumentIfIdle(), null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private static string RealizeLocalFile(string filePath)
    {
        try { return VirtualFileSystem.Instance.MaterializeFile(filePath); }
        catch { return filePath; }
    }

    // ── Panel load ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads everything cheap about the document and fills the panel. Runs entirely off the UI thread; the
    /// renderer paints in parallel and never waits for this.
    /// </summary>
    public async Task LoadAsync()
    {
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
            PanelStatus = "Nexaflow couldn't read this PDF's structure — it may be damaged, or protected "
                        + "with a password. The page view above may still render it.";
            IsMetadataLoading = false;
            OnPropertyChanged(nameof(ContentsEmptyMessage));
            return;
        }

        foreach (var row in BuildPropertyRows(info)) Properties.Add(row);
        foreach (var entry in info.Outline)
            Contents.Add(new PdfOutlineItem(entry.Title, entry.Level, entry.PageNumber, entry.OffsetFromTop)
            {
                CanJump = entry.PageNumber is not null && CanNavigateToPage,
            });

        HasProperties     = Properties.Count > 0;
        HasContents       = Contents.Count > 0;
        PanelStatus       = string.Empty;
        IsMetadataLoading = false;
        OnPropertyChanged(nameof(ContentsEmptyMessage));
    }

    /// <summary>
    /// One row per fact the document actually states. Empty fields are omitted rather than rendered as a
    /// column of dashes — a panel of blanks reads as broken, and the absence is not information.
    /// </summary>
    private IEnumerable<PdfInfoRow> BuildPropertyRows(PdfDocumentInfo info)
    {
        yield return new PdfInfoRow("File", FileName);

        var size = TryFileSize();
        if (size is long bytes) yield return new PdfInfoRow("Size", SizeFormatter.FormatBytes(bytes));

        yield return new PdfInfoRow("Pages", info.PageCount.ToString());
        yield return new PdfInfoRow("PDF version", info.PdfVersion);

        if (info.IsEncrypted)
            // Worth saying plainly: the document IS encrypted, and it opened anyway because it only carries
            // an owner password (a "no copying" flag any reader ignores). Claiming it isn't protected would
            // be wrong; claiming we broke a password would be worse.
            yield return new PdfInfoRow("Protection", "Protected — opened read-only");

        if (info.HasForm)
            yield return new PdfInfoRow("Form", $"{info.FormFieldCount} field{(info.FormFieldCount == 1 ? "" : "s")}");

        if (info.Title      is { } t) yield return new PdfInfoRow("Title", t);
        if (info.Author     is { } a) yield return new PdfInfoRow("Author", a);
        if (info.Subject    is { } s) yield return new PdfInfoRow("Subject", s);
        if (info.Keywords   is { } k) yield return new PdfInfoRow("Keywords", k);
        if (info.Creator    is { } c) yield return new PdfInfoRow("Creator", c);
        if (info.Producer   is { } p) yield return new PdfInfoRow("Producer", p);
        if (info.CreationDate is { } cd) yield return new PdfInfoRow("Created", cd);
        if (info.ModifiedDate is { } md) yield return new PdfInfoRow("Modified", md);
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
            ? $"This PDF is {SizeFormatter.FormatBytes(bytes)}, past the {_config.ViewerMaxFileSizeMb} MB "
              + "limit for reading a document's structure. The page view above still renders it."
            : null;
    }

    // ── Document access ───────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="read"/> against the open document on a background thread, opening it first if
    /// needed. Returns default when the document can't be opened at all — every caller has to be able to
    /// say so honestly rather than reporting an empty document.
    /// </summary>
    private async Task<T?> WithDocumentAsync<T>(Func<PdfDocument, T> read, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var token = linked.Token;

        await _docLock.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                if (_scope is null && !_scopeFailed)
                {
                    _scope = PdfDocumentScope.TryOpen(_path, token);
                    _scopeFailed = _scope is null;
                }

                _lastUsedUtc = DateTime.UtcNow;
                return _scope is null ? default : read(_scope.Document);
            }, token);
        }
        finally
        {
            _docLock.Release();
        }
    }

    /// <summary>
    /// Drops the parsed document after a spell of inactivity. A large scan holds a sizeable object graph and
    /// a file handle open; a background tab nobody has asked anything about in five minutes shouldn't. The
    /// next read reopens it.
    /// </summary>
    private void CloseDocumentIfIdle()
    {
        if (_disposed || _scope is null) return;
        if (DateTime.UtcNow - _lastUsedUtc < TimeSpan.FromMinutes(5)) return;

        if (!_docLock.Wait(0)) return;   // busy — try again on the next tick rather than blocking one
        try
        {
            if (DateTime.UtcNow - _lastUsedUtc >= TimeSpan.FromMinutes(5))
            {
                _scope?.Dispose();
                _scope = null;
                _scopeFailed = false;   // a fresh open may well succeed; the earlier one wasn't a verdict
            }
        }
        finally { _docLock.Release(); }
    }

    // ── Page navigation ───────────────────────────────────────────────────

    /// <summary>
    /// Moves the rendered view to <paramref name="page"/>. False means it wouldn't move — the caller must
    /// say so rather than pretend. The first failure latches <see cref="CanNavigateToPage"/> off, which
    /// turns the Contents rows into plain labels instead of links that do nothing.
    /// </summary>
    public async Task<bool> GoToPageAsync(int page, CancellationToken ct, double? offsetFromTop = null)
    {
        if (NavigateToPageAsync is null || !CanNavigateToPage) return false;
        if (page < 1) return false;

        var moved = await NavigateToPageAsync(page, offsetFromTop, ct);
        if (!moved) DisablePageNavigation();
        return moved;
    }

    private void DisablePageNavigation()
    {
        CanNavigateToPage = false;
        foreach (var item in Contents) item.CanJump = false;
    }

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    /// <summary>Everything the Properties tab shows, as plain lines — the panel's most-wanted action.</summary>
    [RelayCommand]
    private void CopyAllProperties()
    {
        if (Properties.Count == 0) return;
        try { System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, Properties)); }
        catch { _shell.ShowError("Couldn't copy to the clipboard."); }
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

        var pages = Properties.FirstOrDefault(p => p.Label == "Pages")?.Value;
        if (pages is not null) sb.Append($", {pages} page(s)");

        if (Properties.FirstOrDefault(p => p.Label == "Title")?.Value is { } title)
            sb.Append($", titled \"{title}\"");
        if (Properties.FirstOrDefault(p => p.Label == "Author")?.Value is { } author)
            sb.Append($", by {author}");

        sb.Append('.');

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
            parallelizable: false),

        new DelegateClientTool(
            "pdf_page_image",
            "Look at a whole page as a picture. Use this for a scanned page (one that pdf_read_text returns no "
            + "text for), and for any page whose layout, chart or table you need to see rather than read.",
            [new ClientToolParameter("page", "Page to look at, 1-based.", Type: "number")],
            ToolSafety.SafeOperation,
            PageImageToolAsync,
            parallelizable: false),

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
            if (string.IsNullOrWhiteSpace(page.Text))
                sb.AppendLine("(no text on this page — it may be a scan; try pdf_page_image)");
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

        if (CanNavigateToPage && CapturePageAsync is not null)
        {
            var moved = await GoToPageAsync(page, ct);
            if (moved)
            {
                var png = await CapturePageAsync(ct);
                if (png is { Length: > 0 })
                    return ToolResult.Ok(
                        $"Captured page {page} as rendered.",
                        $"A picture of page {page} of {FileName} as it is rendered on screen is attached.")
                        with { Images = [new ContextImage(png, "image/png", $"{FileName} page {page}")] };
            }
        }

        if (embedded is { Count: > 0 })
        {
            var take = embedded.Take(3).ToList();
            return ToolResult.Ok(
                $"Returned {take.Count} image(s) embedded on page {page}.",
                $"The renderer couldn't be photographed, so here is what page {page} of {FileName} embeds "
                + $"({take.Count} of {embedded.Count} image(s)) — this is what the page contains, not how it looks.")
                with
            {
                Images = [.. take.Select((e, i) => new ContextImage(
                    e.Data.Bytes.ToArray(), MimeFor(e.Data.Extension), $"{FileName} page {page}, image {i + 1}"))],
            };
        }

        return ToolResult.Error(
            $"Page {page} has no embedded image and the on-screen renderer isn't available to photograph. "
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

    private async Task<ToolResult> ViewPageToolAsync(JsonObject args, CancellationToken ct)
    {
        if (ReadInt(args, "page") is not int page)
            return ToolResult.Error("No page number was given.");

        if (NavigateToPageAsync is null)
            return ToolResult.Error("The on-screen reader isn't available, so it can't be moved.");
        if (!CanNavigateToPage)
            return ToolResult.Error("This document's renderer doesn't support jumping to a page, so the view "
                                  + "can't be moved. You can still read any page with pdf_read_text.");

        var moved = await GoToPageAsync(page, ct);
        return moved
            ? ToolResult.Ok($"Showed page {page}.", $"The reader is now showing page {page} of {FileName}.")
            : ToolResult.Error($"The reader wouldn't move to page {page}.");
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
        _idleTimer.Dispose();

        // Under the lock so a read in flight isn't parsing a document being torn out from under it. If it
        // doesn't yield in time, close anyway: _cts is already cancelled, so the reader is unwinding, and a
        // tab closing must not wait on it indefinitely.
        var held = false;
        try { held = _docLock.Wait(TimeSpan.FromSeconds(2)); } catch { }

        try
        {
            _scope?.Dispose();
            _scope = null;
        }
        catch { }
        finally
        {
            if (held) _docLock.Release();
        }

        _docLock.Dispose();
        _cts.Dispose();

        // Only a copy we made ourselves — never the user's file.
        if (_tempCopy is not null)
            try { File.Delete(_tempCopy); } catch { }
    }
}
