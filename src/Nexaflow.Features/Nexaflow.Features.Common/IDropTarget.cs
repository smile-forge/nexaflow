using System.Windows;

namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by components that can accept file drag-drop operations.
/// The view resolves the target path from hit-testing and passes it here,
/// keeping the interface clean and free of WPF event types.
/// </summary>
public interface IDropTarget
{
    /// <summary>Returns true when the dragged data contains droppable content (e.g. file paths).</summary>
    bool CanAcceptDrop(IDataObject data);

    /// <summary>
    /// True when <paramref name="destinationPath"/> is one of the things being dragged.
    /// <para>
    /// Dropping something onto itself is a gesture that means nothing, and it is almost never meant: a
    /// click that drifted the four pixels Windows calls a drag is enough to make one. Asked during the
    /// hover so the drop never happens, rather than being refused with a complaint afterwards.
    /// </para>
    /// </summary>
    bool IsSelfDrop(IDataObject data, string destinationPath);

    /// <summary>
    /// Returns the tooltip text to show next to the cursor while hovering,
    /// e.g. "Copy to Temp" or "Move to Temp". <paramref name="targetFolderName"/> is the display
    /// name of the folder under the cursor, or null when hovering over the file list background.
    /// </summary>
    string GetDropDescription(IDataObject data, string? targetFolderName, bool isMove);

    /// <summary>
    /// Performs the drop operation. When <paramref name="move"/> is true the source files are
    /// moved (deleted after copy); otherwise they are copied.
    /// </summary>
    void Drop(IDataObject data, string destinationPath, bool move);
}
