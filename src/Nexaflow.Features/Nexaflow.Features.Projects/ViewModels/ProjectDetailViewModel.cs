using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Projects.Model;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.Projects.ViewModels;

/// <summary>
/// The tabbed project detail editor (Project Details + Backlog). Markdown description, a completion-
/// criteria list, and a backlog with per-item markdown + a configurable status (forward "Progress" step
/// plus an any-status dropdown). Opening a legacy (pre-2.0) project prompts to upgrade; until upgraded the
/// editor is read-only. Two AI buttons open a new conversation seeded with the item and the project folder.
/// </summary>
public partial class ProjectDetailViewModel : ObservableObject, IPageViewModel
{
    private readonly ProjectOperations _svc;
    private readonly ProjectsConfig    _config;
    private readonly IShellServices?   _shell;
    private readonly IAIService?       _ai;
    private readonly string            _projectPath;

    public string FolderName { get; }

    /// <summary>The workspace config — child VMs resolve statuses through it.</summary>
    public ProjectsConfig Config => _config;

    // ── Project header ──
    [ObservableProperty] private string  _projectName  = string.Empty;
    [ObservableProperty] private string  _description  = string.Empty;   // markdown
    [ObservableProperty] private string  _lastModified = string.Empty;
    [ObservableProperty] private string? _solutionFile;

    /// <summary>0 = Project Details, 1 = Backlog. Defaulted in <see cref="Load"/>.</summary>
    [ObservableProperty] private int _selectedTabIndex;

    // ── Legacy-schema upgrade gating ──
    [ObservableProperty] private bool _needsUpgrade;
    [ObservableProperty] private bool _isReadOnly;

    /// <summary>Inverse of <see cref="IsReadOnly"/> — bound to the editors' IsEnabled.</summary>
    public bool IsEditable => !IsReadOnly;
    partial void OnIsReadOnlyChanged(bool value) => OnPropertyChanged(nameof(IsEditable));

    public ObservableCollection<CompletionCriterionViewModel> CompletionCriteria { get; } = [];
    public ObservableCollection<BacklogItemViewModel>          Backlog            { get; } = [];

    [ObservableProperty] private BacklogItemViewModel? _selectedItem;
    [ObservableProperty] private string _newTodoTitle = string.Empty;

    // ── Confirmation overlay (delete) ──
    [ObservableProperty] private bool   _confirmationVisible;
    [ObservableProperty] private string _confirmationTitle  = "Are you sure?";
    [ObservableProperty] private string _confirmationPrompt = string.Empty;
    private Action? _pendingConfirmAction;

    public event Action<string>? OpenFilesRequested;

    private bool _loading;

    public ProjectDetailViewModel(ProjectOperations ops, ProjectsConfig config, string folderName,
                                  IShellServices? shell = null, IAIService? ai = null)
    {
        _svc         = ops;
        _config      = config;
        FolderName   = folderName;
        _shell       = shell;
        _ai          = ai;
        _projectPath = System.IO.Path.Combine(ops.RootPath, folderName);
        Load();
    }

    [RelayCommand]
    private void Refresh() => Load();

    private void Load()
    {
        // A reload replaces every backlog row, so a search's filter would be holding ids that are gone.
        ClearSearch();

        _loading = true;
        try
        {
            var info = _svc.GetProjectInfo(FolderName);
            ProjectName  = string.IsNullOrWhiteSpace(info.Name) ? FolderName : info.Name;
            Description  = info.Description;
            SolutionFile = info.SolutionFile;
            LastModified = Format(info.LastUpdate);

            NeedsUpgrade = info.SchemaVersion < ProjectInfo.CurrentSchemaVersion;
            IsReadOnly   = NeedsUpgrade;

            CompletionCriteria.Clear();
            foreach (var c in info.CompletionCriteria)
                CompletionCriteria.Add(new CompletionCriterionViewModel(c, OnCriteriaChanged));

            Backlog.Clear();
            foreach (var item in info.Backlog)
                Backlog.Add(new BacklogItemViewModel(item, this));

            // More room where it's needed: no description → open on details; otherwise open on the backlog.
            SelectedTabIndex = string.IsNullOrWhiteSpace(Description) ? 0 : 1;
        }
        finally { _loading = false; }

        if (NeedsUpgrade) _ = PromptUpgradeAsync();
    }

    private static string Format(DateTime? t)
        => t.HasValue ? t.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";

    private void RefreshLastModified()
        => LastModified = Format(_svc.GetProjectInfo(FolderName).LastUpdate);

    // ── Legacy upgrade ──

    private async Task PromptUpgradeAsync()
    {
        if (_shell is null) return;   // no shell (e.g. tests) → leave read-only, banner offers Upgrade
        var ok = await _shell.ConfirmAsync(
            "Upgrade project format?",
            "This project uses the old format. Upgrading rolls the previous scope / notes / plan fields into " +
            "the new markdown fields and enables editing. Your data is preserved.",
            "Upgrade", "Keep as-is");
        if (ok) DoUpgrade();
    }

    [RelayCommand]
    private void Upgrade() => DoUpgrade();

    private void DoUpgrade()
    {
        _svc.UpgradeSchema(FolderName);
        Load();   // reloads as v2 → NeedsUpgrade/IsReadOnly clear
    }

    // ── Header saves ──

    partial void OnProjectNameChanged(string value) => SaveHeader();
    partial void OnDescriptionChanged(string value) => SaveHeader();

    private void SaveHeader()
    {
        if (_loading || IsReadOnly) return;
        _svc.ModifyProjectHeader(FolderName, ProjectName, Description);
        RefreshLastModified();
    }

    // ── Completion criteria ──

    private void OnCriteriaChanged()
    {
        if (_loading || IsReadOnly) return;
        SaveCriteria();
    }

    private void SaveCriteria()
    {
        _svc.SetCompletionCriteria(FolderName, CompletionCriteria.Select(c => c.ToModel()).ToList());
        RefreshLastModified();
    }

    [RelayCommand]
    private void AddCriterion()
    {
        if (IsReadOnly) return;
        CompletionCriteria.Add(new CompletionCriterionViewModel(OnCriteriaChanged));
        SaveCriteria();
    }

    [RelayCommand]
    private void RemoveCriterion(CompletionCriterionViewModel? c)
    {
        if (c is null || IsReadOnly) return;
        CompletionCriteria.Remove(c);
        SaveCriteria();
    }

    // ── Backlog ──

    [RelayCommand]
    private void SaveItem()
    {
        if (SelectedItem is not null) SaveBacklogItem(SelectedItem);
    }

    internal void SaveBacklogItem(BacklogItemViewModel vm)
    {
        if (_loading || IsReadOnly) return;
        _svc.ModifyToDo(FolderName, vm.Id, vm.Title, vm.Description);
        RefreshLastModified();
    }

    internal bool CanProgress(string statusKey) => _svc.CanProgress(statusKey);

    internal void ProgressItem(BacklogItemViewModel vm)
    {
        if (IsReadOnly) return;
        _svc.ProgressToDo(FolderName, vm.Id);
        SyncItemStatus(vm);
    }

    internal void SetItemStatus(BacklogItemViewModel vm, string statusKey)
    {
        if (_loading || IsReadOnly) return;
        _svc.SetToDoStatus(FolderName, vm.Id, statusKey);
        RefreshLastModified();
    }

    private void SyncItemStatus(BacklogItemViewModel vm)
    {
        var updated = _svc.GetProjectInfo(FolderName).Backlog.FirstOrDefault(i => i.Id == vm.Id);
        if (updated is not null) vm.SetStatusKeyQuiet(updated.StatusKey);
        RefreshLastModified();
    }

    [RelayCommand]
    private void AddTodo()
    {
        if (IsReadOnly) return;
        var title = NewTodoTitle.Trim();
        if (string.IsNullOrEmpty(title)) return;
        var id = _svc.AddToDo(FolderName, title);
        var created = _svc.GetProjectInfo(FolderName).Backlog.FirstOrDefault(i => i.Id == id)
                      ?? new BacklogItem { Id = id, Title = title };
        var vm = new BacklogItemViewModel(created, this);
        Backlog.Add(vm);
        SelectedItem = vm;
        NewTodoTitle = string.Empty;
        RefreshLastModified();
    }

    [RelayCommand]
    private void DeleteTodo(BacklogItemViewModel? item)
    {
        if (item is null || IsReadOnly) return;
        ShowConfirmation(
            "Delete backlog item",
            $"Delete \"{item.Title}\"? This cannot be undone.",
            () =>
            {
                _svc.RemoveToDo(FolderName, item.Id);
                Backlog.Remove(item);
                if (SelectedItem == item) SelectedItem = null;
                RefreshLastModified();
            });
    }

    [RelayCommand]
    private void OpenFiles()
        => OpenFilesRequested?.Invoke(_projectPath);

    // ── AI buttons ──

    [RelayCommand]
    private Task TaskToAi(BacklogItemViewModel? item) => StartAiConversation(item, plan: false);

    [RelayCommand]
    private Task PlanWithAi(BacklogItemViewModel? item) => StartAiConversation(item, plan: true);

    private async Task StartAiConversation(BacklogItemViewModel? item, bool plan)
    {
        item ??= SelectedItem;
        if (item is null || _shell is null || _ai is null) return;

        var status = Config.Resolve(item.StatusKey).Label;
        var lead = plan
            ? "Let's plan how to approach the following backlog task. Ask any clarifying questions, then propose a plan."
            : "Please carry out the following backlog task.";
        var prompt =
            $"{lead}\n\n" +
            $"Project: {ProjectName}\n" +
            $"Backlog item: {item.Title}\n" +
            $"Status: {status}\n" +
            (string.IsNullOrWhiteSpace(item.Description) ? string.Empty : $"\nDetails:\n{item.Description}\n") +
            "\nThe project folder is attached as context — read its .project file and the files as needed.";

        var record = new ConversationRecord
        {
            Title = (plan ? "Plan: " : "Task: ") + item.Title,
            Context =
            [
                new ContextRef
                {
                    PageKind   = "FileSystem",
                    PageParams = new() { ["mode"] = "path", ["path"] = _projectPath, ["label"] = ProjectName },
                },
            ],
        };

        try { await _ai.SaveConversationAsync(record); } catch { return; }

        _shell.OpenTab("Conversation", new()
        {
            ["conversationId"] = record.Id,
            ["initialPrompt"]  = prompt,
        });
    }

    // ── Confirmation overlay ──

    [RelayCommand]
    private void ConfirmAction()
    {
        ConfirmationVisible = false;
        _pendingConfirmAction?.Invoke();
        _pendingConfirmAction = null;
    }

    [RelayCommand]
    private void CancelConfirmation()
    {
        ConfirmationVisible = false;
        _pendingConfirmAction = null;
    }

    private void ShowConfirmation(string title, string prompt, Action onConfirm)
    {
        ConfirmationTitle     = title;
        ConfirmationPrompt    = prompt;
        _pendingConfirmAction = onConfirm;
        ConfirmationVisible   = true;
    }

    // ── IPageViewModel ──

    public string GetContext()
    {
        if (string.IsNullOrEmpty(ProjectName)) return "Project detail: no project loaded.";

        var parts = new List<string>
        {
            $"Project '{ProjectName}'" + (NeedsUpgrade ? " (legacy format, read-only until upgraded)" : string.Empty) + ".",
        };

        if (!string.IsNullOrWhiteSpace(Description))
            parts.Add("Description: " + Truncate(Description, 280));

        if (CompletionCriteria.Count > 0)
            parts.Add($"Completion criteria ({CompletionCriteria.Count}): "
                + string.Join("; ", CompletionCriteria.Take(8).Select(c => $"[{c.Status}] {Truncate(c.Text, 60)}")) + ".");

        parts.Add(Backlog.Count == 0
            ? "No backlog items."
            : $"{Backlog.Count} backlog item(s): "
              + string.Join("; ", Backlog.Take(12).Select(b => $"{Truncate(b.Title, 50)} [{b.StatusLabel}]"))
              + (Backlog.Count > 12 ? "; …" : string.Empty) + ".");

        parts.Add("Use read_project for the full description, criteria and backlog detail, or "
                + "read_backlog_item to read a single item.");
        return string.Join(" ", parts);
    }

    /// <summary>Scope = this project's folder path, so two Project Detail tabs stay distinguishable when
    /// pinned together and the tools act only within this project.</summary>
    public string? GetSecurityContext() => _projectPath;

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "read_project",
            "Read this project in full: its description, completion criteria and every backlog item "
          + "(title, status, markdown detail).",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                try
                {
                    return Task.FromResult(ToolResult.Ok($"read '{ProjectName}'", _svc.GetProjectReadout(FolderName)));
                }
                catch (Exception ex)
                {
                    return Task.FromResult(ToolResult.Error($"Could not read the project: {ex.Message}"));
                }
            }),

        new DelegateClientTool(
            "read_backlog_item",
            "Read one backlog item's full detail (title, status and markdown description). Identify it by "
          + "title or id; defaults to the selected item.",
            [new ClientToolParameter("item",
                "Backlog item title or id. Optional — defaults to the selected item.",
                Required: false)],
            ToolSafety.SafeOperation,
            (args, _) =>
            {
                if (Backlog.Count == 0)
                    return Task.FromResult(ToolResult.Error("This project has no backlog items."));

                var key = ToolArgs.Str(args, "item", "title", "id");
                var vm  = key is null
                    ? SelectedItem ?? Backlog[0]
                    : Backlog.FirstOrDefault(b =>
                          string.Equals(b.Id.ToString(), key, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(b.Title,         key, StringComparison.OrdinalIgnoreCase));
                if (vm is null)
                    return Task.FromResult(ToolResult.Error($"No backlog item matching '{key}'."));

                var body = $"### [{vm.StatusLabel}] {vm.Title}\n**ID:** {vm.Id}\n\n"
                         + (string.IsNullOrWhiteSpace(vm.Description) ? "_No detail._" : vm.Description);
                return Task.FromResult(ToolResult.Ok($"read '{vm.Title}'", body));
            }),
    ];

    private static string Truncate(string s, int max)
    {
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..max].TrimEnd() + "…";
    }
}
