using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Features.Video.ViewModels;

namespace Nexaflow.Features.Video.Views;

/// <summary>
/// Hosts the video (a WPF <c>Image</c> fed by <see cref="VideoViewModel.VideoFrame"/>), the transport bar,
/// the codec info panel, and the scene strip. A per-frame <see cref="CompositionTarget.Rendering"/> tick
/// blits the latest decoded frame into the bitmap. Fullscreen is just a second window bound to the same
/// <see cref="VideoViewModel.VideoFrame"/> — no surface swapping, no reparenting. Tab-switch pauses
/// playback (Loaded/Unloaded → VM OnActivated/OnDeactivated).
/// </summary>
public partial class VideoView : UserControl, IPageView
{
    public VideoViewModel ViewModel { get; }

    private FullScreenVideoWindow? _fs;
    private bool _renderHooked;

    public VideoView(VideoViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    IPageViewModel? IPageView.ViewModel => ViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnActivated();
        if (!ViewModel.IsLoaded)
            await ViewModel.LoadAsync();

        if (!_renderHooked) { CompositionTarget.Rendering += OnRendering; _renderHooked = true; }
        Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_renderHooked) { CompositionTarget.Rendering -= OnRendering; _renderHooked = false; }
        ViewModel.OnDeactivated();
        if (_fs is not null) CloseFullScreen();
    }

    private void OnRendering(object? sender, EventArgs e) => ViewModel.RenderTick();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VideoViewModel.IsFullScreen)) return;
        if (ViewModel.IsFullScreen) OpenFullScreen();
        else CloseFullScreen();
    }

    // ── Fullscreen: a second window bound to the same VideoFrame bitmap ──

    private void OpenFullScreen()
    {
        if (_fs is not null) return;
        _fs = new FullScreenVideoWindow(ViewModel) { Owner = Window.GetWindow(this) };
        _fs.Closed += OnFullScreenClosed;
        _fs.Show();
        _fs.Activate();
    }

    private void OnFullScreenClosed(object? sender, EventArgs e)
    {
        if (ViewModel.IsFullScreen) ViewModel.ExitFullScreen();   // Alt+F4 → keep VM state in sync
    }

    private void CloseFullScreen()
    {
        if (_fs is not { } w) return;
        _fs = null;
        w.Closed -= OnFullScreenClosed;
        w.Close();
    }
}
