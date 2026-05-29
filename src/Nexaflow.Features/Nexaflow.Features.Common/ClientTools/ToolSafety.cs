namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// Risk classification for a client tool. <see cref="ReadOnly"/> tools observe state only and
/// the harness may run them without asking the user; <see cref="RequiresApproval"/> tools change
/// the machine and must be approved before they run.
/// </summary>
public enum ToolSafety
{
    ReadOnly,
    RequiresApproval
}
