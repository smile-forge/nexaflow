using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The faces text is set in, each made once for the life of the process and shared by every builder — so setting a run
/// of words never looks a font up again, and a face named in a block's settings is found once.
///
/// <para>
/// Nothing here is released: a typeface is a small description of a face and the fonts behind it are WPF's to keep.
/// What is held is the faces the builders and the documents name, which is a handful.
/// </para>
/// </summary>
public static class Typefaces
{
    private static readonly ConcurrentDictionary<(FontFamily Family, FontWeight Weight, FontStyle Style), Typeface> Made = new();

    private static readonly ConcurrentDictionary<string, FontFamily> Named = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The typeface for <paramref name="family"/> at a weight and slant.</summary>
    public static Typeface Of(FontFamily family, FontWeight weight, FontStyle style) =>
        Made.GetOrAdd((family, weight, style), face => new Typeface(face.Family, face.Style, face.Weight, FontStretches.Normal));

    /// <summary>The family a document names by <paramref name="name"/> — a word cloud's <c>font:</c>.</summary>
    public static FontFamily Family(string name) => Named.GetOrAdd(name, named => new FontFamily(named));
}
