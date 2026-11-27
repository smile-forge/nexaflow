using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Microsoft.Extensions.Logging;

namespace Nexaflow.AvaloniaShell.Theming;

/// <summary>A theme's backdrop for a region, as its dictionary names it: the scene's type and the properties set on it.</summary>
/// <param name="TypeName">The scene's class name, which an Avalonia scene of this app carries too.</param>
/// <param name="Properties">Each property the theme sets, by name, as written.</param>
/// <param name="Animated">Drawn under <c>Scene.{Region}</c>, so it animates; a <c>StillScene.{Region}</c> draws once.</param>
public sealed record SceneDefinition(string TypeName, IReadOnlyDictionary<string, string> Properties, bool Animated);

/// <summary>
/// One of the shell's themes, read from the very dictionaries the WPF app applies — <c>Colors.{Theme}.xaml</c> (the palette),
/// <c>Tokens.xaml</c> (the region tokens) and <c>Theme.{Theme}.xaml</c> (the theme's overrides and scenes), in that order, a later
/// key winning — so the two apps share one definition of every theme. The dictionaries are plain data: colours, solid and
/// linear-gradient brushes, lengths, and a template naming a region's scene, each read into its Avalonia equivalent.
/// <para>
/// The files are Nexaflow.Core's own, embedded here as they are (the csproj links them), so a theme edited there is edited here.
/// </para>
/// </summary>
public sealed class ShellTheme
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private ShellTheme(ThemeOption option, ResourceDictionary resources, ThemeVariant variant)
    {
        Option    = option;
        Resources = resources;
        Variant   = variant;
    }

    /// <summary>Which theme this is.</summary>
    public ThemeOption Option { get; }

    /// <summary>Every key the theme defines, as Avalonia resources: what the shell's views bind with <c>{DynamicResource}</c>.</summary>
    public ResourceDictionary Resources { get; }

    /// <summary>The Fluent variant the theme stands on: light where its text is dark, dark otherwise — read from its own palette.</summary>
    public ThemeVariant Variant { get; }

    /// <summary>Reads <paramref name="option"/>'s dictionaries; a key one of them writes in a form this reader does not know is put on
    /// record and left out, rather than guessed at.</summary>
    public static ShellTheme Load(ThemeOption option, ILogger log)
    {
        var resources = new ResourceDictionary();
        foreach (var file in new[] { $"Colors.{option}.xaml", "Tokens.xaml", $"Theme.{option}.xaml" })
            if (Read(file) is { } dictionary)
                Merge(dictionary, resources, file, log);

        var text = resources.TryGetValue("TextColor", out var value) && value is Color color ? color : Colors.White;
        return new ShellTheme(option, resources, Luminance(text) < 0.5 ? ThemeVariant.Light : ThemeVariant.Dark);
    }

    /// <summary>Makes this the theme every window draws with, in place of <paramref name="replacing"/>, and Fluent follows its variant.
    /// Views bind the keys dynamically, so an open window takes the new theme as it stands.</summary>
    public void Apply(Application app, ShellTheme? replacing)
    {
        if (replacing is not null) app.Resources.MergedDictionaries.Remove(replacing.Resources);
        app.Resources.MergedDictionaries.Add(Resources);
        app.RequestedThemeVariant = Variant;
    }

    private static XDocument? Read(string file)
    {
        using var stream = typeof(ShellTheme).Assembly.GetManifestResourceStream($"Themes.{file}");
        return stream is null ? null : XDocument.Load(stream);
    }

    private static void Merge(XDocument dictionary, ResourceDictionary into, string file, ILogger log)
    {
        foreach (var element in dictionary.Root!.Elements())
        {
            if (element.Attribute(Xaml + "Key")?.Value is not { } key) continue;
            object? value = element.Name.LocalName switch
            {
                "Color"               => ColorOf(element.Value.Trim(), into),
                "SolidColorBrush"     => SolidOf(element, into),
                "LinearGradientBrush" => GradientOf(element, into),
                "GridLength"          => GridLength.Parse(element.Value.Trim()),
                "DataTemplate"        => SceneOf(element, key),
                _                     => null,
            };
            if (value is null)
            {
                log.LogWarning("theme file {File}: {Key} is a {Kind}, which the Avalonia theme reader does not read", file, key, element.Name.LocalName);
                continue;
            }
            into[key] = value;
        }
    }

    /// <summary>A colour written out, named, or taken from one the dictionaries already hold (<c>{StaticResource Key}</c>).</summary>
    private static Color ColorOf(string written, ResourceDictionary known)
    {
        if (written.StartsWith("{StaticResource ", StringComparison.Ordinal))
        {
            var key = written["{StaticResource ".Length..^1].Trim();
            return known.TryGetValue(key, out var value) && value is Color color
                ? color
                : throw new InvalidDataException($"The theme names colour '{key}' before defining it.");
        }
        return Color.Parse(written);
    }

    private static IImmutableSolidColorBrush SolidOf(XElement element, ResourceDictionary known)
    {
        var opacity = element.Attribute("Opacity") is { } written ? double.Parse(written.Value, CultureInfo.InvariantCulture) : 1;
        return new ImmutableSolidColorBrush(ColorOf(element.Attribute("Color")!.Value, known), opacity);
    }

    private static IImmutableBrush GradientOf(XElement element, ResourceDictionary known)
    {
        // WPF maps a gradient's points relative to the bounding box unless told otherwise, and these never say otherwise.
        static RelativePoint Point(string? written, RelativePoint fallback)
            => written is null ? fallback : new RelativePoint(Avalonia.Point.Parse(written), RelativeUnit.Relative);

        var stops = element.Elements()
            .Where(child => child.Name.LocalName == "GradientStop")
            .Select(stop => new ImmutableGradientStop(
                double.Parse(stop.Attribute("Offset")?.Value ?? "0", CultureInfo.InvariantCulture),
                ColorOf(stop.Attribute("Color")!.Value, known)))
            .ToList();
        return new ImmutableLinearGradientBrush(stops, 1,
            startPoint: Point(element.Attribute("StartPoint")?.Value, RelativePoint.TopLeft),
            endPoint:   Point(element.Attribute("EndPoint")?.Value, RelativePoint.BottomRight));
    }

    /// <summary>A <c>Scene.{Region}</c> or <c>StillScene.{Region}</c> template: the one element it holds is the scene.</summary>
    private static SceneDefinition? SceneOf(XElement template, string key)
    {
        var animated = key.StartsWith("Scene.", StringComparison.Ordinal);
        if (!animated && !key.StartsWith("StillScene.", StringComparison.Ordinal)) return null;
        if (template.Elements().SingleOrDefault() is not { } scene) return null;
        return new SceneDefinition(scene.Name.LocalName,
                                   scene.Attributes().Where(a => !a.IsNamespaceDeclaration).ToDictionary(a => a.Name.LocalName, a => a.Value),
                                   animated);
    }

    /// <summary>Relative luminance (sRGB), 0 for black to 1 for white.</summary>
    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
