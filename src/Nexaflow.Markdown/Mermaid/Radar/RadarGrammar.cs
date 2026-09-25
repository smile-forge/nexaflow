using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Radar.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Radar;

/// <summary>
/// What a <c>radar-beta</c> block says beyond the lines every diagram shares: a <c>title</c>, the <c>axis</c> lines naming its
/// spokes, the <c>curve</c> lines drawn over them, and the options <c>max</c>, <c>min</c>, <c>ticks</c>, <c>graticule</c> and
/// <c>showLegend</c>.
///
/// <para>
/// The rules are Mermaid's. A name is a word — letters, digits, <c>_</c> and <c>-</c>, starting with a letter or an underscore
/// — and a label is written in brackets after it: <c>m["Math"]</c>. A line names several axes or several curves, a comma
/// between each. A curve's values follow its name and label in braces, a comma between each: numbers, one per axis in the
/// order the axes are written — <c>{85, 90, 80}</c> — or each naming the axis it is for — <c>{ m: 85, s: 90 }</c>. Options may
/// share a line, a comma between each: <c>max 100, min 0</c>.
/// </para>
/// <para>
/// Beyond Mermaid, a name may be written in quotes — which is how one renamed to hold a space still reads — and a label
/// without them. Which axis each value is for, and whether a curve gives each axis one, is a fact about every axis the block
/// writes rather than about the curve's own line, so it is the stage's (<see cref="ResolveCurves"/>). What is written half way
/// — an axis still to name, a value still to come, braces not yet closed — is read as far as it goes.
/// </para>
/// </summary>
public sealed class RadarGrammar : IMermaidGrammar
{
    public const string Axis = "axis";
    public const string Curve = "curve";
    public const string Max = "max";
    public const string Min = "min";
    public const string Ticks = "ticks";
    public const string Graticule = "graticule";
    public const string ShowLegend = "showLegend";

    /// <summary>What a graticule is drawn as.</summary>
    public const string Circle = "circle";
    public const string Polygon = "polygon";

    /// <inheritdoc/>
    /// <remarks>Mermaid allows a colon after <c>radar-beta</c>, and nothing else.</remarks>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments, comments: false);

        return line.Token(":") && line.Done
            ? line.Read(RadarKinds.Colon, MermaidRoles.Arguments)
            : ContentNode.Shown(arguments, "Nothing but a colon follows radar-beta on its line: axes, curves and options go on lines of their own.",
                                MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        return MermaidLine.Keyword(line.Written, MermaidLine.TitleWord, Axis, Curve, Max, Min, Ticks, Graticule, ShowLegend) switch
        {
            MermaidLine.TitleWord => line.Title(),
            Axis => Axes(line),
            Curve => Curves(line),
            null => line.Shown("A radar line is a title, an axis, a curve or an option: axis m[\"Math\"], curve a[\"Alice\"]{85}, max 100."),
            _ => Options(line),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Under a curve, another curve; under an option, nothing, options being what a chart ends with; anywhere else, another
    /// axis. Each with its name still to write, and the caret where it goes.
    /// </remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => above?.Kind switch
    {
        RadarKinds.Curves => ("curve ", 6),
        RadarKinds.Options => null,
        _ => ("axis ", 5),
    };

    /// <inheritdoc/>
    /// <remarks>
    /// A bare name holds a word starting with a letter or an underscore, and is put in quotes to hold anything else; see
    /// <see cref="MermaidWriting.Escape"/> for quotes and labels.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        MermaidWriting.Escape(part, caret, text, (_, said) => Bare(said));

    /// <inheritdoc/>
    /// <remarks>An axis is declared where an <c>axis</c> line names it, and used wherever a curve's value names it.</remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var keys = block.SelfAndDescendants()
            .Where(part => part.Kind == RadarKinds.Entry)
            .Select(entry => entry.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name))
            .OfType<ContentPart>()
            .ToList();

        return
        [
            .. block.SelfAndDescendants()
                .Where(part => part.Kind == RadarKinds.Axis)
                .Select(axis => axis.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name))
                .OfType<ContentPart>()
                .Select(name => (Name: name, Said: Said(name)))
                .Where(declared => declared.Said.Length > 0)
                .Select(declared => new MermaidName(declared.Said, declared.Name, [.. keys.Where(key => Said(key) == declared.Said)])),
        ];

        static string Said(ContentPart name) => name.Words()?.Text ?? string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>Bare where it can be, and in quotes otherwise.</remarks>
    public string Naming(string name) => Bare(name) ? name : "\"" + name + "\"";

    /// <inheritdoc/>
    /// <remarks>
    /// Which axis each value is for is a fact about every axis the block writes, wherever it writes them, so it is worked out and
    /// hung underneath (<see cref="ResolveCurves"/>).
    /// </remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new ResolveCurves()];

    /// <inheritdoc/>
    /// <remarks>
    /// Where an axis's or a curve's name is still to write, and between a label's brackets or its quotes. A value gets none: it
    /// is drawn as how far a curve reaches, not as anything to write in.
    /// </remarks>
    public bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Name or MermaidKinds.Label
        || (node.Kind == MermaidKinds.Quoted && holder?.Kind == MermaidKinds.Label);

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>
    /// An <c>axis</c> line: <c>axis m["Math"], s["Science"]</c> — or, with nothing yet after its word and the space left for it,
    /// an axis still to name, standing where typing its name puts it.
    /// </summary>
    private static ContentNode Axes(MermaidLine line)
    {
        const string Shape = "An axis line names its axes, a comma between each, each with its label after it where it has one: axis m[\"Math\"], s[\"Science\"].";

        line.Word(Axis);
        if (!Spaced(line)) return line.Shown(Shape);

        return line.Names(Named, Roles.Element, RadarRoles.Id, item: RadarKinds.Axis) && line.Done
            ? line.Read(RadarKinds.Axes)
            : line.Shown(Shape);
    }

    /// <summary>
    /// A <c>curve</c> line: <c>curve a["Alice"]{85, 90}, b{70, 75}</c>. A curve whose values are not written yet is still a
    /// curve, being written.
    /// </summary>
    private static ContentNode Curves(MermaidLine line)
    {
        const string Shape = "A curve line names its curves, a comma between each, each with its label and its values in braces after it: curve a[\"Alice\"]{85, 90, 80}.";

        line.Word(Curve);
        if (!Spaced(line)) return line.Shown(Shape);

        return line.Names(Drawn, Roles.Element, RadarRoles.Id, item: RadarKinds.Curve) && line.Done
            ? line.Read(RadarKinds.Curves)
            : line.Shown(Shape);
    }

    /// <summary>An axis: its name, and its label where one is written.</summary>
    private static bool Named(MermaidLine line) => Id(line) && Labelled(line);

    /// <summary>A curve: its name, its label where one is written, and its values where they are.</summary>
    private static bool Drawn(MermaidLine line)
    {
        if (!Named(line)) return false;
        if (line.Past != '{') return true;

        line.Space();
        Values(line);
        return true;
    }

    /// <summary>The label in brackets after a name, where one is written: <c>["Math"]</c>.</summary>
    private static bool Labelled(MermaidLine line)
    {
        if (line.Past != '[') return true;

        line.Space();
        return line.Label("[", "]", RadarRoles.Label);
    }

    /// <summary>
    /// A curve's values in their braces: <c>{85, 90, 80}</c>, or <c>{ m: 85, s: 90 }</c>. A comma with nothing after it yet is
    /// followed by a value still to come. Braces never closed are still read, with the reason on them, so a curve being written
    /// is drawn as far as it goes.
    /// </summary>
    private static void Values(MermaidLine line)
    {
        line.Open();
        line.Token("{", Roles.Open);
        line.Space();

        if (!line.Done && line.Next != '}')
        {
            while (true)
            {
                Entry(line);
                line.Space();
                if (!line.Token(",")) break;
                line.Room();
            }
        }

        var closed = line.Token("}", Roles.Close);
        line.Close(RadarKinds.Values, trouble: closed ? null : "These values are never closed with }.");
    }

    /// <summary>One value: a number — or the axis it is for, a colon, and a number.</summary>
    private static void Entry(MermaidLine line)
    {
        line.Open();

        var start = line.Save();
        if (line.Name(RadarRoles.Key, Letter, Trouble) && line.Past == ':')
        {
            line.Space();
            line.Token(":");
            line.Room();
        }
        else
        {
            line.Restore(start);
        }

        line.Amount(RadarRoles.Value, MermaidNumber.Where(number => number >= 0, "A value is a number, nought or more."), until: ",}");
        line.Close(RadarKinds.Entry);
    }

    /// <summary>A line of options, a comma between each: <c>max 100, graticule polygon</c>.</summary>
    private static ContentNode Options(MermaidLine line)
    {
        const string Shape = "An option line sets max, min, ticks, graticule or showLegend, a comma between each: max 100, graticule polygon.";

        while (true)
        {
            if (MermaidLine.Keyword(line.Rest, Max, Min, Ticks, Graticule, ShowLegend) is not { } word) return line.Shown(Shape);

            line.Open();
            line.Word(word);
            line.Room();
            Set(line, word);
            line.Close(RadarKinds.Option);

            line.Space();
            if (!line.Token(",")) break;

            line.Room();
            if (line.Done) break;
        }

        return line.Done ? line.Read(RadarKinds.Options) : line.Shown(Shape);
    }

    /// <summary>What an option is set to, up to the comma before the next — with what is wrong with it, where anything is.</summary>
    private static void Set(MermaidLine line, string word)
    {
        const string Until = ",";

        switch (word)
        {
            case Max or Min:
                line.Amount(RadarRoles.Value, MermaidNumber.Where(number => number >= 0, $"{word} is a number, nought or more."), Until);
                break;

            case Ticks:
                line.Amount(RadarRoles.Value, MermaidNumber.Where(number => number >= 0 && number == Math.Floor(number),
                                                                  "ticks is a whole number: how many rings the graticule has."), Until);
                break;

            case Graticule:
                line.Setting(RadarRoles.Value, value => Is(value, Circle, Polygon) ? null : "A graticule is a circle or a polygon.", Until);
                break;

            default:
                line.Setting(RadarRoles.Value, value => Is(value, "true", "false") ? null : "showLegend is true or false.", Until);
                break;
        }
    }

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>A name, bare or in quotes.</summary>
    private static bool Id(MermaidLine line) => line.Name(RadarRoles.Id, Letter, Trouble);

    /// <summary>What a bare name is made of.</summary>
    private static bool Letter(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '-';

    /// <summary>What is wrong with a bare name, where anything is.</summary>
    private static string? Trouble(string name) =>
        char.IsAsciiLetter(name[0]) || name[0] == '_' ? null : "A name starts with a letter or an underscore, or is written in quotes.";

    /// <summary>Whether a name can be written without quotes.</summary>
    private static bool Bare(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(Letter);

    private static bool Is(string value, params string[] words) => words.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Takes the space after a line's word — and, where nothing more is written, the space left for what follows it — or says
    /// there is none, which is a word run into what follows it.
    /// </summary>
    private static bool Spaced(MermaidLine line)
    {
        var at = line.At;
        line.Room();
        return line.At > at;
    }
}
