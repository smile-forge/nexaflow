using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>gitGraph</c> diagrams.
///
/// Supported:
///   • Orientation header <c>gitGraph LR:/TB:/BT:</c> and <c>title</c>.
///   • <c>commit</c> with <c>id:</c>, <c>tag:</c>, <c>type: NORMAL|REVERSE|HIGHLIGHT</c>.
///   • <c>branch &lt;name&gt; [order: n]</c>, <c>checkout</c>/<c>switch &lt;name&gt;</c>.
///   • <c>merge &lt;branch&gt;</c> (two-parent commit) and <c>cherry-pick id: "&lt;id&gt;"</c>.
///
/// Resolves the commit stream into commits with explicit parents, a branch lane and a position.
/// </summary>
public sealed class MermaidGitGraphParser
{
    public bool CanParse(string language) =>
        language.Equals("gitGraph", StringComparison.OrdinalIgnoreCase);

    public GitGraph Parse(string source)
    {
        var g = new GitGraph();
        try { ParseInto(source, g); } catch { /* never throw; return partial */ }
        AssignLanes(g);
        foreach (var c in g.Commits) c.Lane = g.FindBranch(c.Branch)?.Lane ?? 0;
        return g;
    }

    private static readonly Regex RxId   = new("id:\\s*\"(?<v>[^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex RxTag  = new("tag:\\s*\"(?<v>[^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex RxType = new(@"type:\s*(?<v>NORMAL|REVERSE|HIGHLIGHT)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxOrder= new(@"order:\s*(?<v>\d+)", RegexOptions.Compiled);

    private static void ParseInto(string source, GitGraph g)
    {
        string current = "main";
        var head = new Dictionary<string, string?>(StringComparer.Ordinal) { ["main"] = null };
        EnsureBranch(g, "main", 0, null);

        int pos = 0, autoId = 0, creation = 1;
        bool inFront = false;

        foreach (var rawLine in source.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;
            if (line == "---") { inFront = !inFront; continue; }
            if (inFront) continue;

            var lower = line.ToLowerInvariant();
            if (lower.StartsWith("gitgraph")) { ParseHeader(line, g); continue; }
            if (lower.StartsWith("title "))   { g.Title = line[6..].Trim(); continue; }

            int sp = line.IndexOfAny([' ', '\t']);
            string keyword = (sp < 0 ? line : line[..sp]).ToLowerInvariant();
            string rest    = sp < 0 ? string.Empty : line[(sp + 1)..].Trim();

            switch (keyword)
            {
                case "commit":
                    AddCommit(g, current, head, ref pos, ref autoId, rest, isMerge: false, mergeFrom: null);
                    break;

                case "branch":
                {
                    string name = FirstToken(rest);
                    if (name.Length == 0) break;
                    int? order = RxOrder.Match(rest) is { Success: true } om ? int.Parse(om.Groups["v"].Value) : null;
                    EnsureBranch(g, name, creation++, order);
                    head[name] = head.GetValueOrDefault(current);   // branch point = current head
                    current = name;                                  // branch also checks out
                    break;
                }

                case "checkout":
                case "switch":
                    if (FirstToken(rest) is { Length: > 0 } target) current = target;
                    break;

                case "merge":
                    AddCommit(g, current, head, ref pos, ref autoId, rest, isMerge: true, mergeFrom: FirstToken(rest));
                    break;

                case "cherry-pick":
                    AddCherryPick(g, current, head, ref pos, ref autoId, rest);
                    break;
            }
        }
    }

    // ── Commit construction ──────────────────────────────────────────────────

    private static void AddCommit(GitGraph g, string branch, Dictionary<string, string?> head,
        ref int pos, ref int autoId, string rest, bool isMerge, string? mergeFrom)
    {
        var (id, tag, type) = ParseOptions(rest);
        string commitId = id ?? (isMerge ? "_m" : "_c") + autoId++;

        var c = new GitCommit
        {
            Id = commitId, Branch = branch, Position = pos++,
            Tag = tag, Type = type, IsMerge = isMerge, ShowLabel = id is not null,
        };
        if (head.GetValueOrDefault(branch) is { } parent) c.Parents.Add(parent);
        if (isMerge && mergeFrom is not null && head.GetValueOrDefault(mergeFrom) is { } mp) c.Parents.Add(mp);

        g.Commits.Add(c);
        head[branch] = commitId;
    }

    private static void AddCherryPick(GitGraph g, string branch, Dictionary<string, string?> head,
        ref int pos, ref int autoId, string rest)
    {
        var (sourceId, tag, _) = ParseOptions(rest);   // id: references the cherry-picked commit
        var c = new GitCommit
        {
            Id = "_cp" + autoId++, Branch = branch, Position = pos++,
            Tag = tag, IsCherryPick = true,
        };
        if (head.GetValueOrDefault(branch) is { } parent) c.Parents.Add(parent);
        if (sourceId is not null) c.Parents.Add(sourceId);   // dashed connector to the source commit

        g.Commits.Add(c);
        head[branch] = c.Id;
    }

    private static (string? id, string? tag, GitCommitType type) ParseOptions(string rest)
    {
        string? id  = RxId.Match(rest)  is { Success: true } m1 ? m1.Groups["v"].Value : null;
        string? tag = RxTag.Match(rest) is { Success: true } m2 ? m2.Groups["v"].Value : null;
        var type = RxType.Match(rest) is { Success: true } m3
            ? m3.Groups["v"].Value.ToUpperInvariant() switch
              {
                  "REVERSE"   => GitCommitType.Reverse,
                  "HIGHLIGHT" => GitCommitType.Highlight,
                  _           => GitCommitType.Normal,
              }
            : GitCommitType.Normal;
        return (id, tag, type);
    }

    // ── Branches / lanes ─────────────────────────────────────────────────────

    private static void EnsureBranch(GitGraph g, string name, int creationOrder, int? order)
    {
        if (g.FindBranch(name) is not null) return;
        g.Branches.Add(new GitBranch { Name = name, Order = order, Lane = creationOrder });
    }

    private static void AssignLanes(GitGraph g)
    {
        // Lane currently holds the creation index; sort by explicit order (falling back to it).
        var ordered = g.Branches.OrderBy(b => b.Order ?? b.Lane).ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Lane = i;
    }

    private static void ParseHeader(string line, GitGraph g)
    {
        var m = Regex.Match(line, @"\b(LR|TB|BT)\b", RegexOptions.IgnoreCase);
        if (m.Success)
            g.Orientation = m.Value.ToUpperInvariant() switch
            {
                "TB" => GitOrientation.TopBottom,
                "BT" => GitOrientation.BottomTop,
                _    => GitOrientation.LeftRight,
            };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string FirstToken(string s)
    {
        var t = s.TrimStart();
        int sp = t.IndexOfAny([' ', '\t']);
        return sp < 0 ? t : t[..sp];
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }
}
