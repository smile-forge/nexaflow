using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by page ViewModels to expose context and client tools to the AI pipeline.
/// </summary>
public interface IPageViewModel
{
    /// <summary>Short description of what the user is currently looking at, for the LLM.</summary>
    string GetContext();

    /// <summary>
    /// Client-side tools this page exposes to the AI agent harness. Default: none. Each tool is a
    /// self-contained <see cref="IClientTool"/> the agent may invoke (read-only tools auto-run;
    /// mutating tools are approved first).
    /// </summary>
    IReadOnlyList<IClientTool> GetClientTools() => [];

    /// <summary>
    /// Optional strongly-typed context for query handlers. Default returns null.
    /// Override to let handlers gate on and extract structured data
    /// (e.g. <see cref="FileSystemContext"/>).
    /// </summary>
    IContext? GetContextObject() => null;

    /// <summary>
    /// A stable identifier for this page's security/scope boundary — the scope its
    /// <see cref="GetClientTools"/> act within (e.g. a file-system tab's current root path).
    /// Default: null (no distinct boundary).
    /// <para>
    /// When several pages are pinned together (e.g. into an AI conversation) and expose identically
    /// named tools, this value disambiguates them: identical values share one tool, distinct values
    /// get a named context so the agent can target the right one. Two pages returning the same string
    /// are treated as the same boundary; null pages can't be disambiguated (first-wins).
    /// </para>
    /// </summary>
    string? GetSecurityContext() => null;

    /// <summary>
    /// How risky it is to let the AI act within this page's <see cref="GetSecurityContext"/> — surfaced
    /// as a warning badge when the page is pinned as AI context. Default:
    /// <see cref="ContextSecurityRisk.Low"/>. (Advisory only; the page enforces its own boundary.)
    /// </summary>
    ContextSecurityRisk GetContextSecurityRisk() => ContextSecurityRisk.Low;
}
