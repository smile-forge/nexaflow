using System.Windows;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// Remembers, from the hover, whether the drag in flight is a right-button one — because the drop
/// itself cannot be asked.
/// <para>
/// A drop <em>is</em> the button coming up, so by the time <c>IDropTarget::Drop</c> arrives the
/// button that started the drag is no longer in the reported key state. Reading it there says
/// "left drag" about every drag there has ever been, which is why a right-drag silently copied
/// instead of offering the choice. Explorer latches it the same way, off <c>DragEnter</c>.
/// </para>
/// <para>
/// Nothing resets it, deliberately: every <c>DragOver</c> reports afresh, so an ordinary left-drag
/// takes the latch back on its own way past. A stale <c>true</c> therefore cannot outlive the next
/// hover, and there is no clearing to forget.
/// </para>
/// </summary>
internal sealed class DropChoiceLatch
{
    /// <summary>True when the drag last seen hovering was held with the right button.</summary>
    public bool OffersChoice { get; private set; }

    /// <summary>Records what a <c>DragEnter</c>/<c>DragOver</c> reported.</summary>
    public void Observe(DragDropKeyStates keyStates)
        => OffersChoice = keyStates.HasFlag(DragDropKeyStates.RightMouseButton);
}
