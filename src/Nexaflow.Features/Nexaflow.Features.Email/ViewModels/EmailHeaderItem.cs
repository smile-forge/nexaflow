using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Email.ViewModels;

/// <summary>One row in the raw "All headers" list. Carries a <see cref="Highlighted"/> flag so a "?" search
/// run while the header list is open can light up the header that matched.</summary>
internal sealed partial class EmailHeaderItem(string field, string value) : ObservableObject
{
    public string Field { get; } = field;
    public string Value { get; } = value;

    /// <summary>True while a search matched this header row (only searched when the list is expanded).</summary>
    [ObservableProperty] private bool _highlighted;
}
