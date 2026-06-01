using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Core.Controls;

/// <summary>A pickable colour from the shared swatch bank: the brush to show + the hex to store.</summary>
public sealed record SwatchOption(string Hex, Brush Brush);

public partial class ProfilesConfigControl : UserControl, ICustomConfigApply
{
    private ObservableCollection<Profile>? _editProfiles;
    private WorkspacesConfig?              _config;

    private static readonly Random _rng = new();

    // The shared categorical bank (Tokens.xaml / per-theme overrides), resolved to brushes + hex for
    // the per-profile colour picker. Same source the ribbon picker and project pie draw from.
    private static readonly string[] SwatchKeys =
    [
        "Swatch.Blue",  "Swatch.Cyan",   "Swatch.Teal",  "Swatch.Green",
        "Swatch.Lime",  "Swatch.Yellow", "Swatch.Amber", "Swatch.Orange",
        "Swatch.Red",   "Swatch.Pink",   "Swatch.Purple","Swatch.Slate",
    ];

    public IReadOnlyList<SwatchOption> Swatches { get; } = BuildSwatches();

    private static IReadOnlyList<SwatchOption> BuildSwatches()
    {
        var list = new List<SwatchOption>();
        foreach (var key in SwatchKeys)
        {
            // Throw (not silently skip) if a bank key is missing, so a typo surfaces instead of a gap.
            if (Application.Current?.Resources[key] is not SolidColorBrush b)
                throw new InvalidOperationException($"Swatch brush '{key}' not found.");
            list.Add(new SwatchOption(b.Color.ToString(), b));
        }
        return list;
    }

    /// <summary>Random colour from the swatch bank (falls back to the legacy palette if unresolved).</summary>
    private string RandomSwatchColor()
        => Swatches.Count > 0 ? Swatches[_rng.Next(Swatches.Count)].Hex : ProfileStyle.Random().Color;

    // Maps each editable copy back to the original profile so Apply can write the edited
    // Name/Color/Icon onto the original without disturbing live workspaces.
    private readonly Dictionary<Profile, Profile> _editToOriginal = [];

    // Maps an editable copy that represents an unsaved CLONE to the source profile it should be
    // cloned from at Apply time (copying its AiConfig + provider configs).
    private readonly Dictionary<Profile, Profile> _editCloneSource = [];

    public ProfilesConfigControl()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is WorkspacesConfig cfg)
            {
                _config = cfg;
                _editToOriginal.Clear();
                _editCloneSource.Clear();
                _editProfiles = [];
                foreach (var p in cfg.Contexts)
                {
                    var edit = new Profile { Name = p.Name, Color = p.Color, Icon = p.Icon };
                    _editToOriginal[edit] = p;
                    _editProfiles.Add(edit);
                }
                ProfileItems.ItemsSource = _editProfiles;
            }
        };
    }

    public void Apply()
    {
        if (_config is null || _editProfiles is null) return;

        var mgr       = WorkspaceManager.Instance;
        var survivors = new HashSet<Profile>();
        var ordered   = new List<Profile>();

        foreach (var edit in _editProfiles)
        {
            if (_editToOriginal.TryGetValue(edit, out var original))
            {
                original.Name  = edit.Name;
                original.Color = edit.Color;
                original.Icon  = edit.Icon;
                survivors.Add(original);
                ordered.Add(original);
            }
            else if (_editCloneSource.TryGetValue(edit, out var source))
            {
                var created = mgr.CloneProfile(source, edit.Name);
                created.Color = edit.Color;
                created.Icon  = edit.Icon;
                survivors.Add(created);
                ordered.Add(created);
            }
            else
            {
                var created = mgr.AddProfile(edit.Name);
                created.Color = edit.Color;
                created.Icon  = edit.Icon;
                survivors.Add(created);
                ordered.Add(created);
            }
        }

        // Remove profiles the user deleted — but never one a live workspace is using, and never the last.
        foreach (var profile in mgr.Profiles.ToList())
            if (!survivors.Contains(profile) && !mgr.IsProfileInUse(profile))
                mgr.RemoveProfile(profile);

        _config.Contexts = ordered.Count > 0 ? ordered : [.. mgr.Profiles];
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var (icon, _) = ProfileStyle.Random();
        _editProfiles?.Add(new Profile { Name = "New Context", Color = RandomSwatchColor(), Icon = icon });
    }

    // Click a swatch → set the colour of the profile that owns this row. Profile is observable, so the
    // hex box and colour preview update live. The owning profile is the nearest DataContext up the tree
    // (the swatch button's own DataContext is the SwatchOption).
    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string hex } fe) return;
        for (DependencyObject? d = fe; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: Profile p })
            {
                p.Color = hex;
                return;
            }
    }

    private void OnCloneClick(object sender, RoutedEventArgs e)
    {
        if (_editProfiles is null) return;
        if (sender is not Button { Tag: Profile edit }) return;

        var source = _editToOriginal.TryGetValue(edit, out var original) ? original
                   : _editCloneSource.TryGetValue(edit, out var src)     ? src
                   : null;
        if (source is null) return;   // a brand-new unsaved profile has no persisted settings to copy

        var (icon, _) = ProfileStyle.Random();
        var clone = new Profile { Name = edit.Name + " copy", Color = RandomSwatchColor(), Icon = icon };
        _editCloneSource[clone] = source;

        _editProfiles.Insert(_editProfiles.IndexOf(edit) + 1, clone);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_editProfiles is null || _editProfiles.Count <= 1) return;
        if (sender is not Button { Tag: Profile edit }) return;

        // Cannot delete a profile a live workspace is using (incl. the active one).
        if (_editToOriginal.TryGetValue(edit, out var original)
            && WorkspaceManager.Instance.IsProfileInUse(original))
            return;

        _editProfiles.Remove(edit);
    }
}
