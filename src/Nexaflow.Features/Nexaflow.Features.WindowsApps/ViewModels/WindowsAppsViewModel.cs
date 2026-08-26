using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Features.WindowsApps.Models;
using Nexaflow.Features.WindowsApps.Services;

namespace Nexaflow.Features.WindowsApps.ViewModels;

/// <summary>
/// The installed-apps page: an "Add or remove programs" style list of Win32 + Store apps, sortable by
/// any column, filterable by name (footer box or the shell query handler), with the same per-row actions
/// Windows offers — Uninstall for both, Modify for a Win32 program, and Move / Advanced options for a
/// Store package (the latter two open the right-hand
/// <see cref="AppAdvancedOptionsViewModel"/> pane). Every operation runs off the UI thread via the
/// shell's background-activity queue.
/// </summary>
public sealed partial class WindowsAppsViewModel : ObservableObject, IPageViewModel, ISearchable
{
    private readonly IShellServices _shell;
    private readonly InstalledAppsService _service;
    private readonly ICollectionView _view;

    [ObservableProperty] private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoading;
    [ObservableProperty] private string _sortColumn = nameof(InstalledAppItem.Name);
    [ObservableProperty] private bool _sortAscending = true;

    /// <summary>All loaded apps; the bound view applies the live filter + sort over this.</summary>
    public ObservableCollection<InstalledAppItem> Apps { get; } = [];

    /// <summary>Current grid selection, pushed from the view (multi-select). Narrows <see cref="GetContext"/>.</summary>
    public IReadOnlyList<InstalledAppItem> SelectedApps { get; private set; } = [];

    public WindowsAppsViewModel(IShellServices shell) : this(shell, new InstalledAppsService()) { }

    public WindowsAppsViewModel(IShellServices shell, InstalledAppsService service)
    {
        _shell   = shell;
        _service = service;

        _view = CollectionViewSource.GetDefaultView(Apps);
        _view.Filter = o => o is InstalledAppItem item && MatchesFilter(item);
        ApplySort();

        Refresh();
    }

    // The footer box sets FilterText (literal); a search from the AI bar may instead install a compiled
    // pattern, or an explicit set of names the agent picked. Checked most-specific first.
    private bool MatchesFilter(InstalledAppItem item)
    {
        if (_pinnedNames is not null) return _pinnedNames.Contains(item.Name);
        if (_filterRequest is not null) return _filterRequest.Matches(item.Name);
        return string.IsNullOrWhiteSpace(FilterText) ||
               item.Name.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // Set when the filter came from a search request rather than the footer box.
    private SearchRequest?  _filterRequest;
    private HashSet<string>? _pinnedNames;

    partial void OnFilterTextChanged(string value)
    {
        // Typing in the footer box supersedes any AI-driven filter.
        if (!_suppressFilterReset) { _filterRequest = null; _pinnedNames = null; }
        _view.Refresh();
    }

    private bool _suppressFilterReset;

    // ── Loading ───────────────────────────────────────────────────────────
    [RelayCommand]
    private void Refresh()
    {
        if (IsLoading) return;
        IsLoading = true;

        // Pass 1 — fast: names + cheap metadata, shown as soon as it returns.
        var task = new LoadInstalledAppsTask(_service);
        _shell.QueueBackgroundTask(task, onComplete: ok =>
        {
            // Runs on the UI thread (ShellServices marshals the callback).
            if (ok) Populate(task.Result);
            IsLoading = false;
            if (ok) StartSizePass();   // pass 2 runs in the background once the list is visible
        });
    }

    private void Populate(IReadOnlyList<InstalledApp> apps)
    {
        Apps.Clear();
        foreach (var app in apps) Apps.Add(new InstalledAppItem(app));
        _view.Refresh();
        RebindAdvancedOptions();
    }

    /// <summary>
    /// A rescan replaces every row object, so an open Advanced-options pane would be left holding a
    /// detached item. Re-point it at the equivalent new row — or close it if the app is gone (which is
    /// what a successful uninstall looks like from here).
    /// </summary>
    private void RebindAdvancedOptions()
    {
        if (AdvancedOptions is not { } pane) return;

        // Match on package identity, never on a null key — that would happily bind to the first Win32 row.
        var key = pane.Item.App.PackageFullName;
        var again = string.IsNullOrEmpty(key)
            ? null
            : Apps.FirstOrDefault(a => a.App.PackageFullName == key);

        if (again is null) AdvancedOptions = null;
        else pane.Rebind(again);
    }

    /// <summary>Pass 2 — measure the size of apps that didn't report one, then patch them in.</summary>
    private void StartSizePass()
    {
        var pending = Apps.Where(a => a.SizeBytes is null
                                      && !string.IsNullOrWhiteSpace(a.App.InstallLocation)).ToList();
        if (pending.Count == 0) return;

        var task = new FillAppSizesTask(pending);
        _shell.QueueBackgroundTask(task, onComplete: _ =>
        {
            foreach (var (item, size) in task.Measured)
                item.SizeBytes = size;

            // If the user is sorting by size, re-apply so the freshly-measured rows land in order.
            if (SortColumn == nameof(InstalledAppItem.SizeBytes))
                _view.Refresh();
        });
    }

    // ── Sorting ───────────────────────────────────────────────────────────
    [RelayCommand]
    private void SortBy(string column)
    {
        if (string.IsNullOrEmpty(column)) return;
        if (SortColumn == column)
            SortAscending = !SortAscending;
        else
        {
            SortColumn    = column;
            SortAscending = true;
        }
        ApplySort();
    }

    private void ApplySort()
    {
        _view.SortDescriptions.Clear();
        _view.SortDescriptions.Add(new SortDescription(
            SortColumn, SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending));
    }

    // ── Selection ─────────────────────────────────────────────────────────
    public void SetSelection(IReadOnlyList<InstalledAppItem> selected) => SelectedApps = selected;

    // ── Uninstall ─────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task Uninstall(InstalledAppItem? item)
    {
        if (item is null) return;

        var kind = item.App.Source == AppSource.Store
            ? "This will remove the Store app for your account."
            : "This launches the program's own uninstaller, which may prompt for confirmation or elevation.";

        if (!await _shell.ConfirmAsync($"Uninstall {item.Name}?", kind))
            return;

        var task = new UninstallAppTask(_service, item.App);
        _shell.QueueBackgroundTask(task, onComplete: _ =>
        {
            if (task.Result.Success)
                Refresh();
            else
                _shell.ShowError($"Couldn't uninstall {item.Name}: {task.Result.Error}");
        });
    }

    // ── Modify (Win32) ────────────────────────────────────────────────────────
    /// <summary>
    /// Reopens the program's own installer in maintenance mode so the user can add or remove features.
    /// No confirmation of ours: the vendor's UI is the confirmation, and nothing is removed until the
    /// user says so there — the same contract as Add/remove programs' "Modify".
    /// </summary>
    [RelayCommand]
    private void Modify(InstalledAppItem? item)
    {
        if (item is null || !item.CanModify) return;

        var task = new ModifyAppTask(_service, item.App);
        _shell.QueueBackgroundTask(task, onComplete: _ =>
        {
            if (task.Result.Success) Refresh();   // features may have changed → re-read the entry
            else _shell.ShowError($"Couldn't change {item.Name}: {task.Result.Error}");
        });
    }

    // ── Advanced options pane (Store) ─────────────────────────────────────────

    /// <summary>The Store app whose Advanced-options pane is open, or null when the pane is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdvancedOpen))]
    private AppAdvancedOptionsViewModel? _advancedOptions;

    /// <summary>Drives the right-hand pane's visibility (and its splitter).</summary>
    public bool IsAdvancedOpen => AdvancedOptions is not null;

    [RelayCommand]
    private void ShowAdvancedOptions(InstalledAppItem? item) => OpenAdvancedOptions(item, forMove: false);

    /// <summary>"Move…" opens the same pane with its Move card called out — that's where the drive picker lives.</summary>
    [RelayCommand]
    private void ShowMove(InstalledAppItem? item) => OpenAdvancedOptions(item, forMove: true);

    private void OpenAdvancedOptions(InstalledAppItem? item, bool forMove)
    {
        if (item is null || !item.IsStore) return;

        var pane = new AppAdvancedOptionsViewModel(_shell, _service, item, CloseAdvancedOptions)
        {
            MoveHighlighted = forMove,
        };
        AdvancedOptions = pane;
        pane.Load();
    }

    [RelayCommand]
    private void CloseAdvancedOptions() => AdvancedOptions = null;

    // ── Remove orphaned record ───────────────────────────────────────────────
    [RelayCommand]
    private async Task DeleteRecord(InstalledAppItem? item)
    {
        if (item is null || !item.IsOrphaned) return;

        var ok = await _shell.ConfirmAsync(
            $"Remove {item.Name} from the list?",
            "This entry looks like a leftover — its files and uninstaller can't be found. " +
            "This removes the registry entry only; it does not delete any files.");
        if (!ok) return;

        var task = new DeleteAppRecordTask(_service, item.App);
        _shell.QueueBackgroundTask(task, onComplete: _ =>
        {
            if (task.Succeeded) Refresh();
            else _shell.ShowError($"Couldn't remove the entry for {item.Name}. " +
                                  "Removing a machine-wide entry may require running as administrator.");
        });
    }

    // ── Open install location ────────────────────────────────────────────────
    [RelayCommand]
    private void OpenLocation(InstalledAppItem? item)
    {
        var path = item?.App.InstallLocation;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        _shell.OpenTab("FileSystem", new Dictionary<string, string>
        {
            ["mode"]  = "path",
            ["path"]  = path,
            ["label"] = item!.Name,
        });
    }

    // ── Query-handler hook ──────────────────────────────────────────────────
    /// <summary>Sets the name filter from the shell input bar; returns null (handled silently).</summary>
    public string? SetFilter(string text)
    {
        FilterText = text ?? string.Empty;
        return null;
    }

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription => "the installed application list, by application name";

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        // Apps have names, not files — a filename glob isn't a constraint this list can honour.
        if (request.HasNameOnlyTerms)
            return SearchOutcome.Unsupported("Filename filters don't apply to the application list.");

        if (request.IsRegex && !request.TryCompileRegex(out _, out var error))
            return SearchOutcome.Unsupported($"Invalid regular expression: {error}");

        // Snapshot the names off the UI thread's collection before scanning.
        var matches = Apps.Where(a => request.Matches(a.Name)).ToList();

        if (display)
        {
            _suppressFilterReset = true;
            FilterText     = SearchSyntax.Format(request);
            _filterRequest = request;
            _pinnedNames   = null;
            _suppressFilterReset = false;
            _view.Refresh();
        }

        await Task.CompletedTask;
        return matches.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(matches));
    }

    /// <summary>Pins the list to exactly the apps the agent chose, by name.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var names = hits.Select(h => h.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return Task.FromResult(false);

        // Decline rather than pin to nothing: see ServicesViewModel.ShowResultsAsync. Returning true here
        // told the agent it had narrowed the list when it had in fact emptied it.
        var kept = Apps.Count(a => names.Contains(a.Name));
        if (kept == 0) return Task.FromResult(false);

        _suppressFilterReset = true;
        FilterText     = $"{kept} selected";
        _filterRequest = null;
        _pinnedNames   = names;
        _suppressFilterReset = false;
        _view.Refresh();
        return Task.FromResult(true);
    }

    private static IReadOnlyList<SearchHit> HitsFor(IEnumerable<InstalledAppItem> apps) =>
        apps.Select(a => new SearchHit(
                a.Name,
                a.Name,
                string.IsNullOrEmpty(a.Publisher) ? a.Version : $"{a.Publisher} {a.Version}".Trim()))
            .ToList();

    // ── IPageViewModel ───────────────────────────────────────────────────────
    public string GetContext()
    {
        if (SelectedApps.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Installed apps — {SelectedApps.Count} selected:");
            foreach (var a in SelectedApps)
                sb.AppendLine($"  • {a.Name}{(string.IsNullOrEmpty(a.Version) ? "" : $" {a.Version}")}"
                              + $"{(string.IsNullOrEmpty(a.Publisher) ? "" : $" — {a.Publisher}")}");
            return sb.ToString().TrimEnd();
        }

        if (IsLoading) return "Installed applications: still scanning…";
        if (Apps.Count == 0) return "Installed applications: none found.";

        const int cap = 200;
        var names = Apps.Take(cap).Select(a => a.Name);
        var more  = Apps.Count > cap ? $" (+{Apps.Count - cap} more)" : "";
        return $"{Apps.Count} installed applications: {string.Join(", ", names)}{more}";
    }

    /// <summary>Ready once pass 1 (the app list) has loaded; the background size pass enriches rows but
    /// isn't context-blocking. Tied to IsLoading so a failed scan still releases the send.</summary>
    public bool IsContextReady => !IsLoading;

    /// <summary>The machine's installed-application set is a single, machine-wide scope — the tools here
    /// query all installed apps, with no per-file/per-folder boundary. A stable constant (not null) so two
    /// installed-apps tabs pinned together collapse to one boundary instead of first-wins.</summary>
    public string? GetSecurityContext() => "installed-applications";

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "get_application_details",
            "Get details (publisher, version, install date, size, location, source) for one installed application by name.",
            [new ClientToolParameter("name", "The application name (case-insensitive, exact or partial match).")],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(GetApplicationDetails(args))),

        new DelegateClientTool(
            "list_installed_applications",
            "List the names of all installed applications (ignores the current selection and filter).",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(ListInstalledApplications())),
    ];

    private ToolResult GetApplicationDetails(JsonObject args)
    {
        var name = args["name"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(name))
            return ToolResult.Error("No name given", "The 'name' argument is required.");

        var match = Apps.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? Apps.FirstOrDefault(a => a.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return ToolResult.Error($"'{name}' not found", $"No installed application matches '{name}'.");

        var a = match.App;
        var sb = new StringBuilder();
        sb.AppendLine($"Name: {a.Name}");
        sb.AppendLine($"Publisher: {a.Publisher ?? "—"}");
        sb.AppendLine($"Version: {a.Version ?? "—"}");
        sb.AppendLine($"Installed: {match.InstallDateText}");
        sb.AppendLine($"Size: {match.SizeText}");
        sb.AppendLine($"Source: {match.SourceLabel}");
        sb.AppendLine($"Location: {a.InstallLocation ?? "—"}");
        return ToolResult.Ok($"Details for {a.Name}", sb.ToString().TrimEnd());
    }

    private ToolResult ListInstalledApplications()
    {
        if (Apps.Count == 0) return ToolResult.Ok("No applications", "No installed applications found.");
        var names = Apps.Select(a => a.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase);
        return ToolResult.Ok($"{Apps.Count} applications", string.Join("\n", names));
    }
}
