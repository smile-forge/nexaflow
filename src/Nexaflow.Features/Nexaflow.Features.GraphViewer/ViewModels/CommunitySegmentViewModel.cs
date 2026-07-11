using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.GraphViewer.ViewModels;

/// <summary>
/// One community in the Segments rail: a swatch (matching the node fill), a representative label, the count of
/// nodes it contributes to the current neighbourhood, and a show/hide toggle. The initial visibility is set via the
/// backing field so constructing the row (during a rail rebuild) never fires <see cref="OnIsVisibleChanged"/> —
/// only a genuine user toggle calls back to the parent view-model.
/// </summary>
public sealed partial class CommunitySegmentViewModel : ObservableObject
{
    private readonly Action<CommunitySegmentViewModel>? _onToggle;

    public CommunitySegmentViewModel(int id, string label, int count, Brush swatch, bool isVisible,
                                     Action<CommunitySegmentViewModel>? onToggle)
    {
        Id = id;
        Label = label;
        Swatch = swatch;
        _count = count;
        _isVisible = isVisible;   // set the backing field directly → no toggle callback during construction
        _onToggle = onToggle;
    }

    public int Id { get; }
    public string Label { get; }
    public Brush Swatch { get; }

    /// <summary>How many nodes this community contributes to the visible neighbourhood.</summary>
    [ObservableProperty] private int _count;

    /// <summary>Whether this community's nodes are shown. Two-way bound to the row's checkbox.</summary>
    [ObservableProperty] private bool _isVisible;

    partial void OnIsVisibleChanged(bool value) => _onToggle?.Invoke(this);
}
