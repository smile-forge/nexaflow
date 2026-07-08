using Nexaflow.Visuals.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nexaflow.Features.Audio.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Audio.Views;

public partial class AudioView : UserControl, IPageView
{
    private const double DrawerWidth = 340;
    private const double PlaylistWidth = 300;
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(200));

    private readonly AudioViewModel _vm;
    private bool _loadedOnce;

    private Point _dragStart;
    private PlaylistItemViewModel? _dragItem;

    public AudioView(AudioViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        Waveform.SeekRequested += (_, fraction) => _vm.SeekToFraction(fraction);
        _vm.Lyrics.PropertyChanged += OnLyricsPropertyChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Snap the drawers to the VM's initial state (no animation on first show).
        SnapDrawer(Drawer, _vm.IsPanelOpen, DrawerWidth);
        SnapDrawer(PlaylistDrawer, _vm.IsPlaylistOpen, PlaylistWidth);

        // Playlist drag-to-reorder + double-click-to-play.
        PlaylistList.PreviewMouseLeftButtonDown += PlaylistList_PreviewMouseLeftButtonDown;
        PlaylistList.MouseMove += PlaylistList_MouseMove;
        PlaylistList.Drop += PlaylistList_Drop;
        PlaylistList.MouseDoubleClick += PlaylistList_MouseDoubleClick;

        // Loaded/Unloaded is the active-tab signal (the shell hosts the active page in one
        // ContentPresenter). Deactivate pauses playback (resumed on return); the engine is torn down
        // via Page.Closed (wired in the registration), not here. Minimize doesn't unload the tree —
        // the watcher suspends only the 33 ms spectrum repaint (playback keeps running).
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _vm.OnDeactivated();
            _minimizeWatcher?.Dispose();
            _minimizeWatcher = null;
        };
    }

    private WindowMinimizeWatcher? _minimizeWatcher;

    IPageViewModel? IPageView.ViewModel => _vm;

    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        var paths = pageParams.GetValueOrDefault("paths")?
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (paths is not { Count: > 0 }) return;

        int index = 0;
        if (pageParams.GetValueOrDefault("index") is { } raw && int.TryParse(raw, out var i))
            index = i;

        _ = _vm.ReinitializeAsync(paths, index);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.OnActivated();
        _minimizeWatcher ??= WindowMinimizeWatcher.Attach(this, _vm.SetRenderSuspended);
        if (_loadedOnce) return;
        _loadedOnce = true;
        await _vm.LoadAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Opening/closing animates; switching Tags↔Lyrics while open is an instant content swap
        // (driven by the ShowTagsPanel / ShowLyricsPanel visibility bindings).
        if (e.PropertyName == nameof(AudioViewModel.IsPanelOpen))
            AnimateDrawer(Drawer, _vm.IsPanelOpen, DrawerWidth, () => _vm.IsPanelOpen);
        else if (e.PropertyName == nameof(AudioViewModel.IsPlaylistOpen))
            AnimateDrawer(PlaylistDrawer, _vm.IsPlaylistOpen, PlaylistWidth, () => _vm.IsPlaylistOpen);
    }

    private static void SnapDrawer(FrameworkElement drawer, bool open, double width)
    {
        drawer.Width = open ? width : 0;
        drawer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void AnimateDrawer(FrameworkElement drawer, bool open, double width, Func<bool> stillOpen)
    {
        if (open) drawer.Visibility = Visibility.Visible;

        var anim = new DoubleAnimation(open ? width : 0, SlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (!open)
            anim.Completed += (_, _) => { if (!stillOpen()) drawer.Visibility = Visibility.Collapsed; };

        drawer.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    // ── Playlist drag-reorder + double-click ─────────────────────────────────

    private void PlaylistList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = ItemUnder(e.OriginalSource);
    }

    private void PlaylistList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null) return;

        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(PlaylistList, item, DragDropEffects.Move);
    }

    private void PlaylistList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PlaylistItemViewModel)) is not PlaylistItemViewModel dragged) return;

        int from = _vm.Playlist.IndexOf(dragged);
        var target = ItemUnder(e.OriginalSource);
        int to = target is null ? _vm.Playlist.Count - 1 : _vm.Playlist.IndexOf(target);
        if (from >= 0 && to >= 0) _vm.MovePlaylistItem(from, to);
    }

    private async void PlaylistList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder(e.OriginalSource) is { } item)
            await _vm.PlayAtAsync(_vm.Playlist.IndexOf(item));
    }

    private static PlaylistItemViewModel? ItemUnder(object source)
    {
        var dep = source as DependencyObject;
        while (dep is not null && (dep as FrameworkElement)?.DataContext is not PlaylistItemViewModel)
            dep = VisualTreeHelper.GetParent(dep);
        return (dep as FrameworkElement)?.DataContext as PlaylistItemViewModel;
    }

    private void OnLyricsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LyricsViewModel.ActiveIndex)) return;

        int idx = _vm.Lyrics.ActiveIndex;
        if (idx < 0 || idx >= LyricsList.Items.Count) return;

        // Defer so the container is realized before scrolling (the list virtualises).
        Dispatcher.BeginInvoke(DispatcherPriority.Background,
            () => LyricsList.ScrollIntoView(LyricsList.Items[idx]));
    }
}
