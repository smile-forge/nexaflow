namespace Nexaflow.Markdown.Mermaid.State;

/// <summary>
/// What a line of a state diagram says beyond the lines every diagram shares. What a line is made of — names, labels, styles — is
/// <see cref="MermaidKinds"/>'.
/// </summary>
public static class StateKinds
{
    /// <summary>A <c>direction LR</c> line, which lays out the diagram, or the composite state it is written in.</summary>
    public const string Direction = "state-direction";

    /// <summary>A state written on its own, with a description or drawn as a fork, a join or a choice.</summary>
    public const string State = "state-state";

    /// <summary>A transition from one state to another, with what is written on it.</summary>
    public const string Transition = "state-transition";

    /// <summary>One state where it is named: its id, and the class it is given with <c>:::</c>.</summary>
    public const string Named = "state-named";

    /// <summary>What is written on a state or a transition, after the colon opening it: the rest of the line, as it is written.</summary>
    public const string Said = "state-said";

    /// <summary>A <c>state … {</c> line, which opens a composite state holding the states written until the <c>}</c> closing it.</summary>
    public const string Opens = "state-opens";

    /// <summary>The <c>}</c> that closes a composite state.</summary>
    public const string Ends = "state-ends";

    /// <summary>The <c>--</c> that divides a composite state into regions running at the same time.</summary>
    public const string Concurrent = "state-concurrent";

    /// <summary>A <c>note</c> line, which opens a note beside a state — or a whole one, where it says what the note says.</summary>
    public const string Note = "state-note";

    /// <summary>A <c>note</c> line whose text is still to come, written on the lines under it until an <c>end note</c>.</summary>
    public const string NoteOpens = "state-note-opens";

    /// <summary>A line inside a note, which is what the note says whatever it would otherwise say on its own.</summary>
    public const string NoteText = "state-note-text";

    /// <summary>The <c>end note</c> that closes a note written across several lines.</summary>
    public const string NoteEnds = "state-note-ends";

    public const string ClassDef = "state-class-def";

    public const string Class = "state-class";

    public const string Style = "state-style";

    /// <summary>A <c>click</c> line: where pressing a state leads, and what it says while pointed at.</summary>
    public const string Click = "state-click";

    /// <summary>A <c>hide empty description</c> line, which draws a state with nothing written on it as its id alone.</summary>
    public const string Hide = "state-hide";

    /// <summary>A <c>scale 350 width</c> line, which Mermaid keeps from its first renderer.</summary>
    public const string Scale = "state-scale";
}

/// <summary>What a piece of a state diagram's line is to the piece holding it.</summary>
public static class StateRoles
{
    /// <summary>What a state is called, which is what a transition, a <c>class</c>, a <c>style</c> and a note name it by.</summary>
    public const string Id = "state-id";

    /// <summary>What is written on a state, on a transition, or in a note.</summary>
    public const string Label = "state-label";

    /// <summary>The name of a class, declared by a <c>classDef</c> and given by a <c>class</c> line or by <c>:::</c>.</summary>
    public const string Class = "state-class-name";

    /// <summary>The way the diagram, or a composite state in it, is laid out: <c>TB</c>, <c>LR</c>.</summary>
    public const string Towards = "state-towards";

    /// <summary>The arrow a transition is drawn as.</summary>
    public const string Arrow = "state-arrow";

    /// <summary>What <c>&lt;&lt;fork&gt;&gt;</c>, <c>&lt;&lt;join&gt;&gt;</c> or <c>&lt;&lt;choice&gt;&gt;</c> says a state is drawn as.</summary>
    public const string Kind = "state-kind";

    /// <summary>Which side of the state a note is written beside: <c>left</c> or <c>right</c>.</summary>
    public const string Side = "state-side";

    /// <summary>Where pressing a state leads.</summary>
    public const string Href = "state-href";

    /// <summary>What a state says while it is pointed at.</summary>
    public const string Tip = "state-tip";

    /// <summary>How wide a <c>scale</c> line asks for the diagram to be drawn.</summary>
    public const string Width = "state-width";


}
