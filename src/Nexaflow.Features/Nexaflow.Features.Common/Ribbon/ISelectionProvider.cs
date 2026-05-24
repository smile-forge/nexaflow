using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by any page view that exposes a current file/path selection.
/// Allows the shell to query selected paths without knowing the concrete page type.
/// </summary>
public interface ISelectionProvider
{
    IReadOnlyList<string> SelectedFilePaths { get; }
}
