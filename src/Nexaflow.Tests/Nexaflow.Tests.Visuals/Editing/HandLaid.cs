using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// Content a test lays out by its own hand rather than by a language that ships — registered as a language of its own, the way
/// a host adds one, so it reaches an element the way every language does: through the engine.
///
/// <para>
/// For a test of what the element does with a layout — a press, the caret, a picture — where no language draws exactly the
/// layout the test is about.
/// </para>
/// </summary>
internal static class HandLaid
{
    private const string Laying = "hand-laid:";
    private const string Unreading = "hand-unread";

    private static readonly ConcurrentDictionary<string, Func<EditState, bool, Laid>> Lays = new();
    private static int _made;

    static HandLaid()
    {
        ContentLanguages.Register(new ContentLanguage(
            Reads: static word => word?.StartsWith(Laying, StringComparison.Ordinal) == true,
            Parser: static () => static source => ContentParse.Of(ContentNode.Shown(source)),
            Stages: static (_, _) => [],
            Builder: static (reading, show) => new Builder(reading, show, Lays[show.Named])));

        ContentLanguages.Register(new ContentLanguage(
            Reads: static word => word == Unreading,
            Parser: static () => static _ => throw new InvalidOperationException("no reader"),
            Stages: static (_, _) => [],
            Builder: static (reading, show) => new Builder(reading, show, static (_, _) => Laid.Nothing)));
    }

    /// <summary>An element showing <paramref name="source"/> as <paramref name="lay"/> lays it, told the state and whether nobody can write in it.</summary>
    public static ContentElement Element(string source, Func<EditState, bool, Laid> lay, bool readOnly = false)
    {
        var named = Laying + Interlocked.Increment(ref _made).ToString(CultureInfo.InvariantCulture);
        Lays[named] = lay;

        return new ContentElement(source, StyleFormat.Dark, new ContentEngine(), named) { IsReadOnly = readOnly };
    }

    /// <summary>An element showing <paramref name="source"/> in a language whose reading falls over before anything is laid out.</summary>
    public static ContentElement Unreadable(string source) => new(source, StyleFormat.Dark, new ContentEngine(), Unreading);

    private sealed class Builder(ContentReading reading, ContentShowing show, Func<EditState, bool, Laid> lay)
        : ContentBuilder(reading, new EditState(reading.Source, 0, null, show.Shown), show.Style, !show.Writing, show.Nesting)
    {
        protected override Laid? Build() => lay(State, IsReadOnly);

        protected override FormattedText Characters(string text) =>
            new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Style.TextSize, Brushes.Black, 1);
    }
}
