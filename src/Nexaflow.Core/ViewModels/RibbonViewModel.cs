using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common.Ribbon;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// Owns one window's ribbon <see cref="Items"/> collection. The ribbon LAYOUT is owned by the
/// <see cref="Profile"/> (shared by every Workspace on it): edits persist through the profile's
/// <see cref="RibbonLayoutService"/> and raise <see cref="Models.Profile.RibbonChanged"/>, which
/// every other window/Workspace on the same profile observes to reload its own items live.
/// The <see cref="Workspace"/> is held separately, only to resolve per-Workspace pin handlers.
/// </summary>
public partial class RibbonViewModel : ObservableObject
{
    private Workspace? _workspace;
    private Profile?   _profile;

    // Guards: _reloading suppresses save while we repopulate Items; _isSaving lets the window that
    // originated a change skip its own RibbonChanged reload (and prevents save/reload feedback loops).
    private bool _reloading;
    private bool _isSaving;

    public ObservableCollection<RibbonItem> Items { get; } = [];

    /// <summary>
    /// Openable page kinds offered in the editor's "add page" dropdown — the registrations whose
    /// <c>CanBeContextItem</c> is true for this workspace. Refreshed each time the editor opens, since
    /// availability can depend on workspace/config (e.g. a feature toggled off).
    /// </summary>
    public ObservableCollection<RibbonCatalogEntry> AvailablePages { get; } = [];

    /// <summary>Open/closed state of the inline ribbon editor overlay.</summary>
    [ObservableProperty] private bool _isEditOpen;

    /// <summary>
    /// Set by the host so the ribbon view can flash an item when a duplicate
    /// pin attempt is rejected.
    /// </summary>
    public Action<RibbonItem>? FlashItem { get; set; }

    public RibbonViewModel()
    {
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public Profile? Profile => _profile;

    /// <summary>The Workspace this ribbon belongs to — used only to resolve pin handlers.</summary>
    public void SetWorkspace(Workspace? workspace) => _workspace = workspace;

    /// <summary>
    /// Swap to a new profile: unhook the old profile's live-sync, clear, load the incoming layout.
    /// Persistence is immediate per-edit, so there's nothing to flush on the way out.
    /// </summary>
    public void SetProfile(Profile? profile)
    {
        if (ReferenceEquals(profile, _profile)) return;

        _reloading = true;
        try
        {
            if (_profile is not null) _profile.RibbonChanged -= OnProfileRibbonChanged;

            foreach (var item in Items)
                item.PropertyChanged -= OnItemChanged;
            Items.Clear();

            _profile = profile;
            if (_profile is not null) _profile.RibbonChanged += OnProfileRibbonChanged;

            LoadOrBuildItems();

            foreach (var item in Items)
            {
                item.PropertyChanged -= OnItemChanged;
                item.PropertyChanged += OnItemChanged;
            }
        }
        finally
        {
            _reloading = false;
        }
    }

    /// <summary>
    /// Another window on the same profile changed the ribbon — reload our items from disk so the
    /// edit shows live. Skipped on the window that originated the change (its items are already current).
    /// </summary>
    private void OnProfileRibbonChanged(object? sender, EventArgs e)
    {
        if (_isSaving) return;
        Application.Current?.Dispatcher.Invoke(ReloadFromDisk);
    }

    private void ReloadFromDisk()
    {
        _reloading = true;
        try
        {
            foreach (var item in Items)
                item.PropertyChanged -= OnItemChanged;
            Items.Clear();
            LoadOrBuildItems();
            foreach (var item in Items)
            {
                item.PropertyChanged -= OnItemChanged;
                item.PropertyChanged += OnItemChanged;
            }
        }
        finally
        {
            _reloading = false;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_reloading) return;

        if (e.OldItems is not null)
            foreach (RibbonItem item in e.OldItems)
                item.PropertyChanged -= OnItemChanged;
        if (e.NewItems is not null)
            foreach (RibbonItem item in e.NewItems)
            {
                item.PropertyChanged -= OnItemChanged;
                item.PropertyChanged += OnItemChanged;
            }

        if (Items.Count == 0)
        {
            // Empty ribbon — re-seed defaults asynchronously (skip during edit mode
            // where the user may be mid-clear).
            if (!IsEditOpen)
                Application.Current.Dispatcher.BeginInvoke(BuildDefaults);
        }
        else
        {
            Save();
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Save();

    [RelayCommand]
    public void Save()
    {
        if (_reloading || _profile?.RibbonService is null) return;

        _isSaving = true;
        try
        {
            _profile.RibbonService.Save(Items);
            _profile.RaiseRibbonChanged();   // live-reload other windows/Workspaces on this profile
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void LoadOrBuildItems()
    {
        var saved = _profile?.RibbonService?.Load();
        if (saved is { Count: > 0 })
        {
            foreach (var item in saved)
                Items.Add(item);
        }
        else
        {
            BuildDefaults();
        }
    }

    public void BuildDefaults()
    {
        foreach (var item in RibbonLayoutService.LoadDefaults())
            Items.Add(item);
    }

    public void AddItem(RibbonItem item, int insertAt = -1)
    {
        if (insertAt >= 0 && insertAt < Items.Count)
            Items.Insert(insertAt, item);
        else
            Items.Add(item);
    }

    [RelayCommand]
    private void ToggleEdit()
    {
        if (!IsEditOpen) RefreshAvailablePages();   // populate the catalog just before showing
        IsEditOpen = !IsEditOpen;
    }

    /// <summary>Rebuilds <see cref="AvailablePages"/> from the workspace's context-item registrations.</summary>
    private void RefreshAvailablePages()
    {
        AvailablePages.Clear();
        if (_workspace is null) return;

        var entries = FeatureManager.Instance.GetContextItemPages(_workspace)
            .Where(p => !string.IsNullOrEmpty(p.PageKind))
            .Select(p => new RibbonCatalogEntry(p.PageKind!, p.Title, p.Icon))
            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase);

        foreach (var entry in entries)
            AvailablePages.Add(entry);
    }

    /// <summary>
    /// Handle a tab drag-drop pin. Delegates to the registered <see cref="IRibbonPinHandler"/>
    /// for the tab's page kind when one exists; otherwise snapshots the tab's current metadata.
    /// </summary>
    [RelayCommand]
    public void Pin(TabPinRequest request)
    {
        var (tab, insertIndex) = request;
        if (string.IsNullOrEmpty(tab.PageKind)) return;

        RibbonPinResult result;
        if (_workspace is not null)
        {
            var handler = FeatureManager.Instance.GetRibbonPinHandler(tab.PageKind, _workspace);
            if (handler is not null)
            {
                var handlerResult = handler.Pin(tab, insertIndex);
                if (handlerResult is null) return;
                result = handlerResult;
            }
            else
            {
                result = TabMetadataResult(tab);
            }
        }
        else
        {
            result = TabMetadataResult(tab);
        }

        InsertPin(tab.PageKind, result, insertIndex);
    }

    /// <summary>
    /// Handle a handler-based pin: looks up the registered <see cref="IRibbonPinHandler"/>
    /// for the request's <see cref="RibbonPinRequest.ContentKind"/>, builds the button,
    /// deduplicates, and inserts.
    /// </summary>
    [RelayCommand]
    public void PinFromHandler(RibbonPinRequest request)
    {
        if (_workspace is null) return;
        var handler = FeatureManager.Instance.GetRibbonPinHandler(request.ContentKind, _workspace);
        if (handler is null) return;

        var result = handler.Pin(request.Payload, request.InsertIndex);
        if (result is null) return;

        InsertPin(request.ContentKind, result, request.InsertIndex);
    }

    private static RibbonPinResult TabMetadataResult(Page tab) => new()
    {
        Label      = tab.Title,
        Icon       = tab.Icon,
        PageParams = tab.PageParams is not null ? new(tab.PageParams) : null
    };

    private void InsertPin(string pageKind, RibbonPinResult result, int insertIndex)
    {
        var duplicate = Items.FirstOrDefault(r =>
            r.Kind == RibbonItemKind.Button &&
            r.PageKind == pageKind &&
            ParamsEqual(r.PageParams, result.PageParams));
        if (duplicate is not null)
        {
            FlashItem?.Invoke(duplicate);
            return;
        }

        AddItem(new RibbonItem
        {
            Kind        = RibbonItemKind.Button,
            Label       = result.Label,
            Icon        = result.Icon,
            AccentColor = result.AccentColor,
            PageKind    = pageKind,
            PageParams  = result.PageParams
        }, insertIndex);
    }

    private static bool ParamsEqual(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        return a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }
}
