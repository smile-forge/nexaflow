using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>What an argument of a macro is to what is drawn for it.</summary>
internal enum C4Means
{
    /// <summary>What the element or the boundary is called, which everything naming it names it by.</summary>
    Alias,

    /// <summary>What is written across the top of it, or over a relationship's line.</summary>
    Label,

    /// <summary>What it is built with, or what a relationship is done with.</summary>
    Technology,

    /// <summary>The sentence saying what it does, or what a relationship is for.</summary>
    Description,

    /// <summary>The element a relationship leaves — the second one written, for a <c>Rel_Back</c>.</summary>
    From,

    /// <summary>And the one it reaches.</summary>
    To,
}

/// <summary>
/// An argument of a macro as the stages leave it (<see cref="Stages.ResolveMacros"/>): what it is to what is drawn. An argument is
/// given by where it is written or by the name it is given under — a name winning — and where it is written counts differently
/// for each macro, so what it means is the macro's to say. It prints as the argument written.
/// </summary>
internal sealed class C4ArgumentNode : ContentNode
{
    internal C4ArgumentNode(ContentNode written, C4Means means) : base(written) => this.Means = means;

    public C4Means Means { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new C4ArgumentNode(shape, this.Means);
}

/// <summary>
/// An element macro as the stages leave it (<see cref="Stages.ResolveMacros"/>): what its name says it is, the line in brackets
/// its card says — worked out from what it is, what it is built with and whether the block hides stereotypes — and what it is
/// painted with, which a tag, a style naming its kind or one naming it may say anywhere in the block. It prints as the line written.
/// </summary>
internal sealed class C4ElementNode : ContentNode
{
    internal C4ElementNode(ContentNode written, string id, C4Level level, C4Shape shape, bool external, string stereotype, C4Paint paint,
                           string? href, bool described) : base(written)
    {
        this.Id = id;
        this.Level = level;
        this.Shape = shape;
        this.External = external;
        this.Stereotype = stereotype;
        this.Paint = paint;
        this.Href = href;
        this.Described = described;
    }

    /// <summary>What it is called — its alias.</summary>
    public string Id { get; }

    public C4Level Level { get; }

    /// <summary>The outline it is drawn with: its macro's, unless a style asks for another.</summary>
    public C4Shape Shape { get; }

    /// <summary>Whether it is somebody else's — a macro ending <c>_Ext</c>.</summary>
    public bool External { get; }

    /// <summary>Which band of C4's grading it takes — see <see cref="C4Elements.Banded"/>.</summary>
    public int Tone => C4Elements.Banded(this.Level, this.External);

    /// <summary>The line in brackets under its name — empty where the block hides them.</summary>
    public string Stereotype { get; }

    /// <summary>What it is filled, written in and outlined with, where anything says — otherwise the theme's own.</summary>
    public C4Paint Paint { get; }

    /// <summary>Where a <c>$link</c> leads, where one was written.</summary>
    public string? Href { get; }

    /// <summary>Whether the block asks for descriptions under a lifeline's card, which <c>SHOW_ELEMENT_DESCRIPTIONS()</c> does.</summary>
    public bool Described { get; }

    protected override ContentNode Reshaped(ContentNode shape) =>
        new C4ElementNode(shape, this.Id, this.Level, this.Shape, this.External, this.Stereotype, this.Paint, this.Href, this.Described);
}

/// <summary>
/// A relationship macro as the stages leave it (<see cref="Stages.ResolveMacros"/>): the element it leaves and the one it reaches
/// — a <c>Rel_Back</c> pointing the other way — whether it points both ways, the stroke its tags and a style naming its ends ask
/// for, and the number it carries where the block counts them, which every <c>Index()</c>, <c>SetIndex()</c> and
/// <c>increment()</c> above it moves. It prints as the line written.
/// </summary>
internal sealed class C4RelationNode : ContentNode
{
    internal C4RelationNode(ContentNode written, string from, string to, bool both, C4Stroke stroke, string? number) : base(written)
    {
        this.From = from;
        this.To = to;
        this.Both = both;
        this.Stroke = stroke;
        this.Number = number;
    }

    public string From { get; }

    public string To { get; }

    /// <summary>A <c>BiRel</c>, which draws a head at each end.</summary>
    public bool Both { get; }

    public C4Stroke Stroke { get; }

    /// <summary>Its number where the block counts its relationships, and null where it does not.</summary>
    public string? Number { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new C4RelationNode(shape, this.From, this.To, this.Both, this.Stroke, this.Number);
}

/// <summary>
/// A boundary or a deployment node as the stages leave it (<see cref="Stages.ResolveMacros"/>): what it is called, the line in
/// brackets under its name, whether it is a real box — a deployment node, drawn solid rather than dashed — and what its tags and
/// a style naming it paint it. It prints as the line written.
/// </summary>
internal sealed class C4BoundaryNode : ContentNode
{
    internal C4BoundaryNode(ContentNode written, string alias, string? says, bool physical, C4Paint paint, string? href) : base(written)
    {
        this.Alias = alias;
        this.Says = says;
        this.Physical = physical;
        this.Paint = paint;
        this.Href = href;
    }

    public string Alias { get; }

    /// <summary>The line in brackets under its name — its <c>$type</c>, or what a deployment node runs on.</summary>
    public string? Says { get; }

    public bool Physical { get; }

    public C4Paint Paint { get; }

    /// <summary>Where a <c>$link</c> leads, where one was written.</summary>
    public string? Href { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new C4BoundaryNode(shape, this.Alias, this.Says, this.Physical, this.Paint, this.Href);
}

/// <summary>
/// A <c>SHOW_LEGEND()</c> as the stages leave it (<see cref="Stages.ResolveMacros"/>): the rows of the key it asks for — one for
/// each kind of element the block writes and one for each tag naming its own — which only the whole block says. It prints as the
/// line written.
/// </summary>
internal sealed class C4LegendNode : ContentNode
{
    internal C4LegendNode(ContentNode written, IReadOnlyList<C4Key> keys) : base(written) => this.Keys = keys;

    public IReadOnlyList<C4Key> Keys { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new C4LegendNode(shape, this.Keys);
}

/// <summary>What the parts of a C4 line are, as its stages said.</summary>
internal static class C4Parts
{
    /// <summary>What the argument of a line meaning <paramref name="means"/> says, as written — or null where none does.</summary>
    public static ContentPart? Argument(this ContentPart stated, C4Means means) =>
        stated.SelfAndDescendants()
              .FirstOrDefault(part => part.Node is C4ArgumentNode argument && argument.Means == means)?
              .SelfAndDescendants()
              .FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role is C4Roles.Value or SequenceRoles.Id);
}
