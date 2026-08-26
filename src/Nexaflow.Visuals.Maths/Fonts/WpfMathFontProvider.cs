using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using XamlMath.Fonts;

namespace WpfMath.Fonts;

/// <summary>A font provider implementation specifically for the WpfMath assembly.</summary>
internal sealed class WpfMathFontProvider : IFontProvider
{
    private WpfMathFontProvider() {}

    public static readonly WpfMathFontProvider Instance = new();

    static WpfMathFontProvider()
    {
        // If the application isn't WPF, pack scheme doesn't get registered.
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (Application.ResourceAssembly == null)
        {
            Application.ResourceAssembly = Assembly.GetExecutingAssembly();
            if (!UriParser.IsKnownScheme("pack"))
                UriParser.Register(new GenericUriParser(GenericUriParserOptions.GenericAuthority), "pack", -1);
        }
    }

    private const string FontsDirectory = "Fonts/";

    /// <summary>
    /// The assembly the faces are resources of, asked for rather than written down. It was the literal
    /// "WpfMath" until this code was ingested and the assembly was renamed, at which point every glyph
    /// stopped resolving and every formula came back as "will not typeset" — because the one thing that
    /// reports it is a catch-all that cannot tell a missing font from a formula it cannot read.
    /// </summary>
    private static readonly string ResourceAssembly =
        typeof(WpfMathFontProvider).Assembly.GetName().Name ?? "Nexaflow.Visuals.Maths";

    public IFontTypeface ReadFontFile(string fontFileName)
    {
        var fontUri = new Uri(
            $"pack://application:,,,/{ResourceAssembly};component/{FontsDirectory}{fontFileName}");
        return new WpfGlyphTypeface(new GlyphTypeface(fontUri));
    }
}
