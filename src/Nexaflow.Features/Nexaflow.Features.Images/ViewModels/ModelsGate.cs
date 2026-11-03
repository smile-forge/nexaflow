using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>Something on screen already offering a download of models — a picture tool's banner, for an edit the recipe asks for.</summary>
public interface IDownloadOffer : INotifyPropertyChanged
{
    bool CanDownload { get; }
}

/// <summary>
/// One set of models some of the editor's controls need — picture selection, picture erasing, the picture tools — and whether
/// they are here. Until they are, those controls are disabled and say why; while a panel that needs them is open, the editor's
/// banner offers AI picture editing's models, all of them as one (<see cref="PictureModels"/>), unless a picture tool's own
/// banner already does.
/// </summary>
public sealed partial class ModelsGate : ObservableObject
{
    private readonly IToolModels? _models;
    private readonly PictureModels _set;
    /// <param name="models">The models these controls need, where the app has them.</param>
    /// <param name="set">AI picture editing's models as one: what is offered, and how it is spoken of.</param>
    /// <param name="runOnUi">Hops to the UI thread, where the gate is read.</param>
    public ModelsGate(IToolModels? models, PictureModels set, Func<Action, Task> runOnUi)
    {
        _models = models;
        _set = set;
        if (models is not null) models.Changed += (_, _) => _ = runOnUi(Refresh);
        set.Changed += (_, _) => Refresh();
        set.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PictureModels.IsOfferedByATool)) OnPropertyChanged(nameof(ShowsOffer));
        };
    }

    /// <summary>This machine runs them at all: the controls that need them show.</summary>
    public bool IsOffered => _models is { State: not ToolModelsState.Unavailable };

    /// <summary>They are here: the controls that need them work.</summary>
    public bool IsReady => _models is { State: ToolModelsState.Ready };

    /// <summary>A panel that needs them is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsOffer))]
    private bool _isWanted;

    /// <summary>The editor's banner offers the models: wanted, runnable here, not here yet, and not offered by a picture tool's banner
    /// already — they are one download, whichever tool's banner offers it (<see cref="PictureModels.IsOfferedByATool"/>).</summary>
    public bool ShowsOffer => IsWanted && IsOffered && !IsReady && !_set.IsOfferedByATool;

    /// <summary>What a control that needs them says while it is disabled; null once they are here.</summary>
    public string? DisabledReason => _set.DisabledReason(_models);

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsOffered));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(ShowsOffer));
        OnPropertyChanged(nameof(DisabledReason));
    }
}
