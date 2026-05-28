using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Markdig;
using Nexaflow.Core.ViewModels;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Core.Controls;

public partial class AiResponseOverlay : UserControl
{
    private ShellViewModel? _vm;

    public AiResponseOverlay()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged   += (_, _) => RebuildBody();
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
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.AiResponseText):
                Dispatcher.Invoke(RebuildBody);
                break;
            case nameof(ShellViewModel.AiResponseOverlayOpen):
                if (_vm?.AiResponseOverlayOpen == true)
                    Dispatcher.Invoke(PlaySlideUp);
                break;
        }
    }

    private void RebuildBody()
    {
        MarkdownBody.Children.Clear();
        var text = _vm?.AiResponseText;
        if (string.IsNullOrWhiteSpace(text)) return;

        var doc = Markdig.Markdown.Parse(text, MarkdownPipelineFactory.Default);
        foreach (var block in doc)
            MarkdownBody.Children.Add(BlockRenderer.Render(block, text));
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
