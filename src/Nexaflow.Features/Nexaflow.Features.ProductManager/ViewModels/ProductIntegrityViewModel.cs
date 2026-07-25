using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.ProductManager.ClientTools;
using Nexaflow.Features.ProductManager.Services;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>
/// The Integrity page: every snaplink whose target no longer exists, each selectable and repairable in place.
/// </summary>
/// <remarks>
/// <para>
/// A full scan tree-sitter-parses every referenced source file — seconds of work — so it <b>never</b> runs on
/// the dispatcher. The page opens instantly on the last saved report (rows re-bound to the live tree where
/// they still match), then a <see cref="ValidateSnaplinksTask"/> refreshes them from the shell's background
/// queue. <see cref="IsContextReady"/> holds the AI send until that first scan lands.
/// </para>
/// <para>
/// Applying an edit re-checks only <em>that</em> link (<see cref="SnaplinkValidator.CheckLink"/>), so a fix is
/// confirmed in milliseconds rather than costing another whole-tree scan.
/// </para>
/// </remarks>
public partial class ProductIntegrityViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly ProductStore _store;
    private readonly IShellServices _shell;
    private ProductState _state = new();
    private IntegrityReport _report = new();
    private TestCoverageManifest? _coverage;
    private IFileWatch? _watch;

    public string ProductRoot { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    [NotifyPropertyChangedFor(nameof(CanScan))]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand))]
    private bool _isScanning;

    [ObservableProperty] private string _headerLine = string.Empty;
    [ObservableProperty] private string _summaryLine = string.Empty;
    [ObservableProperty] private int _issueCount;
    [ObservableProperty] private bool _hasIssues;

    [ObservableProperty] private IIntegrityRow? _selectedIssue;

    /// <summary>Free-text filter over node title, scope, kind and detail — 200+ rows is normal on a real tree.</summary>
    [ObservableProperty] private string _filter = string.Empty;

    /// <summary>
    /// Every row on the page: broken snaplinks (<see cref="IntegrityIssueItem"/>, gating) interleaved with
    /// non-gating coverage suggestions (<see cref="CoverageAdvisoryItem"/> — a test declares via
    /// <c>[CoversNode]</c> that it covers a node the tree hasn't linked). One list, one count, one filter.
    /// </summary>
    public ObservableCollection<IIntegrityRow> Issues { get; } = [];
    public ICollectionView IssuesView { get; }

    // ── Re-pointing aids: what the named document actually offers, and where a moved file went ──
    [ObservableProperty] private string _targetsHeader = string.Empty;

    /// <summary>Picking one writes it onto the selected row's fields (it does not save — "Apply fix" does).</summary>
    [ObservableProperty] private TargetChoice? _selectedTarget;

    /// <summary>Enables the "pick a target" dropdown next to the class/method (or heading) fields.</summary>
    [ObservableProperty] private bool _hasTargets;

    /// <summary>Enables the "did it move?" dropdown next to the document-path field.</summary>
    [ObservableProperty] private bool _hasFileSuggestions;

    public ObservableCollection<TargetChoice> AvailableTargets { get; } = [];

    /// <summary>Same-named files elsewhere under the product root — the usual answer to a "missing file".</summary>
    public ObservableCollection<string> FileSuggestions { get; } = [];

    /// <summary>Lazily-built basename → relative-path index, so suggesting a moved file is instant.</summary>
    private Dictionary<string, List<string>>? _filesByName;

    /// <summary>The view that owns this page, so a right-pane open splits the right window.</summary>
    public IPageView? Host { get; set; }

    /// <summary>The shell holds the AI send until the first scan has landed.</summary>
    public bool IsContextReady => !IsScanning;
    public bool CanScan => !IsScanning;

    public ProductIntegrityViewModel(ProductStore store, string productRoot, IShellServices shell)
    {
        _store = store;
        _shell = shell;
        ProductRoot = productRoot;

        IssuesView = CollectionViewSource.GetDefaultView(Issues);
        IssuesView.Filter = Matches;

        LoadStateAndSavedReport();

        // The tree can be edited from the Product tab while this page is open; reload so the live links we
        // write through stay the ones actually on disk.
        _watch = shell.WatchFile(store.TreeFilePath, () => { if (!IsScanning) LoadStateAndSavedReport(); });

        Rescan();
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    private void LoadStateAndSavedReport()
    {
        _state = _store.Load();
        _report = _store.LoadIntegrity() ?? new IntegrityReport();
        _coverage = _store.LoadTestCoverage();
        RebuildRows();
        HeaderLine = _report.Generated is { } g && DateTime.TryParse(g, out var when)
            ? $"Last validated {when:yyyy-MM-dd HH:mm}"
            : "Never validated";
    }

    private void RebuildRows()
    {
        // Keep the caret where the user left it: a reload after a repair should land on the next row, not jump
        // back to the top of a 200-row list.
        var wasAt = SelectedIssue is null ? 0 : Math.Max(0, Issues.IndexOf(SelectedIssue));

        Issues.Clear();

        // Broken snaplinks first (they gate), then coverage suggestions (non-gating) — one list.
        var live = IntegrityBinder.Bind(_state, _report.Issues);
        for (var i = 0; i < _report.Issues.Count; i++)
            Issues.Add(new IntegrityIssueItem(_report.Issues[i], live[i]));

        var brokenCount = Issues.Count;
        var advisories = TestCoverageReconciler.Reconcile(_state, _coverage).Advisories;
        foreach (var advisory in advisories)
            Issues.Add(new CoverageAdvisoryItem(advisory));

        IssueCount = Issues.Count;
        HasIssues  = IssueCount > 0;
        SummaryLine = _report.ScannedSnaplinks == 0 && advisories.Count == 0
            ? "No scan yet."
            : $"{brokenCount} broken"
              + (advisories.Count > 0 ? $" · {advisories.Count} coverage suggestion{(advisories.Count == 1 ? "" : "s")}" : "")
              + $" · scanned {_report.ScannedSnaplinks} snaplinks across {_report.ScannedNodes} nodes";
        IssuesView.Refresh();
        SelectedIssue = Issues.Count == 0 ? null : Issues[Math.Min(wasAt, Issues.Count - 1)];
    }


    // ── Re-pointing aids ─────────────────────────────────────────────────────

    partial void OnSelectedIssueChanged(IIntegrityRow? oldValue, IIntegrityRow? newValue)
    {
        if (oldValue is IntegrityIssueItem oldIssue) oldIssue.PropertyChanged -= OnRowChanged;
        if (newValue is IntegrityIssueItem newIssue) newIssue.PropertyChanged += OnRowChanged;
        RefreshTargets();
    }

    // Retyping the document path re-offers that document's targets, so fixing a moved file then picking its
    // class is one flow.
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IntegrityIssueItem.Doc)) RefreshTargets();
    }

    /// <summary>Applying a picked target fills the row's fields; the user still presses "Apply fix" to save.</summary>
    partial void OnSelectedTargetChanged(TargetChoice? value)
    {
        if (value is null || SelectedIssue is not IntegrityIssueItem { CanEdit: true } row) return;
        if (value.TitlePath is not null)
            row.TitlePath = string.Join(IntegrityIssueItem.TitlePathSeparator, value.TitlePath);
        else
        {
            row.Class  = value.Class ?? string.Empty;
            row.Method = value.Method ?? string.Empty;
        }
    }

    /// <summary>Lists what the currently-named document can be pointed at — or, if it is gone, where it went.</summary>
    private void RefreshTargets()
    {
        AvailableTargets.Clear();
        FileSuggestions.Clear();
        SelectedTarget = null;
        TargetsHeader = string.Empty;

        if (SelectedIssue is not IntegrityIssueItem { } row || row.IsUrl || string.IsNullOrWhiteSpace(row.Doc)) { UpdateTargetFlags(); return; }

        var full = Path.IsPathRooted(row.Doc) ? row.Doc : Path.Combine(ProductRoot, row.Doc);
        if (!File.Exists(full))
        {
            SuggestMovedFile(row.Doc);
            UpdateTargetFlags();
            return;
        }

        var text = SnaplinkTargets.ReadText(full);
        if (text is null) { TargetsHeader = "File is too large to inspect."; return; }

        if (SnaplinkTargets.IsMarkdown(full))
        {
            foreach (var path in SnaplinkTargets.HeadingPaths(text))
                AvailableTargets.Add(new TargetChoice(path[^1], path.Count - 1, TitlePath: [.. path]));
        }
        else if (SnaplinkTargets.Outline(full, text, Path.GetDirectoryName(full)) is { } outline)
        {
            foreach (var type in outline.Types)
            {
                AvailableTargets.Add(new TargetChoice(type.Name, 0, Class: type.Name));
                foreach (var m in type.Members)
                    AvailableTargets.Add(new TargetChoice(m.Signature, 1, Class: type.Name, Method: m.Name));
            }
            foreach (var m in outline.TopLevel)
                AvailableTargets.Add(new TargetChoice(m.Signature, 0, Method: m.Name));
        }

        TargetsHeader = AvailableTargets.Count > 0
            ? $"{AvailableTargets.Count} targets in this file"
            : "No named targets here — this file has no tree-sitter grammar, so a whole-file link is the only option.";
        UpdateTargetFlags();
    }

    private void UpdateTargetFlags()
    {
        HasTargets = AvailableTargets.Count > 0;
        HasFileSuggestions = FileSuggestions.Count > 0;
    }

    /// <summary>A file the tree still names but that no longer exists usually just moved: offer same-named files.</summary>
    private void SuggestMovedFile(string doc)
    {
        var name = Path.GetFileName(doc);
        if (string.IsNullOrEmpty(name)) return;

        _filesByName ??= BuildFileIndex();
        if (_filesByName.TryGetValue(name, out var matches))
            foreach (var rel in matches.Where(r => !string.Equals(r, doc, StringComparison.OrdinalIgnoreCase)).Take(8))
                FileSuggestions.Add(rel);

        TargetsHeader = FileSuggestions.Count > 0
            ? $"'{name}' no longer exists here — did it move?"
            : $"'{name}' was not found anywhere under the product root.";
    }

    private Dictionary<string, List<string>> BuildFileIndex()
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        Walk(ProductRoot);
        return index;

        void Walk(string dir)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var key = Path.GetFileName(file);
                    var rel = Path.GetRelativePath(ProductRoot, file).Replace('\\', '/');
                    if (!index.TryGetValue(key, out var list)) index[key] = list = [];
                    list.Add(rel);
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    // Skipping build output keeps this walk fast and its answers meaningful.
                    if (name.StartsWith('.') || name is "bin" or "obj" or "node_modules" or "packages" or "TestResults")
                        continue;
                    Walk(sub);
                }
            }
            catch { /* unreadable dir → nothing to offer from it */ }
        }
    }

    /// <summary>Repoint the row at a suggested file; its targets reload automatically.</summary>
    [RelayCommand]
    private void UseSuggestion(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || SelectedIssue is not IntegrityIssueItem { CanEdit: true } row) return;
        row.Doc = relativePath;   // raises PropertyChanged → RefreshTargets
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    partial void OnFilterChanged(string value) => IssuesView.Refresh();

    private bool Matches(object obj)
    {
        if (obj is not IIntegrityRow row) return false;
        if (string.IsNullOrWhiteSpace(Filter)) return true;
        return Contains(row.FilterText, Filter.Trim());
    }

    private static bool Contains(string? s, string f) =>
        s is not null && s.Contains(f, StringComparison.OrdinalIgnoreCase);

    // ── Scanning (background) ────────────────────────────────────────────────

    /// <summary>Queues a full rescan on the shell's background thread; rows swap in when it completes.</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private void Rescan()
    {
        IsScanning = true;
        HeaderLine = "Validating snaplinks…";

        var task = new ValidateSnaplinksTask(_state, ProductRoot);
        _shell.QueueBackgroundTask(task, ok =>          // onComplete runs on the UI thread
        {
            IsScanning = false;
            if (!ok || task.Result is null)
            {
                HeaderLine = "Validation failed.";
                return;
            }

            _report = task.Result;
            _store.SaveIntegrity(_report);
            RebuildRows();
            HeaderLine = $"Validated just now · {DateTime.Now:HH:mm}";
        });
    }

    // ── Coverage advisories ────────────────────────────────────────────────────

    /// <summary>
    /// Turns a "declared but unlinked" advisory into a real <c>tests</c>-concern snaplink on the node — the
    /// user acting on the cross-check, so the tree stays the thing they authored. The link points at a real,
    /// compiled test, so it also marks the concern <c>done</c>: an unassessed (<c>should</c>) tests concern is
    /// now backed by proof (an inadequate test is a later <c>faulted</c> correction). A deliberate
    /// <c>shouldnt</c>/<c>faulted</c>/already-<c>done</c> status is left untouched. Saving fires the tree
    /// watch, which reloads and re-reconciles.
    /// </summary>
    [RelayCommand]
    private void AddDeclaredLink(CoverageAdvisoryItem? item)
    {
        item ??= SelectedIssue as CoverageAdvisoryItem;
        if (item is not { CanAdd: true } || !_state.Nodes.TryGetValue(item.Advisory.NodeId, out var node)) return;

        var tests = node.Concerns?.FirstOrDefault(c => c.Tag == TestCoverageReconciler.TestsConcern);
        if (tests is null)
        {
            tests = new ConcernLink { Tag = TestCoverageReconciler.TestsConcern, Status = Status.Should };
            (node.Concerns ??= []).Add(tests);
        }
        (tests.Snaplinks ??= []).Add(item.Advisory.ToSnaplink());

        var promoted = tests.Status == Status.Should;
        if (promoted) tests.Status = Status.Done;   // proof exists → the concern is done, not "not assessed"

        _store.SaveTree(_state.Nodes);
        LoadStateAndSavedReport();   // re-reads the tree we just wrote and re-reconciles (advisory now gone)
        _shell.ShowNotification(promoted
            ? $"Linked {item.TestDisplay} to '{node.Title}' — tests marked done."
            : $"Linked {item.TestDisplay} to '{node.Title}'.");
    }

    // ── Repair ───────────────────────────────────────────────────────────────

    /// <summary>Writes the edited target back to the live link, saves, and re-checks just that link.</summary>
    [RelayCommand]
    private void Apply()
    {
        if (SelectedIssue is not IntegrityIssueItem { CanEdit: true } row) return;

        row.WriteBack();
        _store.SaveTree(_state.Nodes);

        var (kind, detail) = SnaplinkValidator.CheckLink(row.Live!, ProductRoot);
        if (detail is null) { Resolved(row, "Snaplink fixed."); return; }

        // Still broken. Carry the edit into the stored report too: saving the tree fires the file watch, which
        // re-binds every row by comparing the reported link against the live one. Leave the report holding the
        // pre-edit target and that comparison now fails — the row would silently go read-only.
        row.Issue.Link   = row.Live!;
        row.Issue.Kind   = kind;
        row.Issue.Detail = detail;
        _store.SaveIntegrity(_report);

        row.SetDiagnosis(kind, detail);
        _shell.ShowNotification("Still broken: " + detail);
    }

    /// <summary>Drops the broken link entirely.</summary>
    [RelayCommand]
    private void RemoveLink()
    {
        if (SelectedIssue is not IntegrityIssueItem { CanEdit: true } row) return;
        if (!_state.Nodes.TryGetValue(row.NodeId, out var node)) return;

        var list = IntegrityBinder.LinkList(node, row.Issue.Concern);
        if (list is null) return;

        var removedAt = list.IndexOf(row.Live!);
        if (removedAt < 0) return;
        list.RemoveAt(removedAt);

        if (list.Count == 0)
        {
            if (row.Issue.Concern is null) node.Snaplinks = null;
            else if (node.Concerns?.FirstOrDefault(c => c.Tag == row.Issue.Concern) is { } concern)
                concern.Snaplinks = null;
        }

        // The list just got shorter — keep the other reported indices on it describing the real tree.
        IntegrityBinder.ReindexAfterRemoval(_report, row.Issue, removedAt);

        _store.SaveTree(_state.Nodes);
        Resolved(row, "Snaplink removed.");
    }

    /// <summary>
    /// Drops a repaired row. Indices of the remaining reported issues on the same node may now be stale, so
    /// their rows are re-bound (a drifted row simply becomes read-only until the next full scan).
    /// </summary>
    private void Resolved(IntegrityIssueItem row, string message)
    {
        _report.Issues.Remove(row.Issue);
        _store.SaveIntegrity(_report);
        LoadStateAndSavedReport();   // re-reads the tree we just wrote and re-binds every row
        _shell.ShowNotification(message);
    }

    // Both "opens" land in the right pane: you are repairing a link here and want the evidence beside the list,
    // not on top of it. Note the shell always creates a *fresh* tab for a right-pane open (it deliberately does
    // not adopt an existing match), so repeated opens stack up rather than reusing one tab.

    /// <summary>Focuses the offending node in the Product tab, beside this page.</summary>
    [RelayCommand]
    private void OpenNode()
    {
        if (SelectedIssue is not { } row) return;
        _shell.OpenTab(ProductManagerTabRegistration.StaticPageKind,
            new Dictionary<string, string> { ["path"] = ProductRoot, ["node"] = row.NodeId },
            Host, inRightPane: true);
    }

    /// <summary>Opens the link's target file beside this page, so the right class/heading can be read off it.</summary>
    [RelayCommand]
    private void OpenTarget()
    {
        if (SelectedIssue is not IntegrityIssueItem { } row || string.IsNullOrWhiteSpace(row.Doc)) return;
        var full = Path.IsPathRooted(row.Doc) ? row.Doc : Path.Combine(ProductRoot, row.Doc);
        if (!File.Exists(full)) { _shell.ShowError($"{row.Doc} does not exist."); return; }
        _shell.OpenTab(row.IsMarkdown ? "Markdown" : "Code",
            new Dictionary<string, string> { ["path"] = full }, Host, inRightPane: true);
    }

    // ── IPageViewModel ───────────────────────────────────────────────────────

    public string GetContext()
    {
        if (IsScanning) return "Product integrity: a snaplink validation scan is still running.";
        if (IssueCount == 0) return $"Product integrity for '{ProductRoot}': every snaplink points at a real target.";

        var lines = Issues.Take(30).Select(i => $"  {i.NodeId} [{i.Scope}] {i.Kind}: {i.Detail}");
        return $"Product integrity for '{ProductRoot}': {SummaryLine}.\nBroken snaplinks:\n{string.Join("\n", lines)}"
             + (IssueCount > 30 ? $"\n  …and {IssueCount - 30} more." : string.Empty);
    }

    /// <summary>
    /// The same product surface every other Product view exposes. The Integrity page is the one place the
    /// model is most likely to need it — it is looking at a list of broken snaplinks, and without these it
    /// could describe the breakage but not re-point a single link.
    /// </summary>
    public IReadOnlyList<IClientTool> GetClientTools() => _tools ??= ProductTools.ForRoot(ProductRoot);
    private IReadOnlyList<IClientTool>? _tools;

    /// <summary>The product folder, so this page and the sunburst and the graph share one scope — they are
    /// three views of the same tree, and pinning two of them should not read as two datasets.</summary>
    public string? GetSecurityContext() => ProductRoot;

    public string? GetAiSystemPromptGuidance() =>
        "A list of snaplinks whose target no longer exists (missing file, renamed heading, undeclared " +
        "class/method, malformed URL). Each can be re-pointed by editing its target, or removed — " +
        "product_remap_snaplinks follows a file that moved, and product_validate re-checks the whole tree.";

    public void Dispose() => _watch?.Dispose();
}
