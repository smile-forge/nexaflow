using System.ComponentModel;

namespace Nexaflow.Features.Common;

/// <summary>
/// Whether what needs some abilities can be used now, kept current as the workspace's models change — for a control to bind
/// (<see cref="IAIService.Gate"/>). It is enabled while every ability is <see cref="AiAbilityStatus.IsUsable"/>: one waiting
/// on a download counts, its first use offering the download (<see cref="MakeReadyAsync"/>). Otherwise it is disabled, and
/// <see cref="DisabledTip"/> says how to enable it. Changes are raised on the thread that made it — a page's UI thread.
/// </summary>
public interface IAiAbilityGate : INotifyPropertyChanged, IDisposable
{
    /// <summary>The abilities it watches.</summary>
    IReadOnlyList<AiAbility> Abilities { get; }

    bool IsEnabled { get; }

    /// <summary>What a disabled control's tooltip says — that a provider has to be assigned in the workspace configuration;
    /// null while enabled, so the control keeps its own.</summary>
    string? DisabledTip { get; }

    /// <summary>Gets every ability ready for a use the user asked for, as <see cref="IAIService.MakeReadyAsync"/> does each:
    /// true when all serve now; false when one waits on a download — offered, and started if the user agrees — so the use
    /// stops there.</summary>
    Task<bool> MakeReadyAsync(CancellationToken ct = default);
}
