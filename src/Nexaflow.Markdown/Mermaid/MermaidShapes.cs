using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// The shape a node is drawn as, which is what the brackets its label is written in say, or the name <c>@{ shape: … }</c> gives
/// it. Mermaid's set, shared by every diagram whose nodes are written that way — a block diagram's blocks, a flowchart's nodes.
/// </summary>
public enum MermaidShape
{
    /// <summary>No brackets at all, or brackets that say no shape: a plain box.</summary>
    None,

    /// <summary><c>[…]</c></summary>
    Rectangle,

    /// <summary><c>(…)</c></summary>
    Rounded,

    /// <summary><c>([…])</c></summary>
    Stadium,

    /// <summary><c>[[…]]</c></summary>
    Subroutine,

    /// <summary><c>[(…)]</c></summary>
    Cylinder,

    /// <summary><c>((…))</c></summary>
    Circle,

    /// <summary><c>(((…)))</c></summary>
    DoubleCircle,

    /// <summary><c>&gt;…]</c></summary>
    Asymmetric,

    /// <summary><c>{…}</c></summary>
    Diamond,

    /// <summary><c>{{…}}</c></summary>
    Hexagon,

    /// <summary><c>[/…/]</c></summary>
    Parallelogram,

    /// <summary><c>[\…\]</c></summary>
    ParallelogramAlt,

    /// <summary><c>[/…\]</c></summary>
    Trapezoid,

    /// <summary><c>[\…/]</c></summary>
    TrapezoidAlt,

    /// <summary>A page with a wavy foot: <c>doc</c>.</summary>
    Document,

    /// <summary>A document with a line down its left side: <c>lin-doc</c>.</summary>
    LinedDocument,

    /// <summary>Documents stacked one behind another: <c>docs</c>.</summary>
    StackedDocument,

    /// <summary>A document with its lower right corner turned: <c>tag-doc</c>.</summary>
    TaggedDocument,

    /// <summary>A rectangle with a corner cut off: <c>notch-rect</c>.</summary>
    Card,

    /// <summary>A rectangle with both top corners cut off: <c>notch-pent</c>, a loop limit.</summary>
    NotchedPentagon,

    /// <summary>A rectangle with a second line down its left side: <c>lin-rect</c>.</summary>
    LinedRectangle,

    /// <summary>A rectangle with a line across its top: <c>div-rect</c>.</summary>
    DividedRectangle,

    /// <summary>A rectangle with a line across its top and down its left: <c>win-pane</c>, internal storage.</summary>
    WindowPane,

    /// <summary>A rectangle with its lower right corner turned: <c>tag-rect</c>.</summary>
    TaggedRectangle,

    /// <summary>Rectangles stacked one behind another: <c>st-rect</c>.</summary>
    StackedRectangle,

    /// <summary>A rectangle whose top slopes up to the right: <c>sl-rect</c>, a manual input.</summary>
    SlopedRectangle,

    /// <summary>A rectangle rounded off at its right end: <c>delay</c>.</summary>
    Delay,

    /// <summary>Pointed at its left end and rounded at its right: <c>curv-trap</c>, a display.</summary>
    CurvedTrapezoid,

    /// <summary>A rectangle whose ends both bow to the left: <c>bow-rect</c>, stored data.</summary>
    BowTie,

    /// <summary>A rectangle with a wavy top and foot: <c>flag</c>, paper tape.</summary>
    Flag,

    /// <summary>A triangle standing on its base: <c>tri</c>, an extract.</summary>
    Triangle,

    /// <summary>A triangle standing on its point: <c>flip-tri</c>, a manual file.</summary>
    FlippedTriangle,

    /// <summary>Two triangles point to point: <c>hourglass</c>, a collate. Drawn without its words, as Mermaid draws it.</summary>
    Hourglass,

    /// <summary>A lightning bolt: <c>bolt</c>, a communication link. Drawn without its words.</summary>
    Bolt,

    /// <summary>A solid bar across the flow: <c>fork</c>, a fork or a join. Drawn without its words.</summary>
    Fork,

    /// <summary>A small circle: <c>sm-circ</c>, a start. Drawn without its words.</summary>
    SmallCircle,

    /// <summary>A small ring round a dot: <c>fr-circ</c>, a stop. Drawn without its words.</summary>
    FramedCircle,

    /// <summary>A small solid circle: <c>f-circ</c>, a junction. Drawn without its words.</summary>
    FilledCircle,

    /// <summary>A circle crossed through: <c>cross-circ</c>, a summary. Drawn without its words.</summary>
    CrossedCircle,

    /// <summary>A cylinder lying on its side: <c>h-cyl</c>, direct access storage.</summary>
    HorizontalCylinder,

    /// <summary>A cylinder with a second rim under its lid: <c>lin-cyl</c>, disk storage.</summary>
    LinedCylinder,

    /// <summary>A band ruled along its top and its foot, open at its ends: <c>datastore</c>.</summary>
    DataStore,

    /// <summary>A pail, open at its top: <c>bucket</c>.</summary>
    Bucket,

    /// <summary>A curly brace to the left of the words: <c>brace</c>, a comment.</summary>
    Brace,

    /// <summary>A curly brace to the right of the words: <c>brace-r</c>.</summary>
    BraceRight,

    /// <summary>Curly braces either side of the words: <c>braces</c>.</summary>
    Braces,

    /// <summary>A browser window, with its bar across the top: <c>browser</c>.</summary>
    Browser,

    /// <summary>A terminal window, with its prompt in the corner: <c>console</c>.</summary>
    Console,

    /// <summary>A folder, with its tab on top: <c>folder</c>.</summary>
    Folder,

    /// <summary>A head over a body: <c>person</c>.</summary>
    Person,

    /// <summary>A cloud: <c>cloud</c>.</summary>
    Cloud,

    /// <summary>A starburst: <c>bang</c>.</summary>
    Bang,

    /// <summary>Words with nothing drawn round them: <c>text</c>.</summary>
    Text,
}

/// <summary>
/// The brackets a node's label is written in, and the shape each pair says — what <see cref="MermaidOutline.Node"/> reads a
/// label in for a diagram drawing Mermaid's shapes, and what the tree is asked afterwards to learn which one was written.
/// </summary>
public static class MermaidShapes
{
    /// <summary>
    /// Every pair, the longest opening first so the longest one written is the one read. An opening several closings may end
    /// — <c>[/…/]</c> and <c>[/…\]</c> — is written once for each, together.
    /// </summary>
    public static readonly IReadOnlyList<(string Open, string Close, MermaidShape Shape)> Nodes =
    [
        ("(((", ")))", MermaidShape.DoubleCircle),
        ("((", "))", MermaidShape.Circle),
        ("([", "])", MermaidShape.Stadium),
        ("[[", "]]", MermaidShape.Subroutine),
        ("[(", ")]", MermaidShape.Cylinder),
        ("[/", "/]", MermaidShape.Parallelogram),
        ("[/", "\\]", MermaidShape.Trapezoid),
        ("[\\", "\\]", MermaidShape.ParallelogramAlt),
        ("[\\", "/]", MermaidShape.TrapezoidAlt),
        ("{{", "}}", MermaidShape.Hexagon),
        ("[", "]", MermaidShape.Rectangle),
        ("(", ")", MermaidShape.Rounded),
        ("{", "}", MermaidShape.Diamond),
        (">", "]", MermaidShape.Asymmetric),
    ];

    /// <summary>The pairs alone, which is what <see cref="MermaidOutline.Node"/> takes.</summary>
    public static IReadOnlyList<(string Open, string Close)> Brackets { get; } = [.. Nodes.Select(node => (node.Open, node.Close))];

    /// <summary>The shape a node read into the tree is drawn as — <see cref="MermaidShape.None"/> where nothing says one.</summary>
    public static MermaidShape Of(ContentPart? node)
    {
        if (node?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label) is not { } label) return MermaidShape.None;

        return Of(label.Children.FirstOrDefault(child => child.Role == Roles.Open)?.Text,
                  label.Children.LastOrDefault(child => child.Role == Roles.Close)?.Text);
    }

    /// <summary>The shape a pair of brackets says — <see cref="MermaidShape.None"/> for a pair that says none.</summary>
    public static MermaidShape Of(string? open, string? close) =>
        Nodes.FirstOrDefault(node => node.Open == open && node.Close == close).Shape;

    /// <summary>
    /// The shape a name written in <c>@{ shape: … }</c> says, with every name Mermaid gives it — or null for a name Mermaid has no
    /// shape by, which Mermaid refuses to draw.
    /// </summary>
    public static MermaidShape? Named(string? said) => (said ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "rect" or "proc" or "process" or "rectangle" => MermaidShape.Rectangle,
        "rounded" or "event" => MermaidShape.Rounded,
        "stadium" or "pill" or "terminal" => MermaidShape.Stadium,
        "fr-rect" or "framed-rectangle" or "subproc" or "subprocess" or "subroutine" => MermaidShape.Subroutine,
        "cyl" or "cylinder" or "database" or "db" => MermaidShape.Cylinder,
        "circle" or "circ" => MermaidShape.Circle,
        "dbl-circ" or "double-circle" => MermaidShape.DoubleCircle,
        "odd" => MermaidShape.Asymmetric,
        "diam" or "diamond" or "decision" or "question" => MermaidShape.Diamond,
        "hex" or "hexagon" or "prepare" => MermaidShape.Hexagon,
        "lean-r" or "lean-right" or "in-out" => MermaidShape.Parallelogram,
        "lean-l" or "lean-left" or "out-in" => MermaidShape.ParallelogramAlt,
        "trap-b" or "trapezoid-bottom" or "trapezoid" or "priority" => MermaidShape.Trapezoid,
        "trap-t" or "trapezoid-top" or "inv-trapezoid" or "manual" => MermaidShape.TrapezoidAlt,
        "doc" or "document" => MermaidShape.Document,
        "lin-doc" or "lined-document" => MermaidShape.LinedDocument,
        "docs" or "documents" or "st-doc" or "stacked-document" => MermaidShape.StackedDocument,
        "tag-doc" or "tagged-document" => MermaidShape.TaggedDocument,
        "notch-rect" or "card" or "notched-rectangle" => MermaidShape.Card,
        "notch-pent" or "loop-limit" or "notched-pentagon" => MermaidShape.NotchedPentagon,
        "lin-rect" or "lin-proc" or "lined-process" or "lined-rectangle" or "shaded-process" => MermaidShape.LinedRectangle,
        "div-rect" or "div-proc" or "divided-process" or "divided-rectangle" => MermaidShape.DividedRectangle,
        "win-pane" or "window-pane" or "internal-storage" => MermaidShape.WindowPane,
        "tag-rect" or "tag-proc" or "tagged-process" or "tagged-rectangle" => MermaidShape.TaggedRectangle,
        "st-rect" or "stacked-rectangle" or "procs" or "processes" => MermaidShape.StackedRectangle,
        "sl-rect" or "sloped-rectangle" or "manual-input" => MermaidShape.SlopedRectangle,
        "delay" or "half-rounded-rectangle" => MermaidShape.Delay,
        "curv-trap" or "curved-trapezoid" or "display" => MermaidShape.CurvedTrapezoid,
        "bow-rect" or "bow-tie-rectangle" or "stored-data" => MermaidShape.BowTie,
        "flag" or "paper-tape" => MermaidShape.Flag,
        "tri" or "triangle" or "extract" => MermaidShape.Triangle,
        "flip-tri" or "flipped-triangle" or "manual-file" => MermaidShape.FlippedTriangle,
        "hourglass" or "collate" => MermaidShape.Hourglass,
        "bolt" or "com-link" or "lightning-bolt" => MermaidShape.Bolt,
        "fork" or "join" => MermaidShape.Fork,
        "sm-circ" or "small-circle" or "start" => MermaidShape.SmallCircle,
        "fr-circ" or "framed-circle" or "stop" => MermaidShape.FramedCircle,
        "f-circ" or "filled-circle" or "junction" => MermaidShape.FilledCircle,
        "cross-circ" or "crossed-circle" or "summary" => MermaidShape.CrossedCircle,
        "h-cyl" or "horizontal-cylinder" or "das" => MermaidShape.HorizontalCylinder,
        "lin-cyl" or "lined-cylinder" or "disk" => MermaidShape.LinedCylinder,
        "datastore" or "data-store" => MermaidShape.DataStore,
        "bucket" => MermaidShape.Bucket,
        "brace" or "brace-l" or "comment" => MermaidShape.Brace,
        "brace-r" => MermaidShape.BraceRight,
        "braces" => MermaidShape.Braces,
        "browser" => MermaidShape.Browser,
        "console" => MermaidShape.Console,
        "folder" or "directory" => MermaidShape.Folder,
        "person" => MermaidShape.Person,
        "cloud" => MermaidShape.Cloud,
        "bang" => MermaidShape.Bang,
        "text" => MermaidShape.Text,
        _ => null,
    };
}
