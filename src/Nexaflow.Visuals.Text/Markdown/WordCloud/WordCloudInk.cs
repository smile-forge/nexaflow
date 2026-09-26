using System;
using System.Collections.Generic;
using System.Windows.Media;
using Nexaflow.Markdown.Settings;
using Nexaflow.Markdown.WordCloud;

namespace Nexaflow.Visuals.Text.Markdown.WordCloud;

/// <summary>
/// What the words of a cloud are drawn in — the one part of a block this assembly has to settle, because it
/// is the only part that depends on the theme.
///
/// <para>
/// Four answers, and the default is the interesting one. <c>theme</c> takes the palette's series colours in
/// turn, which is what every other chart in the renderer is drawn in: a cloud beside a pie chart is then the
/// same document rather than two, and both follow the theme when it changes. <c>random-dark</c> and
/// <c>random-light</c> are <c>wordcloud2.js</c>'s, for a cloud that wants to look like scattered ink rather
/// than like data; colours written out are taken in turn as the series would be.
/// </para>
/// </summary>
internal sealed class WordCloudInk
{
    private readonly IReadOnlyList<Brush>? _cycle;
    private readonly WordCloudRandom? _random;
    private readonly bool _dark;

    private WordCloudInk(IReadOnlyList<Brush>? cycle, WordCloudRandom? random, bool dark)
    {
        _cycle = cycle;
        _random = random;
        _dark = dark;
    }

    /// <summary>What the stages said the words are drawn in, as the brushes they are drawn with.</summary>
    public static WordCloudInk Of(WordCloudColours colours, StyleFormat palette, WordCloudRandom random) => colours.Colouring switch
    {
        WordCloudColouring.Themed => new WordCloudInk(palette.Series, null, dark: false),
        WordCloudColouring.RandomDark => new WordCloudInk(null, random, dark: true),
        WordCloudColouring.RandomLight => new WordCloudInk(null, random, dark: false),
        _ => new WordCloudInk([.. colours.Written.Select(colour => Frozen(Color.FromArgb(colour.A, colour.R, colour.G, colour.B)))], null, dark: false),
    };

    /// <summary>What the word at <paramref name="at"/> is drawn in. Asked once per word, in the order they are placed.</summary>
    public Brush For(int at)
    {
        if (_cycle is { Count: > 0 } cycle) return cycle[at % cycle.Count];

        // wordcloud2's own throw: any hue, nearly full saturation, and either the dark end of the lightness
        // or the light one — which is what keeps a scattered cloud legible on its ground either way.
        var hue = _random!.Next() * 360;
        var saturation = 0.7 + _random.Next() * 0.3;
        var lightness = _dark ? _random.Next() * 0.3 : 0.7 + _random.Next() * 0.3;

        return Frozen(FromHsl(hue, saturation, lightness));
    }

    /// <summary>The ground the picture is drawn on, or null for the page it sits on.</summary>
    public static Brush? Background(WordCloudColours colours) =>
        colours.Background is { } colour ? Frozen(Color.FromArgb(colour.A, colour.R, colour.G, colour.B)) : null;

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var sixth = hue / 60;
        var second = chroma * (1 - Math.Abs(sixth % 2 - 1));
        var lift = lightness - chroma / 2;

        var (red, green, blue) = (int)sixth switch
        {
            0 => (chroma, second, 0d),
            1 => (second, chroma, 0d),
            2 => (0d, chroma, second),
            3 => (0d, second, chroma),
            4 => (second, 0d, chroma),
            _ => (chroma, 0d, second),
        };

        return Color.FromRgb(Byte(red + lift), Byte(green + lift), Byte(blue + lift));
    }

    private static byte Byte(double channel) => (byte)Math.Clamp(Math.Round(channel * 255), 0, 255);

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}
