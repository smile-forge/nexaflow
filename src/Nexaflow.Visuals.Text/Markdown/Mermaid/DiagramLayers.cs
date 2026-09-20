using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// Sugiyama layered layout for flowcharts, state charts and class diagrams: cells ranked by link distance, each rank ordered to
/// minimize crossings, then spread across the rank beside what it joins.
///
/// A box is laid out in its own space first, then treated as a cell sized to its contents in the parent arrangement; a link
/// between boxes is drawn between where they actually meet. A link spanning more than one rank gets a place in each rank it
/// passes through, so it is ordered like everything else and routes around what's in the way instead of through it.
///
/// Lanes are this layout under a constraint, not a separate one — same ranking/ordering/spreading/routing, with each cell
/// confined to its lane's band and one cell per lane per rank.
/// </summary>
internal static class DiagramLayers
{
    /// <summary>How many times the order of each rank, and then the place of everything in it, is settled.</summary>
    private const int Passes = 8;

    /// <summary>How far a link back to the cell it leaves reaches out beside it.</summary>
    private const double Loops = 18;

    /// <summary>How far apart the lines joining one pair of cells are bowed, so none is drawn on top of another.</summary>
    private const double Bows = 12;

    /// <summary>
    /// And how far where the ends are spread as well. Wide, because what is written on each line of a couple is set over
    /// the middle of that line: the lens has to open far enough that the two of them do not land on one another.
    /// </summary>
    private const double Opened = 41;

    /// <summary>
    /// What a gap keeps beyond the words written across it: enough line either side of them for the head at each end to
    /// show. What the words themselves come to is measured; this is everything else the gap is for.
    /// </summary>
    private const double Writing = DiagramConnector.Clearance * 2;

    /// <summary>How much of a shape's edge is kept clear at each end when the lines meeting it are spread along it.</summary>
    private const double Ports = 8;

    /// <summary>And the furthest apart two of those ends are set.</summary>
    private const double Stepping = 26;

    /// <summary>
    /// How much more of a gap each line past the first asks for, where several of them leave or arrive at one shape across
    /// it. Their ends are spread along that shape's edge and they swing apart from there, which wants more of the gap than
    /// a single line running straight across it does.
    /// </summary>
    private const double Spreading = 14;

    /// <summary>
    /// Lays every cell out, hands each its <see cref="DiagramCell.Bounds"/> and each join its <see cref="DiagramJoin.Route"/>,
    /// and says how much room it all came to.
    /// </summary>
    /// <param name="between">How far apart two cells in the same rank are set.</param>
    /// <param name="along">How far apart one rank is set from the next.</param>
    /// <param name="laning">The swimlane bands to lay out in, or null for none. See <see cref="DiagramLanes"/>.</param>
    /// <param name="ports">
    /// Whether every line meets what it joins at its own place along that shape's edge, rather than all of them meeting it
    /// at its middle — which is what makes a fan read as a fan and a couple open into a lens.
    ///
    /// <para>
    /// A diagram asks for it once what it writes on its lines has somewhere to go. Lines that were one line to the eye had
    /// one label to the eye as well, and parting them parts those: a diagram whose words are placed by its route's own
    /// middle needs the gaps between its ranks sized for them first, or what was hidden becomes what is overlapping.
    /// </para>
    /// </param>
    public static Size Lay(IReadOnlyList<DiagramCell> cells, IReadOnlyList<DiagramJoin> joins, DiagramWay way,
                           double between, double along, DiagramLanes? laning = null, bool ports = false)
    {
        // Innermost boxes first: a box is the size of what it holds, so what it holds is arranged before the box is placed.
        foreach (var box in Boxes(cells).OrderByDescending(Deep))
        {
            // A box is a space of its own, which the bands of the layout round it do not reach into.
            var held = Arrange(Within(cells, box), Joining(joins, box), box.Way ?? way, between, along, laning: null, ports);

            box.Size = new Size(Math.Max(box.Size.Width, held.Width + (box.Pad * 2)),
                                Math.Max(box.Size.Height, held.Height + (box.Pad * 2) + box.Heading));
        }

        var whole = Arrange(Within(cells, null), Joining(joins, null), way, between, along, laning, ports);

        // Outermost boxes first, so a box is already where it belongs before what is inside it is moved into it.
        foreach (var box in Boxes(cells).OrderBy(Deep)) Moved(cells, joins, box);

        foreach (var join in joins) join.Route = Routed(join, way);
        Parted(cells, joins, way, ports);
        Swung(joins);

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

    /// <summary>Lays one level out: cells into ranks, each rank ordered then set across, every join given its bend places. Hands back the room it took.</summary>
    private static Size Arrange(IReadOnlyList<DiagramCell> cells, IReadOnlyList<Joined> edges, DiagramWay way,
                                double between, double along, DiagramLanes? laning, bool ports = false)
    {
        // Lanes with nothing in them yet are still bands to place.
        if (cells.Count == 0 && laning is null) return default;

        var at = new Dictionary<DiagramCell, int>(cells.Count);
        for (var cell = 0; cell < cells.Count; cell++) at[cells[cell]] = cell;

        var links = edges.Select(edge => new Link(edge.Join, at[edge.From], at[edge.To], edge.Join.Span)).ToList();
        Turned(cells.Count, links);

        var ranks = Ranked(cells, links, laning);
        var rows = Rowed(cells, links, ranks, way, out var placed, out var chains, out var beside);

        Ordered(rows, beside);
        Spread(rows, between, beside);

        var banded = laning?.Held(rows, between) ?? 0;
        var whole = Sized(cells, rows, way, Gaps(links, ranks, rows.Count, way, along, ports), laning?.Heading ?? 0,
                          banded, out var from, out var deep);

        Settled(cells, rows, way, from, deep, whole);
        laning?.Settled(way, whole);

        foreach (var link in links)
            link.Join.Bends =
                [.. (link.Turned ? Enumerable.Reverse(chains[link]) : chains[link]).SelectMany(place => Passing(place, link.Turned))];

        foreach (var edge in edges)
        {
            edge.Join.Level = cells[0].Inside;
            edge.Join.Towards = way;
        }

        return whole;
    }

    /// <summary>
    /// Reverses every link that would close a cycle, so ranking can proceed — only the ranking sees it reversed, the line still
    /// draws as written. Starts from cells nothing reaches so a cycle breaks at the link that closes it, not an arbitrary one,
    /// keeping the diagram reading from its beginning.
    /// </summary>
    private static void Turned(int count, IReadOnlyList<Link> links)
    {
        var leaving = new List<int>[count];
        var reaching = new int[count];
        for (var cell = 0; cell < count; cell++) leaving[cell] = [];

        for (var link = 0; link < links.Count; link++)
        {
            if (links[link].From == links[link].To) continue;

            leaving[links[link].From].Add(link);
            reaching[links[link].To]++;
        }

        var state = new int[count];

        for (var cell = 0; cell < count; cell++)
            if (reaching[cell] == 0 && state[cell] == 0) Walk(cell);

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
    /// The places every rank holds: one per cell, plus one per link passing through — giving a long link somewhere to bend and its
    /// own place in the order. A bend takes the band of the link it's handed from, so it runs down its own band rather than the next.
    /// A same-rank link (span 0) ties nothing in this rank; it's kept in <paramref name="beside"/> instead, so the two cells sit next
    /// to each other without standing in for one another's neighbours.
    /// </summary>
    private static List<List<Place>> Rowed(IReadOnlyList<DiagramCell> cells, IReadOnlyList<Link> links, int[] ranks,
                                           DiagramWay way, out Place[] placed, out Dictionary<Link, List<Place>> chains,
                                           out Dictionary<Place, Place> beside)
    {
        var rows = new List<List<Place>>();
        placed = new Place[cells.Count];
        chains = [];
        beside = [];

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

            if (ranks[first] == ranks[last])
            {
                beside[placed[last]] = placed[first];
                continue;
            }

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

    /// <summary>Settles each rank's order so a cell sits near what it joins in the rank beside it, minimizing crossings. Lanes sort first so a lane's band is the same stretch in every rank; a cell set beside another follows it.</summary>
    private static void Ordered(List<List<Place>> rows, IReadOnlyDictionary<Place, Place> beside)
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
                row.AddRange(Following(settled, beside));
                Numbered(rows);
            }
        }
    }

    /// <summary>Every place set beside another moved to follow it, so the two come out of the ordering next to each other.</summary>
    private static List<Place> Following(List<Place> row, IReadOnlyDictionary<Place, Place> beside)
    {
        if (beside.Count == 0) return row;

        var after = row.Where(place => beside.ContainsKey(place)).ToLookup(place => beside[place]);
        if (after.Count == 0) return row;

        var settled = new List<Place>(row.Count);

        foreach (var place in row.Where(place => !beside.ContainsKey(place)))
        {
            settled.Add(place);
            settled.AddRange(after[place]);
        }

        // Anything set beside a place in another rank is left where the ordering put it.
        settled.AddRange(row.Where(place => beside.ContainsKey(place) && !settled.Contains(place)));

        return settled;
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

    /// <summary>Sets each place across its rank near what it joins beside it, pushes overlaps apart, then re-centers the rank on where it wanted to be so ranks stay lined up.</summary>
    private static void Spread(List<List<Place>> rows, double between, IReadOnlyDictionary<Place, Place> beside)
    {
        foreach (var row in rows) Apart(row, between);

        for (var pass = 0; pass < Passes; pass++)
        {
            var down = pass % 2 == 0;

            foreach (var row in Sweep(rows, down))
            {
                foreach (var place in row)
                {
                    if (beside.TryGetValue(place, out var about))
                    {
                        place.At = about.At;
                        continue;
                    }

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
    /// How far apart each pair of ranks is held: far enough for whatever is written across that gap, and never less than
    /// the diagram's own spacing. Several lines between the same two things write one under another, so it is what they
    /// come to together that has to fit; lines between different things go side by side, so the deepest of those is enough.
    ///
    /// <para>
    /// Only a line that runs between neighbouring ranks is counted. One reaching further has the whole of what it passes
    /// through to be written in, and no one gap of that is the gap it belongs to.
    /// </para>
    ///
    /// <para>
    /// Where the ends are spread there is the fan to allow for as well, and that <em>is</em> the business of every gap a
    /// line crosses on its way — a line reaching past this gap still leaves its shape through it, alongside the rest. A
    /// fan is lines going to <em>different</em> places, though: several between the same two things are a couple, which
    /// opens into a lens of its own and has already been allowed for by what is written on it.
    /// </para>
    /// </summary>
    private static double[] Gaps(IReadOnlyList<Link> links, int[] ranks, int rows, DiagramWay way, double along, bool ports)
    {
        var gaps = new double[Math.Max(1, rows)];
        Array.Fill(gaps, along);

        var neighbours = links
            .Where(link => Math.Abs(ranks[link.From] - ranks[link.To]) == 1 && Along(link.Join.Said, way) > 0)
            .GroupBy(link => (Math.Min(link.From, link.To), Math.Max(link.From, link.To)));

        foreach (var pair in neighbours)
        {
            var gap = Math.Min(ranks[pair.Key.Item1], ranks[pair.Key.Item2]);
            if (gap < 0 || gap >= gaps.Length) continue;

            gaps[gap] = Math.Max(gaps[gap],
                                 pair.Sum(link => Along(link.Join.Said, way))
                                 + ((pair.Count() - 1) * DiagramConnector.Stacking)
                                 + Writing);
        }

        if (!ports) return gaps;

        var fan = new Dictionary<(int Cell, int Gap), HashSet<int>>();

        foreach (var link in links)
        {
            var (one, other) = (ranks[link.From], ranks[link.To]);
            if (one == other) continue;

            var back = other < one;

            Counted(fan, link.From, back ? one - 1 : one, link.To);
            Counted(fan, link.To, back ? other : other - 1, link.From);
        }

        var opened = new double[gaps.Length];

        foreach (var ((_, gap), others) in fan)
            if (gap >= 0 && gap < opened.Length) opened[gap] = Math.Max(opened[gap], (others.Count - 1) * Spreading);

        for (var gap = 0; gap < gaps.Length; gap++) gaps[gap] += opened[gap];

        return gaps;

        static void Counted(Dictionary<(int, int), HashSet<int>> fan, int cell, int gap, int other)
        {
            if (!fan.TryGetValue((cell, gap), out var others)) fan[(cell, gap)] = others = [];

            others.Add(other);
        }
    }

    /// <summary>How much room it all took, and where each rank starts and how deep it is — the first rank starts past the room lanes keep for their labels.</summary>
    private static Size Sized(IReadOnlyList<DiagramCell> cells, List<List<Place>> rows, DiagramWay way, double[] along,
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
            running += deep[rank] + along[Math.Min(rank, along.Length - 1)];
        }

        var whole = Math.Max(heading, running - (rows.Count == 0 ? 0 : along[Math.Min(rows.Count - 1, along.Length - 1)]));
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

                place.Near = Placed(from[place.Rank], place.At, way, whole);
                place.Far = Placed(from[place.Rank] + deep[place.Rank], place.At, way, whole);

                if (place.Cell >= 0)
                    cells[place.Cell].Bounds = Placed(start, place.At - (Across(size, way) / 2), size, way, whole);
            }
    }

    /// <summary>
    /// Where a line bending in a rank runs: into the rank and out of it again, rather than through the middle of it — so a line passing
    /// a rank runs alongside what is in it instead of cutting the corner off a cell as deep as everything it holds. A rank holding
    /// nothing but bends has no depth to run down, so it is the one place.
    /// </summary>
    private static IEnumerable<Point> Passing(Place place, bool turned)
    {
        var (first, last) = turned ? (place.Far, place.Near) : (place.Near, place.Far);

        yield return first;
        if ((last - first).LengthSquared > 1) yield return last;
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
    /// From the middle of what it leaves, through its bends, to the middle of what it reaches. A single-rank join bends halfway
    /// so it leaves and arrives square rather than slanting; a same-rank join (a lane handoff) runs straight, having no rank to
    /// bend in; a self-join loops round beside the cell.
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

        var half = down
            ? Halfway(join.From.Bounds.Top, join.From.Bounds.Bottom, join.To.Bounds.Top, join.To.Bounds.Bottom)
            : Halfway(join.From.Bounds.Left, join.From.Bounds.Right, join.To.Bounds.Left, join.To.Bounds.Right);

        return down
            ? [from, new Point(from.X, half), new Point(to.X, half), to]
            : [from, new Point(half, from.Y), new Point(half, to.Y), to];
    }

    /// <summary>
    /// Bows apart lines that would otherwise overlap: two cells joined more than once (a transition each way, or two links between
    /// the same pair) share the same route and would hide one another. Ends stay put — that's where the line meets its shape —
    /// only the middle moves, by the line's position among the ones sharing its pair.
    /// </summary>
    private static void Parted(IReadOnlyList<DiagramCell> cells, IReadOnlyList<DiagramJoin> joins, DiagramWay way, bool ports)
    {
        var at = new Dictionary<DiagramCell, int>(cells.Count);
        for (var cell = 0; cell < cells.Count; cell++) at[cells[cell]] = cell;

        var shared = joins
            .Where(join => !ReferenceEquals(join.From, join.To) && join.Route.Count >= 2)
            .Where(join => at.ContainsKey(join.From) && at.ContainsKey(join.To))
            .GroupBy(join => (Math.Min(at[join.From], at[join.To]), Math.Max(at[join.From], at[join.To])))
            .ToList();

        var couples = shared.Where(pair => pair.Count() > 1).SelectMany(pair => pair).ToHashSet();

        // The ends first, then the middles. Every line meeting a shape meets it at its own place along that shape's edge —
        // a fan of them leaving one node reads as a fan, and two that leave and arrive at the same point would be one line
        // to the eye however far they bow apart in between.
        //
        // A couple keeps out of it. Its ends are set by the lens it opens into, and counting them into the fan as well would
        // move the lines that are not part of it: a lone line between two shapes that stand one above the other runs straight
        // down, and should go on doing so however many lines its shapes share with somewhere else.
        if (ports) Ported([.. joins.Where(join => !couples.Contains(join))], way);

        foreach (var pair in shared)
        {
            var many = pair.ToList();
            if (many.Count < 2) continue;

            Coupled(many, way, ports);
        }
    }

    /// <summary>
    /// Spends the two bends of a line that leaves and arrives square as the handles of one curve.
    ///
    /// <para>
    /// <see cref="Routed"/> gives such a line a bend at each end of the gap it crosses so that it meets both shapes square
    /// rather than slanting into them. Kept as bends they are drawn through, and the line is a dog-leg; spent as handles
    /// they shape a single swing across the gap instead. It happens last because the bends are what the ends are moved
    /// along — a line given its own place on a shape's edge takes its handle with it.
    /// </para>
    ///
    /// <para>
    /// Only that pair. A line that reaches past the next rank is routed around what stands in its way, and those bends are
    /// places it has to pass through rather than handles to be spent.
    /// </para>
    /// </summary>
    private static void Swung(IReadOnlyList<DiagramJoin> joins)
    {
        foreach (var join in joins)
            if (join.Bends.Count == 0 && join.Route.Count == 4 && !ReferenceEquals(join.From, join.To))
                join.Route = DiagramConnector.Curving(join.Route[0], join.Route[1], join.Route[2], join.Route[3]);
    }

    /// <summary>
    /// The lines of a couple, opened into a lens.
    ///
    /// <para>
    /// Each of them meets both of the things it joins the same distance off their middles — <em>the same</em> distance at
    /// both ends, or the two would cross in the middle instead of opening — and then bows further out on that same side.
    /// The bow has to be worked out in the page's terms rather than the line's: <see cref="Bowing"/> works across the way a
    /// line runs, and the line of a couple that runs back the other way runs across the other way too, so following the
    /// line would fold the pair together rather than open it.
    /// </para>
    /// </summary>
    private static void Coupled(IReadOnlyList<DiagramJoin> many, DiagramWay way, bool ports)
    {
        var sideways = way is DiagramWay.Down or DiagramWay.Up;

        for (var one = 0; one < many.Count; one++)
        {
            var join = many[one];
            if (join.Route.Count < 2) continue;

            var step = (one - ((many.Count - 1) / 2.0));

            if (ports)
            {
                var offset = step * Stepping;
                var route = join.Route.ToList();

                route[0] = Aside(route[0], join.From, offset, sideways);
                route[^1] = Aside(route[^1], join.To, offset, sideways);

                Squared(route, 0, sideways);
                Squared(route, route.Count - 1, sideways);

                join.Route = route;
            }

            var bow = ports ? Math.Sign(step) * Opened : step * Bows;
            if (Math.Abs(bow) < 1e-9) continue;

            var along = join.Route[^1] - join.Route[0];
            var aside = sideways
                ? (along.Y >= 0 ? -bow : bow)
                : (along.X >= 0 ? bow : -bow);

            join.Route = Bowing(join.Route, ports ? aside : bow);
        }
    }

    /// <summary>A line's end moved that far off the middle of the shape it meets, across the way the layout runs.</summary>
    private static Point Aside(Point at, DiagramCell cell, double offset, bool sideways)
    {
        var bounds = cell.Bounds;

        return sideways
            ? new Point(bounds.X + (bounds.Width / 2) + offset, at.Y)
            : new Point(at.X, bounds.Y + (bounds.Height / 2) + offset);
    }

    /// <summary>
    /// Spreads the end every line has at the shape it meets across that shape's edge, in the order they head off in, so a
    /// fan of them reads as a fan rather than piling up on the shape's middle.
    /// </summary>
    private static void Ported(IReadOnlyList<DiagramJoin> joins, DiagramWay way)
    {
        var meeting = new Dictionary<DiagramCell, List<(DiagramJoin Join, int At)>>();

        foreach (var join in joins.Where(join => join.Route.Count >= 2 && !ReferenceEquals(join.From, join.To)))
        {
            Meeting(meeting, join.From, join, at: 0);
            Meeting(meeting, join.To, join, at: join.Route.Count - 1);
        }

        foreach (var (cell, ends) in meeting) Ported(cell, ends, way);
    }

    private static void Meeting(Dictionary<DiagramCell, List<(DiagramJoin, int)>> meeting, DiagramCell cell,
                                DiagramJoin join, int at)
    {
        if (!meeting.TryGetValue(cell, out var ends)) meeting[cell] = ends = [];
        ends.Add((join, at));
    }

    private static void Ported(DiagramCell cell, List<(DiagramJoin Join, int At)> ends, DiagramWay way)
    {
        if (ends.Count < 2) return;

        var across = way is DiagramWay.Down or DiagramWay.Up;
        var bounds = cell.Bounds;

        var span = Math.Max(0, (across ? bounds.Width : bounds.Height) - (Ports * 2));
        var step = Math.Min(Stepping, span / (ends.Count - 1));
        if (step <= 0) return;

        var middle = across ? bounds.X + (bounds.Width / 2) : bounds.Y + (bounds.Height / 2);
        var first = middle - (step * (ends.Count - 1) / 2);

        // In the order they head off in, so the lines of a fan keep out of one another's way.
        ends.Sort((one, other) => Heading(one.Join, one.At, across).CompareTo(Heading(other.Join, other.At, across)));

        for (var one = 0; one < ends.Count; one++)
        {
            var (join, at) = ends[one];
            var route = join.Route.ToList();
            var was = route[at];

            route[at] = across ? new Point(first + (one * step), was.Y) : new Point(was.X, first + (one * step));
            Squared(route, at, across);

            join.Route = route;
        }
    }

    /// <summary>
    /// Where a line is heading from one of its ends, which is the order its end takes along the shape's edge. Read off the
    /// far end of the line rather than the bend beside this one: that bend is square to the edge, so it says the same thing
    /// for every line meeting the shape, and only where each one ends up says which way round they go.
    /// </summary>
    private static double Heading(DiagramJoin join, int at, bool across)
    {
        var toward = join.Route[at == 0 ? ^1 : 0];

        return across ? toward.X : toward.Y;
    }

    /// <summary>
    /// Brings the bend beside a moved end into line with it, so the line sets off square to the edge from the place it was
    /// given rather than heading straight back for the middle it was moved off — which is where they would all meet again.
    /// </summary>
    private static void Squared(List<Point> route, int at, bool across)
    {
        if (route.Count < 3) return;

        var beside = at == 0 ? 1 : route.Count - 2;

        route[beside] = across
            ? new Point(route[at].X, route[beside].Y)
            : new Point(route[beside].X, route[at].Y);
    }

    /// <summary>A route moved aside from the straight run between its ends, which stay where they meet what they join.</summary>
    private static IReadOnlyList<Point> Bowing(IReadOnlyList<Point> route, double aside)
    {
        var along = route[^1] - route[0];
        if (along.Length < 1e-9) return route;

        along.Normalize();
        var across = new Vector(-along.Y, along.X) * aside;

        return route.Count == 2
            ? [route[0], new Point(((route[0].X + route[1].X) / 2) + across.X, ((route[0].Y + route[1].Y) / 2) + across.Y), route[1]]
            : [route[0], .. route.Skip(1).SkipLast(1).Select(bend => bend + across), route[^1]];
    }

    /// <summary>
    /// Where a join bends: halfway across the clear air between the two cells, not halfway between their middles — a box's middle
    /// can sit far past the edge the line leaves by, and bending there would double back into the box. Falls back to the midpoint
    /// between middles when the two overlap (no clear air).
    /// </summary>
    private static double Halfway(double from, double past, double to, double beyond)
    {
        if (to >= past) return (past + to) / 2;
        if (from >= beyond) return (beyond + from) / 2;

        return (from + past + to + beyond) / 4;
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

        /// <summary>Where a line bending here comes into the rank, and where it leaves it.</summary>
        public Point Near { get; set; }

        public Point Far { get; set; }

        public List<Place> Above { get; } = [];

        public List<Place> Below { get; } = [];
    }
}
