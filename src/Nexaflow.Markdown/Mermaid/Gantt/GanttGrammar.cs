using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Gantt.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Gantt;

/// <summary>
/// What a <c>gantt</c> block says beyond the lines every diagram shares: its settings — <c>dateFormat</c>, <c>axisFormat</c>,
/// <c>tickInterval</c>, <c>excludes</c>, <c>includes</c>, <c>todayMarker</c>, <c>weekday</c>, <c>weekend</c>,
/// <c>inclusiveEndDates</c>, <c>topAxis</c> — a <c>title</c>, <c>section</c>s, tasks and <c>click</c>s.
///
/// <para>
/// The rules are Mermaid's. A task is its name, a colon, and its schedule, a comma between each item: its tags first —
/// <c>active</c>, <c>done</c>, <c>crit</c>, <c>milestone</c>, <c>vert</c> — then when it ends; before that when it starts, a
/// date or <c>after</c> other tasks; and before that its id: <c>Design :crit, a1, after a0, 3d</c>. An end is a date, a length
/// of time or <c>until</c> another task. A name is anything to its colon, so it cannot hold one.
/// </para>
/// <para>
/// Whether a date is one depends on the chart's <c>dateFormat</c>, and whether an id names a task on the rest of the chart, so
/// both are the stage's (<see cref="ResolveSchedule"/>).
/// </para>
/// </summary>
public sealed class GanttGrammar : IMermaidGrammar
{
    public const string Section = "section";
    public const string Click = "click";
    public const string After = "after";
    public const string Until = "until";

    /// <summary>The lines setting something for the whole chart, each its word and what it says.</summary>
    public static readonly IReadOnlyList<string> Settings =
        ["dateFormat", "axisFormat", "tickInterval", "excludes", "includes", "todayMarker", "weekday", "weekend", "accDescription"];

    /// <summary>The lines that are only their word.</summary>
    public static readonly IReadOnlyList<string> Flags = ["inclusiveEndDates", "topAxis"];

    /// <summary>A task's tags, written before anything else in its schedule.</summary>
    public static readonly IReadOnlyList<string> Tags = ["active", "done", "crit", "milestone", "vert"];

    /// <summary>The units a <c>tickInterval</c> counts in.</summary>
    public static readonly IReadOnlyList<string> Intervals = ["millisecond", "second", "minute", "hour", "day", "week", "month"];

    public static readonly IReadOnlyList<string> Weekdays = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        return MermaidLine.Keyword(line.Written, [MermaidLine.TitleWord, Section, Click, .. Settings, .. Flags]) switch
        {
            MermaidLine.TitleWord => line.Title(),
            Section => SectionLine(line),
            Click => ClickLine(line),
            { } word when Flags.Contains(word) => FlagLine(line, word),
            { } word => SettingLine(line, word),
            null => Task(line),
        };
    }

    /// <inheritdoc/>
    /// <remarks>Under a task, a section or the header, a task with its name still to write, lasting a day; elsewhere nothing in particular.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) =>
        above is null || above.Kind is GanttKinds.Task or GanttKinds.Section ? (": 1d", 0) : null;

    /// <inheritdoc/>
    /// <remarks>A task's name runs to its colon, so a colon typed into one is not written; a link's quote is written as its entity code.</remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        return part.Parent is { Kind: GanttKinds.Text, Role: GanttRoles.Name } && text.Contains(':')
            ? new MermaidWriting(caret, caret, text.Replace(":", string.Empty, StringComparison.Ordinal), caret + text.Replace(":", string.Empty, StringComparison.Ordinal).Length)
            : null;
    }

    /// <inheritdoc/>
    /// <remarks>A task's id is declared in its schedule, and used by every <c>after</c>, <c>until</c> and <c>click</c> naming it.</remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var uses = block.SelfAndDescendants().Where(part => part is { Kind: MermaidKinds.Name, Role: GanttRoles.Reference }).ToList();

        return
        [
            .. block.SelfAndDescendants()
                .Where(part => part is { Kind: MermaidKinds.Name, Role: GanttRoles.Id })
                .Select(id => (Id: id, Said: Said(id)))
                .Where(declared => declared.Said.Length > 0)
                .Select(declared => new MermaidName(declared.Said, declared.Id, [.. uses.Where(use => Said(use) == declared.Said)])),
        ];

        static string Said(ContentPart name) => name.Words()?.Text ?? string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>An id is letters, digits, <c>-</c> and <c>_</c>, which is all an <c>after</c> or <c>until</c> can name.</remarks>
    public string Naming(string name) => new([.. name.Select(character => Letter(character) ? character : '_')]);

    /// <inheritdoc/>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new ResolveSchedule()];

    /// <inheritdoc/>
    /// <remarks>Where a name, a date or an id is still to write.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind is GanttKinds.Text or GanttKinds.When or MermaidKinds.Name;

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>A setting: <c>dateFormat YYYY-MM-DD</c> — what it says still to come where nothing is written yet.</summary>
    private static ContentNode SettingLine(MermaidLine line, string word)
    {
        line.Word(word);
        if (!Spaced(line)) return line.Shown($"{word} is followed by what it sets: {word} {Example(word)}.");

        line.Setting(GanttRoles.Of(word), value => Trouble(word, value));
        return line.Read(GanttKinds.Setting);
    }

    private static ContentNode FlagLine(MermaidLine line, string word)
    {
        line.Word(word);
        return line.Done ? line.Read(GanttKinds.Flag) : line.Shown($"{word} is a line of its own, with nothing after it.");
    }

    /// <summary>A section: <c>section Documentation</c> — its name still to write where nothing is written yet.</summary>
    private static ContentNode SectionLine(MermaidLine line)
    {
        line.Word(Section);
        if (!Spaced(line)) return line.Shown("A section is its word and its name: section Documentation.");

        Text(line, GanttRoles.SectionName);
        return line.Read(GanttKinds.Section);
    }

    /// <summary>A task: <c>Design :crit, a1, after a0, 3d</c>.</summary>
    private static ContentNode Task(MermaidLine line)
    {
        Text(line, GanttRoles.Name, until: ":");
        line.Space();
        if (!line.Token(":")) return line.Shown("A task is its name, a colon, and when it runs: Design :a1, 2014-01-01, 3d.");

        line.Room();
        Schedule(line);
        return line.Read(GanttKinds.Task);
    }

    /// <summary>A task's schedule: its tags, then its id, its start and its end, as many of those as are written.</summary>
    private static void Schedule(MermaidLine line)
    {
        var items = line.Rest.Split(',');
        var tags = 0;
        while (tags < items.Length && Tags.Contains(items[tags].Trim(), StringComparer.Ordinal)) tags++;

        var said = items.Length - tags;
        string[] roles = said switch
        {
            0 => [],
            1 => [GanttRoles.End],
            2 => [GanttRoles.Start, GanttRoles.End],
            _ => [GanttRoles.Id, GanttRoles.Start, GanttRoles.End, .. Enumerable.Repeat(GanttRoles.Extra, said - 3)],
        };

        line.Open();

        for (var item = 0; item < items.Length; item++)
        {
            if (item > 0)
            {
                line.Space();
                line.Token(",");
                line.Room();
            }

            if (item < tags)
            {
                line.Word(items[item].Trim(), GanttKinds.Tag, GanttRoles.Of("tag"));
                continue;
            }

            switch (roles[item - tags])
            {
                case GanttRoles.Id:
                    line.Open();
                    line.Words(GanttRoles.Id, until: ",");
                    line.Close(MermaidKinds.Name, GanttRoles.Id);
                    break;

                case GanttRoles.Start when MermaidLine.Keyword(line.Rest, After) is not null:
                    References(line, After, GanttKinds.After, GanttRoles.Start);
                    break;

                case GanttRoles.End when MermaidLine.Keyword(line.Rest, Until) is not null:
                    References(line, Until, GanttKinds.Until, GanttRoles.End);
                    break;

                case GanttRoles.Extra:
                    line.Words(GanttRoles.Extra, "A task says at most its id, when it starts and when it ends: a1, 2014-01-01, 3d.", until: ",");
                    break;

                case var role:
                    line.Open();
                    line.Words(role, until: ",");
                    line.Close(GanttKinds.When, role);
                    break;
            }
        }

        line.Close(GanttKinds.Schedule, trouble: said == 0 ? "A task says when it ends, after its tags: milestone, 2014-01-25, 0d." : null);
    }

    /// <summary><c>after a1 b2</c> or <c>until a1</c>: its word, and the ids it names with space between — one still to write where none is.</summary>
    private static void References(MermaidLine line, string word, string kind, string role)
    {
        line.Open();
        line.Word(word);
        line.Room();

        do
        {
            line.Open();
            line.Words(GanttRoles.Reference, until: ", \t");
            line.Close(MermaidKinds.Name, GanttRoles.Reference);

            var at = line.At;
            line.Space();
            if (line.At == at) break;
        }
        while (!line.Done && line.Next != ',');

        line.Close(kind, role);
    }

    /// <summary>A click: <c>click a1 href "https://…"</c>, <c>click a1,a2 call show(a1)</c> — a link and a call in either order.</summary>
    private static ContentNode ClickLine(MermaidLine line)
    {
        const string Shape = "A click names its tasks, then a link or a call: click a1 href \"https://mermaid.js.org\", click a1 call show().";

        line.Word(Click);
        if (!Spaced(line)) return line.Shown(Shape);

        while (true)
        {
            line.Open();
            line.Words(GanttRoles.Reference, until: ", \t");
            line.Close(MermaidKinds.Name, GanttRoles.Reference);
            if (!line.Token(",")) break;
        }

        while (!line.Done)
        {
            var at = line.At;
            line.Space();
            if (line.At == at) return line.Shown(Shape);

            if (line.Word("href"))
            {
                line.Open();
                line.Room();
                if (!line.Done && !line.Quoted(GanttRoles.Url, kind: null, what: "link")) return line.Shown(Shape);
                line.Close(GanttKinds.Link);
            }
            else if (line.Word("call"))
            {
                line.Open();
                line.Room();
                line.Words(GanttRoles.Callback, until: "(");
                line.Space();

                if (line.Token("(", Roles.Open))
                {
                    var close = Closing(line.Rest);
                    line.Add(ContentNode.Leaf(MermaidKinds.Words, close < 0 ? line.Rest : line.Rest[..close], GanttRoles.Arguments,
                                              close < 0 ? "A call's arguments are closed with )." : null));
                    line.Token(")", Roles.Close);
                }

                line.Close(GanttKinds.Call);
            }
            else
            {
                return line.Shown(Shape);
            }
        }

        return line.Read(GanttKinds.Click);
    }

    /// <summary>Where a call's arguments close: at the first <c>)</c> that nothing but a link or a call follows — or -1.</summary>
    private static int Closing(string rest)
    {
        for (var close = rest.IndexOf(')'); close >= 0; close = rest.IndexOf(')', close + 1))
        {
            var after = rest[(close + 1)..].TrimStart();
            if (after.Length == 0 || MermaidLine.Keyword(after, "href", "call") is not null) return close;
        }

        return -1;
    }

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>Text as a piece of its own, to <paramref name="until"/> or the end of the line — still to write where nothing is.</summary>
    private static void Text(MermaidLine line, string role, string? until = null)
    {
        line.Open();
        line.Words(role, until: until);
        line.Close(GanttKinds.Text, role);
    }

    private static string? Trouble(string word, string value) => word switch
    {
        "tickInterval" => Interval(value) ? null : "A tick interval is a whole number and a unit: 1day, 2week, 15minute.",
        "weekday" => Weekdays.Contains(value.ToLowerInvariant()) ? null : "A week starts on a day of the week: weekday monday.",
        "weekend" => value.ToLowerInvariant() is "friday" or "saturday" ? null : "A weekend starts on friday or saturday.",
        _ => null,
    };

    private static bool Interval(string value)
    {
        var digits = 0;
        while (digits < value.Length && char.IsAsciiDigit(value[digits])) digits++;
        return digits > 0 && value[0] != '0' && Intervals.Contains(value[digits..]);
    }

    private static string Example(string word) => word switch
    {
        "dateFormat" => "YYYY-MM-DD",
        "axisFormat" => "%Y-%m-%d",
        "tickInterval" => "1week",
        "excludes" or "includes" => "weekends",
        "todayMarker" => "off",
        "weekday" => "monday",
        "weekend" => "friday",
        _ => "…",
    };

    private static bool Letter(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '-';

    /// <summary>Takes the space after a line's word — and, where nothing more is written, the space left for what follows — or says there is none.</summary>
    private static bool Spaced(MermaidLine line)
    {
        var at = line.At;
        line.Room();
        return line.At > at || line.Done;
    }
}

