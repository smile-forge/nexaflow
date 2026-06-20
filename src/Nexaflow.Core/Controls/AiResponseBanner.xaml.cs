using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Nexaflow.Core.Services;

namespace Nexaflow.Core.Controls;

/// <summary>
/// The in-page AI response surface: a dissolvable banner the shell injects into an
/// <see cref="Nexaflow.Features.Common.IChatEngagement"/> page's host. Bound to an
/// <see cref="InlineAiResponseHandler"/>. It slides up to open (pushing the page content up) and dissolves
/// to close (the page re-expands into the freed space) by animating its height. When a final answer is
/// taller than the banner can show, it asks the handler to escalate to the modal overlay (only while the
/// page is the active tab).
/// </summary>
public partial class AiResponseBanner : UserControl
{
    private static readonly Duration OpenDur  = TimeSpan.FromMilliseconds(180);
    private static readonly Duration CloseDur = TimeSpan.FromMilliseconds(200);

    private InlineAiResponseHandler? _vm;

    // True only once the banner has settled at its natural height — guards the overflow check below from
    // firing mid-slide, when the body is transiently shorter than its content.
    private bool _settled;

    public AiResponseBanner()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook(DataContext as InlineAiResponseHandler);
    }

    private void Hook(InlineAiResponseHandler? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            ApplyVisible(_vm.BannerVisible, animate: false);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InlineAiResponseHandler.BannerVisible))
            ApplyVisible(_vm!.BannerVisible, animate: true);
    }

    private void ApplyVisible(bool visible, bool animate)
    {
        Root.BeginAnimation(HeightProperty, null);
        Root.BeginAnimation(OpacityProperty, null);
        _settled = false;

        if (visible)
        {
            Root.Visibility = Visibility.Visible;
            if (!animate) { Root.Height = double.NaN; Root.Opacity = 1; _settled = true; return; }

            // Lay out at natural height (invisible), read it, then slide up from 0 to it.
            Root.Height  = double.NaN;
            Root.Opacity = 0;
            Root.UpdateLayout();
            var target = Root.ActualHeight;
            if (target <= 0) { Root.Opacity = 1; _settled = true; return; }

            var slide = new DoubleAnimation(0, target, OpenDur)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            slide.Completed += (_, _) =>
            {
                Root.BeginAnimation(HeightProperty, null);
                Root.Height = double.NaN;   // track content thereafter (answer fills in / grows)
                _settled = true;
            };
            Root.BeginAnimation(HeightProperty, slide);
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, OpenDur));
        }
        else
        {
            var from = Root.ActualHeight;
            if (!animate || from <= 0) { Root.Visibility = Visibility.Collapsed; return; }

            var shrink = new DoubleAnimation(from, 0, CloseDur)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            shrink.Completed += (_, _) =>
            {
                Root.BeginAnimation(HeightProperty, null);
                Root.Height     = double.NaN;
                Root.Visibility = Visibility.Collapsed;
                Root.Opacity    = 1;
            };
            Root.Height = from;
            Root.BeginAnimation(HeightProperty, shrink);
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(Root.Opacity, 0, CloseDur));
        }
    }

    private void BodyScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // The content overflows the banner exactly when there is something to scroll. For a finished
        // answer that's too tall, move it to the roomier modal overlay (no-op on a background tab).
        if (_settled
            && DataContext is InlineAiResponseHandler h
            && h.AiOverlayIsMessage
            && h.CanEscalate
            && BodyScroller.ScrollableHeight > 0.5)
        {
            h.Escalate();
        }
    }
}
