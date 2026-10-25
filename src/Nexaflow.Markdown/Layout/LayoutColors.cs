namespace Nexaflow.Markdown.Layout;

/// <summary>
/// The colours that have names, by the names content writes them with — the set WPF knows, which is CSS's first one — and what a
/// colour written as words comes to: a name, <c>#rgb</c>, <c>#argb</c>, <c>#rrggbb</c>, <c>#aarrggbb</c>, or <c>sc#</c> and its linear
/// parts. Decided here once, without a desktop, so a <c>red</c> in one language is the red of every other.
/// </summary>
public static class LayoutColors
{
    public static LayoutColor AliceBlue { get; } = new(255, 240, 248, 255);

    public static LayoutColor AntiqueWhite { get; } = new(255, 250, 235, 215);

    public static LayoutColor Aqua { get; } = new(255, 0, 255, 255);

    public static LayoutColor Aquamarine { get; } = new(255, 127, 255, 212);

    public static LayoutColor Azure { get; } = new(255, 240, 255, 255);

    public static LayoutColor Beige { get; } = new(255, 245, 245, 220);

    public static LayoutColor Bisque { get; } = new(255, 255, 228, 196);

    public static LayoutColor Black { get; } = new(255, 0, 0, 0);

    public static LayoutColor BlanchedAlmond { get; } = new(255, 255, 235, 205);

    public static LayoutColor Blue { get; } = new(255, 0, 0, 255);

    public static LayoutColor BlueViolet { get; } = new(255, 138, 43, 226);

    public static LayoutColor Brown { get; } = new(255, 165, 42, 42);

    public static LayoutColor BurlyWood { get; } = new(255, 222, 184, 135);

    public static LayoutColor CadetBlue { get; } = new(255, 95, 158, 160);

    public static LayoutColor Chartreuse { get; } = new(255, 127, 255, 0);

    public static LayoutColor Chocolate { get; } = new(255, 210, 105, 30);

    public static LayoutColor Coral { get; } = new(255, 255, 127, 80);

    public static LayoutColor CornflowerBlue { get; } = new(255, 100, 149, 237);

    public static LayoutColor Cornsilk { get; } = new(255, 255, 248, 220);

    public static LayoutColor Crimson { get; } = new(255, 220, 20, 60);

    public static LayoutColor Cyan { get; } = new(255, 0, 255, 255);

    public static LayoutColor DarkBlue { get; } = new(255, 0, 0, 139);

    public static LayoutColor DarkCyan { get; } = new(255, 0, 139, 139);

    public static LayoutColor DarkGoldenrod { get; } = new(255, 184, 134, 11);

    public static LayoutColor DarkGray { get; } = new(255, 169, 169, 169);

    public static LayoutColor DarkGreen { get; } = new(255, 0, 100, 0);

    public static LayoutColor DarkKhaki { get; } = new(255, 189, 183, 107);

    public static LayoutColor DarkMagenta { get; } = new(255, 139, 0, 139);

    public static LayoutColor DarkOliveGreen { get; } = new(255, 85, 107, 47);

    public static LayoutColor DarkOrange { get; } = new(255, 255, 140, 0);

    public static LayoutColor DarkOrchid { get; } = new(255, 153, 50, 204);

    public static LayoutColor DarkRed { get; } = new(255, 139, 0, 0);

    public static LayoutColor DarkSalmon { get; } = new(255, 233, 150, 122);

    public static LayoutColor DarkSeaGreen { get; } = new(255, 143, 188, 143);

    public static LayoutColor DarkSlateBlue { get; } = new(255, 72, 61, 139);

    public static LayoutColor DarkSlateGray { get; } = new(255, 47, 79, 79);

    public static LayoutColor DarkTurquoise { get; } = new(255, 0, 206, 209);

    public static LayoutColor DarkViolet { get; } = new(255, 148, 0, 211);

    public static LayoutColor DeepPink { get; } = new(255, 255, 20, 147);

    public static LayoutColor DeepSkyBlue { get; } = new(255, 0, 191, 255);

    public static LayoutColor DimGray { get; } = new(255, 105, 105, 105);

    public static LayoutColor DodgerBlue { get; } = new(255, 30, 144, 255);

    public static LayoutColor Firebrick { get; } = new(255, 178, 34, 34);

    public static LayoutColor FloralWhite { get; } = new(255, 255, 250, 240);

    public static LayoutColor ForestGreen { get; } = new(255, 34, 139, 34);

    public static LayoutColor Fuchsia { get; } = new(255, 255, 0, 255);

    public static LayoutColor Gainsboro { get; } = new(255, 220, 220, 220);

    public static LayoutColor GhostWhite { get; } = new(255, 248, 248, 255);

    public static LayoutColor Gold { get; } = new(255, 255, 215, 0);

    public static LayoutColor Goldenrod { get; } = new(255, 218, 165, 32);

    public static LayoutColor Gray { get; } = new(255, 128, 128, 128);

    public static LayoutColor Green { get; } = new(255, 0, 128, 0);

    public static LayoutColor GreenYellow { get; } = new(255, 173, 255, 47);

    public static LayoutColor Honeydew { get; } = new(255, 240, 255, 240);

    public static LayoutColor HotPink { get; } = new(255, 255, 105, 180);

    public static LayoutColor IndianRed { get; } = new(255, 205, 92, 92);

    public static LayoutColor Indigo { get; } = new(255, 75, 0, 130);

    public static LayoutColor Ivory { get; } = new(255, 255, 255, 240);

    public static LayoutColor Khaki { get; } = new(255, 240, 230, 140);

    public static LayoutColor Lavender { get; } = new(255, 230, 230, 250);

    public static LayoutColor LavenderBlush { get; } = new(255, 255, 240, 245);

    public static LayoutColor LawnGreen { get; } = new(255, 124, 252, 0);

    public static LayoutColor LemonChiffon { get; } = new(255, 255, 250, 205);

    public static LayoutColor LightBlue { get; } = new(255, 173, 216, 230);

    public static LayoutColor LightCoral { get; } = new(255, 240, 128, 128);

    public static LayoutColor LightCyan { get; } = new(255, 224, 255, 255);

    public static LayoutColor LightGoldenrodYellow { get; } = new(255, 250, 250, 210);

    public static LayoutColor LightGray { get; } = new(255, 211, 211, 211);

    public static LayoutColor LightGreen { get; } = new(255, 144, 238, 144);

    public static LayoutColor LightPink { get; } = new(255, 255, 182, 193);

    public static LayoutColor LightSalmon { get; } = new(255, 255, 160, 122);

    public static LayoutColor LightSeaGreen { get; } = new(255, 32, 178, 170);

    public static LayoutColor LightSkyBlue { get; } = new(255, 135, 206, 250);

    public static LayoutColor LightSlateGray { get; } = new(255, 119, 136, 153);

    public static LayoutColor LightSteelBlue { get; } = new(255, 176, 196, 222);

    public static LayoutColor LightYellow { get; } = new(255, 255, 255, 224);

    public static LayoutColor Lime { get; } = new(255, 0, 255, 0);

    public static LayoutColor LimeGreen { get; } = new(255, 50, 205, 50);

    public static LayoutColor Linen { get; } = new(255, 250, 240, 230);

    public static LayoutColor Magenta { get; } = new(255, 255, 0, 255);

    public static LayoutColor Maroon { get; } = new(255, 128, 0, 0);

    public static LayoutColor MediumAquamarine { get; } = new(255, 102, 205, 170);

    public static LayoutColor MediumBlue { get; } = new(255, 0, 0, 205);

    public static LayoutColor MediumOrchid { get; } = new(255, 186, 85, 211);

    public static LayoutColor MediumPurple { get; } = new(255, 147, 112, 219);

    public static LayoutColor MediumSeaGreen { get; } = new(255, 60, 179, 113);

    public static LayoutColor MediumSlateBlue { get; } = new(255, 123, 104, 238);

    public static LayoutColor MediumSpringGreen { get; } = new(255, 0, 250, 154);

    public static LayoutColor MediumTurquoise { get; } = new(255, 72, 209, 204);

    public static LayoutColor MediumVioletRed { get; } = new(255, 199, 21, 133);

    public static LayoutColor MidnightBlue { get; } = new(255, 25, 25, 112);

    public static LayoutColor MintCream { get; } = new(255, 245, 255, 250);

    public static LayoutColor MistyRose { get; } = new(255, 255, 228, 225);

    public static LayoutColor Moccasin { get; } = new(255, 255, 228, 181);

    public static LayoutColor NavajoWhite { get; } = new(255, 255, 222, 173);

    public static LayoutColor Navy { get; } = new(255, 0, 0, 128);

    public static LayoutColor OldLace { get; } = new(255, 253, 245, 230);

    public static LayoutColor Olive { get; } = new(255, 128, 128, 0);

    public static LayoutColor OliveDrab { get; } = new(255, 107, 142, 35);

    public static LayoutColor Orange { get; } = new(255, 255, 165, 0);

    public static LayoutColor OrangeRed { get; } = new(255, 255, 69, 0);

    public static LayoutColor Orchid { get; } = new(255, 218, 112, 214);

    public static LayoutColor PaleGoldenrod { get; } = new(255, 238, 232, 170);

    public static LayoutColor PaleGreen { get; } = new(255, 152, 251, 152);

    public static LayoutColor PaleTurquoise { get; } = new(255, 175, 238, 238);

    public static LayoutColor PaleVioletRed { get; } = new(255, 219, 112, 147);

    public static LayoutColor PapayaWhip { get; } = new(255, 255, 239, 213);

    public static LayoutColor PeachPuff { get; } = new(255, 255, 218, 185);

    public static LayoutColor Peru { get; } = new(255, 205, 133, 63);

    public static LayoutColor Pink { get; } = new(255, 255, 192, 203);

    public static LayoutColor Plum { get; } = new(255, 221, 160, 221);

    public static LayoutColor PowderBlue { get; } = new(255, 176, 224, 230);

    public static LayoutColor Purple { get; } = new(255, 128, 0, 128);

    public static LayoutColor Red { get; } = new(255, 255, 0, 0);

    public static LayoutColor RosyBrown { get; } = new(255, 188, 143, 143);

    public static LayoutColor RoyalBlue { get; } = new(255, 65, 105, 225);

    public static LayoutColor SaddleBrown { get; } = new(255, 139, 69, 19);

    public static LayoutColor Salmon { get; } = new(255, 250, 128, 114);

    public static LayoutColor SandyBrown { get; } = new(255, 244, 164, 96);

    public static LayoutColor SeaGreen { get; } = new(255, 46, 139, 87);

    public static LayoutColor SeaShell { get; } = new(255, 255, 245, 238);

    public static LayoutColor Sienna { get; } = new(255, 160, 82, 45);

    public static LayoutColor Silver { get; } = new(255, 192, 192, 192);

    public static LayoutColor SkyBlue { get; } = new(255, 135, 206, 235);

    public static LayoutColor SlateBlue { get; } = new(255, 106, 90, 205);

    public static LayoutColor SlateGray { get; } = new(255, 112, 128, 144);

    public static LayoutColor Snow { get; } = new(255, 255, 250, 250);

    public static LayoutColor SpringGreen { get; } = new(255, 0, 255, 127);

    public static LayoutColor SteelBlue { get; } = new(255, 70, 130, 180);

    public static LayoutColor Tan { get; } = new(255, 210, 180, 140);

    public static LayoutColor Teal { get; } = new(255, 0, 128, 128);

    public static LayoutColor Thistle { get; } = new(255, 216, 191, 216);

    public static LayoutColor Tomato { get; } = new(255, 255, 99, 71);

    public static LayoutColor Transparent { get; } = new(0, 255, 255, 255);

    public static LayoutColor Turquoise { get; } = new(255, 64, 224, 208);

    public static LayoutColor Violet { get; } = new(255, 238, 130, 238);

    public static LayoutColor Wheat { get; } = new(255, 245, 222, 179);

    public static LayoutColor White { get; } = new(255, 255, 255, 255);

    public static LayoutColor WhiteSmoke { get; } = new(255, 245, 245, 245);

    public static LayoutColor Yellow { get; } = new(255, 255, 255, 0);

    public static LayoutColor YellowGreen { get; } = new(255, 154, 205, 50);

    /// <summary>
    /// The colour <paramref name="written"/> says, read as WPF reads one — the space round it ignored, a name in any case — or null where it
    /// says none.
    /// </summary>
    public static LayoutColor? Parse(string? written)
    {
        if (written is null) return null;

        var colour = written.Trim();
        if (colour.Length == 0) return null;

        if (colour[0] == '#') return Hex(colour);

        if (colour.StartsWith("sc#", StringComparison.Ordinal)) return Linear(colour[3..]);

        return Named.TryGetValue(colour, out var named) ? named : null;
    }

    /// <summary>A colour written in hexadecimal: a digit to each part, or two, with or without its alpha first.</summary>
    private static LayoutColor? Hex(string colour)
    {
        var digits = new int[colour.Length - 1];
        for (var at = 1; at < colour.Length; at++)
        {
            var digit = HexDigit(colour[at]);
            if (digit < 0) return null;

            digits[at - 1] = digit;
        }

        byte Once(int at) => (byte)((digits[at] * 16) + digits[at]);
        byte Twice(int at) => (byte)((digits[at] * 16) + digits[at + 1]);

        return digits.Length switch
        {
            3 => new LayoutColor(255, Once(0), Once(1), Once(2)),
            4 => new LayoutColor(Once(0), Once(1), Once(2), Once(3)),
            6 => new LayoutColor(255, Twice(0), Twice(2), Twice(4)),
            8 => new LayoutColor(Twice(0), Twice(2), Twice(4), Twice(6)),
            _ => null,
        };
    }

    private static int HexDigit(char digit) => digit switch
    {
        >= '0' and <= '9' => digit - '0',
        >= 'a' and <= 'f' => digit - 'a' + 10,
        >= 'A' and <= 'F' => digit - 'A' + 10,
        _ => -1,
    };

    /// <summary>A colour written as its linear parts — red, green and blue, or alpha before them — each from nought to one.</summary>
    private static LayoutColor? Linear(string parts)
    {
        var numbers = parts.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var read = new float[numbers.Length];

        for (var at = 0; at < numbers.Length; at++)
            if (!float.TryParse(numbers[at], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out read[at]))
                return null;

        return read.Length switch
        {
            3 => new LayoutColor(255, Gamma(read[0]), Gamma(read[1]), Gamma(read[2])),
            4 => new LayoutColor(Alpha(read[0]), Gamma(read[1]), Gamma(read[2]), Gamma(read[3])),
            _ => null,
        };
    }

    /// <summary>A linear part as the byte it shows as, through sRGB's curve.</summary>
    private static byte Gamma(float linear)
    {
        if (linear <= 0) return 0;
        if (linear <= 0.0031308) return (byte)((255 * linear * 12.92) + 0.5);
        if (linear < 1) return (byte)((255 * ((1.055 * Math.Pow(linear, 1.0 / 2.4)) - 0.055)) + 0.5);

        return 255;
    }

    private static byte Alpha(float alpha) => alpha <= 0 ? (byte)0 : alpha >= 1 ? (byte)255 : (byte)((alpha * 255) + 0.5);

    private static readonly Dictionary<string, LayoutColor> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AliceBlue"] = AliceBlue,
        ["AntiqueWhite"] = AntiqueWhite,
        ["Aqua"] = Aqua,
        ["Aquamarine"] = Aquamarine,
        ["Azure"] = Azure,
        ["Beige"] = Beige,
        ["Bisque"] = Bisque,
        ["Black"] = Black,
        ["BlanchedAlmond"] = BlanchedAlmond,
        ["Blue"] = Blue,
        ["BlueViolet"] = BlueViolet,
        ["Brown"] = Brown,
        ["BurlyWood"] = BurlyWood,
        ["CadetBlue"] = CadetBlue,
        ["Chartreuse"] = Chartreuse,
        ["Chocolate"] = Chocolate,
        ["Coral"] = Coral,
        ["CornflowerBlue"] = CornflowerBlue,
        ["Cornsilk"] = Cornsilk,
        ["Crimson"] = Crimson,
        ["Cyan"] = Cyan,
        ["DarkBlue"] = DarkBlue,
        ["DarkCyan"] = DarkCyan,
        ["DarkGoldenrod"] = DarkGoldenrod,
        ["DarkGray"] = DarkGray,
        ["DarkGreen"] = DarkGreen,
        ["DarkKhaki"] = DarkKhaki,
        ["DarkMagenta"] = DarkMagenta,
        ["DarkOliveGreen"] = DarkOliveGreen,
        ["DarkOrange"] = DarkOrange,
        ["DarkOrchid"] = DarkOrchid,
        ["DarkRed"] = DarkRed,
        ["DarkSalmon"] = DarkSalmon,
        ["DarkSeaGreen"] = DarkSeaGreen,
        ["DarkSlateBlue"] = DarkSlateBlue,
        ["DarkSlateGray"] = DarkSlateGray,
        ["DarkTurquoise"] = DarkTurquoise,
        ["DarkViolet"] = DarkViolet,
        ["DeepPink"] = DeepPink,
        ["DeepSkyBlue"] = DeepSkyBlue,
        ["DimGray"] = DimGray,
        ["DodgerBlue"] = DodgerBlue,
        ["Firebrick"] = Firebrick,
        ["FloralWhite"] = FloralWhite,
        ["ForestGreen"] = ForestGreen,
        ["Fuchsia"] = Fuchsia,
        ["Gainsboro"] = Gainsboro,
        ["GhostWhite"] = GhostWhite,
        ["Gold"] = Gold,
        ["Goldenrod"] = Goldenrod,
        ["Gray"] = Gray,
        ["Green"] = Green,
        ["GreenYellow"] = GreenYellow,
        ["Honeydew"] = Honeydew,
        ["HotPink"] = HotPink,
        ["IndianRed"] = IndianRed,
        ["Indigo"] = Indigo,
        ["Ivory"] = Ivory,
        ["Khaki"] = Khaki,
        ["Lavender"] = Lavender,
        ["LavenderBlush"] = LavenderBlush,
        ["LawnGreen"] = LawnGreen,
        ["LemonChiffon"] = LemonChiffon,
        ["LightBlue"] = LightBlue,
        ["LightCoral"] = LightCoral,
        ["LightCyan"] = LightCyan,
        ["LightGoldenrodYellow"] = LightGoldenrodYellow,
        ["LightGray"] = LightGray,
        ["LightGreen"] = LightGreen,
        ["LightPink"] = LightPink,
        ["LightSalmon"] = LightSalmon,
        ["LightSeaGreen"] = LightSeaGreen,
        ["LightSkyBlue"] = LightSkyBlue,
        ["LightSlateGray"] = LightSlateGray,
        ["LightSteelBlue"] = LightSteelBlue,
        ["LightYellow"] = LightYellow,
        ["Lime"] = Lime,
        ["LimeGreen"] = LimeGreen,
        ["Linen"] = Linen,
        ["Magenta"] = Magenta,
        ["Maroon"] = Maroon,
        ["MediumAquamarine"] = MediumAquamarine,
        ["MediumBlue"] = MediumBlue,
        ["MediumOrchid"] = MediumOrchid,
        ["MediumPurple"] = MediumPurple,
        ["MediumSeaGreen"] = MediumSeaGreen,
        ["MediumSlateBlue"] = MediumSlateBlue,
        ["MediumSpringGreen"] = MediumSpringGreen,
        ["MediumTurquoise"] = MediumTurquoise,
        ["MediumVioletRed"] = MediumVioletRed,
        ["MidnightBlue"] = MidnightBlue,
        ["MintCream"] = MintCream,
        ["MistyRose"] = MistyRose,
        ["Moccasin"] = Moccasin,
        ["NavajoWhite"] = NavajoWhite,
        ["Navy"] = Navy,
        ["OldLace"] = OldLace,
        ["Olive"] = Olive,
        ["OliveDrab"] = OliveDrab,
        ["Orange"] = Orange,
        ["OrangeRed"] = OrangeRed,
        ["Orchid"] = Orchid,
        ["PaleGoldenrod"] = PaleGoldenrod,
        ["PaleGreen"] = PaleGreen,
        ["PaleTurquoise"] = PaleTurquoise,
        ["PaleVioletRed"] = PaleVioletRed,
        ["PapayaWhip"] = PapayaWhip,
        ["PeachPuff"] = PeachPuff,
        ["Peru"] = Peru,
        ["Pink"] = Pink,
        ["Plum"] = Plum,
        ["PowderBlue"] = PowderBlue,
        ["Purple"] = Purple,
        ["Red"] = Red,
        ["RosyBrown"] = RosyBrown,
        ["RoyalBlue"] = RoyalBlue,
        ["SaddleBrown"] = SaddleBrown,
        ["Salmon"] = Salmon,
        ["SandyBrown"] = SandyBrown,
        ["SeaGreen"] = SeaGreen,
        ["SeaShell"] = SeaShell,
        ["Sienna"] = Sienna,
        ["Silver"] = Silver,
        ["SkyBlue"] = SkyBlue,
        ["SlateBlue"] = SlateBlue,
        ["SlateGray"] = SlateGray,
        ["Snow"] = Snow,
        ["SpringGreen"] = SpringGreen,
        ["SteelBlue"] = SteelBlue,
        ["Tan"] = Tan,
        ["Teal"] = Teal,
        ["Thistle"] = Thistle,
        ["Tomato"] = Tomato,
        ["Transparent"] = Transparent,
        ["Turquoise"] = Turquoise,
        ["Violet"] = Violet,
        ["Wheat"] = Wheat,
        ["White"] = White,
        ["WhiteSmoke"] = WhiteSmoke,
        ["Yellow"] = Yellow,
        ["YellowGreen"] = YellowGreen,
    };
}
