using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Controls;

/// <summary>
/// How the bar turns a page's crumb into the text it draws: the crumb as its page stated it, restated by each display
/// filter in turn (<see cref="Compose"/>), then shortened to fit (<see cref="Shortened"/>). WPF-free, so the rules are
/// proved without building the control.
/// </summary>
internal static class BreadcrumbDrawing
{
    /// <summary>The deepest letter level: 1 keeps three letters, 2 two, 3 one.</summary>
    private const int LetterLevels = 3;

    /// <summary><paramref name="crumb"/> as its page stated it, then as each of <paramref name="filters"/> — already in
    /// their stacking order — restates it.</summary>
    public static BreadcrumbDisplay Compose(BreadcrumbSegment crumb, IReadOnlyList<IBreadcrumbDisplayFilter> filters)
    {
        var display = BreadcrumbDisplay.Of(crumb);
        foreach (var filter in filters)
            filter.Apply(crumb, display);
        return display;
    }

    /// <summary>
    /// How far a crumb can be shortened. One of more than two parts loses its middle parts one at a time wherever it
    /// stands, since its first and last part still say where it is (<c>D:\…\temp</c>); any other crumb in the middle
    /// loses letters down to one; the first and last crumb are otherwise left whole.
    /// </summary>
    public static int DeepestLevel(BreadcrumbDisplay display, bool atEnd)
        => display.Parts.Count > 2 ? display.Parts.Count - 2 : atEnd ? 0 : LetterLevels;

    /// <summary>The crumb's text at <paramref name="level"/>: whole at 0; with that many middle parts elided nearest the
    /// first, for a crumb of more than two parts; else cut to 3, 2 or 1 letters.</summary>
    public static string Shortened(BreadcrumbDisplay display, int level)
    {
        if (level <= 0) return display.Text;
        if (display.Parts.Count > 2)
            return string.Join(display.Separator, [display.Parts[0], "…", .. display.Parts.Skip(1 + level)]);

        var text = display.Text;
        int keep = LetterLevels + 1 - level;
        return text.Length <= keep ? text : text[..keep] + "…";
    }
}
