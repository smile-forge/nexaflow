using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Runs a ribbon button whose behaviour is something other than "open a tab" (e.g. a file action).
/// Resolved at click time by <see cref="PageKind"/>; buttons with no matching executor simply open
/// their page kind as a tab. Auto-discovered by <c>FeatureManager</c>.
/// </summary>
public interface IRibbonItemExecutor
{
    /// <summary>The page kind of ribbon buttons this executor runs.</summary>
    string PageKind { get; }

    /// <summary>Executes the pinned action using the provided shell context.</summary>
    void Execute(Dictionary<string, string>? pageParams, IRibbonExecutionContext context);
}
