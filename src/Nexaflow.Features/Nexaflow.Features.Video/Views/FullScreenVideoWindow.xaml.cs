using System.Windows;
using System.Windows.Input;
using Nexaflow.Features.Video.ViewModels;

namespace Nexaflow.Features.Video.Views;

/// <summary>
/// Borderless, maximized fullscreen view. It binds to the same <see cref="VideoViewModel"/> as the tab and
/// shows the same <see cref="VideoViewModel.VideoFrame"/> bitmap — there is no player or surface handover,
/// so playback is uninterrupted and the picture always appears. Esc leaves fullscreen; Space toggles
/// play/pause; a click on the picture also toggles.
/// </summary>
public partial class FullScreenVideoWindow : Window
{
    private readonly VideoViewModel _vm;

    public FullScreenVideoWindow(VideoViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;
        Loaded += (_, _) => Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _vm.ExitFullScreen();
                e.Handled = true;
                break;
            case Key.Space:
                if (_vm.TogglePlayPauseCommand.CanExecute(null))
                    _vm.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }
}
