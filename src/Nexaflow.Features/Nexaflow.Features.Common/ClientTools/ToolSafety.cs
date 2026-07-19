namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// Whether the agent harness may run a client tool <em>without asking the user first</em>. The distinction
/// is about consequence, not about whether any state changes: a tool can mutate and still be safe.
/// <para>
/// <see cref="SafeOperation"/> tools auto-run — pure observers, and changes that are inherently recoverable
/// or visible: a view/camera move, a selection, or an in-editor edit the user still has to explicitly save
/// (or otherwise confirm) before it touches disk. <see cref="RequiresApproval"/> tools change the machine or
/// the user's data with real, not-obviously-reversible consequence and must be approved before they run.
/// </para>
/// </summary>
public enum ToolSafety
{
    /// <summary>
    /// Safe to run without approval. Either observes state only, or makes a change that is immediately
    /// visible to the user and/or not yet committed (a viewport move, a selection, an in-editor edit
    /// awaiting an explicit save). Auto-runs. This is the default.
    /// </summary>
    SafeOperation,

    /// <summary>
    /// Changes the machine or the user's data with real consequence (delete a file, write HKLM, kill a
    /// process, commit to disk) — must be approved by the user before it runs.
    /// </summary>
    RequiresApproval
}
