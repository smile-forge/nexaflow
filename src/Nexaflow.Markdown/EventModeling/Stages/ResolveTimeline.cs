using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.EventModeling.Stages;

/// <summary>
/// What an event model's frames come to together (<see cref="EventModelingFrameNode"/>): each frame's type, the swimlane it is in,
/// what feeds it and the data block it shows — and the block's swimlanes and what its front matter and directives set
/// (<see cref="EventModelingBlockNode"/>). What a note and a specification are about is said on them (<see cref="EventModelingAboutNode"/>).
///
/// <para>
/// <strong>The rules are Mermaid's.</strong> A ui and a processor go in the UI/Automation lane, a command and a read model in the
/// Command/Read Model lane, an event in the Events lane — and a frame whose name has a namespace, <c>Inventory.ItemAdded</c>, in a lane
/// of its own under its group's, as Mermaid's own drawing puts every such frame in a new lane. A frame is fed by the frames its
/// <c>->></c> name, which are found among the frames written above it; a frame naming none is fed by the nearest frame above it in
/// another lane, unless it is a reset frame or the first.
/// </para>
/// <para>
/// <strong>What is wrong is said where it is written</strong>, and the rest still drawn: a number given to two frames, a <c>->></c>
/// naming no frame above, a frame fed by what the event modeling grammar's own rules say cannot feed it — a command only by a ui or a
/// processor, an event only by a command, a read model only by an event, a ui and a processor only by a read model — a <c>[[name]]</c>
/// naming no data block, a note or a specification naming no frame, and a specification naming no entity; and a setting given what it
/// cannot take.
/// </para>
/// </summary>
public sealed class ResolveTimeline : IAstStage
{
    /// <summary>How much room is left round the diagram where nothing says, as Mermaid leaves it.</summary>
    public const double Padding = 30;

    /// <summary>The theme variables an event model reads its colours from.</summary>
    public static readonly IReadOnlyList<string> ThemeVariables =
    [
        "emUiFill", "emUiStroke", "emProcessorFill", "emProcessorStroke", "emCommandFill", "emCommandStroke", "emReadModelFill",
        "emReadModelStroke", "emEventFill", "emEventStroke", "emRelationStroke", "emArrowhead", "emSwimlaneBackgroundOdd",
        "emSwimlaneBackgroundStroke",
    ];

    public string Name => "eventmodeling:timeline";

    public ContentNode Run(ContentNode tree)
    {
        var marks = new Dictionary<ContentNode, string>(ReferenceEqualityComparer.Instance);
        var settings = EventModelingSettings.Of(tree);

        var padding = Number(settings, "config.eventmodeling.padding", Padding, marks);
        var theme = Theme(settings, marks);

        var data = Named(tree, EventModelingKinds.Data, node => node.Part(EventModelingRoles.DataName)?.Text);
        var entities = Named(tree, EventModelingKinds.Entity, node => Called(node));

        var frames = tree.Children.Where(node => node.Kind == EventModelingKinds.Frame && Typed(node) is not null).ToList();
        var said = new Dictionary<ContentNode, ContentNode>(ReferenceEqualityComparer.Instance);

        var numbered = new Dictionary<string, int>(StringComparer.Ordinal);
        var lanes = new Dictionary<int, EventModelingLane>();
        var laneOf = new int[frames.Count];
        var types = new EventModelingType[frames.Count];

        for (var at = 0; at < frames.Count; at++)
        {
            var frame = frames[at];
            types[at] = Typed(frame)!.Value;

            var name = Words(frame);
            var space = name.Count == 2 ? name[0] : null;
            laneOf[at] = Lane(types[at], space, lanes);

            var feeds = Feeds(frame, at, frames, numbered, types, laneOf, marks);

            if (frame.Part(EventModelingRoles.Frame) is { } number && !numbered.TryAdd(number.Text, at))
                marks[number] = string.Format(CultureInfo.InvariantCulture, "Frame {0} is numbered above already: each frame has a number of its own.", number.Text);

            var shows = Shows(frame, data, marks);
            said[frame] = new EventModelingFrameNode(Marked(frame, marks), at, types[at], Reset(frame), laneOf[at], feeds, shows, space is not null);
        }

        // The lanes top to bottom, as their places say: each group's own, then its namespaces' in the order they were made.
        var order = lanes.Keys.Order().ToList();
        var placed = order.Select((key, index) => (key, index)).ToDictionary(pair => pair.key, pair => pair.index);

        foreach (var (frame, node) in said.ToList())
        {
            var it = (EventModelingFrameNode)node;
            said[frame] = new EventModelingFrameNode(node, it.Order, it.Type, it.Reset, placed[laneOf[it.Order]], it.Feeds, it.Shows, it.Namespaced);
        }

        var children = tree.Children.Select(child => child.Kind switch
        {
            _ when said.TryGetValue(child, out var frame) => frame,
            EventModelingKinds.Data when data.TryGetValue(child.Part(EventModelingRoles.DataName)?.Text ?? string.Empty, out var key) && ReferenceEquals(key.Node, child)
                => new EventModelingDataNode(Marked(child, marks), key.Key),
            EventModelingKinds.Note or EventModelingKinds.Specification => About(child, numbered, entities, marks),
            _ => Marked(child, marks),
        }).ToList();

        return new EventModelingBlockNode(tree.With(children), [.. order.Select(key => lanes[key])], theme, padding);
    }

    /// <summary>The type a frame is written with, or null for one whose type was never written.</summary>
    private static EventModelingType? Typed(ContentNode frame) => frame.Part(EventModelingRoles.Type)?.Text switch
    {
        "ui" => EventModelingType.Ui,
        "pcr" or "processor" => EventModelingType.Processor,
        "cmd" or "command" => EventModelingType.Command,
        "rmo" or "readmodel" => EventModelingType.ReadModel,
        "evt" or "event" => EventModelingType.Event,
        _ => null,
    };

    /// <summary>Whether a frame is a reset frame.</summary>
    private static bool Reset(ContentNode frame) =>
        frame.Children.FirstOrDefault()?.Text is EventModelingParser.ResetFrameWord or EventModelingParser.ResetFrameLongWord;

    /// <summary>The words of what a statement is called, the dots between them left out.</summary>
    private static List<string> Words(ContentNode statement) =>
        statement.Children.FirstOrDefault(child => child.Kind == EventModelingKinds.Name) is { } name
            ? [.. name.Children.Where(part => part.Role == EventModelingRoles.Name).Select(part => part.Text)]
            : [];

    /// <summary>What a statement is called, its words joined by dots as written — or null where it is called nothing yet.</summary>
    private static string? Called(ContentNode statement) => Words(statement) is { Count: > 0 } words ? string.Join('.', words) : null;

    /// <summary>
    /// The lane a frame is in, by its place in Mermaid's numbering: a group's own lane at 0, 100 or 200, and a frame with a namespace in
    /// the next place under its group's, a new lane for each such frame as Mermaid's drawing makes one.
    /// </summary>
    private static int Lane(EventModelingType type, string? space, Dictionary<int, EventModelingLane> lanes)
    {
        var group = type switch
        {
            EventModelingType.Ui or EventModelingType.Processor => EventModelingGroup.Automation,
            EventModelingType.Command or EventModelingType.ReadModel => EventModelingGroup.Commands,
            _ => EventModelingGroup.Events,
        };

        var floor = (int)group * 100;
        var place = space is null ? floor : Math.Max(floor, lanes.Keys.Where(key => key > floor && key < floor + 100).DefaultIfEmpty(floor).Max()) + 1;

        lanes.TryAdd(place, new EventModelingLane(group, space));
        return place;
    }

    /// <summary>
    /// What feeds a frame: the frames its <c>->></c> name among those above it — each checked against what may feed it — or, where it names
    /// none, the nearest frame above it in another lane, unless it is a reset frame or the first.
    /// </summary>
    private static List<EventModelingFeed> Feeds(ContentNode frame, int at, List<ContentNode> frames, Dictionary<string, int> numbered,
                                                 EventModelingType[] types, int[] laneOf, Dictionary<ContentNode, string> marks)
    {
        var feeds = new List<EventModelingFeed>();
        var written = frame.Children.Where(child => child.Kind == EventModelingKinds.Feed).ToList();

        for (var said = 0; said < written.Count; said++)
        {
            if (written[said].Part(EventModelingRoles.Source) is not { } number) continue;

            if (!numbered.TryGetValue(number.Text, out var from))
            {
                marks[number] = string.Format(CultureInfo.InvariantCulture, "No frame numbered {0} is written above this one to feed it.", number.Text);
                continue;
            }

            if (Fault(types[at], types[from]) is { } fault) marks[number] = fault;
            feeds.Add(new EventModelingFeed(from, said));
        }

        if (written.Count > 0 || at == 0 || Reset(frame)) return feeds;

        for (var before = at - 1; before >= 0; before--)
        {
            if (laneOf[before] == laneOf[at]) continue;

            feeds.Add(new EventModelingFeed(before, Written: null));
            break;
        }

        return feeds;
    }

    /// <summary>What is wrong with a frame of <paramref name="type"/> fed by one of <paramref name="from"/>, by the grammar's own rules — or null where nothing is.</summary>
    private static string? Fault(EventModelingType type, EventModelingType from)
    {
        (EventModelingType[] Allowed, string What) rule = type switch
        {
            EventModelingType.Command => ([EventModelingType.Ui, EventModelingType.Processor], "A command is fed by a ui or a processor"),
            EventModelingType.Event => ([EventModelingType.Command], "An event is fed by a command"),
            EventModelingType.ReadModel => ([EventModelingType.Event], "A read model is fed by an event"),
            EventModelingType.Processor => ([EventModelingType.ReadModel], "A processor is fed by a read model"),
            _ => ([EventModelingType.ReadModel], "A ui is fed by a read model"),
        };

        return rule.Allowed.Contains(from) ? null : $"{rule.What}, not by {Said(from)}.";
    }

    /// <summary>What a type is called in a sentence.</summary>
    private static string Said(EventModelingType type) => type switch
    {
        EventModelingType.Ui => "a ui",
        EventModelingType.Processor => "a processor",
        EventModelingType.Command => "a command",
        EventModelingType.ReadModel => "a read model",
        _ => "an event",
    };

    /// <summary>The data block a frame names with <c>[[name]]</c>, by where it comes among the data blocks — null where it names none, and marked where it names one not written.</summary>
    private static int? Shows(ContentNode frame, Dictionary<string, (int Key, ContentNode Node)> data, Dictionary<ContentNode, string> marks)
    {
        if (frame.Children.FirstOrDefault(child => child.Kind == EventModelingKinds.Reference)?.Part(EventModelingRoles.DataName) is not { } name) return null;
        if (data.TryGetValue(name.Text, out var block)) return block.Key;

        marks[name] = $"No data block named {name.Text} is written.";
        return null;
    }

    /// <summary>A note or a specification, said to be about the frame it names — and marked where it names no frame, or a specification an entity not declared.</summary>
    private static ContentNode About(ContentNode statement, Dictionary<string, int> numbered, Dictionary<string, (int Key, ContentNode Node)> entities,
                                     Dictionary<ContentNode, string> marks)
    {
        foreach (var mention in statement.SelfAndDescendants().Where(node => node.Role == EventModelingRoles.Entity))
            if (!entities.ContainsKey(mention.Text))
                marks[mention] = $"No entity named {mention.Text} is written: entity {mention.Text} declares one.";

        if (statement.Part(EventModelingRoles.Source) is not { } number) return Marked(statement, marks);

        if (numbered.TryGetValue(number.Text, out var frame)) return new EventModelingAboutNode(Marked(statement, marks), frame);

        marks[number] = string.Format(CultureInfo.InvariantCulture, "No frame numbered {0} is written.", number.Text);
        return Marked(statement, marks);
    }

    /// <summary>The statements of <paramref name="kind"/>, by what each is called, the first written of each name kept — and where it comes among them.</summary>
    private static Dictionary<string, (int Key, ContentNode Node)> Named(ContentNode tree, string kind, Func<ContentNode, string?> called)
    {
        var named = new Dictionary<string, (int Key, ContentNode Node)>(StringComparer.Ordinal);
        var key = 0;

        foreach (var statement in tree.Children.Where(node => node.Kind == kind))
            if (called(statement) is { Length: > 0 } name) named.TryAdd(name, (key++, statement));

        return named;
    }

    /// <summary>A number a setting holds, or <paramref name="fallback"/> — its value marked where it holds something else.</summary>
    private static double Number(IReadOnlyDictionary<string, EventModelingSettings.Setting> settings, string path, double fallback,
                                 Dictionary<ContentNode, string> marks)
    {
        if (!settings.TryGetValue(path, out var set)) return fallback;
        if (SettingValues.Read(set.Value) is { } read && read >= 0) return read;

        if (set.Written is { } written) marks[written] = $"{path[(path.LastIndexOf('.') + 1)..]} is a number of nought or more.";
        return fallback;
    }

    /// <summary>The theme's colours the block sets — each value marked where it is no colour.</summary>
    private static Dictionary<string, HexColor> Theme(IReadOnlyDictionary<string, EventModelingSettings.Setting> settings, Dictionary<ContentNode, string> marks)
    {
        var theme = new Dictionary<string, HexColor>(StringComparer.Ordinal);

        foreach (var name in ThemeVariables)
        {
            if (!settings.TryGetValue("config.themeVariables." + name, out var set)) continue;

            if (HexColor.TryParse(set.Value, out var colour)) theme[name] = colour;
            else if (set.Written is { } written) marks[written] = $"{name} is a colour written as #rrggbb.";
        }

        return theme;
    }

    /// <summary><paramref name="node"/> with every piece in it that something is wrong with saying so.</summary>
    private static ContentNode Marked(ContentNode node, Dictionary<ContentNode, string> marks) =>
        marks.Count == 0 ? node : AstRewrite.Each(node, piece => marks.TryGetValue(piece, out var reason) ? piece.Saying(reason) : piece);
}
