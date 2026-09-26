using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Plot;

namespace Nexaflow.Visuals.Text.Markdown.Plot;

internal sealed partial class PlotBuilder
{
    /// <summary>
    /// What one channel was given for one mark: the cell it came from, what that cell says, and the number it reads as where it
    /// reads as one.
    /// </summary>
    /// <param name="Cell">Where it was written — which is what a press on the mark means.</param>
    /// <param name="Inner">
    /// The characters themselves, without the quotes the cell may have been written in. What is drawn where a value is drawn, and
    /// so where a caret goes and what typing changes.
    /// </param>
    private sealed record Value(ContentPart Cell, ContentPart Inner, string Text, double? Number)
    {
        /// <summary>Whether this is a number rather than a name, which is what makes its channel continuous.</summary>
        public bool Counts => this.Number is not null;
    }

    /// <summary>One mark of a plot, and what it was given for each channel it feeds.</summary>
    /// <param name="Part">
    /// What the mark stands for: the row it was drawn from, or — down a matrix, where a row is a line of marks rather than one —
    /// the cell itself.
    /// </param>
    private sealed record Mark(ContentPart Part, IReadOnlyDictionary<PlotAesthetic, Value> Channels)
    {
        /// <summary>What this mark was given for a channel, or null where it feeds none.</summary>
        public Value? this[PlotAesthetic channel] => this.Channels.GetValueOrDefault(channel);
    }

    /// <summary>
    /// A plot as it is drawn: what the stages said of the block, and a mark for every value its table holds, read down the tree in
    /// the order it was written. A panel of a divided plot is drawn from the marks that fall in it, so the questions a drawing asks
    /// of its marks — how far a channel reaches, what its names are — are asked of the ones being drawn.
    /// </summary>
    private sealed record Drawing(PlotBlockNode Block, IReadOnlyList<Mark> Marks)
    {
        public PlotSettings Settings => this.Block.Settings;

        /// <summary>Whether the table was read down its side as well as across it, which leaves neither axis a name of its own.</summary>
        public bool Matrix => this.Block.Matrix;

        /// <summary>The coefficient between each pair of numeric columns — the marks of <c>geom: corr</c>.</summary>
        public IReadOnlyList<(string Across, string Down, double R)> Correlations => this.Block.Correlations;

        /// <summary>The plot the tree says, a mark for each row — or each value, down a matrix.</summary>
        public static Drawing Of(ContentPart root, PlotBlockNode block)
        {
            var marks = new List<Mark>();

            foreach (var row in root.SelfAndDescendants().Where(part => part.Kind == PlotKinds.Row))
            {
                if (row.Node.IsHeader()) continue;

                if (block.Matrix) Down(row, marks);
                else Across(row, marks);
            }

            return new Drawing(block, marks);
        }

        /// <summary>A long row: one mark, its channels the cells that feed them.</summary>
        private static void Across(ContentPart row, List<Mark> marks)
        {
            var channels = new Dictionary<PlotAesthetic, Value>();

            foreach (var cell in Cells(row))
                foreach (var channel in Feeds(cell))
                    channels[channel] = Valued(cell, Says(cell));

            if (channels.Count > 0) marks.Add(new Mark(row, channels));
        }

        /// <summary>
        /// A row of a matrix: a mark per value, its x the column above it and its y the name beside it.
        ///
        /// <para>
        /// Both of those are what the stages said of the cell rather than cells of their own, so they carry the cell as where
        /// they came from — which is right, because pressing the tile is what means them.
        /// </para>
        /// </summary>
        private static void Down(ContentPart row, List<Mark> marks)
        {
            var names = (row.Node as PlotRowNode)?.Names ?? string.Empty;

            foreach (var cell in Cells(row))
            {
                if (!Feeds(cell).Contains(PlotAesthetic.Fill)) continue;

                marks.Add(new Mark(cell, new Dictionary<PlotAesthetic, Value>
                {
                    [PlotAesthetic.X] = new(cell, cell, (cell.Node as PlotCellNode)?.Column ?? string.Empty, null),
                    [PlotAesthetic.Y] = new(cell, cell, names, null),
                    [PlotAesthetic.Fill] = Valued(cell, Says(cell)),
                }));
            }
        }

        // ── What a drawing asks that a mark cannot answer ───────────────────────

        /// <summary>
        /// Whether a channel is drawn as numbers rather than as one of a few names.
        ///
        /// <para>
        /// <strong>It is, when more of its values read as numbers than do not.</strong> A column of names has none at all, and a
        /// column of numbers with a half-typed cell in it still has most — which is the difference between an axis of categories
        /// and an axis with something wrong on one row of it. Letting a single unreadable cell turn the axis categorical would
        /// rearrange the whole picture under somebody midway through typing a number, which is the one moment it must not.
        /// </para>
        /// </summary>
        public bool Counts(PlotAesthetic channel)
        {
            var numbers = 0;
            var names = 0;

            foreach (var mark in this.Marks)
            {
                if (mark[channel] is not { } value) continue;

                if (value.Counts) numbers++;
                else names++;
            }

            return numbers > names;
        }

        /// <summary>How far a channel's numbers reach, or null where it has none.</summary>
        public (double Min, double Max)? Reach(PlotAesthetic channel)
        {
            double? min = null, max = null;

            foreach (var mark in this.Marks)
                if (mark[channel]?.Number is { } number)
                {
                    min = min is null ? number : Math.Min(min.Value, number);
                    max = max is null ? number : Math.Max(max.Value, number);
                }

            return min is null || max is null ? null : (min.Value, max.Value);
        }

        /// <summary>
        /// What a channel's values are where they are names, in the order they were first written — which is the order they are
        /// placed along an axis and listed in the key.
        /// </summary>
        public IReadOnlyList<string> Named(PlotAesthetic channel)
        {
            var seen = new List<string>();
            var known = new HashSet<string>(StringComparer.Ordinal);

            foreach (var mark in this.Marks)
                if (mark[channel] is { } value && known.Add(value.Text))
                    seen.Add(value.Text);

            return seen;
        }

        /// <summary>
        /// The distinct values of a channel, in the order they are laid along an axis of slots: by number where they are all
        /// numbers, and in the order they were first written otherwise.
        ///
        /// <para>
        /// What a grid of tiles is laid on. A tile stands for one value rather than a stretch of them, so an axis carrying tiles
        /// has as many places as there are values — and a run of years read as a plain range would leave each tile a third of the
        /// panel wide with three of them to fit, which is how a heat map ends up drawn over its own title.
        /// </para>
        /// </summary>
        public IReadOnlyList<string> Slots(PlotAesthetic channel)
        {
            var seen = new List<Value>();
            var known = new HashSet<string>(StringComparer.Ordinal);

            foreach (var mark in this.Marks)
                if (mark[channel] is { } value && known.Add(value.Text))
                    seen.Add(value);

            return seen.All(value => value.Counts)
                ? [.. seen.OrderBy(value => value.Number!.Value).Select(value => value.Text)]
                : [.. seen.Select(value => value.Text)];
        }

        /// <summary>What a channel is called: what the block says, else the name of the column feeding it.</summary>
        public string? Titled(PlotAesthetic channel, string? written)
        {
            if (written is not null) return written;

            // Both axes of a correlation matrix are the columns themselves, and down a written matrix the columns are the values
            // with the rows named beside them. Either way an axis titled with any one column's name would be naming one of its
            // own ticks.
            if ((this.Matrix || this.Settings.Geom == PlotGeom.Corr)
                && channel is PlotAesthetic.X or PlotAesthetic.Y) return null;

            foreach (var mark in this.Marks)
                if ((mark[channel]?.Cell.Node as PlotCellNode)?.Column is { Length: > 0 } column)
                    return column;

            return null;
        }

        // ── Reading the tree ────────────────────────────────────────────────────

        private static IEnumerable<ContentPart> Cells(ContentPart row) =>
            row.Children.Where(child => child.Kind == PlotKinds.Cell);

        private static string Says(ContentPart cell) => cell.Node.Says();

        /// <summary>Every channel a cell feeds — a column may feed several.</summary>
        private static IReadOnlyList<PlotAesthetic> Feeds(ContentPart cell) => (cell.Node as PlotCellNode)?.Feeds ?? [];

        private static Value Valued(ContentPart cell, string text) =>
            new(cell, Inner(cell), text, (cell.Node as PlotCellNode)?.Number);

        /// <summary>
        /// The part holding a cell's own characters: the cell itself where it is bare, and what is between the quotes where it is
        /// not. A caret goes in here and nowhere else — the quotes are what hold the value together, not part of it.
        /// </summary>
        private static ContentPart Inner(ContentPart cell) =>
            cell.Node.IsLeaf
                ? cell
                : cell.Children.FirstOrDefault(child => child.Kind == PlotKinds.Cell && child.Node.IsLeaf) ?? cell;
    }
}
