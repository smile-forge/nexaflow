using System.Windows;
using System.Windows.Media;
using Nexaflow.Core.Models;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Core.Controls;

/// <summary>The three colours a ribbon button carries.</summary>
public enum RibbonColourSlot { Foreground, Background, Border }

/// <summary>What each colour slot binds to, and what "theme default" looks like in it — shared by the ribbon's quick
/// recolour and the editor so both offer the same thing.</summary>
public static class RibbonColourSlots
{
    public static string PropertyName(RibbonColourSlot slot) => slot switch
    {
        RibbonColourSlot.Background => nameof(RibbonItem.Background),
        RibbonColourSlot.Border     => nameof(RibbonItem.BorderColor),
        _                           => nameof(RibbonItem.Foreground),
    };

    /// <summary>The colour the theme paints the slot with, so a picker can start from it and preview it.</summary>
    public static Color ThemeDefault(RibbonColourSlot slot, Func<object, object?> findResource)
    {
        var key = slot switch
        {
            RibbonColourSlot.Background => "Ribbon.ButtonBg",
            RibbonColourSlot.Border     => "AccentBrush",
            _                           => "TextMutedBrush",
        };
        var colour = findResource(key) is SolidColorBrush b ? b.Color : Colors.Gray;
        // A transparent resting fill (the Dark ribbon's) would preview as nothing; show the strip it sits on.
        if (colour.A == 0 && findResource("Chrome.Bg") is SolidColorBrush strip) colour = strip.Color;
        return colour;
    }

    /// <summary>
    /// What "theme default" icon and text become on a button with its own background: whichever of the theme's text
    /// and page colours reads better on it, so a pale or dark fill never swallows the label.
    /// </summary>
    public static Color ReadableOn(Color background, Func<object, object?> findResource)
    {
        var light = findResource("TextBrush") is SolidColorBrush t ? t.Color : Colors.White;
        var dark  = findResource("BgBrush")   is SolidColorBrush b ? b.Color : Colors.Black;
        return ColorContrast.Ratio(light, background) >= ColorContrast.Ratio(dark, background) ? light : dark;
    }

    /// <summary>The icon and text colour a button actually shows: its own, else readable on its own background, else
    /// the theme's. Null for the theme's, which the button's style supplies.</summary>
    public static Color? EffectiveForeground(RibbonItem item, Func<object, object?> findResource)
    {
        if (item.Foreground.Resolve() is { } own) return own;
        if (RibbonShapeGeometry.IsBadge(item.Shape) || item.Background.Resolve() is not { A: > 0x80 } fill) return null;
        return ReadableOn(fill, findResource);
    }
}

/// <summary>The shapes in the order they are offered, each with its name in the active language.</summary>
public static class RibbonShapeNames
{
    public static IReadOnlyList<(RibbonButtonShape Shape, string Name)> All() =>
    [
        (RibbonButtonShape.Standard, Str.Get("Shell.Ribbon.Shape.Standard")),
        (RibbonButtonShape.Rounded,  Str.Get("Shell.Ribbon.Shape.Rounded")),
        (RibbonButtonShape.Pill,     Str.Get("Shell.Ribbon.Shape.Pill")),
        (RibbonButtonShape.Leaf,     Str.Get("Shell.Ribbon.Shape.Leaf")),
        (RibbonButtonShape.Circle,   Str.Get("Shell.Ribbon.Shape.Circle")),
        (RibbonButtonShape.Squircle, Str.Get("Shell.Ribbon.Shape.Squircle")),
        (RibbonButtonShape.Hexagon,  Str.Get("Shell.Ribbon.Shape.Hexagon")),
    ];
}
