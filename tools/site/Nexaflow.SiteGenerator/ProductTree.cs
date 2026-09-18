using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexaflow.SiteGenerator;

/// <summary>
/// The product tree as it stood at a release — the descriptions the team maintains while building, and the
/// cross-cutting concerns each feature carries. The site reads the newest snapshot under docs/product/ rather
/// than the working .product/ folder, so it describes what actually shipped and not work half-done on a branch.
/// </summary>
internal sealed class ProductTree
{
    /// <summary>A node: what it is called, what it does, and the status of each concern it carries.</summary>
    internal sealed record Node(string Title, string Description, string Status, IReadOnlyDictionary<string, string> Concerns)
    {
        /// <summary>True when the assistant can genuinely work this feature — not a claim, the tree's own tag.</summary>
        public bool AiReady => Concerns.TryGetValue("AI Ready", out var status) && status == "done";

        /// <summary>True when the page answers <c>?</c> itself rather than handing the search elsewhere.</summary>
        public bool Searchable => Concerns.TryGetValue("Search (?)", out var status) && status == "done";
    }

    private readonly IReadOnlyDictionary<string, Node> _nodes;

    /// <summary>The release the descriptions came from, e.g. v1.6.0.</summary>
    public string Version { get; }

    private ProductTree(string version, IReadOnlyDictionary<string, Node> nodes)
    {
        Version = version;
        _nodes = nodes;
    }

    public Node? Find(string? id) => id is not null && _nodes.TryGetValue(id, out var node) ? node : null;

    public static ProductTree Read(string repo)
    {
        var folder = Path.Combine(repo, "docs", "product");
        var snapshot = Directory.GetFiles(folder, "v*.json")
            .Select(path => (Path: path, Order: Sortable(Path.GetFileNameWithoutExtension(path))))
            .OrderByDescending(f => f.Order, StringComparer.Ordinal)
            .Select(f => f.Path)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"no release snapshot (v*.json) in {folder}");

        using var document = JsonDocument.Parse(File.ReadAllText(snapshot));
        var root = document.RootElement;

        var nodes = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in root.GetProperty("nodes").EnumerateObject())
        {
            var concerns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (entry.Value.TryGetProperty("concerns", out var tagged))
                foreach (var concern in tagged.EnumerateArray())
                    concerns[concern.GetProperty("tag").GetString() ?? ""] = Text(concern, "status");

            nodes[entry.Name] = new Node(Text(entry.Value, "title"), Text(entry.Value, "description"), Text(entry.Value, "status"), concerns);
        }

        return new ProductTree(Text(root, "version"), nodes);
    }

    private static string Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>v1-10-0 has to sort above v1-6-0, so each part is padded before the names are compared.</summary>
    private static string Sortable(string name)
        => Regex.Replace(name.TrimStart('v', 'V'), @"\d+", m => m.Value.PadLeft(6, '0'));
}
