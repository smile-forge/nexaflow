namespace Nexaflow.Markdown.Layout;

/// <summary>What a press on a piece means — a verb, what it acts on, and what resting on it says.</summary>
public sealed record LayoutAsk(string Verb, string? Target = null, string? Tip = null);

/// <summary>What the few pieces that answer to a gesture answer to.</summary>
public sealed record LayoutActions
{
    /// <summary>What one press means.</summary>
    public LayoutAsk? Click { get; init; }

    /// <summary>What choosing it means, for a host following the selection.</summary>
    public LayoutAsk? Select { get; init; }

    /// <summary>What sliding it sideways means, for a piece showing only part of what it holds across (<see cref="LayoutSlide"/>).</summary>
    public LayoutSlide? Slide { get; init; }
}

/// <summary>
/// A piece showing only part of what it holds across — a box narrower than the lines of code it keeps whole — and so slid sideways
/// to show the rest: <paramref name="Shown"/> across of the <paramref name="Whole"/> it holds, from <paramref name="At"/> along. The
/// host knows what is slid by <paramref name="Reader"/>, which it handed the builder (<see cref="LayoutHost.SlideOf"/>), so how far
/// the reader slid it is kept against the same content through every edit; nothing a builder reads.
/// </summary>
public sealed record LayoutSlide(double Shown, double Whole, double At, object Reader)
{
    /// <summary>The furthest along it can be slid: to where the last of what it holds shows.</summary>
    public double Furthest => Math.Max(0, Whole - Shown);

    /// <summary>Where what shows stands, in the piece's own frame.</summary>
    public LayoutRect Window { get; init; }

    /// <summary>Where the bar saying how much shows, and where along it, stands in the piece's own frame — empty where it has none.</summary>
    public LayoutRect Bar { get; init; }

    /// <summary>Where the part of that bar standing for what shows stands, in the piece's own frame: what the reader takes hold of.</summary>
    public LayoutRect Thumb { get; init; }
}

/// <summary>
/// The verbs a piece answers to: the few a layout resolves itself where its host does not take them first, and the ones only a
/// host can carry out.
/// </summary>
public static class LayoutVerbs
{
    /// <summary>Go where <see cref="LayoutAsk.Target"/> says.</summary>
    public const string Navigate = "navigate";

    /// <summary>Show what is folded behind the node <see cref="LayoutAsk.Target"/> names.</summary>
    public const string Expand = "expand";

    /// <summary>Fold away what is behind the node <see cref="LayoutAsk.Target"/> names.</summary>
    public const string Collapse = "collapse";

    /// <summary>The node <see cref="LayoutAsk.Target"/> names was chosen.</summary>
    public const string Select = "select";

    /// <summary>
    /// Put what is chosen where things are copied to.
    ///
    /// <para>
    /// <strong>The layout never touches a clipboard.</strong> It knows what was picked out and how to say it as markdown, as plain
    /// words and as marked-up text; a clipboard is the application's, shared with every other thing in the window, and a layout
    /// that reached for it would be reaching past its host. So this is raised and the host does it.
    /// </para>
    /// </summary>
    public const string Copy = "copy";

    /// <summary>
    /// Put what is chosen where things are copied to, and take it away — the host's to do, as copying is, and taking it away only
    /// once it has gone there.
    /// </summary>
    public const string Cut = "cut";

    /// <summary>
    /// Put what was copied where the caret is. The host's to do, as copying is — the clipboard is its — and what is on it goes to
    /// the engine as a paste, for the language the caret is in to say what it comes to there.
    /// </summary>
    public const string Paste = "paste";

    /// <summary>Keep a picture of what is under the pointer. The host decides where, and whether at all.</summary>
    public const string Save = "save";
}
