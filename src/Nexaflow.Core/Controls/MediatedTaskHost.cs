using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Hosts one user-mediated background task in the shell chrome (the activity area's ItemsControl binds a
/// <see cref="MediatedTaskHost"/> per registration): draws the registration's model with the view marked as drawing it, a
/// fresh view per host since a window shows its own, and surfaces the description as the tooltip.
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
        var reg  = e.NewValue as MediatedTaskRegistration;
        host.Content = reg is null ? null : new ModelHost { Model = reg.Model };
        host.ToolTip = string.IsNullOrWhiteSpace(reg?.Description) ? null : reg.Description;
    }
}
