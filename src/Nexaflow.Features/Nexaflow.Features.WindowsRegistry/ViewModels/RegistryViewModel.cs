using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.WindowsRegistry.ClientTools;
using Nexaflow.Features.WindowsRegistry.Services;

namespace Nexaflow.Features.WindowsRegistry.ViewModels;

/// <summary>
/// Drives the registry viewer: the hive tree, the value list, navigation (breadcrumb/title via
/// <see cref="NavigationChanged"/>), editing (in-process with an elevation fallback) and export/import.
/// </summary>
public sealed partial class RegistryViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly IShellServices _shell;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>The workspace shell, for the registry client tools to marshal UI work via RunOnUiAsync.</summary>
    internal IShellServices Shell => _shell;

    // Guards programmatic UI sync (tree selection / hive dropdown) from re-triggering navigation.
    private bool _suppressSync;

    // The tree's root ("base"): the hive root when browsing, or a specific key when the tab was
    // pinned/opened rooted at that key. Navigation stays at or below the base.
    private RegistryRoot _baseRoot = RegistryRoot.CurrentUser;
    private string       _basePath = "";

    public ObservableCollection<RegistryTreeNode> TreeRoots { get; } = [];
    public ObservableCollection<RegistryValue>    Values    { get; } = [];

    public RegistryRoot CurrentRoot    { get; private set; } = RegistryRoot.CurrentUser;
    public string       CurrentSubPath { get; private set; } = "";

    [ObservableProperty] private string         _currentKeyPath = RegistryRoot.CurrentUser.Token;
    [ObservableProperty] private RegistryValue? _selectedValue;
    [ObservableProperty] private RegistryRoot   _selectedHive = RegistryRoot.CurrentUser;

    /// <summary>Hive options for the dropdown.</summary>
    public IReadOnlyList<RegistryRoot> Hives => RegistryRoot.All;

    /// <summary>Raised on navigation so the page can update its breadcrumb and "Reg: …" title.</summary>
    public event Action<IReadOnlyList<(string Label, string Path)>>? NavigationChanged;

    public RegistryViewModel(IShellServices shell)
    {
        _shell = shell;
        NavigateTo(RegistryRoot.CurrentUser.Token);
    }

    /// <summary>Navigates to an absolute path (<c>HKCU\Software\Foo</c>) and syncs the tree + dropdown.</summary>
    public void NavigateTo(string fullPath)
    {
        if (!TrySplit(fullPath, out var root, out var subPath)) return;
        SetCurrent(root, subPath);
        ExpandToCurrent();
    }

    /// <summary>Roots the tree at <paramref name="fullPath"/> so that key is the tree's top node and
    /// navigation can't go above it. Used when a tab is opened/pinned rooted at a key.</summary>
    public void RootAt(string fullPath)
    {
        if (!TrySplit(fullPath, out var root, out var subPath)) return;
        BuildTreeBase(root, subPath);
        SetCurrent(root, subPath);
        ExpandToCurrent();
    }

    /// <summary>Re-roots the tree at the current key (used when pinning the tab to the ribbon).</summary>
    public void ResetRootToCurrentKey()
    {
        if (CurrentSubPath.Length == 0) return;   // hive root — nothing to root
        RootAt(CurrentKeyPath);
    }

    /// <summary>Tree-selection handler — loads the node without re-driving the tree.</summary>
    public void OnTreeNodeSelected(RegistryTreeNode node)
    {
        if (_suppressSync) return;
        SetCurrent(node.Root, node.SubPath);
    }

    partial void OnSelectedHiveChanged(RegistryRoot value)
    {
        if (_suppressSync) return;
        NavigateTo(value.Token);
    }

    // ── Core navigation ────────────────────────────────────────────────────────
    private void SetCurrent(RegistryRoot root, string subPath)
    {
        // Rebuild the tree base only when the hive changes or navigation escapes the current base
        // (rooting is set explicitly via RootAt and preserved across in-base navigation).
        var underBase = _basePath.Length == 0
            || string.Equals(subPath, _basePath, StringComparison.OrdinalIgnoreCase)
            || subPath.StartsWith(_basePath + "\\", StringComparison.OrdinalIgnoreCase);
        if (TreeRoots.Count == 0 || root != _baseRoot || !underBase)
            BuildTreeBase(root, "");

        CurrentRoot    = root;
        CurrentSubPath = subPath;
        CurrentKeyPath = subPath.Length == 0 ? root.Token : $"{root.Token}\\{subPath}";

        _suppressSync = true;
        SelectedHive = root;
        _suppressSync = false;

        LoadValues();
        NavigationChanged?.Invoke(BuildSegments());
    }

    /// <summary>Rebuilds the single-node tree base at the given hive + key path.</summary>
    private void BuildTreeBase(RegistryRoot root, string basePath)
    {
        _baseRoot = root;
        _basePath = basePath;
        TreeRoots.Clear();
        var label = basePath.Length == 0 ? root.Token : $"{root.Token}\\{basePath}";
        TreeRoots.Add(new RegistryTreeNode(root, basePath, label));
    }

    /// <summary>The portion of <paramref name="subPath"/> below the tree base ("" when it is the base).</summary>
    private string RelativeToBase(string subPath)
    {
        if (_basePath.Length == 0) return subPath;
        if (string.Equals(subPath, _basePath, StringComparison.OrdinalIgnoreCase)) return "";
        return subPath.StartsWith(_basePath + "\\", StringComparison.OrdinalIgnoreCase)
            ? subPath[(_basePath.Length + 1)..]
            : subPath;
    }

    private void LoadValues()
    {
        Values.Clear();
        foreach (var v in ReadValues(CurrentSubPath))
            Values.Add(v);
        SelectedValue = null;
    }

    /// <summary>Breadcrumb from the tree base down to the current key (the base is the first crumb).</summary>
    private IReadOnlyList<(string Label, string Path)> BuildSegments()
    {
        var baseFull  = _basePath.Length == 0 ? _baseRoot.Token : $"{_baseRoot.Token}\\{_basePath}";
        var baseLabel = _basePath.Length == 0 ? _baseRoot.Token : LeafOf(_basePath);
        var segs = new List<(string, string)> { (baseLabel, baseFull) };

        var rel = RelativeToBase(CurrentSubPath);
        if (rel.Length > 0)
        {
            var cumulative = baseFull;
            foreach (var part in rel.Split('\\'))
            {
                cumulative += "\\" + part;
                segs.Add((part, cumulative));
            }
        }
        return segs;
    }

    private void ExpandToCurrent()
    {
        _suppressSync = true;
        try
        {
            var node = TreeRoots.FirstOrDefault();
            if (node is null) return;

            // Walk from the tree base down to the current key, expanding each ancestor. The base itself
            // is not force-expanded for a root-only target (a giant hive root would enumerate tens of
            // thousands of keys).
            var rel = RelativeToBase(CurrentSubPath);
            if (rel.Length > 0)
            {
                node.IsExpanded = true;
                foreach (var part in rel.Split('\\'))
                {
                    var child = node.Children.FirstOrDefault(
                        c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                    if (child is null) break;
                    node = child;
                    node.IsExpanded = true;
                }
            }
            node.IsSelected = true;
        }
        finally { _suppressSync = false; }
    }

    // ── Reading (also used by the AI tools) ──────────────────────────────────────
    public IReadOnlyList<string> ReadSubKeys(string subPath)
    {
        try
        {
            var key = subPath.Length == 0 ? CurrentRoot.Key : CurrentRoot.Key.OpenSubKey(subPath, false);
            if (key is null) return [];
            try { return key.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(); }
            finally { if (subPath.Length > 0) key.Dispose(); }
        }
        catch { return []; }
    }

    public IReadOnlyList<RegistryValue> ReadValues(string subPath)
    {
        var list = new List<RegistryValue>();
        try
        {
            var key = subPath.Length == 0 ? CurrentRoot.Key : CurrentRoot.Key.OpenSubKey(subPath, false);
            if (key is null) return list;
            try
            {
                var hasDefault = false;
                foreach (var name in key.GetValueNames())
                {
                    if (name.Length == 0) hasDefault = true;
                    var kind = key.GetValueKind(name);
                    var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    list.Add(new RegistryValue(name, kind, data));
                }
                if (!hasDefault)
                    list.Insert(0, new RegistryValue("", RegistryValueKind.String, "(value not set)"));
            }
            finally { if (subPath.Length > 0) key.Dispose(); }
        }
        catch { /* access denied — show nothing */ }

        return list.OrderByDescending(v => v.IsDefault)
                   .ThenBy(v => v.RawName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Cheap subkey count (avoids enumerating names on huge hives like HKCR).</summary>
    private int SubKeyCount(string subPath)
    {
        try
        {
            var key = subPath.Length == 0 ? CurrentRoot.Key : CurrentRoot.Key.OpenSubKey(subPath, false);
            if (key is null) return 0;
            try { return key.SubKeyCount; }
            finally { if (subPath.Length > 0) key.Dispose(); }
        }
        catch { return 0; }
    }

    public RegistryValueKind? GetValueKind(string name)
    {
        try
        {
            var key = CurrentSubPath.Length == 0 ? CurrentRoot.Key
                                                 : CurrentRoot.Key.OpenSubKey(CurrentSubPath, false);
            if (key is null) return null;
            try { return key.GetValueNames().Contains(name) ? key.GetValueKind(name) : null; }
            finally { if (CurrentSubPath.Length > 0) key.Dispose(); }
        }
        catch { return null; }
    }

    public void Refresh()
    {
        LoadValues();
        ReloadTreeAt(CurrentRoot, CurrentSubPath);
    }

    // ── Writing (in-process, elevation fallback) ─────────────────────────────────

    /// <summary>Creates or changes a value under the current key (used by UI edit and the AI tools).</summary>
    public Task<bool> WriteValueAsync(string name, RegistryValueKind kind, string wire)
    {
        var args = new Dictionary<string, string>
        {
            [ElevatedArgs.RegHive]  = CurrentRoot.Token,
            [ElevatedArgs.RegPath]  = CurrentSubPath,
            [ElevatedArgs.RegName]  = name,
            [ElevatedArgs.RegType]  = kind.ToString(),
            [ElevatedArgs.RegValue] = wire,
        };
        return ApplyWriteAsync(ElevatedOps.RegSetValue, args,
            () => RegistryWriter.SetValue(CurrentRoot, CurrentSubPath, name, kind.ToString(), wire));
    }

    private async Task<bool> ApplyWriteAsync(string opId, Dictionary<string, string> args, Action inProcess)
    {
        try { inProcess(); return true; }
        catch (UnauthorizedAccessException) { /* fall through to elevation */ }
        catch (SecurityException)            { /* fall through to elevation */ }
        catch (Exception ex)                 { _shell.ShowError(ex.Message); return false; }

        var res = await _shell.RunElevatedAsync(ElevatedRequest.Single(opId, args), _cts.Token);
        if (res.Success) return true;
        if (!res.WasDeclined) _shell.ShowError(res.Message);
        return false;
    }

    // ── Value commands ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void EditValue(RegistryValue? value)
    {
        value ??= SelectedValue;
        if (value is null) return;

        var seed = ReadEditSeed(value.RawName, value.Kind);
        ShowInputPrompt($"Edit {value.TypeLabel}", $"Value data for '{value.Name}':", seed,
            async text =>
            {
                string wire;
                try { wire = RegistryValueCodec.ParseUserInput(text, value.Kind); }
                catch (Exception ex) { _shell.ShowError($"Invalid {value.TypeLabel} data: {ex.Message}"); return; }
                if (await WriteValueAsync(value.RawName, value.Kind, wire)) Refresh();
            },
            () => { });
    }

    [RelayCommand]
    private void NewValue(string kindName)
    {
        if (!Enum.TryParse<RegistryValueKind>(kindName, out var kind)) return;

        ShowInputPrompt($"New {RegistryValueCodec.TypeLabel(kind)}", "Value name:", "",
            async name =>
            {
                name = name.Trim();
                if (name.Length == 0) { _shell.ShowError("Value name cannot be empty."); return; }
                var wire = RegistryValueCodec.ParseUserInput("", kind);
                if (await WriteValueAsync(name, kind, wire)) Refresh();
            },
            () => { });
    }

    [RelayCommand]
    private void DeleteValue(RegistryValue? value)
    {
        value ??= SelectedValue;
        if (value is null || value.IsDefault) return;   // the default value can't be deleted, only cleared

        ShowConfirmation("Delete value", $"Delete value '{value.Name}'? This cannot be undone.",
            async () =>
            {
                var args = new Dictionary<string, string>
                {
                    [ElevatedArgs.RegHive] = CurrentRoot.Token,
                    [ElevatedArgs.RegPath] = CurrentSubPath,
                    [ElevatedArgs.RegName] = value.RawName,
                };
                if (await ApplyWriteAsync(ElevatedOps.RegDeleteValue, args,
                        () => RegistryWriter.DeleteValue(CurrentRoot, CurrentSubPath, value.RawName)))
                    Refresh();
            },
            () => { });
    }

    private string ReadEditSeed(string name, RegistryValueKind kind)
    {
        try
        {
            var key = CurrentSubPath.Length == 0 ? CurrentRoot.Key
                                                 : CurrentRoot.Key.OpenSubKey(CurrentSubPath, false);
            if (key is null) return "";
            try
            {
                var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return RegistryValueCodec.ToEditText(data, kind);
            }
            finally { if (CurrentSubPath.Length > 0) key.Dispose(); }
        }
        catch { return ""; }
    }

    // ── Key commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void NewKey()
    {
        ShowInputPrompt("New Key", "Key name:", "New Key",
            async name =>
            {
                name = name.Trim();
                if (name.Length == 0 || name.Contains('\\')) { _shell.ShowError("Invalid key name."); return; }
                var childSub = CurrentSubPath.Length == 0 ? name : $"{CurrentSubPath}\\{name}";
                var args = new Dictionary<string, string>
                {
                    [ElevatedArgs.RegHive] = CurrentRoot.Token,
                    [ElevatedArgs.RegPath] = childSub,
                };
                if (await ApplyWriteAsync(ElevatedOps.RegCreateKey, args,
                        () => RegistryWriter.CreateKey(CurrentRoot, childSub)))
                {
                    ReloadTreeAt(CurrentRoot, CurrentSubPath);
                    NavigateTo($"{CurrentRoot.Token}\\{childSub}");
                }
            },
            () => { });
    }

    [RelayCommand]
    private void RenameKey()
    {
        if (CurrentSubPath.Length == 0) return;   // can't rename a hive root
        var leaf = LeafOf(CurrentSubPath);
        var parent = ParentOf(CurrentSubPath);

        ShowInputPrompt("Rename Key", "New name:", leaf,
            async name =>
            {
                name = name.Trim();
                if (name.Length == 0 || name.Contains('\\')) { _shell.ShowError("Invalid key name."); return; }
                if (string.Equals(name, leaf, StringComparison.Ordinal)) return;
                var args = new Dictionary<string, string>
                {
                    [ElevatedArgs.RegHive]    = CurrentRoot.Token,
                    [ElevatedArgs.RegPath]    = CurrentSubPath,
                    [ElevatedArgs.RegNewName] = name,
                };
                if (await ApplyWriteAsync(ElevatedOps.RegRenameKey, args,
                        () => RegistryWriter.RenameKey(CurrentRoot, CurrentSubPath, name)))
                {
                    ReloadTreeAt(CurrentRoot, parent);
                    var dest = parent.Length == 0 ? name : $"{parent}\\{name}";
                    NavigateTo($"{CurrentRoot.Token}\\{dest}");
                }
            },
            () => { });
    }

    [RelayCommand]
    private void DeleteKey()
    {
        if (CurrentSubPath.Length == 0) return;   // can't delete a hive root
        var path = CurrentKeyPath;
        var parent = ParentOf(CurrentSubPath);

        ShowConfirmation("Delete key",
            $"Delete '{path}' and all its subkeys and values? This cannot be undone.",
            async () =>
            {
                var args = new Dictionary<string, string>
                {
                    [ElevatedArgs.RegHive] = CurrentRoot.Token,
                    [ElevatedArgs.RegPath] = CurrentSubPath,
                };
                if (await ApplyWriteAsync(ElevatedOps.RegDeleteKey, args,
                        () => RegistryWriter.DeleteKey(CurrentRoot, CurrentSubPath)))
                {
                    ReloadTreeAt(CurrentRoot, parent);
                    NavigateTo(parent.Length == 0 ? CurrentRoot.Token : $"{CurrentRoot.Token}\\{parent}");
                }
            },
            () => { });
    }

    // ── Export / Import ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Export()
    {
        var defaultName = (LeafOf(CurrentSubPath) is { Length: > 0 } leaf ? leaf : CurrentRoot.Token) + ".reg";
        var file = await _shell.PickSaveFileAsync(defaultName, [".reg"]);
        if (file is null) return;

        var exitCode = await RunRegAsync($"export \"{CurrentKeyPath}\" \"{file}\" /y");
        if (exitCode != 0) _shell.ShowError($"Export failed (reg.exe exit {exitCode}).");
        else _shell.ShowNotification($"Exported to {file}");
    }

    [RelayCommand]
    private async Task Import()
    {
        var file = await _shell.PickOpenFileAsync([".reg"]);
        if (file is null) return;

        // Try in-process; if reg.exe fails (commonly an access-denied write into a protected hive),
        // retry the import elevated via the bridge.
        var exitCode = await RunRegAsync($"import \"{file}\"");
        if (exitCode == 0) { Refresh(); _shell.ShowNotification("Import complete."); return; }

        var res = await _shell.RunElevatedAsync(
            ElevatedRequest.Single(ElevatedOps.RegImport, (ElevatedArgs.RegFile, file)), _cts.Token);
        if (res.Success) { Refresh(); _shell.ShowNotification("Import complete."); }
        else if (!res.WasDeclined) _shell.ShowError(res.Message);
    }

    private Task<int> RunRegAsync(string arguments) => Task.Run(() =>
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("reg.exe", arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardError = true, RedirectStandardOutput = true,
                },
            };
            proc.Start();
            proc.StandardError.ReadToEnd();
            proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch { return -1; }
    }, _cts.Token);

    // ── Tree maintenance ─────────────────────────────────────────────────────────
    private void ReloadTreeAt(RegistryRoot root, string subPath)
    {
        if (root != _baseRoot) return;
        var node = TreeRoots.FirstOrDefault();
        if (node is null) return;

        var rel = RelativeToBase(subPath);
        if (rel.Length > 0)
        {
            foreach (var part in rel.Split('\\'))
            {
                node.IsExpanded = true;
                node = node.Children.FirstOrDefault(
                    c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase))!;
                if (node is null) return;
            }
        }
        node.ResetChildren();
        node.IsExpanded = true;
    }

    private static string LeafOf(string subPath)
    {
        if (subPath.Length == 0) return "";
        var slash = subPath.LastIndexOf('\\');
        return slash >= 0 ? subPath[(slash + 1)..] : subPath;
    }

    private static string ParentOf(string subPath)
    {
        var slash = subPath.LastIndexOf('\\');
        return slash >= 0 ? subPath[..slash] : "";
    }

    // ── Down-only path resolution for the AI tools ───────────────────────────────
    /// <summary>Resolves a tool-supplied path relative to the current key, refusing any move at or
    /// above the current node ("navigate down, not up").</summary>
    public bool TryResolveDown(string? relative, out string subPath, out string error)
    {
        subPath = CurrentSubPath; error = "";
        if (string.IsNullOrWhiteSpace(relative)) return true;

        var rel = relative.Replace('/', '\\').Trim('\\');
        if (rel.Contains("..") ||
            rel.Contains(':') ||
            RegistryRoot.All.Any(r => rel.StartsWith(r.Token, StringComparison.OrdinalIgnoreCase)
                                   || rel.StartsWith(r.DisplayName, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Only downward, relative paths are allowed (no '..', drive letters, or hive names).";
            return false;
        }
        subPath = CurrentSubPath.Length == 0 ? rel : $"{CurrentSubPath}\\{rel}";
        return true;
    }

    // ── IPageViewModel ───────────────────────────────────────────────────────────
    public string GetContext()
    {
        var subKeys = SubKeyCount(CurrentSubPath);
        var sb = new StringBuilder(
            $"Registry — at '{CurrentKeyPath}' ({subKeys} subkey{(subKeys == 1 ? "" : "s")}, " +
            $"{Values.Count} value{(Values.Count == 1 ? "" : "s")}).");
        if (SelectedValue is not null)
            sb.Append($" Selected value: {SelectedValue.Name} ({SelectedValue.TypeLabel}).");
        return sb.ToString();
    }

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new RegistryListSubkeysTool(this),
        new RegistryGetValuesTool(this),
        new RegistrySetValueTool(this),
        new RegistryInsertValueTool(this),
    ];

    public string? GetSecurityContext() => CurrentKeyPath;

    public ContextSecurityRisk GetContextSecurityRisk() =>
        CurrentRoot == RegistryRoot.CurrentUser ? ContextSecurityRisk.Medium : ContextSecurityRisk.High;

    // ── Overlays (mirrors the file-system tab's in-tab prompt/confirm) ───────────
    [ObservableProperty] private bool   _confirmationVisible;
    [ObservableProperty] private string _confirmationTitle  = "Are you sure?";
    [ObservableProperty] private string _confirmationPrompt = string.Empty;
    private Action? _pendingConfirm;
    private Action? _pendingCancel;

    public void ShowConfirmation(string title, string prompt, Action onConfirm, Action onCancel)
    {
        _pendingConfirm = onConfirm; _pendingCancel = onCancel;
        ConfirmationTitle = title; ConfirmationPrompt = prompt; ConfirmationVisible = true;
    }

    [RelayCommand]
    private void ConfirmAction()
    {
        ConfirmationVisible = false;
        var a = _pendingConfirm; _pendingConfirm = null; _pendingCancel = null; a?.Invoke();
    }

    [RelayCommand]
    private void CancelConfirmation()
    {
        ConfirmationVisible = false;
        var c = _pendingCancel; _pendingConfirm = null; _pendingCancel = null; c?.Invoke();
    }

    [ObservableProperty] private bool   _inputPromptVisible;
    [ObservableProperty] private string _inputPromptTitle = string.Empty;
    [ObservableProperty] private string _inputPromptLabel = string.Empty;
    [ObservableProperty] private string _inputPromptValue = string.Empty;
    private Action<string>? _pendingInputConfirm;
    private Action?         _pendingInputCancel;

    public void ShowInputPrompt(string title, string label, string initialValue,
                                Action<string> onConfirm, Action onCancel)
    {
        _pendingInputConfirm = onConfirm; _pendingInputCancel = onCancel;
        InputPromptTitle = title; InputPromptLabel = label; InputPromptValue = initialValue;
        InputPromptVisible = true;
    }

    [RelayCommand]
    private void ConfirmInputPrompt()
    {
        InputPromptVisible = false;
        var confirm = _pendingInputConfirm; var value = InputPromptValue;
        _pendingInputConfirm = null; _pendingInputCancel = null; confirm?.Invoke(value);
    }

    [RelayCommand]
    private void CancelInputPrompt()
    {
        InputPromptVisible = false;
        var cancel = _pendingInputCancel; _pendingInputConfirm = null; _pendingInputCancel = null; cancel?.Invoke();
    }

    // ── Path parsing ─────────────────────────────────────────────────────────────
    private static bool TrySplit(string fullPath, out RegistryRoot root, out string subPath)
    {
        root = RegistryRoot.CurrentUser; subPath = "";
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        var slash = fullPath.IndexOf('\\');
        var token = slash >= 0 ? fullPath[..slash] : fullPath;
        var resolved = RegistryRoot.FromToken(token)
                       ?? RegistryRoot.All.FirstOrDefault(r =>
                              string.Equals(r.DisplayName, token, StringComparison.OrdinalIgnoreCase));
        if (resolved is null) return false;

        root = resolved;
        subPath = slash >= 0 ? fullPath[(slash + 1)..].Trim('\\') : "";
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
