using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Projects.Model;

/// <summary>
/// Maps a legacy (pre-2.0) <c>.project</c> JSON object forward into the v2 <see cref="ProjectInfo"/>,
/// entirely in memory — the loader never writes, so merely browsing an old project is safe. Removed
/// fields are folded into the new markdown fields under headings so nothing is lost:
/// <list type="bullet">
///   <item><c>Scope</c> → appended to the project <c>Description</c> under a "## Scope" heading.</item>
///   <item>each <c>Objectives[i]</c> → a completion criterion (status <c>Should</c>).</item>
///   <item>a backlog item's <c>Notes</c> / <c>ImpPlan</c> / <c>TestPlan</c> → its <c>Description</c> under
///         "## Notes" / "## Implementation Plan" / "## Test Plan" headings.</item>
/// </list>
/// The migrated model carries <see cref="ProjectInfo.SchemaVersion"/> = 1 so callers can prompt to
/// persist the upgrade (which stamps it to the current version).
/// </summary>
public static class ProjectMigration
{
    public static ProjectInfo FromLegacy(JsonObject node, string fallbackName)
    {
        var info = new ProjectInfo
        {
            SchemaVersion = 1,  // legacy-in-memory; not yet persisted as v2
            Name          = Str(node, "name") is { Length: > 0 } n ? n : fallbackName,
            SolutionFile  = Str(node, "solutionFile"),
            LastUpdate    = DateTimeOrNull(Prop(node, "lastUpdate")),
        };

        // Description + rolled-in Scope.
        var description = Str(node, "description") ?? string.Empty;
        var scope = Str(node, "scope");
        if (!string.IsNullOrWhiteSpace(scope))
            description = Append(description, "## Scope", scope!);
        info.Description = description;

        // Objectives → completion criteria (all "should" by default).
        if (Prop(node, "objectives") is JsonArray objectives)
            foreach (var o in objectives)
                if (o?.GetValue<string>() is { Length: > 0 } text)
                    info.CompletionCriteria.Add(new CompletionCriterion { Text = text, Status = CompletionStatus.Should });

        // Backlog: fold notes / impl plan / test plan into a single markdown description.
        if (Prop(node, "backlog") is JsonArray backlog)
            foreach (var b in backlog)
            {
                if (b is not JsonObject item) continue;
                var desc = string.Empty;
                if (Str(item, "notes") is { Length: > 0 } notes)       desc = Append(desc, "## Notes", notes);
                if (Str(item, "impPlan") is { Length: > 0 } impPlan)   desc = Append(desc, "## Implementation Plan", impPlan);
                if (Str(item, "testPlan") is { Length: > 0 } testPlan) desc = Append(desc, "## Test Plan", testPlan);

                info.Backlog.Add(new BacklogItem
                {
                    Id          = GuidOrNew(Str(item, "id")),
                    Title       = Str(item, "title") ?? string.Empty,
                    Description = desc,
                    // The legacy enum was serialized by name (e.g. "AwaitingFinalisation"); it matches the
                    // default seeded status keys 1:1, so copy it verbatim.
                    StatusKey   = Str(item, "status") is { Length: > 0 } s ? s : "NotStarted",
                });
            }

        return info;
    }

    // ── helpers ──

    private static string Append(string body, string heading, string content)
    {
        var sb = new StringBuilder(body);
        if (sb.Length > 0) sb.Append("\n\n");
        sb.Append(heading).Append("\n\n").Append(content.Trim());
        return sb.ToString();
    }

    private static JsonNode? Prop(JsonObject o, string name)
    {
        foreach (var kv in o)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    private static string? Str(JsonObject o, string name)
    {
        var node = Prop(o, name);
        try { return node?.GetValue<string>(); }
        catch { return node?.ToString(); }
    }

    private static Guid GuidOrNew(string? s) => Guid.TryParse(s, out var g) ? g : Guid.NewGuid();

    private static DateTime? DateTimeOrNull(JsonNode? node)
    {
        if (node is null) return null;
        try { return node.GetValue<DateTime>(); }
        catch { return DateTime.TryParse(node.ToString(), out var d) ? d : null; }
    }
}
