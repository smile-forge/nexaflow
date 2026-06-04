namespace Nexaflow.Features.Common;

/// <summary>
/// How much damage the AI could do acting within a page's security context — used to warn the user
/// when they pin a broadly-scoped page (e.g. a whole drive, or an unconfined "This PC" view) as AI
/// context. The page still enforces its own boundary; this only drives the advisory warning UI.
/// </summary>
public enum ContextSecurityRisk
{
    /// <summary>Tightly scoped (e.g. a specific folder).</summary>
    Low,
    /// <summary>Broad but bounded (e.g. a whole non-system drive).</summary>
    Medium,
    /// <summary>Unconfined or system-critical (e.g. This PC, the system drive, the Windows folder).</summary>
    High,
}
