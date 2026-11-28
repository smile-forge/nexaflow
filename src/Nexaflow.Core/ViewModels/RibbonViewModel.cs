using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Icons;
using Nexaflow.Interaction;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Icons;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// One window's ribbon: the <see cref="Items"/> it draws, mirroring its workspace's <see cref="WorkspaceRibbon"/>, which saves
/// them and keeps every window on the workspace in step. An edit made here is saved as it happens; a change made elsewhere is
/// taken up here. The <see cref="WorkspaceRuntime"/> is held separately, to resolve the pin handlers and the pages the editor offers.
/// </summary>
public partial class RibbonViewModel : ObservableObject
{
    private WorkspaceRuntime? _runtime;
    private Workspace?   _workspace;

    // The UI dispatcher this view-model was created on; a change saved from another thread is taken up through it.
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    // Set while Items is filled from the workspace's ribbon, so filling it saves nothing.
    private bool _reloading;

    public ObservableCollection<RibbonItem> Items { get; } = [];

    /// <summary>
    /// Openable pages offered in the editor's "add page" dropdown, less anything already on the ribbon. Refreshed each time the
    /// editor opens, since what is offered can depend on the workspace's config and on the ribbon's contents.
    /// </summary>
    public ObservableCollection<RibbonCatalogEntry> AvailablePages { get; } = [];

    /// <summary>Open/closed state of the inline ribbon editor overlay.</summary>
    [ObservableProperty] private bool _isEditOpen;

    /// <summary>The open editor, or null. Created on open and dropped on close, so an editor nobody opens costs nothing.</summary>
    [ObservableProperty] private RibbonEditorViewModel? _editor;

    partial void OnIsEditOpenChanged(bool value)
    {
        if (!value) Editor = null;
    }

    /// <summary>
    /// Set by the host so the ribbon view can flash an item when a duplicate
    /// pin attempt is rejected.
    /// </summary>
    public Action<RibbonItem>? FlashItem { get; set; }

    public RibbonViewModel()
    {
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public Workspace? Workspace => _workspace;

    /// <summary>The runtime this ribbon belongs to — used only to resolve its pin handlers and the pages the editor offers.</summary>
    public void SetRuntime(WorkspaceRuntime? workspace) => _runtime = workspace;

    /// <summary>Shows another workspace's ribbon, following its changes instead of the last one's.</summary>
    public void SetWorkspace(Workspace? workspace)
    {
        if (ReferenceEquals(workspace, _workspace)) return;

        if (_workspace?.Ribbon is { } left) left.Changed -= OnRibbonChanged;
        _workspace = workspace;
        if (_workspace?.Ribbon is { } ribbon) ribbon.Changed += OnRibbonChanged;

        Reload();
    }

    /// <summary>
    /// Stops following the workspace's ribbon and the items. Must be called when the owning window closes: the workspace
    /// outlives its windows, so without this every closed window's ribbon stays rooted by the event and keeps reloading.
    /// </summary>
    public void Detach()
    {
        if (_workspace?.Ribbon is { } ribbon) ribbon.Changed -= OnRibbonChanged;
        _workspace = null;
        Items.CollectionChanged -= OnItemsCollectionChanged;
        foreach (var item in Items)
            item.PropertyChanged -= OnItemChanged;
    }

    /// <summary>The ribbon changed: taken up here unless this window made the change, whose items are already current.</summary>
    private void OnRibbonChanged(object? by)
    {
        if (ReferenceEquals(by, this)) return;
        _ui.Invoke(Reload);
    }

    private void Reload()
    {
        _reloading = true;
        try
        {
            foreach (var item in Items)
                item.PropertyChanged -= OnItemChanged;
            Items.Clear();
            foreach (var entry in _workspace?.Ribbon?.Entries ?? [])
            {
                var item = RibbonItem.FromEntry(entry);
                item.PropertyChanged += OnItemChanged;
                Items.Add(item);
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

        if (Items.Count > 0)
            Save();
        // Emptied: saving brings the shipped buttons back, which refills Items — after this change has been heard, as a
        // collection can't change while its change is told. Not while the editor is open, where the user may be mid-clear.
        else if (!IsEditOpen)
            _ui.BeginInvoke(Save);
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Save();

    [RelayCommand]
    public void Save()
    {
        if (_reloading) return;
        _workspace?.Ribbon?.Save(Items.Select(item => item.ToEntry()), by: this);
    }

    [RelayCommand]
    private void ToggleEdit()
    {
        if (IsEditOpen)
        {
            IsEditOpen = false;
            return;
        }

        RefreshAvailablePages();   // populate the catalog just before showing
        Editor = new RibbonEditorViewModel(
            Items, AvailablePages, () => RibbonLayoutService.LoadDefaults().Select(RibbonItem.FromEntry).ToList(),
            commit: ReplaceItems,
            close: () => IsEditOpen = false,
            findResource: key => Application.Current?.TryFindResource(key));
        IsEditOpen = true;
    }

    /// <summary>Replaces the ribbon with the editor's draft and saves once — not once per item, as adding them
    /// one by one to the live collection would.</summary>
    public void ReplaceItems(IReadOnlyList<RibbonItem> items)
    {
        _reloading = true;
        try
        {
            foreach (var item in Items)
                item.PropertyChanged -= OnItemChanged;
            Items.Clear();
            foreach (var item in items)
            {
                item.PropertyChanged += OnItemChanged;
                Items.Add(item);
            }
        }
        finally
        {
            _reloading = false;
        }

        Save();
    }

    private void RefreshAvailablePages()
    {
        AvailablePages.Clear();
        if (_runtime is null || _workspace?.Ribbon is not { } ribbon) return;

        foreach (var entry in ribbon.OfferedPages(_runtime))
            AvailablePages.Add(entry);
    }

    /// <summary>A tab dropped on the ribbon is pinned there; a button it already has flashes instead.</summary>
    [RelayCommand]
    public void Pin(TabPinRequest request)
    {
        if (_runtime is null || _workspace?.Ribbon is not { } ribbon) return;
        Flash(ribbon.PinTab(request.Tab, request.InsertIndex, _runtime));
    }

    /// <summary>The pin handlers of the runtime this ribbon belongs to: what may be dropped on it besides a tab. None before the
    /// ribbon is given its runtime.</summary>
    public IReadOnlyList<IRibbonPinHandler> PinHandlers
        => _runtime is { } runtime ? runtime.FeatureManager.GetRibbonPinHandlers(runtime) : [];

    /// <summary>Something other than a tab dropped on the ribbon is pinned by the handler that takes it; buttons it already has
    /// flash instead.</summary>
    [RelayCommand]
    public void PinFromHandler(RibbonPinRequest request)
    {
        if (_runtime is null || _workspace?.Ribbon is not { } ribbon) return;
        Flash(ribbon.PinDropped(request, _runtime));
    }

    private void Flash(IReadOnlyList<int> already)
    {
        foreach (var at in already)
            if (at >= 0 && at < Items.Count) FlashItem?.Invoke(Items[at]);
    }
}
