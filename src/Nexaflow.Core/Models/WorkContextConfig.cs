namespace Nexaflow.Core.Models;

/// <summary>
/// Saved configuration for a named workspace — what the user creates, names, and sees in the
/// context dropdown. Multiple <see cref="WorkContext"/> runtime instances (one per window) can
/// share the same config.
/// </summary>
public class WorkContextConfig
{
    public string Name  { get; set; } = "Default";
    public string Color { get; set; } = "#5B8CFF";
    public string Icon  { get; set; } = "⬡";
}
