using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Markdown.ViewModels;

/// <summary>
/// The gate a whole-document button sits behind, bound as any <see cref="IAiAbilityGate"/> is: open when the workspace's
/// Documents model serves and nothing stands in the button's way — the document not one it works on, the same work under way
/// on it already — closed otherwise, its tooltip saying which. What stands in the way is said first: it is what the reader can
/// see and act on, and no model would change it.
/// </summary>
public sealed class DocumentWorkGate : ObservableObject, IAiAbilityGate
{
    private readonly IAiAbilityGate _model;
    private string? _refusal;

    /// <param name="model">The gate on the workspace's Documents model; shared by a page's buttons and disposed by the page.</param>
    public DocumentWorkGate(IAiAbilityGate model)
    {
        _model = model;
        _model.PropertyChanged += OnModelChanged;
    }

    public IReadOnlyList<AiAbility> Abilities => _model.Abilities;

    public bool IsEnabled => _refusal is null && _model.IsEnabled;

    public string? DisabledTip => _refusal ?? _model.DisabledTip;

    /// <summary>What stands in the button's way now, as its tooltip says it; null where nothing does.</summary>
    public string? Refusal
    {
        get => _refusal;
        set
        {
            if (!SetProperty(ref _refusal, value)) return;
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(DisabledTip));
        }
    }

    public Task<bool> MakeReadyAsync(CancellationToken ct = default) => _refusal is null ? _model.MakeReadyAsync(ct) : Task.FromResult(false);

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(e.PropertyName);

    public void Dispose() => _model.PropertyChanged -= OnModelChanged;
}
