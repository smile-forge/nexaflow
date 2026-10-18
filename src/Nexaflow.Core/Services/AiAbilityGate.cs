using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services;

/// <summary>
/// <see cref="IAiAbilityGate"/> over a workspace's <see cref="IAIService"/>: read again whenever its abilities may have
/// changed, on the thread that made it.
/// </summary>
internal sealed partial class AiAbilityGate : ObservableObject, IAiAbilityGate
{
    private readonly IAIService _ai;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    public AiAbilityGate(IAIService ai, IReadOnlyList<AiAbility> abilities)
    {
        _ai       = ai;
        Abilities = abilities;
        _ai.AbilitiesChanged += OnAbilitiesChanged;
        Refresh();
    }

    public IReadOnlyList<AiAbility> Abilities { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisabledTip))]
    private bool _isEnabled;

    public string? DisabledTip => IsEnabled ? null : Str.Get("Shell.Ai.AbilityDisabled");

    public async Task<bool> MakeReadyAsync(CancellationToken ct = default)
    {
        foreach (var ability in Abilities)
            if (!await _ai.MakeReadyAsync(ability, ct)) return false;
        return true;
    }

    private void OnAbilitiesChanged(object? sender, EventArgs e)
    {
        if (_ui.CheckAccess()) Refresh();
        else _ui.InvokeAsync(Refresh);
    }

    private void Refresh() => IsEnabled = Abilities.All(a => _ai.StatusOf(a).IsUsable);

    public void Dispose() => _ai.AbilitiesChanged -= OnAbilitiesChanged;
}
