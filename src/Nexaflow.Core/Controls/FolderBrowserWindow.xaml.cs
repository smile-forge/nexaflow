using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Visuals.Common.Layout;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Core.Controls;

// ── Per-node view model ──────────────────────────────────────────────────────

internal sealed class FolderNodeViewModel : PickerNode<FolderNodeViewModel>
{
    private static readonly FolderNodeViewModel Placeholder = new("", "");

    public string Glyph { get; }

    public FolderNodeViewModel(string fullPath, string displayName, string glyph = "📁 ")
        : base(fullPath, displayName, fullPath.Length > 0 ? Placeholder : null)
    {
        Glyph = glyph;
    }

    protected override IReadOnlyList<FolderNodeViewModel> ReadChildren(string path) =>
    [
        .. Directory.EnumerateDirectories(path)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Select(dir => (Dir: dir, Name: Path.GetFileName(dir)))
                    .Where(entry => !string.IsNullOrEmpty(entry.Name))
                    .Select(entry => new FolderNodeViewModel(entry.Dir, entry.Name)),
    ];
}

// ── Root view model ──────────────────────────────────────────────────────────

internal sealed class FolderBrowserViewModel
{
    public ObservableCollection<FolderNodeViewModel> Roots { get; } = [];

    public FolderBrowserViewModel(WorkspaceRuntime? workspace = null,
                                  IReadOnlyList<ThisPcPlace>? places = null)
    {
        foreach (var place in places ?? PickerRoots.For(workspace))
            Roots.Add(new FolderNodeViewModel(place.RealPath, place.Label, PickerRoots.GlyphFor(place.Icon)));
    }
}

// ── Window ───────────────────────────────────────────────────────────────────

public partial class FolderBrowserWindow : Window
{
    public string? SelectedPath { get; private set; }

    public FolderBrowserWindow() : this(null) { }

    public FolderBrowserWindow(WorkspaceRuntime? workspace)
    {
        InitializeComponent();
        DataContext = new FolderBrowserViewModel(workspace);
    }

    /// <summary>Shows the folder browser and returns the selected path, or null if cancelled.</summary>
    public static string? Show(string? initialPath = null, Window? owner = null,
                              WorkspaceRuntime? workspace = null)
    {
        var win = new FolderBrowserWindow(workspace)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        // The dialog draws first and opens onto the folder after: reading the way there can take a moment.
        win.Loaded += async (_, _) => await win.NavigateToAsync(initialPath);
        return win.ShowDialog() == true ? win.SelectedPath : null;
    }

    /// <summary>Opens the tree down to <paramref name="initialPath"/>, selects it and scrolls it into view.</summary>
    private async Task NavigateToAsync(string? initialPath)
    {
        if (string.IsNullOrEmpty(initialPath)) return;
        try { if (File.Exists(initialPath)) initialPath = Path.GetDirectoryName(initialPath); }
        catch { return; }
        if (string.IsNullOrEmpty(initialPath)) return;
        try { initialPath = Path.GetFullPath(initialPath); } catch { return; }
        if (DataContext is not FolderBrowserViewModel vm) return;

        var root = Path.GetPathRoot(initialPath)?.TrimEnd('\\');
        var node = vm.Roots.FirstOrDefault(r =>
            string.Equals(r.FullPath.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase));
        if (node is null) return;

        await node.LoadAsync();
        node.IsExpanded = true;
        var trail = new List<object> { node };

        var rel = Path.GetRelativePath(node.FullPath, initialPath);
        if (rel is not "." and not "")
        {
            foreach (var seg in rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                var child = node.Children.FirstOrDefault(c =>
                    string.Equals(c.DisplayName, seg, StringComparison.OrdinalIgnoreCase));
                if (child is null) break;
                await child.LoadAsync();
                child.IsExpanded = true;
                node = child;
                trail.Add(child);
            }
        }

        // The reader may have closed the dialog while the way was being read.
        if (!IsVisible) return;

        node.IsSelected = true;
        SelectedPath = node.FullPath;
        SelectedPathText.Text = node.FullPath;
        OkBtn.IsEnabled = true;
        VirtualTreeScroll.BringIntoView(FolderTree, trail);
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
            _ = node.LoadAsync();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>The header is the caption: with the native chrome off, dragging it moves the dialog.</summary>
    private void Caption_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}

// ── Picker roots ─────────────────────────────────────────────────────────────

/// <summary>
/// The top-level places a picker lists: the ready drives plus any contributed location, from the same
/// <see cref="ThisPcItemSet.Enumerate"/> the file browser uses — so the two can't drift apart on naming
/// or iconography, which is what four independent drive loops had already let happen.
/// <para>
/// Everything is listed by its REAL path under a friendly label: a picker's result is consumed by
/// ordinary <see cref="System.IO"/> callers, so it must hand back something they can open. Locations a
/// provider serves itself have no such path and are excluded.
/// </para>
/// </summary>
internal static class PickerRoots
{
    public static IReadOnlyList<ThisPcPlace> For(WorkspaceRuntime? workspace)
    {
        // Providers don't vary by workspace, so any live one will do when the caller has none to hand
        // (the Options browse buttons, the workspace folder picker).
        workspace ??= WorkspaceManager.Instance.FirstActive;

        IEnumerable<IThisPcItemProvider> providers = [];
        if (workspace is not null)
        {
            // No live workspace — tests, early startup — means nothing to build providers from; the
            // drives are still listed, which is what the picker did before any of this existed.
            try { providers = FeatureManager.Instance.GetThisPcItemProviders(workspace); }
            catch { providers = []; }
        }

        try { return ThisPcItemSet.Enumerate(providers, readyDrivesOnly: true, allowVirtual: false); }
        catch { return []; }
    }

    /// <summary>The picker's render vocabulary. It covers every place kind — a USB stick reading as a
    /// plain folder while a cloud location gets its own mark is exactly the incoherence that having two
    /// half-mappings produced.</summary>
    public static string GlyphFor(ThisPcItemIcon icon) => icon switch
    {
        ThisPcItemIcon.Disk      => "🖴 ",
        ThisPcItemIcon.Removable => "🔌 ",
        ThisPcItemIcon.Optical   => "💿 ",
        ThisPcItemIcon.Network   => "🖧 ",
        ThisPcItemIcon.Cloud     => "☁ ",
        _                        => "📁 ",
    };
}
