using System.Linq;
using System.Windows.Media;
using WpfMath.Fonts;
using XamlMath;

namespace WpfMath.Rendering;

public static class WpfTeXEnvironment
{
    /// <summary>Creates an instance of <see cref="TexEnvironment"/> for a WPF program.</summary>
    /// <param name="style">Initial style for the formula content.</param>
    /// <param name="scale">Formula font size.</param>
    /// <param name="systemTextFontName">Name of the system font to use for the <code>\text</code> blocks.</param>
    /// <param name="foreground">Foreground color. Black if not specified.</param>
    /// <param name="background">Background color.</param>
    public static TexEnvironment Create(
        TexStyle style = TexStyle.Display,
        double scale = 20.0,
        string systemTextFontName = "Arial",
        Brush? foreground = null,
        Brush? background = null)
    {
        var mathFont = new DefaultTexFont(WpfMathFontProvider.Instance, scale);
        var textFont = GetSystemFont(systemTextFontName, scale);

        return new TexEnvironment(
            style,
            mathFont,
            textFont,
            background.ToPlatform(),
            foreground.ToPlatform());
    }

    /// <summary>
    /// The installed family of that name, looked up once per name rather than once per formula.
    ///
    /// <para>
    /// <see cref="System.Windows.Media.Fonts.SystemFontFamilies"/> walks every font on the machine, and the
    /// predicate then walks each one's localised names, so this is not a lookup but a scan — and
    /// <see cref="Create"/> sits behind every formula drawn. Once per name is the honest frequency: the
    /// answer depends on the name alone, and the set of installed fonts does not change while a formula is
    /// being typed into.
    /// </para>
    /// <para>
    /// A <c>FontFamily</c> is an ordinary immutable object rather than a <c>DispatcherObject</c>, so one
    /// shared between threads is safe — which matters, because the corpus sweep draws on all of them.
    /// </para>
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, FontFamily> Families = new();

    private static WpfSystemFont GetSystemFont(string fontName, double size) =>
        new(size, Families.GetOrAdd(
            fontName,
            name => System.Windows.Media.Fonts.SystemFontFamilies.First(
                ff => ff.ToString() == name || ff.FamilyNames.Values?.Contains(name) == true)));
}
