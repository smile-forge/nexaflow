using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Mermaid;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// The colours a diagram offers a reader to paint something with: the bank the style names (<see cref="LayoutStyle.Swatches"/>), which
/// the host fills from the app's shared swatch bank as the theme tunes it, so a node coloured from the ribbon is coloured from the same
/// bank as everything else in the window, and one theme retunes them all at once.
///
/// <para>
/// A diagram names a colour by the bank's own name for it and never by its value. Two the theme happens to tune alike are still
/// two choices, where offers keyed by the colour itself would collapse into one. What goes into the source is the value, because a
/// value is all a diagram can hold.
/// </para>
/// </summary>
internal static class DiagramSwatches
{
    /// <summary>What no colour at all is named — a choice like any other, and the one that takes a colour back out again.</summary>
    public const string Clear = "None";

    /// <summary>The colour that is no colour, which is how a reader is shown it.</summary>
    public static LayoutColor Nothing => LayoutColors.Transparent;

    /// <summary>A colour written as a diagram writes one.</summary>
    public static string Hex(LayoutColor shade) => $"#{shade.R:x2}{shade.G:x2}{shade.B:x2}";

    /// <summary>What a colour written in a diagram's source comes to, or null where nothing is written or it says no colour.</summary>
    public static LayoutColor? Shade(string? written) => DiagramColour.ParseCss(written);
}
