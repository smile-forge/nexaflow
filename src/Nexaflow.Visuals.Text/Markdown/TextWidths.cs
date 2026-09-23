using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// How wide a stretch of text is set, remembered. Measuring a word is shaping it, and a document that has not changed
/// around a word is measured again on every keystroke; this answers from what was measured before.
///
/// <para>
/// Only the width is kept, never the shaped text, so nothing here is ever drawn from and nothing drawn is shared.
/// Bounded: past <see cref="Most"/> entries it starts again, which costs one measuring of each word it meets.
/// </para>
/// </summary>
public static class TextWidths
{
    private const int Most = 1 << 16;

    private static readonly ConcurrentDictionary<(string Text, Typeface Face, double Size), double> Known = new();

    /// <summary>How wide <paramref name="text"/> is set in <paramref name="face"/> at <paramref name="size"/>, trailing space left off.</summary>
    public static double Of(string text, Typeface face, double size)
    {
        if (Known.TryGetValue((text, face, size), out var width)) return width;

        if (Known.Count >= Most) Known.Clear();

        width = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, Brushes.Black,
                                  LayoutText.Density).Width;
        Known[(text, face, size)] = width;

        return width;
    }
}
