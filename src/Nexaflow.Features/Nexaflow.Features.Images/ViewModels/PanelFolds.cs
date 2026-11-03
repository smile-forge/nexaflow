using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Which of the Artist panels' groups a person has folded away, by name — kept in the Images settings
/// (<see cref="ImagesConfig.FoldedPanelGroups"/>), so every Artist tab opens its panels as they were left. Read by name, as
/// <c>Folds[Light]</c>: true while the group is open.
/// </summary>
public sealed class PanelFolds(ImagesConfig config, IShellServices? shell) : ObservableObject
{
    /// <summary>What a binding to any group listens to: an indexer's change.</summary>
    private const string Indexer = "Item[]";

    public bool this[string group]
    {
        get => !config.FoldedPanelGroups.Contains(group, StringComparer.Ordinal);
        set
        {
            if (value == this[group]) return;
            if (value) config.FoldedPanelGroups.RemoveAll(g => string.Equals(g, group, StringComparison.Ordinal));
            else config.FoldedPanelGroups.Add(group);
            shell?.SaveFeatureConfig(config);
            OnPropertyChanged(Indexer);
        }
    }
}
