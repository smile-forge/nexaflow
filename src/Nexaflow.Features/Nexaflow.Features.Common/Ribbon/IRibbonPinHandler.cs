using Nexaflow.Features.Common.Ribbon;
using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Turns a foreign drag payload (a file action, a URL dropped from a browser, …) into a ribbon button.
/// Implementations are auto-discovered from feature assemblies by <see cref="FeatureManager"/>.
/// Tab drag-pinning is a separate concern — see <see cref="ITabPinHandler"/>; click-time behaviour that
/// isn't simply "open a tab" is another — see <see cref="IRibbonItemExecutor"/>.
/// </summary>
public interface IRibbonPinHandler
{
    /// <summary>
    /// WPF drag-data format keys this handler consumes — an internal kind (e.g. "FileAction") or native
    /// OS formats (e.g. "UniformResourceLocator"). A drag matches when it carries any of these formats.
    /// The resulting button's identity comes from <see cref="RibbonPinResult.PageKind"/>, not from here,
    /// so a handler can accept a foreign format yet produce a clean internal page kind.
    /// </summary>
    IReadOnlyList<string> AcceptedFormats { get; }

    /// <summary>
    /// Creates ribbon item metadata from a dropped payload, or returns <c>null</c> to reject.
    /// </summary>
    RibbonPinResult? Pin(object payload, int insertIndex = -1);
}
