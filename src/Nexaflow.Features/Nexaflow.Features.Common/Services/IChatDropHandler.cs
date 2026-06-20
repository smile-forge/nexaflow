using System.Windows;

namespace Nexaflow.Features.Common;

/// <summary>
/// Lets a feature turn something dropped onto the shell's AI input bar into text inserted at the caret —
/// e.g. a file dragged from the terminal's Files panel becomes its quoted path. Mirrors the ribbon's
/// <c>IRibbonPinHandler</c> (format-matched, discovered by reflection, instantiated per workspace) but
/// produces insert text rather than a ribbon item.
/// </summary>
public interface IChatDropHandler
{
    /// <summary>Drag-data formats this handler consumes (e.g. <see cref="DataFormats.FileDrop"/>).</summary>
    IReadOnlyList<string> AcceptedFormats { get; }

    /// <summary>Optional gate on the active page; default accepts regardless of which page is active.</summary>
    bool CanHandle(IPageViewModel? pageVm) => true;

    /// <summary>The text to insert at the caret, or null to reject this drop.</summary>
    string? BuildInsertText(IDataObject data, IPageViewModel? pageVm);
}
