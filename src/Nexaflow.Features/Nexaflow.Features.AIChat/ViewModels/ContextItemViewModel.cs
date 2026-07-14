using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// One chip in the conversation's context strip. Wraps the pinned <see cref="Page"/> because the chip needs
/// view state the page cannot carry: <see cref="IsSelected"/> (which drives the preview panel) and
/// <see cref="IsFlashing"/> (the brief pulse that answers a duplicate add). <see cref="Page"/> lives in
/// <c>Features.Common</c> and is shared with the tab strip — it has no business growing per-conversation
/// selection state.
/// </summary>
public sealed partial class ContextItemViewModel(Page page) : ObservableObject
{
    public Page Page => page;

    /// <summary>Selected chip — exactly one at a time; opens the preview panel.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Pulses when the user tries to add this item again, so the answer to "why did nothing
    /// happen?" is "because it's already here — look."</summary>
    [ObservableProperty] private bool _isFlashing;
}
