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
}
