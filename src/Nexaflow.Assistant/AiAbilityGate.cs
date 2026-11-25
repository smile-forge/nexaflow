
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Localization;

namespace Nexaflow.Assistant;

/// <summary>
/// <see cref="IAiAbilityGate"/> over a workspace's <see cref="IAIService"/>: read again whenever its abilities may have
/// changed, on the thread that made it.
/// </summary>
internal sealed partial class AiAbilityGate : ObservableObject, IAiAbilityGate
{
    private readonly IAIService _ai;
    /// <summary>The thread that made the gate, which its bindings read it on; null when that thread has none to post to.</summary>
    private readonly SynchronizationContext? _home = SynchronizationContext.Current;

    public AiAbilityGate(IAIService ai, IReadOnlyList<AiAbility> abilities)
    {
        _ai       = ai;
        Abilities = abilities;
        _ai.AbilitiesChanged += OnAbilitiesChanged;
        Refresh();
    }

    public IReadOnlyList<AiAbility> Abilities { get; }

    [ObservableProperty] private bool _isEnabled;

    public string? DisabledTip => Str.Get("Shell.Ai.AbilityDisabled");

    public async Task<bool> MakeReadyAsync(CancellationToken ct = default)
    {
        foreach (var ability in Abilities)
            if (!await _ai.MakeReadyAsync(ability, ct)) return false;
        return true;
    }

    private void OnAbilitiesChanged(object? sender, EventArgs e)
    {
        if (_home is null || SynchronizationContext.Current == _home) Refresh();
        else _home.Post(_ => Refresh(), null);
    }

    private void Refresh() => IsEnabled = Abilities.All(a => _ai.StatusOf(a).IsUsable);

    public void Dispose() => _ai.AbilitiesChanged -= OnAbilitiesChanged;
}
