namespace Nexaflow.Features.Common;

/// <summary>
/// A feature-provided handler for "do the default thing with this object" — the non-drag
/// sibling of <see cref="IDropTarget"/>. A feature advertises one (discovered by reflection,
/// like <see cref="IDropTarget"/>) so another feature can act on an object it owns without a
/// direct reference. Core dispatches via <see cref="Services.IShellServices.HandleObject"/>,
/// which walks every implementor and invokes the first whose <see cref="CanHandleObject"/>
/// returns true.
///
/// Example: the file-system feature handles a string file path by opening it exactly as a
/// double-click in the file list would; the web feature handles an http(s) URL by opening a
/// browser tab. The scratchpad calls <c>HandleObject</c> for a clicked link and stays ignorant
/// of which feature claims it.
///
/// (Future: this and <see cref="IDropTarget"/> may merge into one "act on this payload" contract.)
/// </summary>
public interface IGenericObjectHandler
{
    /// <summary>True when this handler recognises <paramref name="obj"/> and can act on it.</summary>
    bool CanHandleObject(object obj);

    /// <summary>Performs the default action for <paramref name="obj"/>. Only called after
    /// <see cref="CanHandleObject"/> returned true. Runs on the UI thread.</summary>
    void Handle(object obj);
}
