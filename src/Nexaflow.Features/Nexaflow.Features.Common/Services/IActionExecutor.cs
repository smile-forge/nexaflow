namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by page ViewModels (or pages) to receive and execute JSON action payloads
/// returned by the AI when no direct query handler matched.
/// </summary>
public interface IActionExecutor
{
    /// <summary>
    /// Attempts to execute the action described in <paramref name="actionJson"/>.
    /// Returns true if the action was recognized and executed; false if it was unrecognized or failed.
    /// </summary>
    Task<bool> TryExecuteActionAsync(string actionJson);
}
