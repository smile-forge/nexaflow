using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Features.Common.Viewlets;

/// <summary>
/// Optional capability a folder viewlet's view can implement to contribute to the AI context and
/// tool set of the page that hosts it. The hosting page (e.g. the file browser) merges every active
/// viewlet's context line and tools into what it exposes to the agent, so the AI can see — and act
/// on — folder-specific state (a .NET solution's build, a git repo's status) it would otherwise miss.
/// </summary>
public interface IViewletAiSurface
{
    /// <summary>One-line description of this viewlet's live state for the AI context.
    /// Null or empty contributes nothing.</summary>
    string? GetContext();

    /// <summary>Tools this viewlet contributes to the host page's tool list. Default: none.</summary>
    IReadOnlyList<IClientTool> GetClientTools() => [];
}
