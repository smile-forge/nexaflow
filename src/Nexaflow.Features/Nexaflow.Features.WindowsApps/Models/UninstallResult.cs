namespace Nexaflow.Features.WindowsApps.Models;

/// <summary>Outcome of an uninstall attempt — success, or a failure with a human-readable reason.</summary>
public sealed record UninstallResult(bool Success, string? Error)
{
    public static readonly UninstallResult Ok = new(true, null);
    public static UninstallResult Fail(string error) => new(false, error);
}
