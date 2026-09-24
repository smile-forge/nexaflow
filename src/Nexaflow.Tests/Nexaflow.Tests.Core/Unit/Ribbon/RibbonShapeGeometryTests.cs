using System;
using System.Windows;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Ribbon;

/// <summary>
/// Every shape must draw inside its button with room for its outline, and a badge — the same for every button that
/// wears it — must be built once and shared, since the ribbon draws it in every window.
/// </summary>
[TestClass]
[CoversNode("ribbon-button-shape")]
public class RibbonShapeGeometryTests
{
    [TestMethod]
    public void Frames_StayInsideTheButton()
    {
        var size = new Size(62, 64);
        foreach (RibbonButtonShape shape in Enum.GetValues(typeof(RibbonButtonShape)))
            foreach (var compact in new[] { false, true })
                foreach (var stroke in new[] { 0d, 3d })
                {
                    var bounds = RibbonShapeGeometry.Frame(shape, size, compact, stroke).Bounds;
                    var what   = $"{shape} compact={compact} stroke={stroke}: {bounds}";
                    Assert.IsFalse(bounds.IsEmpty, what);
                    Assert.IsTrue(bounds.Left >= stroke / 2 - 1e-9 && bounds.Top >= stroke / 2 - 1e-9, what);
                    Assert.IsTrue(bounds.Right <= size.Width - stroke / 2 + 1e-9, what);
                    Assert.IsTrue(bounds.Bottom <= size.Height - stroke / 2 + 1e-9, what);
                }
    }

    [TestMethod]
    public void Badges_AreTheIconShapes()
    {
        Assert.IsTrue(RibbonShapeGeometry.IsBadge(RibbonButtonShape.Circle));
        Assert.IsTrue(RibbonShapeGeometry.IsBadge(RibbonButtonShape.Squircle));
        Assert.IsTrue(RibbonShapeGeometry.IsBadge(RibbonButtonShape.Hexagon));
        Assert.IsFalse(RibbonShapeGeometry.IsBadge(RibbonButtonShape.Pill));
        Assert.IsNull(RibbonShapeGeometry.Badge(RibbonButtonShape.Rounded, compact: false, RibbonBorderWeight.None));
    }

    [TestMethod]
    public void Badges_AreSharedAndFrozen()
    {
        var a = RibbonShapeGeometry.Badge(RibbonButtonShape.Squircle, compact: true, RibbonBorderWeight.Thin);
        var b = RibbonShapeGeometry.Badge(RibbonButtonShape.Squircle, compact: true, RibbonBorderWeight.Thin);
        Assert.AreSame(a, b);
        Assert.IsTrue(a!.IsFrozen);
    }

    [TestMethod]
    public void Badges_FitTheirSize()
    {
        foreach (var shape in new[] { RibbonButtonShape.Circle, RibbonButtonShape.Squircle, RibbonButtonShape.Hexagon })
        {
            var badge = RibbonShapeGeometry.Badge(shape, compact: false, RibbonBorderWeight.Thick)!.Bounds;
            Assert.IsTrue(badge.Left >= 0 && badge.Top >= 0, $"{shape}: {badge}");
            Assert.IsTrue(badge.Right <= RibbonShapeGeometry.FullBadge + 1e-9 && badge.Bottom <= RibbonShapeGeometry.FullBadge + 1e-9, $"{shape}: {badge}");
        }
    }
}
