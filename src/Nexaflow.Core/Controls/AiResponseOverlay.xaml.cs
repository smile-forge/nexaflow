using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Nexaflow.Core.ViewModels;

namespace Nexaflow.Core.Controls;

public partial class AiResponseOverlay : UserControl
{
    private ShellViewModel? _vm;

    public AiResponseOverlay()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as ShellViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.AiResponseOverlayOpen)
            && _vm?.AiResponseOverlayOpen == true)
            Dispatcher.Invoke(PlaySlideUp);
    }

    private void PlaySlideUp()
    {
        if (Panel.ActualHeight <= 0) return;
        var anim = new DoubleAnimation
        {
            From           = Panel.ActualHeight,
            To             = 0,
            Duration       = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        SlideXform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, anim);
    }
}
