using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// How wide a stretch of text is set, remembered. Measuring a word is shaping it, and a document that has not changed
/// around a word is measured again on every keystroke; this answers from what was measured before.
///
/// <para>
/// Only the width is kept, never the shaped text, so nothing here is ever drawn from and nothing drawn is shared. Asked
/// with the stretch itself rather than a copy of it, so a line broken into words makes no string for a word it has
/// seen. Bounded: past <see cref="Most"/> words it starts again, which costs one measuring of each word it meets.
/// </para>
/// </summary>
public static class TextWidths
{
    private const int Most = 1 << 16;

    private static readonly ConcurrentDictionary<(Typeface Face, double Size), In> Faces = new();

    private static int _known;

    /// <summary>How wide <paramref name="text"/> is set in <paramref name="face"/> at <paramref name="size"/>, trailing space left off.</summary>
    public static double Of(string text, Typeface face, double size) => Set(face, size).Of(text);

    /// <summary>What measures text set in <paramref name="face"/> at <paramref name="size"/> — found once for a run of words, not once a word.</summary>
    public static In Set(Typeface face, double size) => Faces.GetOrAdd((face, size), static set => new In(set.Face, set.Size));

    /// <summary>The widths of one face at one size.</summary>
    public sealed class In
    {
        private readonly Typeface _face;
        private readonly double _size;
        private readonly ConcurrentDictionary<string, double> _widths = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, double>.AlternateLookup<ReadOnlySpan<char>> _looking;

        internal In(Typeface face, double size)
        {
            _face = face;
            _size = size;
            _looking = _widths.GetAlternateLookup<ReadOnlySpan<char>>();
        }

        /// <summary>How wide <paramref name="text"/> is set, trailing space left off.</summary>
        public double Of(ReadOnlySpan<char> text)
        {
            if (_looking.TryGetValue(text, out var width)) return width;

            if (Interlocked.Increment(ref _known) > Most)
            {
                Faces.Clear();
                Interlocked.Exchange(ref _known, 0);
            }

            var written = text.ToString();
            width = new FormattedText(written, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _face, _size,
                                      Brushes.Black, LayoutText.Density).Width;
            _widths[written] = width;

            return width;
        }
    }
}
