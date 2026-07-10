using System.Windows;

namespace Nexaflow.Features.ProductManager.Views;

/// <summary>
/// Carries a DataContext across a <see cref="System.Windows.Controls.Primitives.Popup"/> boundary. A Popup is
/// not in the visual tree, so <c>RelativeSource AncestorType</c> can't reach the page from inside one; a proxy
/// declared in the surrounding resources can, because resource lookup follows the logical tree.
/// </summary>
public sealed class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));
}
