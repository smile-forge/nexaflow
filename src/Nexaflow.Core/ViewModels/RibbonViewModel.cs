using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.FileSystem;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.Views;
using Nexaflow.Features.Common;

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
        foreach (var item in BuildDefaultItems())
            Items.Add(item);
    }

    public static IList<RibbonItem> BuildDefaultItems()
    {
        return
        [
            MakeButton("Projects", "🗂", "Projects"),
            new RibbonItem { Kind = RibbonItemKind.Separator },
            MakeButton("This PC", "🖥", PageKinds.FileSystem, new() { ["mode"] = "thispc" }),
            MakeButton("AI Chat", "💬", PageKinds.AiChat),
            MakeButton("Console", "⌨", "Console"),
            MakeButton("Scratchpad", "📌", "Scratchpad"),
            new RibbonItem { Kind = RibbonItemKind.Separator },
            MakeButton("Documents", "📄", PageKinds.FileSystem,
                new() { ["mode"] = "path", ["path"] = KnownFolderService.DocumentsPath }),
            MakeButton("Pictures", "🖼", PageKinds.FileSystem,
                new() { ["mode"] = "path", ["path"] = KnownFolderService.PicturesPath }),
            MakeButton("Videos", "🎬", PageKinds.FileSystem,
                new() { ["mode"] = "path", ["path"] = KnownFolderService.VideosPath }),
            MakeButton("Music", "🎵", PageKinds.FileSystem,
                new() { ["mode"] = "path", ["path"] = KnownFolderService.MusicPath }),
        ];
    }

    public static RibbonItem MakeButton(string label, string icon, string pageKind,
                                        Dictionary<string, string>? pageParams = null)
        => new()
        {
            Kind       = RibbonItemKind.Button,
            Label      = label,
            Icon       = icon,
            PageKind   = pageKind,
            PageParams = pageParams
        };

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
    /// Handle a tab drag-drop pin onto the ribbon: build a button from the tab's
    /// metadata, dedupe against existing buttons (flash on collision), and insert
    /// at the requested index. FileSystem tabs get their current path baked in.
    /// </summary>
    [RelayCommand]
    public void Pin(TabPinRequest request)
    {
        var (tab, insertIndex) = request;

        var label    = tab.Title;
        var icon     = tab.Icon;
        var pageKind = tab.PageKind;
        var pageParams = tab.PageParams is not null
            ? new Dictionary<string, string>(tab.PageParams)
            : null;

        if (string.IsNullOrEmpty(pageKind)) return;

        // For FileSystem tabs, root the button to its current path so it always
        // opens the exact location the user is browsing.
        if (tab.Content is FileSystemView fsPage)
        {
            var vm       = fsPage.ViewModel;
            var path     = vm.CurrentPath;
            var isThisPc = string.IsNullOrEmpty(path) || path == "This PC";
            icon = isThisPc ? "🖥" : "📁";
            if (!isThisPc)
            {
                vm.ResetRootToCurrentPath();
                pageParams = new() { ["mode"] = "path", ["path"] = path };
            }
            else
            {
                pageParams = new() { ["mode"] = "thispc" };
            }
        }

        var duplicate = Items.FirstOrDefault(r => r.Kind == RibbonItemKind.Button &&
                                                  r.PageKind == pageKind &&
                                                  ParamsEqual(r.PageParams, pageParams));
        if (duplicate is not null)
        {
            FlashItem?.Invoke(duplicate);
            return;
        }

        AddItem(MakeButton(label, icon, pageKind, pageParams), insertIndex);
    }

    /// <summary>
    /// Handle a handler-based pin: looks up the registered <see cref="IRibbonPinHandler"/>
    /// for the request's <see cref="RibbonPinRequest.ContentKind"/>, builds the button,
    /// deduplicates, and inserts.
    /// </summary>
    [RelayCommand]
    public void PinFromHandler(RibbonPinRequest request)
    {
        var handler = FeatureManager.Instance.GetRibbonPinHandler(request.ContentKind);
        if (handler is null) return;

        var result = handler.Pin(request.Payload, request.InsertIndex);
        if (result is null) return;

        var pageParams = result.PageParams;

        var duplicate = Items.FirstOrDefault(r =>
            r.Kind == RibbonItemKind.Button &&
            r.PageKind == request.ContentKind &&
            ParamsEqual(r.PageParams, pageParams));
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
            PageKind    = request.ContentKind,
            PageParams  = pageParams
        }, request.InsertIndex);
    }

    private static bool ParamsEqual(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        return a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }
}
