using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Git;

/// <summary>Which way a git graph runs: its commits across the page, down it, or up it.</summary>
public enum GitWay { LeftRight, TopBottom, BottomTop }

/// <summary>How a commit is kept: an ordinary one, one undoing what came before, or one drawn to stand out.</summary>
public enum GitKept { Normal, Reverse, Highlight }

/// <summary>What an option says, without the quotes it is written in, and the piece it was written as.</summary>
/// <param name="Part">What pressing it means.</param>
public sealed record GitSaid(ContentPart Part, string Says);

/// <summary>One branch: where it is made, what it is called, where it asked its lane to go, and where the lane is.</summary>
/// <param name="Part">The line making it — null for the branch everything starts on, which nothing makes.</param>
/// <param name="Named">The name as written, typed into where the branch is labelled.</param>
public sealed record GitBranch(ContentPart? Part, ContentPart? Named, string Name, double? Order, int Made, int Lane);

/// <summary>
/// One commit: where it is written, what it is called, the commits it follows, and how it is drawn.
/// </summary>
/// <param name="Part">The line it is written on — what pressing it means.</param>
/// <param name="Id">What it is called: the id written for it, or one of the graph's own where none is.</param>
/// <param name="Said">The id as written, where one is — what a commit says under itself.</param>
/// <param name="Parents">What it follows: the branch's last commit, then the branch merged in or the commit picked.</param>
/// <param name="Taken">The commit a cherry-pick takes, as written.</param>
/// <param name="Tags">Every tag written for it, in the order written — or, for a cherry-pick nothing tags, what it takes.</param>
public sealed record GitCommit(ContentPart Part, string Id, GitSaid? Said, GitSaid? Taken, IReadOnlyList<GitSaid> Tags, GitKept Kept, GitBranch Branch,
                               int Position, IReadOnlyList<string> Parents, bool Merge, bool Picked, int Order);

/// <summary>
/// A <c>gitGraph</c> block, read: its branches in the lanes they take, and its commits with what each follows — the
/// history the block writes as a graph to draw. Its title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// <para>
/// A branch takes its lane from its <c>order:</c> as Mermaid orders them — a branch writing none ordered as "0." and how
/// many were made before it, so before any asking for 1 or more — and the branch everything starts on is the front
/// matter's <c>mainBranchName</c>, at its <c>mainBranchOrder</c>. A commit follows the last commit on the branch it is
/// made on, a merge follows that branch merged in as well, and a cherry-pick follows the commit it takes. Which branch a
/// commit is made on is the stage's (<see cref="Stages.ResolveGraph"/>).
/// </para>
/// </summary>
public sealed class GitGraph
{
    private GitGraph(MermaidBlock block, GitConfig config) => (Block, Config) = (block, config);

    

    public static GitGraph Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static GitGraph Of(MermaidBlock block)
    {
        var graph = new GitGraph(block, GitConfig.Read(block.Config));
        var config = graph.Config;

        var branches = new List<(ContentPart? Part, ContentPart? Named, string Name, double? Order, int Made)>
        {
            (null, null, config.MainBranchName, config.MainBranchOrder, 0),
        };

        var made = new List<(ContentPart Part, string Id, GitSaid? Said, GitSaid? Taken, IReadOnlyList<GitSaid> Tags, GitKept Kept, string Branch,
                             int Position, List<string> Parents, bool Merge, bool Picked)>();
        var placed = new Dictionary<string, int>(StringComparer.Ordinal);

        var heads = new Dictionary<string, string>(StringComparer.Ordinal);

        var current = config.MainBranchName;
        var at = 0;
        var own = 0;

        foreach (var part in block.Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case GitKinds.Direction when Running(part) is { } way:
                    graph.Way = way;
                    break;

                case GitKinds.Branch when Named(part) is { Length: > 0 } name:
                    if (!branches.Any(branch => StringComparer.Ordinal.Equals(branch.Name, name)))
                        branches.Add((part, part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name), name, Number(part, "order"), branches.Count));

                    if (heads.TryGetValue(current, out var branched)) heads[name] = branched;
                    current = name;
                    break;

                case GitKinds.Checkout when Named(part) is { Length: > 0 } wanted:
                    if (branches.Any(branch => StringComparer.Ordinal.Equals(branch.Name, wanted))) current = wanted;
                    break;

                case GitKinds.Commit or GitKinds.Merge or GitKinds.Pick:
                    var on = part.Fact(GitRoles.On) is { Length: > 0 } fact ? fact : current;
                    var picked = part.Kind == GitKinds.Pick;
                    var taken = Said(part, "id");

                    // An id written twice is wrong (the stage says so), but is drawn as Mermaid draws it: the id names the newest
                    // commit given it, and the older keeps one of its own.
                    var id = !picked && taken is { Says.Length: > 0 } ? taken.Says : Own(part.Kind, own++);
                    if (placed.ContainsKey(id) && made.FindIndex(commit => StringComparer.Ordinal.Equals(commit.Id, id)) is var older and >= 0)
                    {
                        var renamed = Own(part.Kind, own++);
                        made[older] = made[older] with { Id = renamed };
                        placed[renamed] = placed[id];
                        foreach (var (branch, head) in heads.ToList())
                            if (StringComparer.Ordinal.Equals(head, id)) heads[branch] = renamed;
                        foreach (var commit in made)
                            for (var follows = 0; follows < commit.Parents.Count; follows++)
                                if (StringComparer.Ordinal.Equals(commit.Parents[follows], id)) commit.Parents[follows] = renamed;
                    }
                    var parents = new List<string>();
                    if (heads.TryGetValue(on, out var parent)) parents.Add(parent);

                    if (part.Kind == GitKinds.Merge && Named(part) is { Length: > 0 } merged && heads.TryGetValue(merged, out var second)) parents.Add(second);
                    if (picked && taken is { Says.Length: > 0 }) parents.Add(taken.Says);

                    // One after another, every commit takes the next place along; side by side, as Mermaid places them,
                    // a commit stands one past the furthest of what it follows, so branches made together run level.
                    var position = config.ParallelCommits
                        ? parents.Select(follows => placed.TryGetValue(follows, out var there) ? there + 1 : 0).DefaultIfEmpty(0).Max()
                        : at;
                    placed[id] = position;
                    at++;

                    // A cherry-pick nothing tags is tagged with the commit it takes, as Mermaid tags one — and, taking a merge,
                    // with which of the merge's parents it takes it against.
                    var tags = Saying(part, "tag");
                    if (picked && tags.Count == 0 && taken is { Says.Length: > 0 })
                    {
                        var against = made.Any(commit => commit.Merge && StringComparer.Ordinal.Equals(commit.Id, taken.Says)) && Said(part, "parent") is { Says.Length: > 0 } of
                            ? $"|parent:{of.Says}"
                            : "";
                        tags = [new GitSaid(taken.Part, $"cherry-pick:{taken.Says}{against}")];
                    }

                    made.Add((part, id, picked ? null : taken, picked ? taken : null, tags, Keeping(part), on, position, parents,
                              part.Kind == GitKinds.Merge, picked));
                    heads[on] = id;
                    break;
            }
        }

        // A lane each, in the order the branches ask for. As Mermaid orders them, a branch asking nothing is ordered as
        // "0." and the count of branches made before it — so it comes before any asking for 1 or more — and the branch
        // everything starts on asks for nought where the front matter does not say.
        var lanes = branches
            .OrderBy(branch => branch.Made == 0 ? branch.Order ?? 0 : branch.Order ?? Unasked(branch.Made))
            .Select((branch, lane) => new GitBranch(branch.Part, branch.Named, branch.Name, branch.Order, branch.Made, lane))
            .ToList();

        graph.Branches = [.. lanes.OrderBy(branch => branch.Made)];
        graph.Commits =
        [
            .. made.Select((commit, order) => new GitCommit(commit.Part, commit.Id, commit.Said, commit.Taken, commit.Tags, commit.Kept,
                lanes.First(branch => StringComparer.Ordinal.Equals(branch.Name, commit.Branch)),
                commit.Position, commit.Parents, commit.Merge, commit.Picked, order)),
        ];

        return graph;
    }

    public MermaidBlock Block { get; }

    public GitConfig Config { get; }

    /// <summary>Which way it runs — what the header asks for, or across the page.</summary>
    public GitWay Way { get; private set; } = GitWay.LeftRight;

    /// <summary>The branches, in the order they are made — the branch everything starts on first.</summary>
    public IReadOnlyList<GitBranch> Branches { get; private set; } = [];

    /// <summary>The commits, in the order they are written.</summary>
    public IReadOnlyList<GitCommit> Commits { get; private set; } = [];

    /// <summary>Whether nothing is written for the diagram to draw.</summary>
    public bool Empty => Commits.Count == 0;

    /// <summary>How far along the last commit is, which is how long the history runs.</summary>
    public int Length => Commits.Count == 0 ? 0 : Commits.Max(commit => commit.Position);

    /// <summary>How many lanes there are, which is how wide the graph is across the branches.</summary>
    public int Across => Branches.Count == 0 ? 0 : Branches.Max(branch => branch.Lane);

    /// <summary>The commit something names, or null where nothing above has that id.</summary>
    public GitCommit? Of(string? id) => id is null ? null : Commits.FirstOrDefault(commit => StringComparer.Ordinal.Equals(commit.Id, id));

    /// <summary>What a commit the block does not name is called, which nothing is drawn from.</summary>
    private static string Own(string kind, int at) =>
        (kind == GitKinds.Merge ? "_merge" : kind == GitKinds.Pick ? "_pick" : "_commit") + at.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static GitWay? Running(ContentPart line) =>
        line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting) is { } way
            ? way.Text.Trim().ToUpperInvariant() switch { "LR" => GitWay.LeftRight, "TB" => GitWay.TopBottom, "BT" => GitWay.BottomTop, _ => null }
            : null;

    private static GitKept Keeping(ContentPart line) =>
        Said(line, "type")?.Says.ToUpperInvariant() switch
        {
            "REVERSE" => GitKept.Reverse,
            "HIGHLIGHT" => GitKept.Highlight,
            _ => GitKept.Normal,
        };

    /// <summary>The branch a line names, or null where it names none.</summary>
    private static string? Named(ContentPart line) =>
        line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text;

    /// <summary>What a line's option says, and the piece saying it — or null where the line does not set it, or what it sets is wrong.</summary>
    private static GitSaid? Said(ContentPart line, string key) =>
        line.Inner(MermaidKinds.Properties)?.Children
            .Where(property => property.Kind == MermaidKinds.Property)
            .Where(property => property.Children.Any(child => child.Kind == MermaidKinds.Key && child.Text.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Select(property => property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting))
            .OfType<ContentPart>()
            .Select(setting => new GitSaid(setting, MermaidText.Bare(setting.Text)))
            .FirstOrDefault();

    /// <summary>Everything a line's option says, each time the line sets it — a commit may be tagged more than once.</summary>
    private static IReadOnlyList<GitSaid> Saying(ContentPart line, string key) =>
        line.Inner(MermaidKinds.Properties)?.Children
            .Where(property => property.Kind == MermaidKinds.Property)
            .Where(property => property.Children.Any(child => child.Kind == MermaidKinds.Key && child.Text.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Select(property => property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting))
            .OfType<ContentPart>()
            .Select(setting => new GitSaid(setting, MermaidText.Bare(setting.Text)))
            .ToList() ?? [];

    /// <summary>Where Mermaid orders a branch that asks for nowhere: the number written as "0." and how many were made before it.</summary>
    private static double Unasked(int made) => double.Parse("0." + made.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);

    private static double? Number(ContentPart line, string key) => MermaidNumber.Read(Said(line, key)?.Says);
}
