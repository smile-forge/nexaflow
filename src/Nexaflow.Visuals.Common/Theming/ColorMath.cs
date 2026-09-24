using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Theming;

/// <summary>A colour as hue (0–360), saturation and value (0–1) — the axes a picker's square and strip drag along.</summary>
public readonly record struct HsvColor(double H, double S, double V)
{
    public static HsvColor FromColor(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max   = Math.Max(r, Math.Max(g, b));
        var min   = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r)      h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * (((b - r) / delta) + 2);
            else               h = 60 * (((r - g) / delta) + 4);
        }
        if (h < 0) h += 360;

        return new HsvColor(h, max == 0 ? 0 : delta / max, max);
    }

    public Color ToColor(byte alpha = 255)
    {
        var h = ((H % 360) + 360) % 360;
        var s = Math.Clamp(S, 0, 1);
        var v = Math.Clamp(V, 0, 1);

        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        var m = v - c;

        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromArgb(alpha, ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double unit) => (byte)Math.Round(Math.Clamp(unit, 0, 1) * 255);
}

/// <summary>WCAG contrast between two colours, for warning when a label would be hard to read on its background.</summary>
public static class ColorContrast
{
    /// <summary>The ratio from 1 (identical) to 21 (black on white). Alpha is ignored — both are taken as opaque.</summary>
    public static double Ratio(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Below this, large text (a ribbon label, an icon) stops being comfortably readable.</summary>
    public const double MinimumForLargeText = 3.0;

    private static double Luminance(Color c)
        => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(byte value)
    {
        var s = value / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
