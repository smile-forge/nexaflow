using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

// ── Per-node view model ──────────────────────────────────────────────────────

internal sealed partial class FolderNodeViewModel : ObservableObject
{
    private static readonly FolderNodeViewModel _placeholder = new("", "");

    public string FullPath    { get; }
    public string DisplayName { get; }

    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    [ObservableProperty] private bool _isExpanded;

    private bool _loaded;

    public FolderNodeViewModel(string fullPath, string displayName)
    {
        FullPath    = fullPath;
        DisplayName = displayName;
        if (!string.IsNullOrEmpty(fullPath))
            Children.Add(_placeholder);  // shows expand arrow
    }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Children.Clear();
        try
        {
            foreach (var dir in Directory.GetDirectories(FullPath)
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(name))
                    Children.Add(new FolderNodeViewModel(dir, name));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}

// ── Root view model ──────────────────────────────────────────────────────────

internal sealed class FolderBrowserViewModel
{
    public ObservableCollection<FolderNodeViewModel> Roots { get; } = [];

    public FolderBrowserViewModel()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
            Roots.Add(new FolderNodeViewModel(drive.RootDirectory.FullName, label));
        }
    }
}

// ── Window ───────────────────────────────────────────────────────────────────

public partial class FolderBrowserWindow : Window
{
    public string? SelectedPath { get; private set; }

    public FolderBrowserWindow()
    {
        InitializeComponent();
        DataContext = new FolderBrowserViewModel();
    }

    /// <summary>Shows the folder browser and returns the selected path, or null if cancelled.</summary>
    public static string? Show(string? initialPath = null, Window? owner = null)
    {
        var win = new FolderBrowserWindow
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return win.ShowDialog() == true ? win.SelectedPath : null;
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNodeViewModel node && !string.IsNullOrEmpty(node.FullPath))
        {
            SelectedPath             = node.FullPath;
            SelectedPathText.Text    = node.FullPath;
            OkBtn.IsEnabled          = true;
        }
        else
        {
            SelectedPath          = null;
            SelectedPathText.Text = string.Empty;
            OkBtn.IsEnabled       = false;
        }
    }

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: FolderNodeViewModel node })
            node.EnsureLoaded();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
