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
    /// Put what was copied where the caret is. The host's to do, as copying is — the clipboard is its — and what is on it goes to
    /// the engine as a paste, for the language the caret is in to say what it comes to there.
    /// </summary>
    public const string Paste = "paste";

    /// <summary>Keep a picture of what is under the pointer. The host decides where, and whether at all.</summary>
    public const string Save = "save";
}
