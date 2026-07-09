using System;

namespace Nexaflow.Features.Projects.Model;

/// <summary>
/// One backlog item. <see cref="StatusKey"/> references a configured <see cref="BacklogStatusDef.Key"/>
/// by NAME (never an index), so reordering the workspace's statuses never moves an item and deleting a
/// status leaves the item flagged invalid rather than silently remapped. <see cref="Description"/> is
/// markdown (was the separate notes / implementation-plan / test-plan fields).
/// </summary>
public class BacklogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The configured status this item is in, by <see cref="BacklogStatusDef.Key"/>.</summary>
    public string StatusKey { get; set; } = "NotStarted";
}
