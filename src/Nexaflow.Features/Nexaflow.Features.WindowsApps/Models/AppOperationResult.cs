using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.WindowsApps.Models;

/// <summary>
/// Outcome of an operation performed on an installed app — uninstall, modify, move, repair or reset:
/// success, or a failure with a human-readable reason to surface to the user.
/// </summary>
public sealed record AppOperationResult(bool Success, string? Error)
{
    public static readonly AppOperationResult Ok = new(true, null);
    public static AppOperationResult Fail(string error) => new(false, error);

    /// <summary>
    /// The app's discovery source has no implementation for <paramref name="operation"/> — the operation's
    /// label as the person sees it.
    /// </summary>
    public static AppOperationResult Unsupported(string operation) =>
        new(false, Str.Format("WindowsApps.Error.UnsupportedFormat", operation));
}
