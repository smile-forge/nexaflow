using System.Windows;
using Nexaflow.Interaction;

namespace Nexaflow.Visuals.Common.Dropping;

/// <summary>
/// An area of a view given its drop target in markup: <c>dropping:DropSurface.Target="{Binding}"</c>. For a target that
/// is not the page's whole — a banner, a box — or that is a view-model rather than an element.
/// </summary>
public static class DropSurface
{
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.RegisterAttached("Target", typeof(IDropTarget), typeof(DropSurface), new PropertyMetadata(null));

    public static IDropTarget? GetTarget(DependencyObject area) => (IDropTarget?)area.GetValue(TargetProperty);

    public static void SetTarget(DependencyObject area, IDropTarget? target) => area.SetValue(TargetProperty, target);
}
