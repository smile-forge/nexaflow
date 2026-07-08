using Nexaflow.Visuals.Common.Dialogs;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// The shared modal confirmation dialog: scrim + centered card + title/prompt + confirm/cancel.
/// Drop one instance into a view's root grid (set <c>Panel.ZIndex</c> there) and bind
/// <see cref="Request"/> to a <see cref="ConfirmationRequest"/>? property on the view-model —
/// the dialog shows itself while the request is open and hides when it's answered or null.
/// </summary>
public partial class ConfirmationDialog : UserControl
{
    public static readonly DependencyProperty RequestProperty = DependencyProperty.Register(
        nameof(Request), typeof(ConfirmationRequest), typeof(ConfirmationDialog));

    public ConfirmationRequest? Request
    {
        get => (ConfirmationRequest?)GetValue(RequestProperty);
        set => SetValue(RequestProperty, value);
    }

    public ConfirmationDialog() => InitializeComponent();
}
