namespace Nexaflow.IO.Network.Guard;

/// <summary>Why a send was refused. Named cases rather than a bare bool so the audit log, the UI message
/// and the AI's error result all say the same specific thing.</summary>
public enum GuardRefusal
{
    None = 0,

    /// <summary>Discovery and actions are switched off entirely — the kill switch.</summary>
    Disabled,

    /// <summary>Not an address on a locally attached prefix, and not a target the user typed.</summary>
    TargetNotLocal,

    /// <summary>Loopback, or an address belonging to this machine. Closes "make the app talk to the app",
    /// the local dev server, an MCP endpoint, and the privilege bridge's pipe.</summary>
    TargetIsLocalMachine,

    /// <summary>A broadcast or multicast the initiator is not permitted to make.</summary>
    BroadcastNotPermitted,

    /// <summary>Below UDP — raw IP or Ethernet frame crafting.</summary>
    LayerNotPermitted,

    /// <summary>A per-run ceiling was hit: packets, bytes or duration.</summary>
    RunBudgetExceeded,

    /// <summary>Too fast for this target or adapter.</summary>
    RateLimited,

    /// <summary>An unreviewed draft attempted something only a reviewed document may do.</summary>
    DraftRestriction,

    /// <summary>Forbidden outright, with no override — see <see cref="NetworkGuard"/>.</summary>
    Forbidden,
}

/// <summary>The guard's answer. <paramref name="Reason"/> is user-facing: a refusal the user cannot
/// understand reads as a bug, and a refusal the model cannot understand it will simply retry.</summary>
public readonly record struct GuardDecision(bool Allowed, GuardRefusal Refusal, string Reason)
{
    public static GuardDecision Allow() => new(true, GuardRefusal.None, "");
    public static GuardDecision Deny(GuardRefusal refusal, string reason) => new(false, refusal, reason);
}
