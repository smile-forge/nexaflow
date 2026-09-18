using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>Which way a layered layout runs: where a line drawn from one cell to the next points.</summary>
internal enum DiagramWay
{
    Down,
    Up,
    Right,
    Left,
}

/// <summary>
/// One thing a layered layout places: a node, or a box holding other cells — a flowchart's subgraph, a cluster. A box is laid
/// out first, in its own space, and is then the size of what it holds; its cells are moved inside it once it is placed.
/// </summary>
internal sealed class DiagramCell(Size size)
{
    /// <summary>How much room it takes. For a box, the most of what it was given and what it turned out to hold.</summary>
    public Size Size { get; set; } = size;

    /// <summary>The box it is in, or null for one at the outermost level.</summary>
    public DiagramCell? Inside { get; init; }

    /// <summary>The way what is inside it runs, where it is a box laid out its own way rather than the chart's.</summary>
    public DiagramWay? Way { get; init; }

    /// <summary>The clear air a box keeps between its edge and what is inside it.</summary>
    public double Pad { get; init; }

    /// <summary>The room a box keeps at the top of it for what is written there.</summary>
    public double Heading { get; init; }

    /// <summary>
    /// The band it keeps to, where the layout is laid out in bands — a swimlane's lanes, numbered from one in the order they are given
    /// (<see cref="DiagramLane"/>). Nought for a cell in no band, and for every cell of a layout laid out in none.
    /// </summary>
    public int Lane { get; init; }

    /// <summary>Where it ended up — in the space of the box it is in until that box is placed, and absolute after.</summary>
    public Rect Bounds { get; set; }
}

/// <summary>One line a layered layout draws between two cells, and the way it ended up running.</summary>
/// <param name="span">How many ranks it reaches over at the least, which is how far apart it holds what it joins.</param>
internal sealed class DiagramJoin(DiagramCell from, DiagramCell to, int span = 1)
{
    public DiagramCell From { get; } = from;

    public DiagramCell To { get; } = to;

    public int Span { get; } = Math.Max(1, span);

    /// <summary>Where it runs, from the middle of what it leaves to the middle of what it reaches, bending on the way.</summary>
    public IReadOnlyList<Point> Route { get; set; } = [];

    /// <summary>Where it bends, in the space of the box it was laid out in — <see cref="DiagramLayers"/>' own bookkeeping.</summary>
    internal IReadOnlyList<Point> Bends { get; set; } = [];

    /// <summary>The box it was laid out in, which is the space <see cref="Bends"/> are in.</summary>
    internal DiagramCell? Level { get; set; }

    /// <summary>Which way the level it was laid out in runs, which is the way it leaves and arrives.</summary>
    internal DiagramWay Towards { get; set; }
}

/// <summary>
/// The layered layout every diagram whose nodes are joined by lines is drawn with — a flowchart, a state chart, a class diagram.
/// Sugiyama's: the cells are put in ranks by how far along the links reach them, each rank is ordered so as few lines cross as
/// can be managed, and each cell is then set across its rank beside the ones it joins.
///
/// <para>
/// <strong>A box is laid out in its own space first.</strong> What is inside a subgraph is arranged on its own, which is what
/// lets it run its own way; the box is then a cell like any other in its parent's arrangement, and what it holds is moved inside
/// it once it is placed. A link between cells in different boxes is arranged where the two of them meet — between the boxes at
/// that level — and drawn from the cell it really leaves to the one it really reaches.
/// </para>
/// <para>
/// <strong>Links that reach over more than one rank bend.</strong> Each rank a link passes through keeps a place of its own for
/// it, so it is ordered along with everything else and the line goes round what is in the way rather than through it.
/// </para>
/// <para>
/// <strong>Lanes are this layout under a constraint, not another one.</strong> <see cref="Lanes"/> ranks, orders, spreads and
/// routes exactly as <see cref="Lay"/> does; what it adds is that each cell keeps to the band of the lane holding it, and that a
/// lane's cells come one to a rank. Everything either of them needs round that — the cycles turned, the bends, the order settled,
/// the way round it runs — is the same code, which is why both are here.
/// </para>
/// </summary>
internal static class DiagramLayers
{
    /// <summary>How many times the order of each rank, and then the place of everything in it, is settled.</summary>
    private const int Passes = 8;

    /// <summary>How far a link back to the cell it leaves reaches out beside it.</summary>
    private const double Loops = 18;

    /// <summary>
    /// Lays every cell out, hands each its <see cref="DiagramCell.Bounds"/> and each join its <see cref="DiagramJoin.Route"/>,
    /// and says how much room it all came to.
    /// </summary>
    /// <param name="between">How far apart two cells in the same rank are set.</param>
    /// <param name="along">How far apart one rank is set from the next.</param>
    /// <param name="laning">
    /// The bands to lay it out in — a swimlane's lanes, each cell keeping to the one its <see cref="DiagramCell.Lane"/> names — or null
    /// for a layout with none. See <see cref="DiagramLanes"/> for what the bands change.
    /// </param>
    public static Size Lay(IReadOnlyList<DiagramCell> cells, IReadOnlyList<DiagramJoin> joins, DiagramWay way,
                           double between, double along, DiagramLanes? laning = null)
    {
        // Innermost boxes first: a box is the size of what it holds, so what it holds is arranged before the box is placed.
        foreach (var box in Boxes(cells).OrderByDescending(Deep))
        {
            // A box is a space of its own, which the bands of the layout round it do not reach into.
            var held = Arrange(Within(cells, box), Joining(joins, box), box.Way ?? way, between, along, laning: null);

            box.Size = new Size(Math.Max(box.Size.Width, held.Width + (box.Pad * 2)),
                                Math.Max(box.Size.Height, held.Height + (box.Pad * 2) + box.Heading));
        }

        var whole = Arrange(Within(cells, null), Joining(joins, null), way, between, along, laning);

        // Outermost boxes first, so a box is already where it belongs before what is inside it is moved into it.
        foreach (var box in Boxes(cells).OrderBy(Deep)) Moved(cells, joins, box);

        foreach (var join in joins) join.Route = Routed(join, way);

        return whole;
    }

    /// <summary>The cells directly inside a box — those at the outermost level, for null.</summary>
    private static List<DiagramCell> Within(IReadOnlyList<DiagramCell> cells, DiagramCell? box) =>
        [.. cells.Where(cell => ReferenceEquals(cell.Inside, box))];

    /// <summary>The cells holding other cells.</summary>
    private static List<DiagramCell> Boxes(IReadOnlyList<DiagramCell> cells) =>
        [.. cells.Where(box => cells.Any(cell => ReferenceEquals(cell.Inside, box)))];

    /// <summary>How many boxes a cell is inside.</summary>
    private static int Deep(DiagramCell cell)
    {
        var deep = 0;
        for (var at = cell.Inside; at is not null; at = at.Inside) deep++;

        return deep;
    }

    /// <summary>Moves everything laid out inside a box to where the box turned out to be.</summary>
    private static void Moved(IReadOnlyList<DiagramCell> cells, IReadOnlyList<DiagramJoin> joins, DiagramCell box)
    {
        var origin = new Vector(box.Bounds.X + box.Pad, box.Bounds.Y + box.Pad + box.Heading);

        foreach (var cell in cells.Where(cell => ReferenceEquals(cell.Inside, box)))
            cell.Bounds = Rect.Offset(cell.Bounds, origin);

        foreach (var join in joins.Where(join => ReferenceEquals(join.Level, box)))
            join.Bends = [.. join.Bends.Select(bend => bend + origin)];
    }

    /// <summary>
    /// The joins to arrange at one level: each one between the cells at that level that hold its ends. A join whose ends are held
    /// by the same cell is arranged inside that one instead, and one reaching outside the box is none of this level's business.
    /// </summary>
    private static List<Joined> Joining(IReadOnlyList<DiagramJoin> joins, DiagramCell? box)
    {
        var edges = new List<Joined>();

        foreach (var join in joins)
        {
            if (Holding(join.From, box) is not { } from || Holding(join.To, box) is not { } to) continue;
            if (!ReferenceEquals(from, to) || ReferenceEquals(join.From, join.To)) edges.Add(new Joined(join, from, to));
        }

        return edges;
    }

    /// <summary>The cell directly inside <paramref name="box"/> that holds this one — itself, where it is one of them.</summary>
    private static DiagramCell? Holding(DiagramCell cell, DiagramCell? box)
    {
        for (var at = cell; at is not null; at = at.Inside)
            if (ReferenceEquals(at.Inside, box)) return at;

        return null;
    }

    // ── One level of it ─────────────────────────────────────────────────────

    /// <summary>
    /// Lays one level out: the cells in ranks, each rank ordered and then set across, and every join given the places it bends at. Hands
    /// back the room it all took. Where it is laid out in bands, the laning says how far the links hold things apart and holds each cell
    /// to its own band.
    /// </summary>
    private static Size Arrange(IReadOnlyList<DiagramCell> cells, IReadOnlyList<Joined> edges, DiagramWay way,
                                double between, double along, DiagramLanes? laning)
    {
        // Lanes with nothing in them yet are still bands to place.
        if (cells.Count == 0 && laning is null) return default;

        var at = new Dictionary<DiagramCell, int>(cells.Count);
        for (var cell = 0; cell < cells.Count; cell++) at[cells[cell]] = cell;

        var links = edges.Select(edge => new Link(edge.Join, at[edge.From], at[edge.To], edge.Join.Span)).ToList();
        Turned(cells.Count, links);

        var ranks = Ranked(cells, links, laning);
        var rows = Rowed(cells, links, ranks, way, out var placed, out var chains);

        Ordered(rows);
        Spread(rows, between);

        var banded = laning?.Held(rows, between) ?? 0;
        var whole = Sized(cells, rows, way, along, laning?.Heading ?? 0, banded, out var from, out var deep);

        Settled(cells, rows, way, from, deep, whole);
        laning?.Settled(way, whole);

        foreach (var link in links)
            link.Join.Bends = [.. (link.Turned ? Enumerable.Reverse(chains[link]) : chains[link]).Select(place => place.Middle)];

        foreach (var edge in edges)
        {
            edge.Join.Level = cells[0].Inside;
            edge.Join.Towards = way;
        }

        return whole;
    }

    /// <summary>
    /// Turns round every link that goes back to something already on the way down, so the ranks can be worked out at all. The
    /// line is still drawn the way it was written; only the ranking sees it the other way about.
    /// </summary>
    private static void Turned(int count, IReadOnlyList<Link> links)
    {
        var leaving = new List<int>[count];
        for (var cell = 0; cell < count; cell++) leaving[cell] = [];
        for (var link = 0; link < links.Count; link++)
            if (links[link].From != links[link].To) leaving[links[link].From].Add(link);

        var state = new int[count];

        for (var cell = 0; cell < count; cell++)
            if (state[cell] == 0) Walk(cell);

        return;

        void Walk(int cell)
        {
            state[cell] = 1;

            foreach (var link in leaving[cell])
            {
                var to = links[link].To;
                if (state[to] == 1) links[link].Turned = true;
                else if (state[to] == 0) Walk(to);
            }

            state[cell] = 2;
        }
    }

    /// <summary>
    /// Which rank each cell is in: as far along as the longest run of links reaching it, every link reaching at least as far as it was
    /// written long — and, where it is laid out in bands, as far as the band it is in has got to (<see cref="DiagramLanes"/>).
    /// </summary>
    private static int[] Ranked(IReadOnlyList<DiagramCell> cells, IReadOnlyList<Link> links, DiagramLanes? laning)
    {
        var ranks = new int[cells.Count];
        var reaching = new List<Link>[cells.Count];
        for (var cell = 0; cell < cells.Count; cell++) reaching[cell] = [];

        foreach (var link in links)
        {
            var (from, to) = link.Way;
            if (from != to) reaching[to].Add(link);
        }

        foreach (var cell in Sorted(cells.Count, links))
        {
            var wanted = 0;

            foreach (var link in reaching[cell])
            {
                var from = link.Way.From;
                wanted = Math.Max(wanted, ranks[from] + (laning?.Apart(cells[from], cells[cell], link.Span) ?? link.Span));
            }

            ranks[cell] = laning?.Ranked(cells[cell], wanted) ?? wanted;
        }

        return ranks;
    }

    /// <summary>
    /// The cells in an order where everything a link leaves comes before what it reaches, the links having been turned round until
    /// they can be. Anything still left waiting — nothing, once the cycles are turned — comes last, as it was written.
    /// </summary>
    private static IEnumerable<int> Sorted(int count, IReadOnlyList<Link> links)
    {
        var waiting = new int[count];
        var after = new List<int>[count];
        for (var cell = 0; cell < count; cell++) after[cell] = [];

        foreach (var link in links)
        {
            var (from, to) = link.Way;
            if (from == to) continue;

            after[from].Add(to);
            waiting[to]++;
        }

        var ready = new Queue<int>(Enumerable.Range(0, count).Where(cell => waiting[cell] == 0));
        var order = new List<int>(count);

        while (ready.Count > 0)
        {
            var cell = ready.Dequeue();
            order.Add(cell);

            foreach (var to in after[cell])
                if (--waiting[to] == 0) ready.Enqueue(to);
        }

        order.AddRange(Enumerable.Range(0, count).Where(cell => waiting[cell] > 0));

        return order;
    }

    /// <summary>
    /// The places every rank holds: one for each cell in it, and one for every link passing through it — which is what gives a long link
    /// somewhere to bend and a place in the order of its own. A bend is in the band the link is handed from, so a line reaching past a
    /// rank runs down its own band rather than through the next one.
    /// </summary>
    private static List<List<Place>> Rowed(IReadOnlyList<DiagramCell> cells, IReadOnlyList<Link> links, int[] ranks,
                                           DiagramWay way, out Place[] placed, out Dictionary<Link, List<Place>> chains)
    {
        var rows = new List<List<Place>>();
        placed = new Place[cells.Count];
        chains = [];

        for (var cell = 0; cell < cells.Count; cell++)
        {
            placed[cell] = new Place
            {
                Cell = cell, Rank = ranks[cell], Lane = cells[cell].Lane, Size = Across(cells[cell].Size, way),
            };

            Row(ranks[cell]).Add(placed[cell]);
        }

        foreach (var link in links)
        {
            var (first, last) = link.Way;
            var chain = new List<Place>();
            chains[link] = chain;

            if (first == last) continue;

            var before = placed[first];

            for (var rank = ranks[first] + 1; rank < ranks[last]; rank++)
            {
                var bend = new Place { Rank = rank, Lane = cells[first].Lane };
                Row(rank).Add(bend);
                chain.Add(bend);
                Tied(before, bend);
                before = bend;
            }

            Tied(before, placed[last]);
        }

        return rows;

        List<Place> Row(int rank)
        {
            while (rows.Count <= rank) rows.Add([]);
            return rows[rank];
        }
    }

    private static void Tied(Place above, Place below)
    {
        above.Below.Add(below);
        below.Above.Add(above);
    }

    /// <summary>
    /// What order each rank is in: as written to start with, then settled so everything sits near what it joins in the rank beside it
    /// — which is what keeps the lines between two ranks from crossing. Lanes come first: a rank is ordered lane by lane, so a lane's
    /// band is the same stretch of every rank.
    /// </summary>
    private static void Ordered(List<List<Place>> rows)
    {
        Numbered(rows);

        for (var pass = 0; pass < Passes; pass++)
        {
            var down = pass % 2 == 0;

            foreach (var row in Sweep(rows, down))
            {
                var settled = row
                    .Select((place, order) => (Place: place, Near: Nearest(place, down, order)))
                    .OrderBy(entry => entry.Place.Lane)
                    .ThenBy(entry => entry.Near)
                    .Select(entry => entry.Place)
                    .ToList();

                row.Clear();
                row.AddRange(settled);
                Numbered(rows);
            }
        }
    }

    private static void Numbered(List<List<Place>> rows)
    {
        foreach (var row in rows)
            for (var order = 0; order < row.Count; order++)
                row[order].Order = order;
    }

    /// <summary>Where in the rank beside it what this one joins sits, on average — where it is itself, where it joins nothing.</summary>
    private static double Nearest(Place place, bool down, double fallback)
    {
        var near = down ? place.Above : place.Below;

        return near.Count == 0 ? fallback : near.Average(other => (double)other.Order);
    }

    /// <summary>
    /// Where everything sits across its rank: beside what it joins in the rank beside it, then pushed apart until nothing
    /// overlaps, and the rank put back where it wanted to be so the ranks stay lined up with each other.
    /// </summary>
    private static void Spread(List<List<Place>> rows, double between)
    {
        foreach (var row in rows) Apart(row, between);

        for (var pass = 0; pass < Passes; pass++)
        {
            var down = pass % 2 == 0;

            foreach (var row in Sweep(rows, down))
            {
                foreach (var place in row)
                {
                    var near = down ? place.Above : place.Below;
                    if (near.Count > 0) place.At = near.Average(other => other.At);
                }

                Apart(row, between);
            }
        }

        var least = rows.SelectMany(row => row).Select(place => place.At - (place.Size / 2)).DefaultIfEmpty(0).Min();
        foreach (var place in rows.SelectMany(row => row)) place.At -= least;
    }

    private static void Apart(List<Place> row, double between)
    {
        if (row.Count == 0) return;

        var wanted = row.Average(place => place.At);

        for (var at = 1; at < row.Count; at++)
        {
            var least = row[at - 1].At + (row[at - 1].Size / 2) + between + (row[at].Size / 2);
            if (row[at].At < least) row[at].At = least;
        }

        // Pushing them apart moves the whole rank along; putting it back where it wanted to be is what keeps the ranks lined up.
        var drift = wanted - row.Average(place => place.At);
        foreach (var place in row) place.At += drift;
    }

    private static List<List<Place>> Sweep(List<List<Place>> rows, bool down) =>
        down ? rows : [.. Enumerable.Reverse(rows)];

    /// <summary>
    /// How much room it all took, and where each rank starts and how deep it is. The first rank starts past whatever room the lanes
    /// keep at the near end of their bands for what is written on them.
    /// </summary>
    private static Size Sized(IReadOnlyList<DiagramCell> cells, List<List<Place>> rows, DiagramWay way, double along,
                              double heading, double banded, out double[] from, out double[] deep)
    {
        deep = [.. rows.Select(row => row.Where(place => place.Cell >= 0)
                                        .Select(place => Along(cells[place.Cell].Size, way))
                                        .DefaultIfEmpty(0)
                                        .Max())];

        from = new double[rows.Count];
        var running = heading;

        for (var rank = 0; rank < rows.Count; rank++)
        {
            from[rank] = running;
            running += deep[rank] + along;
        }

        var whole = Math.Max(heading, running - along);
        var across = Math.Max(banded, rows.SelectMany(row => row)
                                          .Select(place => place.At + (place.Size / 2))
                                          .DefaultIfEmpty(0)
                                          .Max());

        return way is DiagramWay.Down or DiagramWay.Up ? new Size(across, whole) : new Size(whole, across);
    }

    /// <summary>Turns what each rank settled on into where every cell and every bend is, the way round the layout runs.</summary>
    private static void Settled(IReadOnlyList<DiagramCell> cells, List<List<Place>> rows, DiagramWay way, double[] from,
                                double[] deep, Size whole)
    {
        foreach (var row in rows)
            foreach (var place in row)
            {
                var size = place.Cell >= 0 ? cells[place.Cell].Size : default;
                var start = from[place.Rank] + ((deep[place.Rank] - Along(size, way)) / 2);

                place.Middle = Placed(from[place.Rank] + (deep[place.Rank] / 2), place.At, way, whole);

                if (place.Cell >= 0)
                    cells[place.Cell].Bounds = Placed(start, place.At - (Across(size, way) / 2), size, way, whole);
            }
    }

    internal static Rect Placed(double along, double across, Size size, DiagramWay way, Size whole) => way switch
    {
        DiagramWay.Down => new Rect(across, along, size.Width, size.Height),
        DiagramWay.Up => new Rect(across, whole.Height - along - size.Height, size.Width, size.Height),
        DiagramWay.Right => new Rect(along, across, size.Width, size.Height),
        _ => new Rect(whole.Width - along - size.Width, across, size.Width, size.Height),
    };

    private static Point Placed(double along, double across, DiagramWay way, Size whole) => way switch
    {
        DiagramWay.Down => new Point(across, along),
        DiagramWay.Up => new Point(across, whole.Height - along),
        DiagramWay.Right => new Point(along, across),
        _ => new Point(whole.Width - along, across),
    };

    private static double Along(Size size, DiagramWay way) => way is DiagramWay.Down or DiagramWay.Up ? size.Height : size.Width;

    private static double Across(Size size, DiagramWay way) => way is DiagramWay.Down or DiagramWay.Up ? size.Width : size.Height;

    // ── Where a line runs ───────────────────────────────────────────────────

    /// <summary>
    /// Where a join runs once everything is placed: from the middle of what it leaves, through wherever it bends, to the middle of what
    /// it reaches. A join across one rank bends halfway between the two, so it leaves and arrives square rather than slanting across; a
    /// join between two cells side by side in the same rank — a handoff from one lane to the next — runs straight across, there being no
    /// rank between them to bend in; and a join back to the cell it leaves goes round beside it.
    /// </summary>
    private static IReadOnlyList<Point> Routed(DiagramJoin join, DiagramWay way)
    {
        if (ReferenceEquals(join.From, join.To)) return Loop(join.From.Bounds, join.Towards);

        var from = Middle(join.From.Bounds);
        var to = Middle(join.To.Bounds);

        if (join.Bends.Count > 0) return [from, .. join.Bends, to];

        var down = join.Towards is DiagramWay.Down or DiagramWay.Up;
        if (Math.Abs(down ? from.X - to.X : from.Y - to.Y) < 1) return [from, to];
        if (Math.Abs(down ? from.Y - to.Y : from.X - to.X) < 1) return [from, to];

        var half = down ? (from.Y + to.Y) / 2 : (from.X + to.X) / 2;

        return down
            ? [from, new Point(from.X, half), new Point(to.X, half), to]
            : [from, new Point(half, from.Y), new Point(half, to.Y), to];
    }

    /// <summary>A join back to the cell it leaves: out beside it and back again.</summary>
    private static IReadOnlyList<Point> Loop(Rect bounds, DiagramWay way) =>
        way is DiagramWay.Down or DiagramWay.Up
            ? [new Point(bounds.Right, bounds.Y + (bounds.Height * 0.3)),
               new Point(bounds.Right + Loops, bounds.Y + (bounds.Height * 0.3)),
               new Point(bounds.Right + Loops, bounds.Y + (bounds.Height * 0.7)),
               new Point(bounds.Right, bounds.Y + (bounds.Height * 0.7))]
            : [new Point(bounds.X + (bounds.Width * 0.3), bounds.Bottom),
               new Point(bounds.X + (bounds.Width * 0.3), bounds.Bottom + Loops),
               new Point(bounds.X + (bounds.Width * 0.7), bounds.Bottom + Loops),
               new Point(bounds.X + (bounds.Width * 0.7), bounds.Bottom)];

    private static Point Middle(Rect bounds) => new(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>A join and the two cells at one level that hold its ends.</summary>
    private sealed record Joined(DiagramJoin Join, DiagramCell From, DiagramCell To);

    /// <summary>A join between two cells of one arrangement, and whether the ranking had to turn it round.</summary>
    private sealed class Link(DiagramJoin join, int from, int to, int span)
    {
        public DiagramJoin Join { get; } = join;

        public int From { get; } = from;

        public int To { get; } = to;

        public int Span { get; } = span;

        /// <summary>Whether it goes back to something already on the way down, and so was turned round to be ranked.</summary>
        public bool Turned { get; set; }

        /// <summary>Which way round it is ranked and bent.</summary>
        public (int From, int To) Way => Turned ? (To, From) : (From, To);
    }

    /// <summary>A place in a rank: a cell, or somewhere a link passing through bends.</summary>
    internal sealed class Place
    {
        /// <summary>Which cell it holds, or -1 for a link passing through.</summary>
        public int Cell { get; init; } = -1;

        public int Rank { get; init; }

        /// <summary>Which band it is in — the lane holding it, numbered from one, and nought for one in no lane.</summary>
        public int Lane { get; init; }

        /// <summary>How much room it takes across its rank — none, for a bend.</summary>
        public double Size { get; init; }

        /// <summary>Where in the order of its rank it comes.</summary>
        public int Order { get; set; }

        /// <summary>Where across its rank it sits.</summary>
        public double At { get; set; }

        /// <summary>Its middle, which is where a line bending here runs through.</summary>
        public Point Middle { get; set; }

        public List<Place> Above { get; } = [];

        public List<Place> Below { get; } = [];
    }
}
