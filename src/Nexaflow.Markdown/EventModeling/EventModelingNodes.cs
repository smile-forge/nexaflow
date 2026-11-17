using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.EventModeling;

/// <summary>What a frame is: the entity type it is written with, short form or long.</summary>
public enum EventModelingType
{
    Ui,
    Processor,
    Command,
    ReadModel,
    Event,
}

/// <summary>Which of the three swimlanes a frame's type puts it in, before a namespace gives it a lane of its own.</summary>
public enum EventModelingGroup
{
    /// <summary>UI/Automation: a ui and a processor.</summary>
    Automation,

    /// <summary>Command/Read Model: a command and a read model.</summary>
    Commands,

    /// <summary>Events.</summary>
    Events,
}

/// <summary>A swimlane: the group it is one of, and the namespace it is for — null for the group's own lane.</summary>
public sealed record EventModelingLane(EventModelingGroup Group, string? Namespace);

/// <summary>
/// What feeds a frame: the frame feeding it, by where it is written among the frames, and which of the frame's <c>->></c> wrote it —
/// null where it was inferred from the frames before it, as Mermaid infers one.
/// </summary>
public readonly record struct EventModelingFeed(int From, int? Written);

/// <summary>
/// An <c>eventmodeling</c> block as its stage leaves it (<see cref="Stages.ResolveTimeline"/>): its swimlanes in the order they are
/// drawn, and what its front matter and directives set. It prints as the block written.
/// </summary>
internal sealed class EventModelingBlockNode : BlockNode
{
    internal EventModelingBlockNode(ContentNode written, IReadOnlyList<EventModelingLane> lanes, IReadOnlyDictionary<string, HexColor> theme,
                                    double padding) : base((BlockNode)written)
    {
        this.Lanes = lanes;
        this.Theme = theme;
        this.Padding = padding;
    }

    /// <summary>The swimlanes, top to bottom.</summary>
    public IReadOnlyList<EventModelingLane> Lanes { get; }

    /// <summary>The theme's colours the block sets, by the variable that sets each — <c>emEventFill</c>, <c>emRelationStroke</c>.</summary>
    public IReadOnlyDictionary<string, HexColor> Theme { get; }

    /// <summary>How much room is left round the diagram.</summary>
    public double Padding { get; }


    protected override BlockNode Retyped(BlockNode shape) => new EventModelingBlockNode(shape, this.Lanes, this.Theme, this.Padding);
}

/// <summary>
/// A frame as its stage leaves it: where it comes among the frames, what it is, the swimlane it is in, what feeds it, the data block
/// it shows where it names one, and whether the first word of its name is its lane's namespace rather than its own.
/// </summary>
internal sealed class EventModelingFrameNode : ContentNode
{
    internal EventModelingFrameNode(ContentNode written, int order, EventModelingType type, bool reset, int lane,
                                    IReadOnlyList<EventModelingFeed> feeds, int? shows, bool namespaced) : base(written)
    {
        this.Order = order;
        this.Type = type;
        this.Reset = reset;
        this.Lane = lane;
        this.Feeds = feeds;
        this.Shows = shows;
        this.Namespaced = namespaced;
    }

    /// <summary>Where it comes among the frames, as written — which is where it stands along the timeline.</summary>
    public int Order { get; }

    public EventModelingType Type { get; }

    /// <summary>Whether it is a reset frame, which nothing before it feeds unless it says so.</summary>
    public bool Reset { get; }

    /// <summary>The swimlane it is in, counted from the top.</summary>
    public int Lane { get; }

    /// <summary>What feeds it.</summary>
    public IReadOnlyList<EventModelingFeed> Feeds { get; }

    /// <summary>The data block it shows, by where that block comes among the data blocks — or null where it names none, or one not written.</summary>
    public int? Shows { get; }

    /// <summary>Whether the first word of its name is its lane's namespace, so only the second is drawn on it.</summary>
    public bool Namespaced { get; }

    protected override ContentNode Reshaped(ContentNode shape) =>
        new EventModelingFrameNode(shape, this.Order, this.Type, this.Reset, this.Lane, this.Feeds, this.Shows, this.Namespaced);
}

/// <summary>A <c>data</c> block as its stage leaves it: where it comes among the data blocks, which is what a frame showing it names it by.</summary>
internal sealed class EventModelingDataNode : ContentNode
{
    internal EventModelingDataNode(ContentNode written, int key) : base(written) => this.Key = key;

    public int Key { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new EventModelingDataNode(shape, this.Key);
}

/// <summary>A note or a specification as its stage leaves it: the frame it is about, by where that frame comes among the frames.</summary>
internal sealed class EventModelingAboutNode : ContentNode
{
    internal EventModelingAboutNode(ContentNode written, int frame) : base(written) => this.Frame = frame;

    public int Frame { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new EventModelingAboutNode(shape, this.Frame);
}
