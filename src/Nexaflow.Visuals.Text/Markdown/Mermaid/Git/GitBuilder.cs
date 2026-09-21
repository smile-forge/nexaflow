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
/// drawn as a ring, a cherry-pick with a mark taken out of it, a reversed commit crossed through and a highlighted one
/// squared off, as Mermaid draws them; the front matter's <c>rotateCommitLabel</c> turns an id where it is written, which
/// a press and a caret follow round.
/// </para>
/// </summary>
internal sealed class GitBuilder : MermaidBuilder<GitGraph>
{
    /// <summary>How far one commit is from the next, and one lane from the next.</summary>
    private const double Along = 54;
    private const double Across = 46;

    /// <summary>How big a commit is drawn, and how much bigger a highlighted one is.</summary>
    private const double Node = 9;
    private const double Raised = 1.3;

    private const double Gap = 6;
    private const double Pad = 5;

    private const double IdSize = 10;
    private const double TagSize = 10;
    private const double LabelSize = 11;

    /// <summary>How far an id is turned where the front matter asks for it, as Mermaid turns one.</summary>
    private const double Turned = 45;

    internal GitBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly) : base(reading, state, style, isReadOnly) { }

    /// <inheritdoc/>
    protected override GitGraph Of(MermaidBlock block) => GitGraph.Of(block);

    protected override Size Draw(GitGraph graph, LayoutBuilder build)
    {
        // A graph with no commits written in it is the source.
        if (graph.Empty) return AsWritten(build);

        var down = graph.Way != GitWay.LeftRight;
        var labels = graph.Config.ShowBranches ? Labels(graph) : [];
        var beside = labels.Values.Select(label => label.Width + (Pad * 2)).DefaultIfEmpty(0).Max();

        // The labels stand where the lanes start: beside them running across the page, over them running down it.
        var start = new Point(down ? Node : beside + Gap + Node, down ? (labels.Count > 0 ? LabelSize + (Pad * 2) + Gap : 0) + Node : Node);

        Point Where(int position, int lane)
        {
            var along = (graph.Way == GitWay.BottomTop ? graph.Length - position : position) * Along;
            var across = lane * Across;

            return down ? new Point(start.X + across, start.Y + along) : new Point(start.X + along, start.Y + across);
        }

        var room = new DiagramRoom(Pad);
        foreach (var commit in graph.Commits)
        {
            var node = Where(commit.Position, commit.Branch.Lane);
            room.Reach(new Rect(node.X - Node, node.Y - Node, Node * 2, Node * 2));
        }

        Follows(build, graph, Where, down);
        Commits(build, graph, Where, room);
        Branches(build, graph, labels, Where, down, room);

        return room.Size;
    }

    // ── What a branch is called ─────────────────────────────────────────────

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

    private void Branches(LayoutBuilder build, GitGraph graph, IReadOnlyDictionary<int, DiagramWords> labels,
                          Func<int, int, Point> where, bool down, DiagramRoom room)
    {
        if (labels.Count == 0) return;

        build.Open(GitPiece.Branches, part: null, stops: Stops.None);

        foreach (var branch in graph.Branches)
        {
            if (!labels.TryGetValue(branch.Lane, out var name)) continue;

            var at = where(0, branch.Lane);
            var size = new Size(name.Width + (Pad * 2), name.Height + Pad);
            var bounds = down
                ? new Rect(at.X - (size.Width / 2), Pad, size.Width, size.Height)
                : new Rect(Pad, at.Y - (size.Height / 2), size.Width, size.Height);

            room.Reach(bounds);
            DiagramShapes.Draw(build, GitPiece.Branch, branch.Part, DiagramShape.Stadium, bounds, Lane(graph, branch.Lane), stroke: null,
                name, GitPiece.Name);
        }

        build.Close();
    }

    // ── What a commit follows ───────────────────────────────────────────────

    private void Follows(LayoutBuilder build, GitGraph graph, Func<int, int, Point> where, bool down)
    {
        build.Open(GitPiece.Follows, part: null, stops: Stops.None);

        foreach (var commit in graph.Commits)
        {
            var to = where(commit.Position, commit.Branch.Lane);

            for (var at = 0; at < commit.Parents.Count; at++)
            {
                if (graph.Of(commit.Parents[at]) is not { } parent) continue;

                var from = where(parent.Position, parent.Branch.Lane);
                var taken = at > 0 && commit.Picked;
                var ink = Lane(graph, at > 0 ? parent.Branch.Lane : commit.Branch.Lane);

                // One lane runs straight; from another it bends over, halfway along, as a branch or a merge does.
                var straight = down ? Math.Abs(from.X - to.X) < 0.5 : Math.Abs(from.Y - to.Y) < 0.5;
                var half = down ? (from.Y + to.Y) / 2 : (from.X + to.X) / 2;

                IReadOnlyList<Point> route = straight
                    ? [from, to]
                    : down
                        ? [from, new Point((from.X + to.X) / 2, half), to]
                        : [from, new Point(half, (from.Y + to.Y) / 2), to];

                DiagramConnector.Draw(build, GitPiece.Follow, commit.Part, route,
                    new DiagramStroke(ink, 2, taken ? DiagramStroke.Dashed : null), end: DiagramHead.None, curved: !straight);
            }
        }

        build.Close();
    }

    // ── The commits themselves ──────────────────────────────────────────────

    private void Commits(LayoutBuilder build, GitGraph graph, Func<int, int, Point> where, DiagramRoom room)
    {
        var down = graph.Way != GitWay.LeftRight;
        var config = graph.Config;

        build.Open(GitPiece.Commits, part: null, stops: Stops.None);

        foreach (var commit in graph.Commits)
        {
            var at = where(commit.Position, commit.Branch.Lane);
            Drawn(build, commit, at, Lane(graph, commit.Branch.Lane));

            // A cherry-pick says which commit it took where nothing else tags it, as Mermaid tags one.
            if ((commit.Tag ?? commit.Taken) is { Says.Length: > 0 } tag)
            {
                var words = Worked(tag.Says, tag.Part, config.TagLabelFontSize ?? TagSize, Ink.Written(config.TagLabelColour) ?? Palette.Text);
                var size = new Size(words.Width + (Pad * 2), words.Height + Pad);
                var bounds = down
                    ? new Rect(at.X + Node + Gap, at.Y - (size.Height / 2), size.Width, size.Height)
                    : new Rect(at.X - (size.Width / 2), at.Y - Node - Gap - size.Height, size.Width, size.Height);

                room.Reach(bounds);
                DiagramShapes.Draw(build, GitPiece.Tag, tag.Part, DiagramShape.Rounded, bounds,
                    Ink.Written(config.TagLabelBackground) ?? Palette.CodeBg,
                    new DiagramStroke(Ink.Written(config.TagLabelBorder) ?? Lane(graph, commit.Branch.Lane), 1.2),
                    words, MermaidPiece.Words);
            }

            if (!config.ShowCommitLabel || commit.Said is not { Says.Length: > 0 } said) continue;

            var id = Worked(said.Says, said.Part, config.CommitLabelFontSize ?? IdSize,
                            Ink.Written(config.CommitLabelColour) ?? Palette.TextMuted);

            // Turned where the front matter asks, which is how Mermaid writes an id under its commit.
            var turn = !down && config.RotateCommitLabel ? Turned : 0;
            var place = down
                ? new Point(at.X + Node + Gap, at.Y + Gap)
                : new Point(at.X - (turn > 0 ? 0 : id.Width / 2), at.Y + Node + Gap);

            room.Reach(turn > 0 ? new Rect(place.X - id.Height, place.Y, id.Width + id.Height, id.Width + id.Height)
                                : new Rect(place, new Size(id.Width, id.Height)));

            build.Open(GitPiece.Id, said.Part, stops: Stops.None);
            id.Set(build, place, MermaidPiece.Words, turn);
            build.Close();
        }

        build.Close();
    }

    /// <summary>One commit: a circle on its lane — ringed where it merges, marked where it is picked, crossed where it reverses, squared where it stands out.</summary>
    private void Drawn(LayoutBuilder build, GitCommit commit, Point at, Brush ink)
    {
        build.Open(GitPiece.Commit, commit.Part, stops: Stops.None);

        if (commit.Kept == GitKept.Highlight)
        {
            var square = new RectangleGeometry(new Rect(at.X - (Node * Raised), at.Y - (Node * Raised), Node * Raised * 2, Node * Raised * 2), 3, 3);
            square.Freeze();

            build.Draw(new GeometryMark(square, ink, Palette.CodeBg, 2));
            build.Occupies(square);
            build.Close();
            return;
        }

        var circle = new EllipseGeometry(at, Node, Node);
        circle.Freeze();
        build.Draw(new GeometryMark(circle, ink, Palette.CodeBg, 1.5));

        switch (commit.Kept)
        {
            case GitKept.Reverse:
                var cross = new GeometryGroup
                {
                    Children =
                    {
                        new LineGeometry(new Point(at.X - 4, at.Y - 4), new Point(at.X + 4, at.Y + 4)),
                        new LineGeometry(new Point(at.X - 4, at.Y + 4), new Point(at.X + 4, at.Y - 4)),
                    },
                };
                cross.Freeze();
                build.Draw(new GeometryMark(cross, null, Palette.CodeBg, 1.6));
                break;

            default:
                if (commit.Merge) build.Draw(new GeometryMark(Inner(at, Node * 0.45), Palette.CodeBg, null, 0));
                else if (commit.Picked) build.Draw(new GeometryMark(Inner(at, 2.5), Palette.CodeBg, null, 0));
                break;
        }

        build.Occupies(circle);
        build.Close();
    }

    private static Geometry Inner(Point at, double radius)
    {
        var circle = new EllipseGeometry(at, radius, radius);
        circle.Freeze();
        return circle;
    }

    /// <summary>What a lane is drawn in: the colour its <c>git</c> slot writes, or one of the theme's.</summary>
    private Brush Lane(GitGraph graph, int lane) => Ink.Series(lane, graph.Config.LaneAt(lane));
}
