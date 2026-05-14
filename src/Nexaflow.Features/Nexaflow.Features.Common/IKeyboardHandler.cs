using System.Windows.Input;

namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by components that want to claim global keyboard shortcuts.
/// The active page is queried via <see cref="CanProcessKey"/> before the key
/// event is consumed, so callers never trigger side-effects speculatively.
/// </summary>
public interface IKeyboardHandler
{
    /// <summary>Returns true when this handler can act on the given key combination.</summary>
    bool CanProcessKey(Key key, ModifierKeys modifiers);

    /// <summary>Performs the action for the key combination. Returns true if the key was consumed.</summary>
    bool ProcessKey(Key key, ModifierKeys modifiers);
}
