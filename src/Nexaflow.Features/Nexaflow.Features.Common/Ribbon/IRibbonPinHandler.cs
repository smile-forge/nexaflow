using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Interaction;
using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Turns what is dropped on the ribbon (a file, a link from a browser, a file action, …) into buttons.
/// Implementations are auto-discovered from feature assemblies by <c>FeatureManager</c>.
/// Tab drag-pinning is a separate concern — see <see cref="ITabPinHandler"/>; click-time behaviour that
/// isn't simply "open a tab" is another — see <see cref="IRibbonItemExecutor"/>.
/// </summary>
public interface IRibbonPinHandler
{
    /// <summary>
    /// The kinds this handler consumes (<see cref="DragKinds"/>, or the app's own, e.g. "FileAction"). A button's
    /// identity comes from <see cref="RibbonPinResult.PageKind"/>, not from here, so a handler can accept a shared
    /// kind yet produce a page kind of its feature's.
    /// </summary>
    IReadOnlyList<string> AcceptedKinds { get; }

    /// <summary>
    /// The buttons <paramref name="payload"/> comes to — one for each thing dropped — or none to leave it to the next
    /// handler. Read the payload before returning.
    /// </summary>
    IReadOnlyList<RibbonPinResult> Pin(IDragPayload payload);
}
