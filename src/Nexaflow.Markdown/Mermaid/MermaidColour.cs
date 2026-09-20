namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Whether a colour is written where a line may start with one, and how many characters it takes — a sequence diagram's
/// <c>box Aqua Group Description</c>, where the first word is the colour only if it names one and is otherwise the start of
/// what the box is called.
///
/// <para>
/// This says only that a colour is written there, which is a reading of the characters. What colour it comes to is the
/// builder's, where the theme and the palette are.
/// </para>
/// </summary>
public static class MermaidColour
{
    /// <summary>The ways a colour is written as a function of its parts.</summary>
    private static readonly string[] Functions = ["rgba(", "rgb(", "hsla(", "hsl("];

    /// <summary>The colours CSS names, which is the set a browser reads and so the set Mermaid does.</summary>
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black", "blanchedalmond", "blue",
        "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse", "chocolate", "coral", "cornflowerblue", "cornsilk",
        "crimson", "cyan", "darkblue", "darkcyan", "darkgoldenrod", "darkgray", "darkgreen", "darkgrey", "darkkhaki",
        "darkmagenta", "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon", "darkseagreen", "darkslateblue",
        "darkslategray", "darkslategrey", "darkturquoise", "darkviolet", "deeppink", "deepskyblue", "dimgray", "dimgrey",
        "dodgerblue", "firebrick", "floralwhite", "forestgreen", "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod",
        "gray", "green", "greenyellow", "grey", "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender",
        "lavenderblush", "lawngreen", "lemonchiffon", "lightblue", "lightcoral", "lightcyan", "lightgoldenrodyellow",
        "lightgray", "lightgreen", "lightgrey", "lightpink", "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray",
        "lightslategrey", "lightsteelblue", "lightyellow", "lime", "limegreen", "linen", "magenta", "maroon",
        "mediumaquamarine", "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen", "mediumslateblue",
        "mediumspringgreen", "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin",
        "navajowhite", "navy", "oldlace", "olive", "olivedrab", "orange", "orangered", "orchid", "palegoldenrod", "palegreen",
        "paleturquoise", "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple",
        "rebeccapurple", "red", "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell",
        "sienna", "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow", "springgreen", "steelblue", "tan",
        "teal", "thistle", "tomato", "transparent", "turquoise", "violet", "wheat", "white", "whitesmoke", "yellow",
        "yellowgreen",
    };

    /// <summary>Whether <paramref name="word"/> is a colour's name.</summary>
    public static bool Named(string word) => Names.Contains(word);

    /// <summary>
    /// How many characters the colour written at the start of <paramref name="text"/> takes — a name, a <c>#</c> and its digits,
    /// or a function and everything to the bracket closing it — or null where none is written there.
    /// </summary>
    public static int? At(string text)
    {
        foreach (var function in Functions)
        {
            if (!text.StartsWith(function, StringComparison.OrdinalIgnoreCase)) continue;

            var close = text.IndexOf(')');
            return close > 0 ? close + 1 : null;
        }

        var space = text.AsSpan().IndexOfAny(' ', '\t');
        var word = space < 0 ? text : text[..space];
        if (word.Length == 0) return null;

        if (word[0] == '#') return word.Length > 1 ? word.Length : null;

        return Named(word) ? word.Length : null;
    }
}
