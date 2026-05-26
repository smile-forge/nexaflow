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
/// Owns the ribbon's <see cref="Items"/> collection and the per-context load/save lifecycle.
/// Switching the <see cref="WorkContext"/> saves the outgoing context's items and loads the
/// incoming context's items (or seeds defaults). Edits to items auto-save.
/// </summary>
public partial class RibbonViewModel : ObservableObject
{
    private WorkContext? _workContext;
    private bool _switchingContext;

    public ObservableCollection<RibbonItem> Items { get; } = [];

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

    public WorkContext? WorkContext => _workContext;

    /// <summary>Swap to a new work context: save outgoing items, load incoming.</summary>
    public void SetWorkContext(WorkContext? newContext)
    {
        if (ReferenceEquals(newContext, _workContext)) return;

        _switchingContext = true;
        try
        {
            _workContext?.RibbonService?.Save(Items);

            foreach (var item in Items)
                item.PropertyChanged -= OnItemChanged;

            Items.Clear();
            _workContext = newContext;
            LoadOrBuildItems();

            foreach (var item in Items)
            {
                item.PropertyChanged -= OnItemChanged;
                item.PropertyChanged += OnItemChanged;
            }
        }
        finally
        {
            _switchingContext = false;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_switchingContext) return;

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
    public void Save() => _workContext?.RibbonService?.Save(Items);

    private void LoadOrBuildItems()
    {
        var saved = _workContext?.RibbonService?.Load();
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
    private void ToggleEdit() => IsEditOpen = !IsEditOpen;

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
        if (_workContext is not null)
        {
            var handler = FeatureManager.Instance.GetRibbonPinHandler(tab.PageKind, _workContext);
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
        if (_workContext is null) return;
        var handler = FeatureManager.Instance.GetRibbonPinHandler(request.ContentKind, _workContext);
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
