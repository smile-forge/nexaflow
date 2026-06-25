using System.Text;
using Nexaflow.Features.ProductManager.Model;

namespace Nexaflow.Features.ProductManager.Services;

/// <summary>
/// Renders a product snapshot to the human-readable <c>PRODUCT.md</c> dashboard — the committed artifact a
/// PR reviewer reads. Pure; no UI/IO.
/// </summary>
public static class ProductMarkdown
{
    public static string Render(ProductState s, string version, string? date)
    {
        var sb = new StringBuilder();
        var name = string.IsNullOrWhiteSpace(s.Product.Product) ? "Product" : s.Product.Product;
        sb.AppendLine($"# {name} — product status").AppendLine();
        sb.Append($"_Version **{version}**");
        if (!string.IsNullOrWhiteSpace(date)) sb.Append($" · {date}");
        sb.AppendLine("_").AppendLine();

        var leaves = s.Nodes.Where(kv => kv.Value.Children.All(c => !s.Nodes.ContainsKey(c))).Select(kv => kv.Value).ToList();
        int Count(Status st) => leaves.Count(n => n.Status == st);
        int done = Count(Status.Done), total = leaves.Count;
        var pct = total == 0 ? 0 : (int)Math.Round(100.0 * done / total);
        sb.AppendLine($"**{pct}% shipping** — {done}/{total} components done " +
                      $"({Count(Status.Should)} should · {Count(Status.Faulted)} faulted · {Count(Status.Shouldnt)} shouldn't)")
          .AppendLine();

        sb.AppendLine("## Tree").AppendLine();
        foreach (var root in ProductAggregator.Roots(s)) RenderNode(s, root, 0, sb);
        sb.AppendLine();

        var faulted = s.Nodes.Where(kv => kv.Value.Status == Status.Faulted).ToList();
        sb.AppendLine("## Needs attention").AppendLine();
        if (faulted.Count == 0) sb.AppendLine("_Nothing faulted._");
        else
            foreach (var (_, n) in faulted)
                sb.AppendLine($"- **{Word(n.Status)}** {n.Title}" + (string.IsNullOrWhiteSpace(n.Note) ? "" : $" — {n.Note}"));
        sb.AppendLine();

        var usedConcerns = s.Product.Concerns.Select(c => c.Name)
            .Where(name => leaves.Any(n => n.Concerns?.Any(l => l.Tag == name) == true)).ToList();
        if (usedConcerns.Count > 0)
        {
            sb.AppendLine("## Concerns").AppendLine().AppendLine("| Concern | Done |").AppendLine("|---|---|");
            foreach (var c in usedConcerns)
            {
                var cd = leaves.Count(n => n.Concerns?.Any(l => l.Tag == c && l.Status == Status.Done) == true);
                sb.AppendLine($"| {c} | {cd}/{leaves.Count} |");
            }
        }
        return sb.ToString();
    }

    private static void RenderNode(ProductState s, string id, int depth, StringBuilder sb)
    {
        var n = s.Nodes[id];
        var indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}- **{n.Title}** — _{Word(n.Status)}_" + (string.IsNullOrWhiteSpace(n.Note) ? "" : $" — {n.Note}"));
        foreach (var c in n.Children.Where(s.Nodes.ContainsKey)) RenderNode(s, c, depth + 1, sb);
    }

    private static string Word(Status s) => s == Status.Shouldnt ? "shouldn't" : s.ToString().ToLowerInvariant();
}
