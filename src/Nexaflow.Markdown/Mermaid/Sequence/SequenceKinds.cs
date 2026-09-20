namespace Nexaflow.Markdown.Mermaid.Sequence;

/// <summary>What a line of a sequence diagram says beyond the lines every diagram shares. What a line is made of — names,
/// labels, quoted text — is <see cref="MermaidKinds"/>'.</summary>
public static class SequenceKinds
{
    /// <summary>A <c>participant A as Alice</c> or <c>actor A</c> line, which puts a lifeline where it is written.</summary>
    public const string Participant = "sequence-participant";

    /// <summary>A <c>create participant B</c> line: the lifeline starts at the message under it rather than at the top.</summary>
    public const string Created = "sequence-created";

    /// <summary>A <c>destroy B</c> line: the lifeline stops there, with a cross where it ends.</summary>
    public const string Destroyed = "sequence-destroyed";

    /// <summary>A message from one lifeline to another: <c>Alice-&gt;&gt;John: Hello</c>.</summary>
    public const string Message = "sequence-message";

    /// <summary>A <c>Note over A,B: …</c> line, beside one lifeline or spanning several.</summary>
    public const string Note = "sequence-note";

    /// <summary>An <c>activate A</c> or <c>deactivate A</c> line, which starts or ends a bar on the lifeline.</summary>
    public const string Activation = "sequence-activation";

    /// <summary>An <c>autonumber</c> line, which numbers the messages under it.</summary>
    public const string Numbering = "sequence-numbering";

    /// <summary>A <c>box Aqua Group</c> line, which tints the participants written until the <c>end</c> closing it.</summary>
    public const string Box = "sequence-box";

    /// <summary>An <c>alt</c>, <c>opt</c>, <c>loop</c>, <c>par</c>, <c>critical</c>, <c>break</c> or <c>rect</c> line.</summary>
    public const string Frame = "sequence-frame";

    /// <summary>An <c>else</c>, <c>and</c> or <c>option</c> line, which divides the frame it is written in.</summary>
    public const string Section = "sequence-section";

    /// <summary>The <c>end</c> that closes a box or a frame.</summary>
    public const string Ends = "sequence-ends";

    /// <summary>A <c>link A: Label @ url</c> line, which gives a participant somewhere to lead.</summary>
    public const string Link = "sequence-link";

    /// <summary>A <c>links</c>, <c>properties</c> or <c>details</c> line, which gives a participant several at once.</summary>
    public const string Menu = "sequence-menu";

    /// <summary>What a message or a note says, after the colon opening it.</summary>
    public const string Said = "sequence-said";

    /// <summary>One participant where it is named: its name, the label drawn instead of it, and what kind of thing it is.</summary>
    public const string Named = "sequence-named";

    /// <summary>What the stages hang under a line: the frame it is in, the one it opens, and the number a message takes.</summary>
    public const string Fact = "sequence-fact";
}

/// <summary>What a piece of a sequence diagram's line is to the piece holding it.</summary>
public static class SequenceRoles
{
    /// <summary>What a participant is called, which is what a message and a note name it by.</summary>
    public const string Id = "sequence-id";

    /// <summary>What is drawn in place of its name, from <c>participant A as Alice</c>.</summary>
    public const string Label = "sequence-label";

    /// <summary>What kind of thing a participant is drawn as, from <c>actor</c> or <c>@{ "type": "database" }</c>.</summary>
    public const string Type = "sequence-type";

    /// <summary>A key of the <c>@{ … }</c> written against a participant's name.</summary>
    public const string Key = "sequence-key";

    /// <summary>The characters a message is drawn with, which say its line and the head at each end.</summary>
    public const string Arrow = "sequence-arrow";

    /// <summary>What a message or a note says, drawn over the line or in the note.</summary>
    public const string Said = "sequence-said-text";

    /// <summary>Where a note sits: <c>right of</c>, <c>left of</c> or <c>over</c>.</summary>
    public const string Place = "sequence-place";

    /// <summary>What a box or a <c>rect</c> is washed with, written before its name.</summary>
    public const string Colour = "sequence-colour";

    /// <summary>What a box is called, and what a frame is written with — the condition an <c>alt</c> holds under.</summary>
    public const string Space = "sequence-space";

    /// <summary>Which word opened a frame, which is what is drawn in its tab: <c>alt</c>, <c>loop</c>.</summary>
    public const string Word = "sequence-word";

    /// <summary>Where <c>autonumber</c> starts, and how far it steps.</summary>
    public const string Start = "sequence-start";
    public const string Step = "sequence-step";

    /// <summary>Whether <c>autonumber off</c> was written, which stops the numbering under it.</summary>
    public const string Off = "sequence-off";

    /// <summary>What a link is called, and where it leads.</summary>
    public const string Menu = "sequence-menu-label";
    public const string Url = "sequence-url";

    /// <summary>The <c>+</c> or <c>-</c> written against a message's target, which turns a bar on or off.</summary>
    public const string Turns = "sequence-turns";

    /// <summary>The <c>()</c> written against an end of a message, which runs it to the middle of the lifeline.</summary>
    public const string Centre = "sequence-centre";

    /// <summary>Whether a participant is created or destroyed by a message, rather than standing from the top.</summary>
    public const string Lifetime = "sequence-lifetime";

    /// <summary>The box or frame a line is written in (<see cref="MermaidNesting"/>).</summary>
    public const string Inside = "sequence-inside";

    /// <summary>The box or frame a line opens.</summary>
    public const string Opened = "sequence-opened";

    /// <summary>The number <c>autonumber</c> gives a message (<see cref="Stages.ResolveNumbers"/>).</summary>
    public const string Number = "sequence-number";
}
