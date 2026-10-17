using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// How content sets raw characters: the face, size and ink of source shown as it is written. A value rather than a function
/// handing back characters already set, because shown source is set two ways: whole, as the run that is drawn, and a line at a
/// time, to find where each of its letters stands. A letter found inside a run of many lines sets every line of the run again for
/// every letter; found on its own line, set once, it is read off what was already set.
/// </summary>
/// <param name="Decorations">The rules drawn through or under the characters, if any.</param>
/// <param name="Culture">What the characters are set as; the current culture where none is named.</param>
public sealed record CharacterSetting(Typeface Face, double Size, Brush Ink,
                                      TextDecorationCollection? Decorations = null, CultureInfo? Culture = null)
{
    /// <summary>The characters set as one run, as they are drawn.</summary>
    public FormattedText Set(string text)
    {
        var set = new FormattedText(text, Culture ?? CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, Size, Ink,
                                    LayoutText.Density);

        if (Decorations is not null) set.SetTextDecorations(Decorations);

        return set;
    }

    /// <summary>
    /// One line of characters set by <paramref name="formatter"/> and never broken: the line <see cref="Set"/> would set from the
    /// same characters, so a letter's box read off it is where that run draws the letter.
    /// </summary>
    public TextLine Line(TextFormatter formatter, string line)
    {
        var run = new RunSetting(this);
        return formatter.FormatLine(new OneRun(line, run), 0, 0, new LineSetting(run), null);
    }

    /// <summary>The characters as the type engine takes them: one run, with nothing either side.</summary>
    private sealed class OneRun(string text, TextRunProperties run) : TextSource
    {
        public override TextRun GetTextRun(int textSourceCharacterIndex) =>
            textSourceCharacterIndex < text.Length
                ? new TextCharacters(text, textSourceCharacterIndex, text.Length - textSourceCharacterIndex, run)
                : new TextEndOfParagraph(1);

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit) =>
            new(textSourceCharacterIndexLimit,
                new CultureSpecificCharacterBufferRange(run.CultureInfo, new CharacterBufferRange(text, 0, textSourceCharacterIndexLimit)));

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex) =>
            textSourceCharacterIndex;
    }

    /// <summary>The run's face, size and ink, as <see cref="FormattedText"/> hands them to the type engine.</summary>
    private sealed class RunSetting : TextRunProperties
    {
        private readonly CharacterSetting _setting;
        private readonly CultureInfo _culture;

        public RunSetting(CharacterSetting setting)
        {
            _setting = setting;
            _culture = setting.Culture ?? CultureInfo.CurrentCulture;
            PixelsPerDip = LayoutText.Density;
        }

        public override Typeface Typeface => _setting.Face;
        public override double FontRenderingEmSize => _setting.Size;
        public override double FontHintingEmSize => _setting.Size;
        public override TextDecorationCollection? TextDecorations => _setting.Decorations;
        public override Brush ForegroundBrush => _setting.Ink;
        public override Brush? BackgroundBrush => null;
        public override CultureInfo CultureInfo => _culture;
        public override TextEffectCollection? TextEffects => null;
    }

    /// <summary>
    /// A paragraph as <see cref="FormattedText"/> sets one with no width given: left to right, never wrapped, its tab stops every
    /// four ems.
    /// </summary>
    private sealed class LineSetting(TextRunProperties run) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => run;
        public override TextWrapping TextWrapping => TextWrapping.NoWrap;
        public override TextMarkerProperties? TextMarkerProperties => null;
        public override double Indent => 0;
        public override double DefaultIncrementalTab => 4 * run.FontRenderingEmSize;
    }
}
