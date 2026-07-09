using Nexaflow.Features.Common;

namespace Nexaflow.Features.Projects.Model;

/// <summary>
/// One project completion criterion — an acceptance statement plus its <see cref="CompletionStatus"/>
/// (should / shouldn't / done / faulted), the same four-state vocabulary the Product feature uses.
/// Replaces the old free-text objectives list.
/// </summary>
public class CompletionCriterion
{
    public string Text { get; set; } = string.Empty;
    public CompletionStatus Status { get; set; } = CompletionStatus.Should;
}
