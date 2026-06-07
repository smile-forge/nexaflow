using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using Nexaflow.Features.WindowsRegistry.Services;

namespace Nexaflow.Features.WindowsRegistry.ViewModels;

/// <summary>
/// A key node in the left tree. Children are discovered lazily on first expand (the regedit behaviour),
/// using the same dummy-child trick as the file-system tree so the expand arrow shows before load.
/// </summary>
public sealed class RegistryTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public RegistryRoot Root    { get; }
    /// <summary>Path under the hive (e.g. <c>Software\Foo</c>); empty for the hive root node.</summary>
    public string       SubPath { get; }
    public string       Name    { get; }

    /// <summary>Full path including the hive token, e.g. <c>HKCU\Software\Foo</c> (or just <c>HKCU</c>).</summary>
    public string FullPath => SubPath.Length == 0 ? Root.Token : $"{Root.Token}\\{SubPath}";

    public ObservableCollection<RegistryTreeNode> Children { get; } = [];

    // Dummy child keeps the expand arrow visible before the real load.
    internal static readonly RegistryTreeNode Dummy = new();

    private RegistryTreeNode()   // dummy only
    {
        Root = RegistryRoot.CurrentUser;
        SubPath = ""; Name = "…";
    }

    public RegistryTreeNode(RegistryRoot root, string subPath, string name)
    {
        Root = root; SubPath = subPath; Name = name;
        // Only show an expand arrow when the key actually has subkeys (cheap SubKeyCount probe).
        if (HasSubKeys(root, subPath))
            Children.Add(Dummy);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            if (value) LoadChildren();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    /// <summary>Discards loaded children back to the unexpanded (dummy) state so a re-expand re-reads.</summary>
    public void ResetChildren()
    {
        Children.Clear();
        if (HasSubKeys(Root, SubPath))
            Children.Add(Dummy);
        _isExpanded = false;
        OnPropertyChanged(nameof(IsExpanded));
    }

    private void LoadChildren()
    {
        if (!(Children.Count == 1 && Children[0] == Dummy)) return;
        Children.Clear();
        try
        {
            var key = OpenKey();
            if (key is null) return;
            try
            {
                foreach (var name in key.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    var childPath = SubPath.Length == 0 ? name : $"{SubPath}\\{name}";
                    Children.Add(new RegistryTreeNode(Root, childPath, name));
                }
            }
            finally { if (SubPath.Length > 0) key.Dispose(); }
        }
        catch { /* access denied / transient — leave with no children */ }
    }

    /// <summary>Opens this node's key for reading. Hive roots return the shared singleton (do not dispose).</summary>
    private RegistryKey? OpenKey() =>
        SubPath.Length == 0 ? Root.Key : Root.Key.OpenSubKey(SubPath, writable: false);

    /// <summary>Cheap "does this key have subkeys?" probe (RegQueryInfoKey, no name enumeration).</summary>
    private static bool HasSubKeys(RegistryRoot root, string subPath)
    {
        try
        {
            var key = subPath.Length == 0 ? root.Key : root.Key.OpenSubKey(subPath, writable: false);
            if (key is null) return false;
            try { return key.SubKeyCount > 0; }
            finally { if (subPath.Length > 0) key.Dispose(); }
        }
        catch { return false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
