using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;

namespace Nexaflow.Features.Projects.ViewModels;

/// <summary>
/// Editable row in the project's Completion criteria list: an acceptance statement plus its
/// should / shouldn't / done / faulted status. Edits fire <c>onChanged</c> so the parent persists.
/// </summary>
public partial class CompletionCriterionViewModel : ObservableObject
{
    private readonly Action _onChanged;

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private CompletionStatus _status = CompletionStatus.Should;

    /// <summary>The four statuses offered by the row's status dropdown.</summary>
    public IReadOnlyList<CompletionStatus> AllStatuses { get; } =
        [CompletionStatus.Should, CompletionStatus.Shouldnt, CompletionStatus.Done, CompletionStatus.Faulted];

    public CompletionCriterionViewModel(CompletionCriterion model, Action onChanged)
    {
        _text     = model.Text;
        _status   = model.Status;
        _onChanged = onChanged;
    }

    public CompletionCriterionViewModel(Action onChanged) => _onChanged = onChanged;

    partial void OnTextChanged(string value) => _onChanged();
    partial void OnStatusChanged(CompletionStatus value) => _onChanged();

    public CompletionCriterion ToModel() => new() { Text = Text, Status = Status };
}
