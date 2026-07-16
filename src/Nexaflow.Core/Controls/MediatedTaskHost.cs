using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Hosts one user-mediated background-task control in the shell chrome (the activity area's ItemsControl
/// binds a <see cref="MediatedTaskHost"/> per registration). When its <see cref="Registration"/> is set it
/// builds the control via the registration's factory — a fresh element per host, since a
/// <see cref="FrameworkElement"/> has a single visual parent — and surfaces the description as the tooltip.
/// </summary>
public sealed class MediatedTaskHost : ContentControl
{
    public static readonly DependencyProperty RegistrationProperty =
        DependencyProperty.Register(nameof(Registration), typeof(MediatedTaskRegistration),
            typeof(MediatedTaskHost), new PropertyMetadata(null, OnRegistrationChanged));

    public MediatedTaskRegistration? Registration
    {
        get => (MediatedTaskRegistration?)GetValue(RegistrationProperty);
        set => SetValue(RegistrationProperty, value);
    }

    private static void OnRegistrationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (MediatedTaskHost)d;
        if (e.NewValue is MediatedTaskRegistration reg)
        {
            host.Content = reg.CreateControl();
            host.ToolTip = string.IsNullOrWhiteSpace(reg.Description) ? null : reg.Description;
        }
        else
        {
            host.Content = null;
            host.ToolTip = null;
        }
    }
}
