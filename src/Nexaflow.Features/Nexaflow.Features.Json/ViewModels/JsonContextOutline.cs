using Nexaflow.Features.Json.Models;

namespace Nexaflow.Features.Json.ViewModels;

/// <summary>
/// A node's entries as a few lines of text, for the context preview: each child's key and value, with a
/// container shown by its size (<c>{3}</c>, <c>[12]</c>) rather than opened, and an entry not yet loaded from a
/// large file as <c>…</c>. A reminder of where the user is in the document, not a second tree.
/// </summary>
internal static class JsonContextOutline
{
    /// <summary>The most entries listed; the rest are counted.</summary>
    internal const int MaxEntries = 12;

    private const int MaxValueLength = 60;

    public static string Of(JsonNodeModel? node) => node switch
    {
        null                  => string.Empty,
        JsonObjectNodeModel o => Entries(o.Children),
        JsonArrayNodeModel a  => Entries(a.Children),
        _                     => Line(node),
    };

    private static string Entries(IReadOnlyList<JsonNodeModel> children)
    {
        var lines = children.Take(MaxEntries).Select(Line).ToList();
        if (children.Count > MaxEntries) lines.Add($"… +{children.Count - MaxEntries:N0}");
        return string.Join("\n", lines);
    }

    private static string Line(JsonNodeModel node) => $"{node.DisplayKey}: {Summary(node)}";

    private static string Summary(JsonNodeModel node) => node switch
    {
        JsonObjectNodeModel o => $"{{{o.Children.Count:N0}}}",
        JsonArrayNodeModel a  => $"[{a.Children.Count:N0}]",
        JsonValueNodeModel v  => v.DisplayValue.Length > MaxValueLength ? v.DisplayValue[..(MaxValueLength - 1)] + "…" : v.DisplayValue,
        _                     => "…",
    };
}
