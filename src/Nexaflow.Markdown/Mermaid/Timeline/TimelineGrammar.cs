using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Timeline.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Timeline;

/// <summary>
/// What a <c>timeline</c> block says beyond the lines every diagram shares: a <c>title</c>, which way it runs, the
/// <c>section</c>s grouping its periods, and the periods with their events.
///
/// <para>
/// The rules are Mermaid's. A period is what it is called and then an event after each colon — <c>2004 : Facebook :
/// Google</c> — and a line starting with a colon goes on adding events to the period above it. Every colon splits, so a
/// colon inside what something says is written <c>#colon;</c> (<see cref="MermaidText"/>), which is what typing one in
/// writes. The way it runs is <c>direction LR</c> or <c>direction TD</c>, and may follow the keyword: <c>timeline TD</c>.
/// </para>
/// <para>
/// Which section a period is in, and which period a line of further events belongs to, are facts about the block rather
/// than about those lines, so they are the stage's (<see cref="ResolveSections"/>).
/// </para>
/// </summary>
public sealed class TimelineGrammar : IMermaidGrammar
{
    public const string SectionWord = "section";
    public const string DirectionWord = "direction";

    /// <summary>The ways a timeline runs: across the page, or down it — <c>TB</c> being Mermaid's other word for down.</summary>
    public static readonly IReadOnlyList<string> Ways = ["LR", "TD", "TB"];

    private const string Running = "A timeline runs LR, across the page, or TD, down it.";
    private const string Grouping = "A section names the periods it groups: section Early days.";
    private const string Timing = "A period is what it is called, then an event after each colon: 2004 : Facebook : Google.";

    /// <inheritdoc/>
    /// <remarks>The way it runs, written after the keyword: <c>timeline TD</c>.</remarks>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments);
        if (line.Done || MermaidLine.Keyword(line.Written, [.. Ways]) is null) return null;

        line.Setting(TimelineRoles.Way, Known);
        return line.Done ? line.Read(TimelineKinds.Direction) : null;
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        switch (MermaidLine.Keyword(line.Written, [MermaidLine.TitleWord, SectionWord, DirectionWord]))
        {
            case MermaidLine.TitleWord: return line.Title();
            case SectionWord: return Section(line);
            case DirectionWord: return Way(line);
        }

        // A line starting with a colon is more events for the period above it; anything else is a period of its own.
        return line.Next == ':' ? More(line) : Period(line);
    }

    /// <inheritdoc/>
    /// <remarks>Under a period, or under more of its events, another event; anywhere else nothing, since a period is named before it says anything.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) =>
        above?.Kind is TimelineKinds.Period or TimelineKinds.More ? (": ", 2) : null;

    /// <inheritdoc/>
    /// <remarks>
    /// Every colon splits a period from its events, and a <c>%%</c> closes the line, so both go in as the entity codes
    /// standing for them — which is how Mermaid writes a colon inside what something says.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Parent is not { Kind: TimelineKinds.Text } || !text.Any(character => character is ':' or '%')) return null;

        var written = text.Replace(":", "#colon;", StringComparison.Ordinal).Replace("%", "#37;", StringComparison.Ordinal);
        return new MermaidWriting(caret, caret, written, caret + written.Length);
    }

    /// <inheritdoc/>
    /// <remarks>Which section each period is in, and which period each line of further events belongs to (<see cref="ResolveSections"/>).</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new ResolveSections()];

    /// <inheritdoc/>
    /// <remarks>Where a period's name, an event or a section's name is still to write.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind == TimelineKinds.Text;

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>A period: what it is called, and an event after each colon.</summary>
    private static ContentNode Period(MermaidLine line)
    {
        Text(line, TimelineRoles.Says, until: ":");
        Events(line);

        return line.Done ? line.Read(TimelineKinds.Period) : line.Shown(Timing);
    }

    /// <summary>More events for the period above: <c>: Orkut</c>.</summary>
    private static ContentNode More(MermaidLine line)
    {
        Events(line);
        return line.Done ? line.Read(TimelineKinds.More) : line.Shown(Timing);
    }

    /// <summary>An event after each colon, the one still being written included.</summary>
    private static void Events(MermaidLine line)
    {
        line.Space();

        while (line.Token(":"))
        {
            line.Room();
            if (line.Done) return;

            Text(line, TimelineRoles.Event, until: ":");
            line.Space();
        }
    }

    /// <summary>A section: <c>section Early days</c>.</summary>
    private static ContentNode Section(MermaidLine line)
    {
        line.Word(SectionWord);
        if (!Spaced(line)) return line.Shown(Grouping);

        Text(line, TimelineRoles.Name);
        return line.Done ? line.Read(TimelineKinds.Section) : line.Shown(Grouping);
    }

    /// <summary>Which way it runs: <c>direction TD</c>.</summary>
    private static ContentNode Way(MermaidLine line)
    {
        line.Word(DirectionWord);
        if (!Spaced(line)) return line.Shown(Running);

        // The way it runs is still to write.
        if (line.Done) return line.Read(TimelineKinds.Direction);

        line.Setting(TimelineRoles.Way, Known);
        return line.Done ? line.Read(TimelineKinds.Direction) : line.Shown(Running);
    }

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>Text as a piece of its own: what is written up to <paramref name="until"/>, or to the end of the line.</summary>
    private static void Text(MermaidLine line, string role, string? until = null)
    {
        line.Open();
        line.Words(role, until: until);
        line.Close(TimelineKinds.Text, role);
    }

    /// <summary>What is wrong with a way that is none of them.</summary>
    private static string? Known(string said) =>
        Ways.Any(way => way.Equals(said.Trim(), StringComparison.OrdinalIgnoreCase)) ? null : Running;

    /// <summary>Takes the space after a line's word — and, where nothing more is written, the space left for what follows.</summary>
    private static bool Spaced(MermaidLine line)
    {
        var at = line.At;
        line.Room();
        return line.At > at;
    }
}
