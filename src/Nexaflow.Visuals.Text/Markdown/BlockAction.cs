using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A button a host puts on the toolbar shown over a rendered block: what it says, the id a journey finds it by, and what
/// pressing it does — handed the block it was pressed on, to ask of it what it needs.
///
/// <para>
/// What a button does is the host's entirely. The renderer draws blocks and knows nothing of clipboards or files: it puts
/// the host's buttons where a reader can reach them, and says which block was pressed.
/// </para>
/// </summary>
/// <param name="Label">What the button says.</param>
/// <param name="AutomationId">The id a journey finds the button by.</param>
/// <param name="Pressed">What pressing it does, with the block it was pressed on.</param>
public sealed record BlockAction(string Label, string AutomationId, Action<RenderedBlock> Pressed)
{
    /// <summary>What the button says it does when the pointer rests on it, or null.</summary>
    public string? ToolTip { get; init; }
}

/// <summary>A rendered block a toolbar button was pressed on, and what the button can ask of it.</summary>
public sealed class RenderedBlock
{
    private readonly ContentElement _element;

    internal RenderedBlock(ContentElement element, string? language)
    {
        _element = element;
        Language = language;
    }

    /// <summary>The language of the fence the block is written in — <c>mermaid</c>, <c>smiles</c> — or null where it is in none.</summary>
    public string? Language { get; }

    /// <summary>What the block draws, as written.</summary>
    public string Source => _element.Source;

    /// <summary>What the block looks like, on <paramref name="ground"/> where one is given — see <see cref="ContentElement.Picture"/>.</summary>
    public BitmapSource Picture(Brush? ground = null) => _element.Picture(ground);
}
