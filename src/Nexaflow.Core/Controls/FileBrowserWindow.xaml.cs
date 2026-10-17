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
using Nexaflow.Visuals.Common.Layout;
using System.Threading.Tasks;

namespace Nexaflow.Core.Controls;

// ── Per-node view model ──────────────────────────────────────────────────────

internal sealed partial class FileNodeViewModel : PickerNode<FileNodeViewModel>
{
    private static readonly FileNodeViewModel Placeholder = new("", "", isDirectory: true, null);

    private readonly IReadOnlyList<string>? _extensions;  // null/empty = any file

    /// <summary>Told when a file's tick changes; null unless the picker is choosing several files.</summary>
    private readonly Action<FileNodeViewModel>? _ticked;

    /// <summary>A file in a picker choosing several carries a tick.</summary>
    public bool CanTick => _ticked is not null && !IsDirectory;

    [ObservableProperty] private bool _isTicked;

    partial void OnIsTickedChanged(bool value) => _ticked?.Invoke(this);

    public bool IsDirectory { get; }

    /// <summary>Leading icon; a provided root supplies its own so a cloud location reads as one.</summary>
    public string Glyph => _glyphOverride ?? (IsDirectory ? "📁 " : "📄 ");
    private readonly string? _glyphOverride;

    public FileNodeViewModel(string fullPath, string displayName, bool isDirectory,
                             IReadOnlyList<string>? extensions, string? glyph = null,
                             Action<FileNodeViewModel>? ticked = null)
        : base(fullPath, displayName, isDirectory && fullPath.Length > 0 ? Placeholder : null)
    {
        IsDirectory    = isDirectory;
        _extensions    = extensions;
        _glyphOverride = glyph;
        _ticked        = ticked;
    }

    private bool Matches(string file) =>
        _extensions is null || _extensions.Count == 0
        || _extensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>Folders first, then the files the picker offers.</summary>
    protected override IReadOnlyList<FileNodeViewModel> ReadChildren(string path) =>
    [
        .. Directory.EnumerateDirectories(path)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Select(dir => (Path: dir, Name: Path.GetFileName(dir)))
                    .Where(entry => !string.IsNullOrEmpty(entry.Name))
                    .Select(entry => new FileNodeViewModel(entry.Path, entry.Name, isDirectory: true, _extensions, ticked: _ticked)),
        .. Directory.EnumerateFiles(path)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Select(file => (Path: file, Name: Path.GetFileName(file)))
                    .Where(entry => !string.IsNullOrEmpty(entry.Name) && Matches(entry.Path))
                    .Select(entry => new FileNodeViewModel(entry.Path, entry.Name, isDirectory: false, _extensions, ticked: _ticked)),
    ];
}

// ── Root view model ──────────────────────────────────────────────────────────

internal sealed class FileBrowserViewModel
{
    public ObservableCollection<FileNodeViewModel> Roots { get; } = [];

    public FileBrowserViewModel(IReadOnlyList<string>? extensions,
                                WorkspaceRuntime? workspace = null,
                                IReadOnlyList<ThisPcPlace>? places = null,
                                Action<FileNodeViewModel>? ticked = null)
    {
        foreach (var place in places ?? PickerRoots.For(workspace))
            Roots.Add(new FileNodeViewModel(place.RealPath, place.Label, isDirectory: true, extensions,
                                            PickerRoots.GlyphFor(place.Icon), ticked));
    }
}

// ── Window ───────────────────────────────────────────────────────────────────

public partial class FileBrowserWindow : Window
{
    public string? SelectedPath { get; private set; }

    /// <summary>What an Open choosing several answers: the files ticked, in the order they were ticked, or else the
    /// one file selected.</summary>
    public IReadOnlyList<string> SelectedPaths =>
        _ticked is { Count: > 0 } ticked ? [.. ticked.Select(node => node.FullPath)]
        : SelectedPath is { } one ? [one] : [];

    private readonly IReadOnlyList<string>? _extensions;

/// <summary>The files ticked, when choosing several; null when choosing one.</summary>
private readonly List<FileNodeViewModel>? _ticked;

/// <summary>What a save can be written as; the one chosen is <see cref="_chosen"/>. Empty for an open.</summary>
private IReadOnlyList<SaveFileType> _types = [];

private int _chosen;

    private bool    _saving;
    private long    _bytes;
    private string? _folder;

    public FileBrowserWindow() : this(null) { }

    public FileBrowserWindow(IReadOnlyList<string>? extensions, WorkspaceRuntime? workspace = null, bool several = false)
    {
        InitializeComponent();
        _extensions = extensions;
    if (several)
        {
            _ticked = [];
            ChoiceLabel.Text = Str.Get("Shell.Picker.FilesLabel");
        }
        DataContext = new FileBrowserViewModel(extensions, workspace, ticked: several ? OnTicked : null);
        HeaderText.Text = several ? Str.Get("Shell.Picker.SelectFiles")
                        : extensions is { Count: > 0 }
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
        // The dialog draws first and opens onto the file after: reading the way there can take a moment.
        win.Loaded += async (_, _) => await win.NavigateToAsync(initialPath);
        return win.ShowDialog() == true ? win.SelectedPath : null;
    }

    /// <summary>Shows the file browser choosing one or several files — a tick beside each — and returns them, or an
    /// empty list if cancelled.</summary>
    public static IReadOnlyList<string> ShowMany(string? initialPath = null,
                                                 IReadOnlyList<string>? extensions = null,
                                                 Window? owner = null,
                                                 WorkspaceRuntime? workspace = null)
    {
        var win = new FileBrowserWindow(extensions, workspace, several: true)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        win.Loaded += async (_, _) => await win.NavigateToAsync(initialPath);
        return win.ShowDialog() == true ? win.SelectedPaths : [];
    }

    /// <summary>A file ticked or unticked: the footer names the files ticked, and OK takes them.</summary>
    private void OnTicked(FileNodeViewModel node)
    {
        if (_ticked is null) return;
        if (node.IsTicked) { if (!_ticked.Contains(node)) _ticked.Add(node); }
        else _ticked.Remove(node);
        ShowChoice();
    }

    /// <summary>The footer and OK for an Open: the files ticked, or else the one file selected.</summary>
    private void ShowChoice()
    {
        if (_ticked is { Count: > 0 } ticked)
        {
            SelectedPathText.Text = ticked.Count == 1
                ? ticked[0].FullPath
                : Str.Format("Shell.Picker.Ticked", ticked.Count, string.Join(", ", ticked.Select(node => node.DisplayName)));
            OkBtn.IsEnabled = true;
            return;
        }
        SelectedPathText.Text = SelectedPath ?? string.Empty;
        OkBtn.IsEnabled = SelectedPath is not null;
    }

    /// <summary>Space ticks the file selected, when choosing several.</summary>
    private void FileTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || FileTree.SelectedItem is not FileNodeViewModel { CanTick: true } node) return;
        node.IsTicked = !node.IsTicked;
        e.Handled = true;
    }

    /// <summary>Shows the browser as a Save As dialog and returns the file to write, or null if cancelled.</summary>
    /// <param name="types">What the file can be written as, the first offered first; more than one is a choice.</param>
    /// <param name="bytes">How many bytes are about to be written, or 0 where the caller cannot say before it runs.
    /// It buys the reader a size, a free-space check and a warning over a file already there.</param>
    public static string? ShowSave(string suggestedName,
                                   IReadOnlyList<SaveFileType> types,
                                   string? initialPath = null,
                                   long bytes = 0,
                                   Window? owner = null,
                                   WorkspaceRuntime? workspace = null)
    {
        IReadOnlyList<string> extensions = [.. types.Select(type => type.Extension)];
        var win = new FileBrowserWindow(extensions.Count > 0 ? extensions : null, workspace)
        {
            Owner = owner ?? Application.Current.MainWindow
        };

        var start = SavePicking.Start(initialPath, extensions);
        win.Loaded += async (_, _) => await win.NavigateToAsync(start);
        win.Saving(suggestedName, types, start, bytes);

        return win.ShowDialog() == true ? win.SelectedPath : null;
    }

    /// <summary>Turns the browser into a Save As: a name to edit, what to write it as where there is a choice, the file it
    /// would write, and what writing it would mean.</summary>
    private void Saving(string suggested, IReadOnlyList<SaveFileType> types, string folder, long bytes)
    {
        _saving = true;
        _bytes  = bytes;
        _folder = folder;
        _types  = types;
        _chosen = 0;

        HeaderText.Text = Str.Get("Shell.Picker.SaveAs");
        SubText.Text    = types.Count == 1 && types[0].Extension.TrimStart('.') is { Length: > 0 } kind
                              ? Str.Format("Shell.Picker.SaveWhereFormat", kind.ToUpperInvariant())
                              : Str.Get("Shell.Picker.SaveWhere");

        SubText.Visibility  = Visibility.Visible;
        SaveFoot.Visibility = Visibility.Visible;
        OpenFoot.Visibility = Visibility.Collapsed;

        // One kind of file is said beside the name; several are a choice there, the first chosen.
        ExtText.Text          = types.Count == 1 ? types[0].Extension : string.Empty;
        ExtText.Visibility    = types.Count > 1 ? Visibility.Collapsed : Visibility.Visible;
        TypeChoice.Visibility = types.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        TypeChoice.ItemsSource = types.Count > 1 ? types.Select(type => Str.Format("Shell.Picker.TypeFormat", type.Name, type.Extension)).ToList() : null;
        TypeChoice.SelectedIndex = types.Count > 1 ? 0 : -1;

        // The stem is what a reader retypes; the extension is the caller's and goes back on whatever they leave.
        NameBox.Text = Stem(suggested);
        Weigh();

        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void TypeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_saving || TypeChoice.SelectedIndex < 0) return;
        _chosen = TypeChoice.SelectedIndex;
        Weigh();
    }

    private string Stem(string name) =>
        _types.FirstOrDefault(type => name.EndsWith(type.Extension, StringComparison.OrdinalIgnoreCase)) is { } named
            ? name[..^named.Extension.Length]
            : name;

    /// <summary>Works the folder showing and the name typed into the file that would be written, and says what
    /// writing it would cost. Runs on every keystroke and every move through the tree.</summary>
    private void Weigh()
    {
        if (SavePicking.Named(_folder, NameBox.Text, _extensions, _types.Count > 0 ? _types[_chosen].Extension : null) is not { } target)
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

    /// <summary>Opens the tree down to <paramref name="initialPath"/> (a folder, or a file's folder), selects it —
    /// the file, when one was given — and scrolls it into view.</summary>
    private async Task NavigateToAsync(string? initialPath)
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

        await node.LoadAsync();
        node.IsExpanded = true;
        var trail = new List<object> { node };

        var rel = Path.GetRelativePath(node.FullPath, dir);
        if (rel is not "." and not "")
        {
            foreach (var seg in rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                var child = node.Children.FirstOrDefault(c =>
                    c.IsDirectory && string.Equals(c.DisplayName, seg, StringComparison.OrdinalIgnoreCase));
                if (child is null) break;
                await child.LoadAsync();
                child.IsExpanded = true;
                node = child;
                trail.Add(child);
            }
        }

        // The reader may have closed the dialog while the way was being read.
        if (!IsVisible) return;

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
                VirtualTreeScroll.BringIntoView(FileTree, [.. trail, fileNode]);
                return;
            }
        }

        node.IsSelected = true;  // highlight the folder; OK stays disabled until a file is chosen
        VirtualTreeScroll.BringIntoView(FileTree, trail);
    }

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FileNodeViewModel { FullPath.Length: > 0 } node)
        {
            if (!_saving) { SelectedPath = null; ShowChoice(); }
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
        SelectedPath = node.IsDirectory ? null : node.FullPath;
        ShowChoice();
    }

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: FileNodeViewModel node })
            _ = node.LoadAsync();
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

        // Choosing several, with some ticked already: a double-click ticks this one too rather than answering.
        if (_ticked is { Count: > 0 } && node.CanTick)
        {
            node.IsTicked = !node.IsTicked;
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
