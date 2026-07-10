using System.Text.Json.Serialization;

namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// The result of validating every snaplink in a tree, persisted to <c>.product/integrity.json</c> so the
/// count survives a restart and the Integrity view has something to show before the next scan. It is a
/// <em>derived</em> artifact — always safe to delete and regenerate by re-running the validation.
/// </summary>
public sealed class IntegrityReport
{
    /// <summary>When the scan ran (round-trip "o" format), for the "last checked" line.</summary>
    public string? Generated { get; set; }

    public int ScannedNodes { get; set; }
    public int ScannedSnaplinks { get; set; }

    /// <summary>Broken links, ordered by node then concern then index so the report diffs cleanly.</summary>
    public List<IntegrityIssue> Issues { get; set; } = [];

    [JsonIgnore] public bool IsClean => Issues.Count == 0;
    [JsonIgnore] public int IssueCount => Issues.Count;
}
