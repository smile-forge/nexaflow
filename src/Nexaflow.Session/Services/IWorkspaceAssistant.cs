using Nexaflow.Providers.Common;

namespace Nexaflow.Session.Services;

/// <summary>
/// What a workspace runtime holds of its assistant, which the session does not build itself: the assistant as features see it
/// (<see cref="IAIService"/>), and its part in the runtime's lifetime — the runtime hands it the providers it acquired, one per
/// column of its ability grid, takes them back as it rests, and disposes it as it ends.
/// </summary>
public interface IWorkspaceAssistant : IAIService, IDisposable
{
    /// <summary>Uses <paramref name="execution"/> — each ability-grid column's provider — in place of whatever it used.</summary>
    void UseProviders(IReadOnlyDictionary<string, IAiProvider> execution);

    /// <summary>Lets go of every provider: they are pooled and outlive the assistant, so one another workspace still uses no
    /// longer holds it alive.</summary>
    void ReleaseProviders();
}
