using Nexaflow.Interaction;

namespace Nexaflow.Features.Common;

/// <summary>
/// Lets a feature turn something dropped onto the shell's AI input bar into text inserted at the caret —
/// e.g. a file dragged from Explorer becomes its quoted path. Mirrors the ribbon's <c>IRibbonPinHandler</c>
/// (matched by kind, discovered by reflection, instantiated per workspace) but produces insert text rather than a
/// ribbon item.
/// </summary>
public interface IChatDropHandler
{
    /// <summary>The kinds this handler consumes (<see cref="DragKinds"/>, or the app's own).</summary>
    IReadOnlyList<string> AcceptedKinds { get; }

    /// <summary>Optional gate on the active page; default accepts regardless of which page is active.</summary>
    bool CanHandle(IPageViewModel? pageVm) => true;

    /// <summary>The text to insert at the caret, or null to reject this drop. Read the payload before returning.</summary>
    string? BuildInsertText(IDragPayload payload, IPageViewModel? pageVm);
}
