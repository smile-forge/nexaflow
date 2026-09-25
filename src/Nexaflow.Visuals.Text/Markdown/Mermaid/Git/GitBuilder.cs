using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Git;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Git;

/// <summary>The pieces a git graph's layout is made of — its layers, and what is in them.</summary>
public static class GitPiece
{
    /// <summary>The branches' labels, and one — standing for the line making that branch.</summary>
    public const string Branches = "Branches";
    public const string Branch = "Branch";

    /// <summary>The lines the lanes run along, and one — standing for the line making its branch.</summary>
    public const string Lanes = "Lanes";
    public const string Lane = "Lane";

    /// <summary>What each commit follows, and one such line — standing for the commit that follows.</summary>
    public const string Follows = "Follows";
    public const string Follow = "Follow";

    /// <summary>The commits, and one — standing for the line it is written on.</summary>
    public const string Commits = "Commits";
    public const string Commit = "Commit";

    /// <summary>What a commit says about itself: the tag hung on it, and the id written for it.</summary>
    public const string Tags = "Tags";
    public const string Tag = "Tag";
    public const string Id = "Id";

    /// <summary>A branch's name, written in its label.</summary>
    public const string Name = "Name";
}

/// <summary>
/// Draws a <c>gitGraph</c> block: a lane per branch, a commit where each one is made, and a line from every commit to
/// what it follows — the branch it is made on, the branch a merge brings in, and the commit a cherry-pick takes.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A commit stands for its line, a branch's label for the
/// line making it — with its name typed into where it is drawn — and a tag or an id for the option writing it. A merge is
/// drawn as a ring, a cherry-pick as a cherry, a reversed commit crossed through and a highlighted one squared off, as
/// Mermaid draws them; the front matter's <c>rotateCommitLabel</c> turns an id where it is written, which a press and a
/// caret follow round.
/// </para>
/// <para>
/// <strong>The lanes are a grid the lines run on.</strong> Each lane is a faint dashed line from its label, which stands
/// right against where the lane starts, to past its last commit, and the lanes stand as far apart as what their commits
/// write needs. A line between lanes runs along them through the columns: out of a branch it turns at the commit it leaves,
/// into a merge at the merge, and where a commit stands in that way it takes whichever turn is clear — or, where neither
/// is, a track of its own between the lanes, as Mermaid reroutes one.
/// </para>
/// </summary>
internal sealed class GitBuilder : MermaidBuilder<GitGraph>
{
    /// <summary>How far one commit is from the next, and the least one lane is from the next.</summary>
    private const double Along = 54;
    private const double Across = 50;

    /// <summary>How big a commit is drawn, and how far a highlighted one's square reaches round it.</summary>
    private const double Node = 9;
    private const double Raised = 1.15;

    private const double Gap = 6;
    private const double Pad = 5;

    /// <summary>The air between what one lane writes and what the next one does, and between a branch's label and its lane.</summary>
    private const double Air = 8;
    private const double Beside = 14;

    /// <summary>How far a lane's line runs on past its last commit.</summary>
    private const double Tail = 22;

    /// <summary>How round a line's corners are, and how far a track round what is in the way keeps from lanes and other tracks.</summary>
    private const double Round = 14;
    private const double Clear = 10;

    private const double IdSize = 10;
    private const double TagSize = 10;
    private const double LabelSize = 11;

    /// <summary>How far an id is turned where the front matter asks for it, as Mermaid turns one.</summary>
    private const double Turned = 45;
    private static readonly double Slant = Math.Sqrt(0.5);

    /// <summary>How faint a lane's line is drawn, and a commit's id's backing.</summary>
    private const double LaneWash = 0.4;
    private const double Backing = 0.6;

    private static readonly DoubleCollection Dotted = Frozen([3, 3]);

    internal GitBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly) : base(reading, state, style, isReadOnly) { }

    /// <inheritdoc/>
    protected override GitGraph Of(MermaidBlock block) => GitGraph.Of(block);

    protected override Size Draw(GitGraph graph, LayoutBuilder build)
    {
        // A graph with no commits written in it is the source.
        if (graph.Empty) return AsWritten(build);

        var config = graph.Config;
        var down = graph.Way != GitWay.LeftRight;
        var turn = !down && config.RotateCommitLabel ? Turned : 0;

        // What every commit writes and every branch is called first, since that is what says how far apart the lanes stand.
        var writing = graph.Commits.ToDictionary(commit => commit, commit => Writes(graph, commit));
        var labels = config.ShowBranches ? Labels(graph) : [];

        var lanes = new double[graph.Across + 1];
        for (var lane = 1; lane < lanes.Length; lane++)
            lanes[lane] = lanes[lane - 1] + Math.Max(Across, Apart(graph, writing, labels, lane - 1, down, turn));

        var length = graph.Length * Along;
        Point Where(int position, int lane)
        {
            var along = graph.Way == GitWay.BottomTop ? length - (position * Along) : position * Along;
            return down ? new Point(lanes[lane], along) : new Point(along, lanes[lane]);
        }

        var layout = new Layout();
        var room = new DiagramRoom(Pad);

        foreach (var commit in graph.Commits)
        {
            var at = Where(commit.Position, commit.Branch.Lane);
            room.Reach(new Rect(at.X - (Node * Raised), at.Y - (Node * Raised), Node * Raised * 2, Node * Raised * 2));
            Written(layout, commit, writing[commit], at, down, turn, room);
        }

        foreach (var branch in graph.Branches)
        {
            if (!labels.TryGetValue(branch.Lane, out var name)) continue;
            Laned(layout, graph, branch, name, Where, lanes[branch.Lane], length, down, room);
        }

        Routed(layout, graph, Where, lanes, down);

        var shift = room.Shift;
        Lanes(build, graph, layout, shift);
        Follows(build, graph, layout, shift);
        Commits(build, graph, layout, shift);
        Branches(build, graph, layout, shift);

        return room.Size;
    }

    // ── What is laid out before it is drawn ────────────────────────────────────

    /// <summary>What a commit writes: its tags and its id.</summary>
    private sealed record Inscribed(IReadOnlyList<(DiagramWords Words, ContentPart Part)> Tags, DiagramWords? Id, GitSaid? Said);

    /// <summary>Everything drawn, where it goes before it is moved clear of the edges.</summary>
    private sealed class Layout
    {
        public List<(GitCommit Commit, Point At)> Commits { get; } = [];
        public List<(GitCommit Commit, DiagramWords Words, ContentPart Part, Rect Box)> Tags { get; } = [];
        public List<(DiagramWords Words, GitSaid Said, Point Place, Rect Box)> Ids { get; } = [];
        public List<(GitBranch Branch, DiagramWords Name, Rect Box, Point From, Point To)> Lanes { get; } = [];
        public List<(GitCommit Commit, IReadOnlyList<Point> Route, int Lane, bool Taken)> Follows { get; } = [];
    }

    private Inscribed Writes(GitGraph graph, GitCommit commit)
    {
        var config = graph.Config;
        var size = config.TagLabelFontSize ?? TagSize;
        var ink = Ink.Written(config.TagLabelColour) ?? Palette.Text;

        var tags = commit.Tags.Where(tag => tag.Says.Length > 0).Select(tag => (Worked(tag.Says, tag.Part, size, ink), tag.Part)).ToList();
        var id = config.ShowCommitLabel && commit.Said is { Says.Length: > 0 } said
            ? Worked(said.Says, said.Part, config.CommitLabelFontSize ?? IdSize, Ink.Written(config.CommitLabelColour) ?? Palette.TextMuted)
            : null;

        return new Inscribed(tags, id, id is null ? null : commit.Said);
    }

    /// <summary>
    /// How far a lane must stand from the one after it for nothing either writes to meet: running across the page, the ids
    /// under one lane's commits and the tags over the next's; running down it, the tags beside one lane's commits and the ids
    /// on the far side of the next's, and the branches' labels over them.
    /// </summary>
    private static double Apart(GitGraph graph, IReadOnlyDictionary<GitCommit, Inscribed> writing, IReadOnlyDictionary<int, DiagramWords> labels,
                                int lane, bool down, double turn)
    {
        double Most(int of, Func<Inscribed, double> reach) =>
            graph.Commits.Where(commit => commit.Branch.Lane == of).Select(commit => reach(writing[commit])).DefaultIfEmpty(0).Max();

        double Stacked(Inscribed said) => said.Tags.Sum(tag => tag.Words.Height + Pad + 2);

        if (!down)
        {
            var under = Most(lane, said => said.Id is not { } id ? 0 : Gap + (turn > 0 ? (id.Width + id.Height) * Slant : id.Height));
            var over = Most(lane + 1, said => said.Tags.Count == 0 ? 0 : Gap + Stacked(said));
            return (Node * 2) + under + over + Air;
        }

        var beside = Most(lane, said => said.Tags.Count == 0 ? 0 : Gap + said.Tags.Max(tag => tag.Words.Width + (Pad * 2)));
        var far = Most(lane + 1, said => said.Id is not { } id ? 0 : Gap + id.Width);
        var named = (labels.TryGetValue(lane, out var one) ? (one.Width / 2) + Pad : 0) + (labels.TryGetValue(lane + 1, out var next) ? (next.Width / 2) + Pad : 0);

        return Math.Max((Node * 2) + beside + far + Air, named + Air);
    }

    /// <summary>Where a commit's tags and id go: running across the page, the tags stacked over it and the id under it; running down it, the tags beside it and the id on its other side.</summary>
    private static void Written(Layout layout, GitCommit commit, Inscribed said, Point at, bool down, double turn, DiagramRoom room)
    {
        layout.Commits.Add((commit, at));

        var reach = Node + Gap;
        foreach (var (words, part) in said.Tags)
        {
            var size = new Size(words.Width + (Pad * 2), words.Height + Pad);
            var box = down
                ? new Rect(at.X + Node + Gap, at.Y - (size.Height / 2) + (reach - Node - Gap), size.Width, size.Height)
                : new Rect(at.X - (size.Width / 2), at.Y - reach - size.Height, size.Width, size.Height);

            layout.Tags.Add((commit, words, part, box));
            room.Reach(box);
            reach += size.Height + 2;
        }

        if (said.Id is not { } id) return;

        var place = down
            ? new Point(at.X - Node - Gap - id.Width, at.Y - (id.Height / 2))
            : new Point(at.X - (turn > 0 ? 0 : id.Width / 2), at.Y + Node + Gap);
        var taken = turn > 0
            ? new Rect(place.X - (id.Height * Slant), place.Y, (id.Width + id.Height) * Slant, (id.Width + id.Height) * Slant)
            : new Rect(place, new Size(id.Width, id.Height));

        layout.Ids.Add((id, said.Said!, place, taken));
        room.Reach(taken);
    }

    /// <summary>
    /// Where a branch's label goes — right against where its lane starts, or over it running down the page, under it running
    /// up — and the faint line of its lane, from its label to past the last commit.
    /// </summary>
    private static void Laned(Layout layout, GitGraph graph, GitBranch branch, DiagramWords name, Func<int, int, Point> where, double across,
                              double length, bool down, DiagramRoom room)
    {
        var size = new Size(name.Width + (Pad * 2), name.Height + Pad);
        var (first, last) = (-Node - Beside, length + Node + Tail);

        (Rect Box, Point From, Point To) placed = graph.Way switch
        {
            GitWay.LeftRight => (new Rect(first - size.Width, across - (size.Height / 2), size.Width, size.Height),
                                 new Point(first, across), new Point(last, across)),
            GitWay.TopBottom => (new Rect(across - (size.Width / 2), first - size.Height, size.Width, size.Height),
                                 new Point(across, first), new Point(across, last)),
            _ => (new Rect(across - (size.Width / 2), length + Node + Beside, size.Width, size.Height),
                  new Point(across, length + Node + Beside), new Point(across, -Node - Tail)),
        };

        layout.Lanes.Add((branch, name, placed.Box, placed.From, placed.To));
        room.Reach(placed.Box);
        room.Reach(placed.From, placed.To);
    }

    /// <summary>
    /// The line from every commit to what it follows. Within a lane it runs straight. Between lanes it runs along them: out
    /// of a branch it turns across at the commit it leaves and runs along the new lane; into a merge or a cherry-pick it runs
    /// along the lane it comes from and turns across at the commit taking it. Where a commit stands in the way it takes the
    /// other turn, and where both are
    /// in the way, a track of its own between the lanes, clear of them and of every other track.
    /// </summary>
    private static void Routed(Layout layout, GitGraph graph, Func<int, int, Point> where, IReadOnlyList<double> lanes, bool down)
    {
        var tracks = new List<double>();

        bool Along(int lane, int from, int to) =>
            !graph.Commits.Any(commit => commit.Branch.Lane == lane && commit.Position > Math.Min(from, to) && commit.Position < Math.Max(from, to));

        bool Over(int position, int from, int to) =>
            !graph.Commits.Any(commit => commit.Position == position && commit.Branch.Lane > Math.Min(from, to) && commit.Branch.Lane < Math.Max(from, to));

        foreach (var commit in graph.Commits)
            for (var at = 0; at < commit.Parents.Count; at++)
            {
                if (graph.Of(commit.Parents[at]) is not { } parent) continue;

                var (from, to) = (where(parent.Position, parent.Branch.Lane), where(commit.Position, commit.Branch.Lane));
                var (a1, l1, a2, l2) = (parent.Position, parent.Branch.Lane, commit.Position, commit.Branch.Lane);
    // What a merge brings in, or a cherry-pick takes, comes along its own lane and turns in at the commit taking it.
                var merged = at > 0;
                IReadOnlyList<Point> corners;

                if (l1 == l2) corners = [from, to];
                else
                {
                    // Turning early runs across at the commit left, then along the lane come to; turning late the other way round.
                    var early = Over(a1, l1, l2) && Along(l2, a1, a2);
                    var late = Along(l1, a1, a2) && Over(a2, l1, l2);
                    Point Corner(bool first) => first ? (down ? new Point(to.X, from.Y) : new Point(from.X, to.Y))
                                                      : (down ? new Point(from.X, to.Y) : new Point(to.X, from.Y));

                    if (merged ? late : early) corners = [from, Corner(!merged), to];
                    else if (merged ? early : late) corners = [from, Corner(merged), to];
                    else
                    {
                        var track = Track(lanes, tracks, lanes[l1], lanes[l2]);
                        corners = down
                            ? [from, new Point(track, from.Y), new Point(track, to.Y), to]
                            : [from, new Point(from.X, track), new Point(to.X, track), to];
                    }
                }

                layout.Follows.Add((commit, Rounded(corners), at > 0 ? parent.Branch.Lane : commit.Branch.Lane, at > 0 && commit.Picked));
            }
    }

    /// <summary>A track between two lanes for a line to go round what is in its way: halfway between, or as near it as keeps clear.</summary>
    private static double Track(IReadOnlyList<double> lanes, List<double> tracks, double from, double to)
    {
        foreach (var share in new[] { 0.5, 0.35, 0.65, 0.2, 0.8 })
        {
            var track = from + ((to - from) * share);
            if (lanes.Concat(tracks).All(taken => Math.Abs(taken - track) >= Clear))
            {
                tracks.Add(track);
                return track;
            }
        }

        return (from + to) / 2;
    }

    /// <summary>A route through its corners, each corner rounded off.</summary>
    private static IReadOnlyList<Point> Rounded(IReadOnlyList<Point> corners)
    {
        var through = corners.Where((point, at) => at == 0 || (point - corners[at - 1]).Length > 0.5).ToList();
        var points = new List<Point> { through[0] };

        for (var at = 1; at < through.Count - 1; at++)
        {
            var (before, corner, after) = (through[at - 1], through[at], through[at + 1]);
            var round = Math.Min(Round, Math.Min((corner - before).Length, (after - corner).Length) / 2);
            var (inward, outward) = (corner - before, after - corner);
            inward.Normalize();
            outward.Normalize();

            points.AddRange(DiagramConnector.Curving(corner - (inward * round), corner, corner, corner + (outward * round), 8));
        }

        if (through.Count > 1) points.Add(through[^1]);
        return points;
    }

    // ── Drawing it ────────────────────────────────────────────────────────────

    private void Lanes(LayoutBuilder build, GitGraph graph, Layout layout, Vector shift)
    {
        if (layout.Lanes.Count == 0) return;

        build.Open(GitPiece.Lanes, part: null, stops: Stops.None);
        foreach (var (branch, _, _, from, to) in layout.Lanes)
            DiagramConnector.Draw(build, GitPiece.Lane, branch.Part, [from + shift, to + shift],
                                  new DiagramStroke(DiagramInk.Faded(Lane(graph, branch.Lane), LaneWash), 1, Dotted), end: DiagramHead.None);
        build.Close();
    }

    private void Follows(LayoutBuilder build, GitGraph graph, Layout layout, Vector shift)
    {
        build.Open(GitPiece.Follows, part: null, stops: Stops.None);
        foreach (var (commit, route, lane, taken) in layout.Follows)
            DiagramConnector.Draw(build, GitPiece.Follow, commit.Part, [.. route.Select(point => point + shift)],
                                  new DiagramStroke(Lane(graph, lane), 2, taken ? DiagramStroke.Dashed : null), end: DiagramHead.None);
        build.Close();
    }

    private void Commits(LayoutBuilder build, GitGraph graph, Layout layout, Vector shift)
    {
        var config = graph.Config;

        build.Open(GitPiece.Commits, part: null, stops: Stops.None);

        foreach (var (commit, at) in layout.Commits) Drawn(build, graph, commit, at + shift);

        foreach (var (commit, words, part, box) in layout.Tags)
            DiagramShapes.Draw(build, GitPiece.Tag, part, DiagramShape.Rounded, Rect.Offset(box, shift),
                               Ink.Written(config.TagLabelBackground) ?? Palette.CodeBg,
                               new DiagramStroke(Ink.Written(config.TagLabelBorder) ?? Lane(graph, commit.Branch.Lane), 1.2),
                               words, MermaidPiece.Words);

        // An id is written on a faint backing of its own, so a line running under it does not strike it through.
        var turn = graph.Way == GitWay.LeftRight && config.RotateCommitLabel ? Turned : 0;
        foreach (var (id, said, place, _) in layout.Ids)
        {
            var at = place + shift;
            var backing = new RectangleGeometry(new Rect(at.X - 2, at.Y, id.Width + 4, id.Height), 2, 2);
            if (turn > 0) backing.Transform = new RotateTransform(turn, at.X, at.Y);
            backing.Freeze();

            build.Open(GitPiece.Id, said.Part, stops: Stops.None);
            build.Draw(new GeometryMark(backing, DiagramInk.Faded(Ink.Written(config.CommitLabelBackground) ?? Ink.Surface, Backing), null, 0));
            id.Set(build, at, MermaidPiece.Words, turn);
            build.Close();
        }

        build.Close();
    }

    private void Branches(LayoutBuilder build, GitGraph graph, Layout layout, Vector shift)
    {
        if (layout.Lanes.Count == 0) return;

        build.Open(GitPiece.Branches, part: null, stops: Stops.None);
        foreach (var (branch, name, box, _, _) in layout.Lanes)
            DiagramShapes.Draw(build, GitPiece.Branch, branch.Part, DiagramShape.Stadium, Rect.Offset(box, shift), Lane(graph, branch.Lane), stroke: null,
                               name, GitPiece.Name);
        build.Close();
    }

    /// <summary>Each branch's label, by its lane — the name as written, where anything writes it.</summary>
    private Dictionary<int, DiagramWords> Labels(GitGraph graph)
    {
        var labels = new Dictionary<int, DiagramWords>();

        foreach (var branch in graph.Branches)
        {
            var ink = Ink.Written(graph.Config.LaneLabelAt(branch.Lane)) ?? Ink.Over(Lane(graph, branch.Lane));

            labels[branch.Lane] = branch.Named is { } named
                ? Written(named.Words(), named.Hole(), LabelSize, ink, FontWeights.SemiBold)
                : Worked(branch.Name, branch.Part, LabelSize, ink, FontWeights.SemiBold);
        }

        return labels;
    }

    /// <summary>
    /// One commit: a circle on its lane — ringed where it merges, a cherry where it is picked, crossed where it reverses, and
    /// squared where it stands out, its square set in a wider one of the lane's <c>gitInv</c> colour.
    /// </summary>
    private void Drawn(LayoutBuilder build, GitGraph graph, GitCommit commit, Point at)
    {
        var ink = Lane(graph, commit.Branch.Lane);
        var under = Ink.Surface;

        build.Open(GitPiece.Commit, commit.Part, stops: Stops.None);

        if (commit.Kept == GitKept.Highlight)
        {
            var outer = Square(at, Node * Raised);
            build.Draw(new GeometryMark(outer, Ink.Written(graph.Config.InverseAt(commit.Branch.Lane)) ?? DiagramInk.Faded(ink, LaneWash), null, 0));
            build.Draw(new GeometryMark(Square(at, Node * 0.65), ink, null, 0));
            build.Occupies(outer);
            build.Close();
            return;
        }

        var circle = Circle(at, Node);
        build.Draw(new GeometryMark(circle, ink, under, 1.5));

        if (commit.Kept == GitKept.Reverse)
        {
            var cross = new GeometryGroup
            {
                Children =
                {
                    new LineGeometry(new Point(at.X - 4.5, at.Y - 4.5), new Point(at.X + 4.5, at.Y + 4.5)),
                    new LineGeometry(new Point(at.X - 4.5, at.Y + 4.5), new Point(at.X + 4.5, at.Y - 4.5)),
                },
            };
            cross.Freeze();
            build.Draw(new GeometryMark(cross, null, under, 1.8));
        }
        else if (commit.Merge) build.Draw(new GeometryMark(Circle(at, Node * 0.55), under, null, 0));
        else if (commit.Picked)
        {
            // A cherry, as Mermaid draws one: two small fruit hanging from their stalks.
            var stalks = new GeometryGroup
            {
                Children =
                {
                    new LineGeometry(new Point(at.X + 2.7, at.Y + 1), new Point(at.X, at.Y - 4.5)),
                    new LineGeometry(new Point(at.X - 2.7, at.Y + 1), new Point(at.X, at.Y - 4.5)),
                },
            };
            stalks.Freeze();
            build.Draw(new GeometryMark(stalks, null, under, 1));
            build.Draw(new GeometryMark(Circle(new Point(at.X - 2.7, at.Y + 2), 2.5), under, null, 0));
            build.Draw(new GeometryMark(Circle(new Point(at.X + 2.7, at.Y + 2), 2.5), under, null, 0));
        }

        build.Occupies(circle);
        build.Close();
    }

    private static Geometry Circle(Point at, double radius)
    {
        var circle = new EllipseGeometry(at, radius, radius);
        circle.Freeze();
        return circle;
    }

    private static Geometry Square(Point at, double half)
    {
        var square = new RectangleGeometry(new Rect(at.X - half, at.Y - half, half * 2, half * 2), 2, 2);
        square.Freeze();
        return square;
    }

    private static DoubleCollection Frozen(DoubleCollection dashes)
    {
        dashes.Freeze();
        return dashes;
    }

    /// <summary>What a lane is drawn in: the colour its <c>git</c> slot writes, or one of the theme's — the ninth lane taking the first's.</summary>
    private Brush Lane(GitGraph graph, int lane) => Ink.Series(lane % GitConfig.Lanes, graph.Config.LaneAt(lane));
}
