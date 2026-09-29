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
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Editing;

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
    /// <summary>
    /// A language whose reading falls over — named so a fence in another document can be written in it.
    /// </summary>
    /// <remarks>
    /// Read rather than a constant on purpose. A constant is written into whoever names it, so naming one would never run
    /// the static constructor below, and the language would be registered only if some other test happened to have asked
    /// for one first.
    /// </remarks>
    public static readonly string Unreading = "hand-unread";

    /// <summary>A language that draws a block fine but falls over when asked what its corner offers. Read, for the same reason.</summary>
    public static readonly string Uncornering = "hand-uncorner";

    private const string Unstaging = "hand-unstaged";
    private const string Unbinding = "hand-unbound";
    private const string Unbuilding = "hand-unbuilt";

    private static readonly ConcurrentDictionary<string, Func<EditState, bool, Laid>> Lays = new();
    private static int _made;

    /// <summary>
    /// Registers the languages these tests lay content in: one that lays whatever a test hands it, and one for each place
    /// the pipeline can fall over — reading, working over, binding, being given a builder, and being asked about a corner.
    /// </summary>
    static HandLaid()
    {
        ContentLanguages.Register(new ContentLanguage(
            Reads: static word => word?.StartsWith(Laying, StringComparison.Ordinal) == true,
            Parser: static () => static source => ContentParse.Of(ContentNode.Shown(source)),
            Stages: static (_, show) => [new AstStage("hand-laid", tree => new Handed(tree, Lays[show.Named]))],
            Builder: typeof(Builder))
            {
                Writable = true,
                Transpile = ITranspile.AsGiven,
            });

        ContentLanguages.Register(new ContentLanguage(
            Reads: static word => word == Unreading,
            Parser: static () => static _ => throw new InvalidOperationException("no reader"),
            Stages: static (_, _) => [],
            Builder: typeof(Builder)));

        ContentLanguages.Register(new ContentLanguage(
            Reads: static word => word == Unstaging,
            Parser: static () => static source => ContentParse.Of(ContentNode.Shown(source)),
            Stages: static (_, _) => [new AstStage(Unstaging, static _ => throw new InvalidOperationException("no stage"))],
            Builder: typeof(Builder)));

        ContentLanguages.Register(new ContentLanguage(
            Reads: static word => word == Unbinding,
            Parser: static () => static source => ContentParse.Of(ContentNode.Shown(source)),
            Stages: static (_, _) => [],
            Builder: typeof(Builder))
            {
                Bind = static (_, _) => throw new InvalidOperationException("no binding"),
            });

        // Its builder is not a builder at all, which is what the engine's own maker falls over on.
        ContentLanguages.Register(new ContentLanguage(
            Reads: static word => word == Unbuilding,
            Parser: static () => static source => ContentParse.Of(ContentNode.Shown(source)),
            Stages: static (_, _) => [],
            Builder: typeof(object)));

        Lays[Uncornering] = static (_, _) => Boxed();
        ContentLanguages.Register(new ContentLanguage(
            Reads: static word => word == Uncornering,
            Parser: static () => static source => ContentParse.Of(ContentNode.Shown(source)),
            Stages: static (_, show) => [new AstStage(Uncornering, tree => new Handed(tree, Lays[show.Named]))],
            Builder: typeof(Builder))
            {
                Editing = new Uncorners(),
            });
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

    /// <summary>An element showing <paramref name="source"/> in a language one of whose stages falls over.</summary>
    public static ContentElement Unstageable(string source) => new(source, StyleFormat.Dark, new ContentEngine(), Unstaging);

    /// <summary>An element showing <paramref name="source"/> in a language whose binding falls over.</summary>
    public static ContentElement Unbindable(string source) => new(source, StyleFormat.Dark, new ContentEngine(), Unbinding);

    /// <summary>An element showing <paramref name="source"/> in a language whose builder is not one.</summary>
    public static ContentElement Unbuildable(string source) => new(source, StyleFormat.Dark, new ContentEngine(), Unbuilding);

    /// <summary>A fixed box, so a block in a test language is something drawn and something to hover over.</summary>
    private static Laid Boxed()
    {
        var build = new LayoutBuilder();
        build.Open("hand-boxed");
        build.Draw(new RuleMark(new Rect(0, 0, 60, 20), Brushes.Blue));
        build.Close();

        return new Laid(build.Seal(), new Size(60, 20), []);
    }

    /// <summary>A language that falls over when asked what its block's corner offers.</summary>
    private sealed class Uncorners : IContentLanguage
    {
        public BlockCorner Corner(ContentAsk ask) => throw new InvalidOperationException("no corner");
    }

    /// <summary>The content, carrying how the test lays it out — hung on it by a stage, as any language's stages hang what they work out.</summary>
    private sealed class Handed : ContentNode
    {
        public Handed(ContentNode written, Func<EditState, bool, Laid> lay) : base(written) => Lay = lay;

        public Func<EditState, bool, Laid> Lay { get; }

        protected override ContentNode Reshaped(ContentNode shape) => new Handed(shape, Lay);
    }

    private sealed class Builder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : ContentBuilder(reading, state, style, isReadOnly, nesting)
    {
        protected override Laid? Build() => ((Handed)Reading.Root.Node).Lay(State, IsReadOnly);

        protected override FormattedText Characters(string text) =>
            new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Style.TextSize, Brushes.Black, 1);
    }
}
