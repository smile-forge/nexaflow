using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using System.Globalization;
using System.Windows.Media;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Core.Controls;

// ── Per-node view model ──────────────────────────────────────────────────────

internal sealed partial class FileNodeViewModel : ObservableObject
{
    private static readonly FileNodeViewModel _placeholder = new("", "", isDirectory: true, null);

    private readonly IReadOnlyList<string>? _extensions;  // null/empty = any file

    public string FullPath    { get; }
    public string DisplayName { get; }
    public bool   IsDirectory { get; }

    /// <summary>Leading icon; a provided root supplies its own so a cloud location reads as one.</summary>
    public string Glyph => _glyphOverride ?? (IsDirectory ? "📁 " : "📄 ");
    private readonly string? _glyphOverride;

    public ObservableCollection<FileNodeViewModel> Children { get; } = [];

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    private bool _loaded;

    public FileNodeViewModel(string fullPath, string displayName, bool isDirectory,
                             IReadOnlyList<string>? extensions, string? glyph = null)
    {
        FullPath       = fullPath;
        DisplayName    = displayName;
        IsDirectory    = isDirectory;
        _extensions    = extensions;
        _glyphOverride = glyph;
        if (isDirectory && !string.IsNullOrEmpty(fullPath))
            Children.Add(_placeholder);  // shows expand arrow
    }

    private bool Matches(string file) =>
        _extensions is null || _extensions.Count == 0
        || _extensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

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
                    Children.Add(new FileNodeViewModel(dir, name, isDirectory: true, _extensions));
            }
            foreach (var file in Directory.GetFiles(FullPath)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(name) && Matches(file))
                    Children.Add(new FileNodeViewModel(file, name, isDirectory: false, _extensions));
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

    public FileBrowserViewModel(IReadOnlyList<string>? extensions,
                                WorkspaceRuntime? workspace = null,
                                IReadOnlyList<ThisPcPlace>? places = null)
    {
        foreach (var place in places ?? PickerRoots.For(workspace))
            Roots.Add(new FileNodeViewModel(place.RealPath, place.Label, isDirectory: true, extensions,
                                            PickerRoots.GlyphFor(place.Icon)));
    }
}

// ── Window ───────────────────────────────────────────────────────────────────

public partial class FileBrowserWindow : Window
{
    public string? SelectedPath { get; private set; }

    private readonly IReadOnlyList<string>? _extensions;

    private bool    _saving;
    private long    _bytes;
    private string? _folder;

    public FileBrowserWindow() : this(null) { }

    public FileBrowserWindow(IReadOnlyList<string>? extensions, WorkspaceRuntime? workspace = null)
    {
        InitializeComponent();
        _extensions = extensions;
        DataContext = new FileBrowserViewModel(extensions, workspace);
        HeaderText.Text = extensions is { Count: > 0 }
                              ? Str.Format("Shell.Picker.SelectFileOfFormat",
                                           string.Join(", ", extensions.Select(ext => "*" + ext)))
                              : Str.Get("Shell.Picker.SelectFile");
    }

    /// <summary>Shows the file browser and returns the selected file path, or null if cancelled.</summary>
    /// <param name="extensions">Allowed extensions (e.g. ".exe"); null/empty offers any file.</param>
    public static string? Show(string? initialPath = null,
                               IReadOnlyList<string>? extensions = null,
                               Window? owner = null,
                               WorkspaceRuntime? workspace = null)
    {
        var win = new FileBrowserWindow(extensions, workspace)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        win.NavigateTo(initialPath);
        return win.ShowDialog() == true ? win.SelectedPath : null;
    }

    /// <summary>Shows the browser as a Save As dialog and returns the file to write, or null if cancelled.</summary>
    /// <param name="bytes">How many bytes are about to be written, or 0 where the caller cannot say before it runs.
    /// It buys the reader a size, a free-space check and a warning over a file already there.</param>
    public static string? ShowSave(string suggestedName,
                                   IReadOnlyList<string>? extensions = null,
                                   string? initialPath = null,
                                   long bytes = 0,
                                   Window? owner = null,
                                   WorkspaceRuntime? workspace = null)
    {
        var win = new FileBrowserWindow(extensions, workspace)
        {
            Owner = owner ?? Application.Current.MainWindow
        };

        var start = SavePicking.Start(initialPath, extensions);
        win.NavigateTo(start);
        win.Saving(suggestedName, start, bytes);

        return win.ShowDialog() == true ? win.SelectedPath : null;
    }

    /// <summary>Turns the browser into a Save As: a name to edit, the file it would write, and what writing it
    /// would mean.</summary>
    private void Saving(string suggested, string folder, long bytes)
    {
        _saving = true;
        _bytes  = bytes;
        _folder = folder;

        HeaderText.Text = Str.Get("Shell.Picker.SaveAs");
        SubText.Text    = _extensions is { Count: > 0 } && _extensions[0].TrimStart('.') is { Length: > 0 } kind
                              ? Str.Format("Shell.Picker.SaveWhereFormat", kind.ToUpperInvariant())
                              : Str.Get("Shell.Picker.SaveWhere");

        SubText.Visibility  = Visibility.Visible;
        SaveFoot.Visibility = Visibility.Visible;
        OpenFoot.Visibility = Visibility.Collapsed;
        ExtText.Text        = _extensions is { Count: > 0 } ? _extensions[0] : string.Empty;

        // The stem is what a reader retypes; the extension is the caller's and goes back on whatever they leave.
        NameBox.Text = Stem(suggested);
        Weigh();

        NameBox.Focus();
        NameBox.SelectAll();
    }

    private string Stem(string name) =>
        _extensions is { Count: > 0 } && name.EndsWith(_extensions[0], StringComparison.OrdinalIgnoreCase)
            ? name[..^_extensions[0].Length]
            : name;

    /// <summary>Works the folder showing and the name typed into the file that would be written, and says what
    /// writing it would cost. Runs on every keystroke and every move through the tree.</summary>
    private void Weigh()
    {
        if (SavePicking.Named(_folder, NameBox.Text, _extensions) is not { } target)
        {
            SelectedPath        = null;
            WhereText.Text      = Str.Get("Shell.Picker.PickAFolder");
            RoomText.Text       = string.Empty;
            FlagText.Visibility = Visibility.Collapsed;
            OkBtn.IsEnabled     = false;
            OkBtn.Content       = Str.Get("Shell.Picker.Save");
            OkBtn.Background    = Tone("AccentBrush");
            return;
        }

        var room = SaveRoom.For(target, _bytes);

        SelectedPath     = room.Target;
        WhereText.Text   = room.Target;
        RoomText.Text    = Costing(room);
        OkBtn.IsEnabled  = room.Allowed;
        OkBtn.Content    = room.Replaces ? Str.Get("Shell.Picker.Replace") : Str.Get("Shell.Picker.Save");
        OkBtn.Background = Tone(room.Replaces && room.Allowed ? "WarningBrush" : "AccentBrush");

        var (flag, tone) = Warning(room);

        FlagText.Text       = flag ?? string.Empty;
        FlagText.Visibility = flag is null ? Visibility.Collapsed : Visibility.Visible;
        if (tone is not null) FlagText.Foreground = tone;
    }

    private static string Costing(SaveRoom room)
    {
        string?[] parts =
        [
            room.Bytes > 0 ? Str.Format("Shell.Picker.WritingFormat", SizeFormatter.FormatBytes(room.Bytes)) : null,
            room.Free is { } spare
                ? Str.Format("Shell.Picker.FreeFormat", SizeFormatter.FormatBytes(spare), Volume(room.Target))
                : null,
        ];

        return string.Join("  ·  ", parts.Where(part => part is { Length: > 0 }));
    }

    /// <summary>What is wrong with this destination, or what the reader is about to lose by using it.</summary>
    private (string? Flag, Brush? Tone) Warning(SaveRoom room)
    {
        if (!room.Fits)
            return (Str.Format("Shell.Picker.NoRoomFormat", Volume(room.Target),
                               SizeFormatter.FormatBytes(room.Shortfall)), Tone("DangerBrush"));

        var stopped = room.Trouble switch
        {
            SaveTrouble.NoName        => Str.Get("Shell.Picker.TroubleNoName"),
            SaveTrouble.NotAPath      => Str.Get("Shell.Picker.TroubleNotAPath"),
            SaveTrouble.NoFolder      => Str.Get("Shell.Picker.TroubleNoFolder"),
            SaveTrouble.FolderRefused => Str.Get("Shell.Picker.TroubleFolderRefused"),
            SaveTrouble.FileReadOnly  => Str.Get("Shell.Picker.TroubleFileReadOnly"),
            SaveTrouble.FileInUse     => Str.Get("Shell.Picker.TroubleFileInUse"),
            SaveTrouble.FileRefused   => Str.Get("Shell.Picker.TroubleFileRefused"),
            _                         => null,
        };

        if (stopped is not null) return (stopped, Tone("DangerBrush"));

        return room.Replaces
                   ? (Str.Format("Shell.Picker.ReplacingFormat",
                                 SizeFormatter.FormatBytes(room.Replacing ?? 0), When(room.LastWritten)),
                      Tone("WarningBrush"))
                   : (null, null);
    }

    private static string Volume(string target) => Path.GetPathRoot(target)?.TrimEnd('\\') ?? "";

    private static string When(DateTime? stamp)
    {
        if (stamp is not { } at) return "";

        var clock = at.ToString("t", CultureInfo.CurrentCulture);

        return (DateTime.Now.Date - at.Date).Days switch
        {
            0 => Str.Format("Shell.Picker.TodayAtFormat", clock),
            1 => Str.Format("Shell.Picker.YesterdayAtFormat", clock),
            _ => at.ToString("g", CultureInfo.CurrentCulture),
        };
    }

    private Brush? Tone(string key) => TryFindResource(key) as Brush;

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_saving) Weigh();
    }

    /// <summary>Expands the tree to <paramref name="initialPath"/> (a folder, or a file's folder),
    /// pre-selecting the file when one was given.</summary>
    private void NavigateTo(string? initialPath)
    {
        if (string.IsNullOrEmpty(initialPath)) return;
        try { initialPath = Path.GetFullPath(initialPath); } catch { return; }
        if (DataContext is not FileBrowserViewModel vm) return;

        var targetIsFile = File.Exists(initialPath);
        var dir = targetIsFile ? Path.GetDirectoryName(initialPath) : initialPath;
        if (string.IsNullOrEmpty(dir)) return;

        var root = Path.GetPathRoot(dir)?.TrimEnd('\\');
        var node = vm.Roots.FirstOrDefault(r =>
            string.Equals(r.FullPath.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase));
        if (node is null) return;

        node.EnsureLoaded();
        node.IsExpanded = true;

        var rel = Path.GetRelativePath(node.FullPath, dir);
        if (rel is not "." and not "")
        {
            foreach (var seg in rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                var child = node.Children.FirstOrDefault(c =>
                    c.IsDirectory && string.Equals(c.DisplayName, seg, StringComparison.OrdinalIgnoreCase));
                if (child is null) break;
                child.EnsureLoaded();
                child.IsExpanded = true;
                node = child;
            }
        }

        if (targetIsFile)
        {
            var fileNode = node.Children.FirstOrDefault(c =>
                !c.IsDirectory && string.Equals(c.FullPath, initialPath, StringComparison.OrdinalIgnoreCase));
            if (fileNode is not null)
            {
                fileNode.IsSelected   = true;
                SelectedPath          = fileNode.FullPath;
                SelectedPathText.Text = fileNode.FullPath;
                OkBtn.IsEnabled       = true;
                return;
            }
        }

        node.IsSelected = true;  // highlight the folder; OK stays disabled until a file is chosen
    }

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FileNodeViewModel { FullPath.Length: > 0 } node)
        {
            if (!_saving) { SelectedPath = null; SelectedPathText.Text = string.Empty; OkBtn.IsEnabled = false; }
            return;
        }

        if (_saving)
        {
            // A folder is a destination. A file is a destination and a name too, since choosing one is choosing
            // to replace it — and the warning that then appears is the point of showing files here at all.
            _folder = node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath);
            if (!node.IsDirectory) NameBox.Text = Stem(node.DisplayName);

            Weigh();
            return;
        }

        // Open: only a file is an answer, never the folder holding it.
        if (node.IsDirectory)
        {
            SelectedPath          = null;
            SelectedPathText.Text = string.Empty;
            OkBtn.IsEnabled       = false;
            return;
        }

        SelectedPath          = node.FullPath;
        SelectedPathText.Text = node.FullPath;
        OkBtn.IsEnabled       = true;
    }

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: FileNodeViewModel node })
            node.EnsureLoaded();
    }

    private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem { IsSelected: true } tvi) tvi.BringIntoView();
    }

    private void TreeViewItem_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem { DataContext: FileNodeViewModel node } || node.IsDirectory) return;

        // Saving: a double-click takes the file's name, and stops there. Confirming on the same gesture would
        // overwrite it before the reader had read the line telling them what they were replacing.
        if (_saving)
        {
            NameBox.Text = Stem(node.DisplayName);
            NameBox.Focus();
            NameBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (!OkBtn.IsEnabled) return;

        DialogResult = true;
        e.Handled    = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>The header is the caption: with the native chrome off, dragging it moves the dialog.</summary>
    private void Caption_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
