using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

// ── Per-node view model ──────────────────────────────────────────────────────

internal sealed partial class FileNodeViewModel : ObservableObject
{
    private static readonly FileNodeViewModel _placeholder = new("", "", isDirectory: true);

    public string FullPath    { get; }
    public string DisplayName { get; }
    public bool   IsDirectory { get; }

    public string Glyph => IsDirectory ? "📁 " : "📄 ";

    public ObservableCollection<FileNodeViewModel> Children { get; } = [];

    [ObservableProperty] private bool _isExpanded;

    private bool _loaded;

    public FileNodeViewModel(string fullPath, string displayName, bool isDirectory)
    {
        FullPath    = fullPath;
        DisplayName = displayName;
        IsDirectory = isDirectory;
        if (isDirectory && !string.IsNullOrEmpty(fullPath))
            Children.Add(_placeholder);  // shows expand arrow
    }

    public void EnsureLoaded()
    {
        if (_loaded || !IsDirectory) return;
        _loaded = true;
        Children.Clear();
        try
        {
            foreach (var dir in Directory.GetDirectories(FullPath)
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(name))
                    Children.Add(new FileNodeViewModel(dir, name, isDirectory: true));
            }
            foreach (var file in Directory.GetFiles(FullPath)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(name))
                    Children.Add(new FileNodeViewModel(file, name, isDirectory: false));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}

// ── Root view model ──────────────────────────────────────────────────────────

internal sealed class FileBrowserViewModel
{
    public ObservableCollection<FileNodeViewModel> Roots { get; } = [];

    public FileBrowserViewModel()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
            Roots.Add(new FileNodeViewModel(drive.RootDirectory.FullName, label, isDirectory: true));
        }
    }
}

// ── Window ───────────────────────────────────────────────────────────────────

public partial class FileBrowserWindow : Window
{
    public string? SelectedPath { get; private set; }

    public FileBrowserWindow()
    {
        InitializeComponent();
        DataContext = new FileBrowserViewModel();
    }

    /// <summary>Shows the file browser and returns the selected file path, or null if cancelled.</summary>
    public static string? Show(string? initialPath = null, Window? owner = null)
    {
        var win = new FileBrowserWindow
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return win.ShowDialog() == true ? win.SelectedPath : null;
    }

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // OK is only valid when a file (not a folder) is selected.
        if (e.NewValue is FileNodeViewModel { IsDirectory: false } node && !string.IsNullOrEmpty(node.FullPath))
        {
            SelectedPath          = node.FullPath;
            SelectedPathText.Text = node.FullPath;
            OkBtn.IsEnabled       = true;
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
        if (sender is TreeViewItem { DataContext: FileNodeViewModel node })
            node.EnsureLoaded();
    }

    private void TreeViewItem_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        // Double-clicking a file confirms the selection.
        if (sender is TreeViewItem { DataContext: FileNodeViewModel { IsDirectory: false } } && OkBtn.IsEnabled)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
