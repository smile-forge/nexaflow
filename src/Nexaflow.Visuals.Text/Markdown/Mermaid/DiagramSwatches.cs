using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// The colours a diagram offers a reader to paint something with: the app's shared swatch bank as the theme tunes it
/// (<see cref="SwatchPalette"/>), so a node coloured from the ribbon is coloured from the same bank as everything else in the
/// window, and one theme retunes them all at once.
///
/// <para>
/// A diagram names a colour by the bank's own name for it and never by its value. Two the theme happens to tune alike are still
/// two choices, where offers keyed by the colour itself would collapse into one; and a bank that cannot be resolved at all — no
/// application, as in a test — still offers the same twelve names rather than nothing. What goes into the source is the value,
/// because a value is all a diagram can hold.
/// </para>
/// </summary>
internal static class DiagramSwatches
{
    /// <summary>What no colour at all is named — a choice like any other, and the one that takes a colour back out again.</summary>
    public const string Clear = "None";

    /// <summary>The colour that is no colour, which is how a reader is shown it.</summary>
    public static Color Nothing => Colors.Transparent;

    /// <summary>The bank, each colour under the name it is offered by.</summary>
    public static IEnumerable<(string Name, Color Shade)> Bank() =>
        SwatchPalette.Keys.Select(key => (Named(key), DiagramColour.ColorOf(SwatchPalette.Resolve(key), Colors.Gray)));

    /// <summary>The colour the bank holds by a name, written as a diagram writes one — or null for a name it holds none by.</summary>
    public static string? Hex(string name) =>
        Bank().Where(one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase))
              .Select(one => (string?)$"#{one.Shade.R:x2}{one.Shade.G:x2}{one.Shade.B:x2}")
              .FirstOrDefault();

    /// <summary>What a colour written in a diagram's source comes to, or null where nothing is written or it says no colour.</summary>
    public static Color? Shade(string? written) => DiagramColour.ParseCss(written);

    /// <summary>A swatch's name, which is its key without the word every key in the bank starts with.</summary>
    private static string Named(string key) => key[(key.IndexOf('.') + 1)..];
}
