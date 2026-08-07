using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.SystemInfo.ClientTools;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.Services;

namespace Nexaflow.Features.SystemInfo.ViewModels;

/// <summary>
/// The Environment Variables page. Shows User, Machine and Process scopes; User edits happen in-process,
/// Machine edits go through the elevated bridge (UAC), Process is read-only. Long values like PATH get a
/// roomy multiline editor and a per-entry list. Surfaces a summary + read/act tools to the AI.
/// </summary>
public sealed partial class EnvironmentVariablesViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly IShellServices _shell;
    private readonly EnvVarsCollector _collector;
    private CancellationTokenSource _cts = new();
    private EnvVarsBundle? _bundle;
    private bool _syncingEntries;
    private string? _reselectName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoading;

    [ObservableProperty]
    private EnvScope _selectedScope = EnvScope.User;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSelection))]
    [NotifyPropertyChangedFor(nameof(IsEditorReadOnly))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private EnvVarRow? _selectedVariable;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingList))]
    private string _editValue = "";

    [ObservableProperty]
    private string _newEntry = "";

    public ObservableCollection<EnvVarRow> Variables { get; } = [];
    public ICollectionView VariablesView { get; }

    /// <summary>Working copy of a ';'-delimited value as individual entries (PATH editing).</summary>
    public ObservableCollection<string> EditEntries { get; } = [];

    public IReadOnlyList<EnvScope> Scopes { get; } = [EnvScope.User, EnvScope.Machine];

    /// <summary>A variable is selected (both User and Machine scopes are editable).</summary>
    public bool CanEditSelection => SelectedVariable is not null;

    /// <summary>The value editor is read-only only when nothing is selected.</summary>
    public bool IsEditorReadOnly => SelectedVariable is null;

    /// <summary>Show the per-entry editor when the value being edited is ';'-delimited.</summary>
    public bool IsEditingList => EditValue.Contains(';');

    private string _contextText = "Environment variables: still gathering…";

    public EnvironmentVariablesViewModel(IShellServices shell) : this(shell, new EnvVarsCollector()) { }

    public EnvironmentVariablesViewModel(IShellServices shell, EnvVarsCollector collector)
    {
        _shell = shell;
        _collector = collector;
        VariablesView = CollectionViewSource.GetDefaultView(Variables);
        VariablesView.Filter = MatchesFilter;
        Refresh();
    }

    partial void OnFilterTextChanged(string value)
    {
        // Typing over the box drops whatever "?" put behind it — the text on screen is always what is
        // being filtered by.
        if (!_suppressFilterReset) ClearSearchState();
        VariablesView.Refresh();
    }

    partial void OnSelectedScopeChanged(EnvScope value)
    {
        // The page shows one scope at a time, so a search of the old one describes rows that are no
        // longer on screen.
        ClearSearchCommand.Execute(null);
        PopulateScope();
    }

    partial void OnSelectedVariableChanged(EnvVarRow? value) => EditValue = value?.Value ?? "";

    partial void OnEditValueChanged(string value)
    {
        if (_syncingEntries) return;
        RebuildEntries();
    }

    /// <summary>
    /// Most specific first: an agent's pinned set, then a compiled "?" query, then the typed text.
    /// <para>
    /// All three judge the name <b>and the value</b>. A variable's value is the part people are looking
    /// for — which one has the stale SDK path in it — and a box that filtered by a narrower rule than "?"
    /// would have the same string give two different answers depending on where it was typed.
    /// </para>
    /// </summary>
    private bool MatchesFilter(object item)
    {
        if (item is not EnvVarRow r) return false;
        if (_pinnedNames is not null)   return _pinnedNames.Contains(r.Name);
        if (_filterRequest is not null) return _filterRequest.Matches(r.Name) || _filterRequest.Matches(r.Value);
        if (string.IsNullOrWhiteSpace(FilterText)) return true;
        var f = FilterText.Trim();
        return r.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
            || r.Value.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Refresh()
    {
        if (IsLoading) return;
        IsLoading = true;

        var task = new LoadEnvVarsTask(_collector);
        _shell.QueueBackgroundTask(task, onComplete: ok =>
        {
            if (ok && task.Result is { } bundle)
            {
                _bundle = bundle;
                PopulateScope();
                UpdateContext();
            }
            IsLoading = false;
        }, _cts.Token);
    }

    private void PopulateScope()
    {
        if (_bundle is null) return;
        var rows = SelectedScope == EnvScope.User ? _bundle.User : _bundle.Machine;

        Variables.Clear();
        foreach (var r in rows) Variables.Add(r);

        SelectedVariable = _reselectName is { } name
            ? Variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            : Variables.FirstOrDefault();
        _reselectName = null;
    }

    // ── PATH-entry editing (operates on EditValue) ──────────────────────────────────────────────
    private void RebuildEntries()
    {
        _syncingEntries = true;
        EditEntries.Clear();
        if (!string.IsNullOrEmpty(EditValue))
            foreach (var e in EditValue.Split(';')) EditEntries.Add(e);
        _syncingEntries = false;
    }

    private void RecomposeValue()
    {
        _syncingEntries = true;
        EditValue = string.Join(';', EditEntries);
        _syncingEntries = false;
    }

    [RelayCommand] private void AddEntry()
    {
        if (string.IsNullOrWhiteSpace(NewEntry)) return;
        EditEntries.Add(NewEntry.Trim());
        NewEntry = "";
        RecomposeValue();
    }

    [RelayCommand] private void RemoveEntry(string entry)
    {
        if (EditEntries.Remove(entry)) RecomposeValue();
    }

    [RelayCommand] private void MoveEntryUp(string entry)
    {
        var i = EditEntries.IndexOf(entry);
        if (i > 0) { EditEntries.Move(i, i - 1); RecomposeValue(); }
    }

    [RelayCommand] private void MoveEntryDown(string entry)
    {
        var i = EditEntries.IndexOf(entry);
        if (i >= 0 && i < EditEntries.Count - 1) { EditEntries.Move(i, i + 1); RecomposeValue(); }
    }

    // ── Save / Delete / Add ─────────────────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private async Task Save()
    {
        if (SelectedVariable is not { } row) return;
        await SetVariableAsync(row.Scope, row.Name, EditValue);
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private async Task Delete()
    {
        if (SelectedVariable is not { } row) return;
        var confirmed = await _shell.ConfirmAsync("Delete variable",
            $"Delete {Scope(row.Scope)} variable '{row.Name}'?");
        if (confirmed) await DeleteVariableAsync(row.Scope, row.Name);
    }

    [RelayCommand]
    private void AddNew()
    {
        _shell.ShowPrompt("New variable", "Name", "",
            onConfirm: name =>
            {
                if (!string.IsNullOrWhiteSpace(name))
                    _ = SetVariableAsync(SelectedScope, name.Trim(), "");
            },
            onCancel: () => { });
    }

    /// <summary>Sets a variable: User in-process (no UAC), Machine via the bridge (UAC). Shared by UI + tools.</summary>
    internal async Task<bool> SetVariableAsync(EnvScope scope, string name, string value)
    {
        bool ok;
        if (scope == EnvScope.Machine)
        {
            var res = await _shell.RunElevatedAsync(ElevatedRequest.Single(ElevatedOps.EnvSet,
                (ElevatedArgs.EnvName, name), (ElevatedArgs.EnvValue, value), (ElevatedArgs.EnvTarget, "Machine")));
            if (!res.Success && !res.WasDeclined) _shell.ShowError(res.Message);
            ok = res.Success;
        }
        else
        {
            ok = TrySetUser(name, value);
        }

        if (ok) { _reselectName = name; Refresh(); }
        return ok;
    }

    /// <summary>Deletes a variable: User in-process, Machine via the bridge. Shared by UI + tools.</summary>
    internal async Task<bool> DeleteVariableAsync(EnvScope scope, string name)
    {
        bool ok;
        if (scope == EnvScope.Machine)
        {
            var res = await _shell.RunElevatedAsync(ElevatedRequest.Single(ElevatedOps.EnvDelete,
                (ElevatedArgs.EnvName, name), (ElevatedArgs.EnvTarget, "Machine")));
            if (!res.Success && !res.WasDeclined) _shell.ShowError(res.Message);
            ok = res.Success;
        }
        else
        {
            ok = TrySetUser(name, null);
        }

        if (ok) Refresh();
        return ok;
    }

    // Environment.SetEnvironmentVariable for User/Machine writes the registry AND broadcasts
    // WM_SETTINGCHANGE itself, so no manual notify is needed on the in-process (User) path.
    private bool TrySetUser(string name, string? value)
    {
        try { Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User); return true; }
        catch (Exception ex) { _shell.ShowError($"Could not update '{name}': {ex.Message}"); return false; }
    }

    internal EnvVarsBundle? Bundle => _bundle;

    internal IReadOnlyList<EnvVarRow> ScopeRows(EnvScope scope) =>
        _bundle is null ? [] : scope == EnvScope.User ? _bundle.User : _bundle.Machine;

    private void UpdateContext()
    {
        if (_bundle is null) return;
        bool HasPath(IReadOnlyList<EnvVarRow> rows) => rows.Any(r => r.Name.Equals("Path", StringComparison.OrdinalIgnoreCase));
        _contextText =
            $"Environment variables on {Environment.MachineName}: {_bundle.User.Count} user, " +
            $"{_bundle.Machine.Count} machine (system). " +
            $"PATH is set ({(HasPath(_bundle.User) ? "user" : "no-user")}/{(HasPath(_bundle.Machine) ? "machine" : "no-machine")}). " +
            $"Use the environment tools to read a full value (e.g. PATH) or to set/delete a variable " +
            $"(user-scope is immediate; machine-scope requires admin approval via a UAC prompt).";
    }

    private static string Scope(EnvScope s) => s == EnvScope.User ? "user" : "machine";

    // ── IPageViewModel ──────────────────────────────────────────────────────────────────────────
    public string GetContext() =>
        IsLoading && _bundle is null ? "Environment variables: still gathering…" : _contextText;

    public bool IsContextReady => !IsLoading;

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new EnvVarTools.GetEnvironmentVariables(this),
        new EnvVarTools.GetEnvironmentVariable(this),
        new EnvVarTools.SetEnvironmentVariable(this),
        new EnvVarTools.DeleteEnvironmentVariable(this),
    ];

    public string? GetSecurityContext() => "system-env-vars";

    public ContextSecurityRisk GetContextSecurityRisk() => ContextSecurityRisk.High;

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
