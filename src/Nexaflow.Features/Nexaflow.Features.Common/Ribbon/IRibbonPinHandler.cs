using Nexaflow.Features.Common.Ribbon;
using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Allows any feature to extend what can be dragged or pinned to the ribbon.
/// Implementations are auto-discovered from feature assemblies by <see cref="FeatureManager"/>
/// and can also be registered manually (e.g. for Core-owned handlers).
/// </summary>
public interface IRibbonPinHandler
{
    /// <summary>
    /// Identifies this handler's content type. Used as the WPF drag-data format key
    /// and stored as the ribbon item's <c>PageKind</c> in ribbon.json.
    /// </summary>
    string ContentKind { get; }

    /// <summary>
    /// Creates ribbon item metadata from a dropped payload, or returns <c>null</c> to reject.
    /// </summary>
    RibbonPinResult? Pin(object payload, int insertIndex = -1);

    /// <summary>
    /// Executes the pinned action using the provided shell context.
    /// </summary>
    void Execute(Dictionary<string, string>? pageParams, IRibbonExecutionContext context);
}
