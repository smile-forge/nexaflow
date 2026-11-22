using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Nexaflow.Interaction;

namespace Nexaflow.Visuals.Common.Dropping;

/// <summary>For a target that needs to know what of its own is under a drop: the row, the node, the area.</summary>
public static class DropHitTesting
{
    /// <summary>What of <paramref name="surface"/> is at <paramref name="at"/>, or null.</summary>
    public static DependencyObject? ElementUnder(this UIElement surface, DropPoint at)
        => surface.InputHitTest(new Point(at.X, at.Y)) as DependencyObject;

    /// <summary>The nearest <typeparamref name="T"/> that is <paramref name="element"/> or holds it.</summary>
    public static T? Holding<T>(DependencyObject? element) where T : DependencyObject
    {
        for (; element is not null; element = Parent(element))
            if (element is T found) return found;
        return null;
    }

    /// <summary>Whether <paramref name="element"/> is <paramref name="area"/> or in it.</summary>
    public static bool IsInside(DependencyObject? element, DependencyObject area)
    {
        for (; element is not null; element = Parent(element))
            if (ReferenceEquals(element, area)) return true;
        return false;
    }

    internal static DependencyObject? Parent(DependencyObject element) => element switch
    {
        Visual or Visual3D              => VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element),
        FrameworkContentElement content => content.Parent,
        ContentElement content          => ContentOperations.GetParent(content),
        _                               => LogicalTreeHelper.GetParent(element),
    };
}
