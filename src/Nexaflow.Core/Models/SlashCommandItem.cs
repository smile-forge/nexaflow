using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Core.Models;

/// <summary>
/// One row in the AI input's "/" command palette: a ribbon item or a default-openable page the user can
/// jump to by name. <see cref="Invoke"/> carries the open action (open the tab / run the ribbon item) so
/// the palette doesn't need to know how each kind is launched.
/// </summary>
public sealed partial class SlashCommandItem : ObservableObject
{
    public required string Icon     { get; init; }
    public required string Label    { get; init; }

    /// <summary>"Page" or "Ribbon" — shown faintly on the right so duplicate labels are distinguishable.</summary>
    public required string Category { get; init; }

    /// <summary>Opens the target. Run on the UI thread from the palette.</summary>
    public required Action Invoke   { get; init; }

    /// <summary>The keyboard-highlighted row. Driven from the view-model, since focus stays in the text box
    /// (so the list can't rely on its own selection visuals).</summary>
    [ObservableProperty] private bool _isHighlighted;
}
