using System;
using System.Collections.Generic;
using System.Linq;
using Place = Nexaflow.Visuals.Text.Markdown.Mermaid.DiagramLayers.Place;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// Where every place of a layered layout goes across its rank: Brandes and Köpf's coordinate assignment, the one dagre and ELK
/// lay graphs out with.
///
/// <para>
/// Each place is lined up with the middle of what it joins in the rank beside it, a run of places lined up one under another
/// becoming one block that moves as one, and the blocks are packed as close as their neighbours allow. That is done four ways —
/// lining up with the rank above and with the rank below, packed from the left and from the right — since each leans the drawing
/// one way, and the four are balanced into one: each place stands at the middle two of its four settings.
/// </para>
/// <para>
/// A long line comes first. The places it is threaded through line up with one another before anything else is lined up with
/// them, and a short line crossing one gives way — so a long line runs straight, and a run of cells joined one to the next still
/// stands in a line beside it, rather than every cell near a long line being dragged over to stand under it.
/// </para>
/// </summary>
internal static class DiagramAlignment
{
    /// <summary>Where each place goes across its rank, every two neighbours in a rank at least <paramref name="between"/> clear of each other.</summary>
    public static Dictionary<Place, double> Of(IReadOnlyList<List<Place>> rows, double between)
    {
        var places = rows.SelectMany(row => row).ToList();
        if (places.Count == 0) return [];

        var yielding = Conflicts(rows);
        var ways = new List<(Dictionary<Place, double> At, bool Leftwards)>();

        foreach (var down in new[] { true, false })
            foreach (var leftwards in new[] { true, false })
                ways.Add((Run(rows, between, yielding, down, leftwards), leftwards));

        // All four brought to the narrowest of them — each against the edge it was packed towards — before they are balanced.
        var narrowest = ways.MinBy(way => Width(way.At, places))!.At;
        var (left, right) = Edges(narrowest, places);

        foreach (var (at, leftwards) in ways)
        {
            var (least, most) = Edges(at, places);
            var shift = leftwards ? left - least : right - most;
            foreach (var place in places) at[place] += shift;
        }

        return places.ToDictionary(place => place, place =>
        {
            var four = ways.Select(way => way.At[place]).OrderBy(at => at).ToArray();
            return (four[1] + four[2]) / 2;
        });
    }

    /// <summary>
    /// The short lines that cross a long line, which give way to it: between each pair of ranks, any line from a place outside the
    /// stretch two long lines bound. A line between two places a long line is threaded through is never one of them.
    /// </summary>
    private static HashSet<(Place Above, Place Below)> Conflicts(IReadOnlyList<List<Place>> rows)
    {
        var yielding = new HashSet<(Place, Place)>();

        for (var rank = 0; rank + 1 < rows.Count; rank++)
        {
            var (upper, lower) = (rows[rank], rows[rank + 1]);
            if (upper.Count == 0 || lower.Count == 0) continue;
            var (from, next) = (0, 0);

            for (var at = 0; at < lower.Count; at++)
            {
                var inner = Threaded(lower[at], upper);
                if (at != lower.Count - 1 && inner is null) continue;

                var to = inner?.Order ?? upper.Count - 1;

                for (; next <= at; next++)
                    foreach (var above in lower[next].Above.Where(above => above.Rank == upper[0].Rank))
                        if (above.Order < from || above.Order > to) yielding.Add((above, lower[next]));

                from = to;
            }
        }

        return yielding;
    }

    /// <summary>The place a long line threaded through this one came from in the rank above, or null where this is no such place.</summary>
    private static Place? Threaded(Place place, List<Place> upper) =>
        place.Cell >= 0 || upper.Count == 0 ? null : place.Above.FirstOrDefault(above => above.Cell < 0 && above.Rank == upper[0].Rank);

    /// <summary>One of the four: lined up with the rank above (<paramref name="down"/>) or below, packed from the left or the right.</summary>
    private static Dictionary<Place, double> Run(IReadOnlyList<List<Place>> rows, double between, HashSet<(Place, Place)> yielding,
                                                 bool down, bool leftwards)
    {
        var layers = (down ? rows : rows.Reverse()).Select(row => leftwards ? row.ToList() : Enumerable.Reverse(row).ToList()).ToList();

        var at = new Dictionary<Place, int>();
        var of = new Dictionary<Place, int>();
        for (var layer = 0; layer < layers.Count; layer++)
            for (var index = 0; index < layers[layer].Count; index++)
            {
                at[layers[layer][index]] = index;
                of[layers[layer][index]] = layer;
            }

        var root = at.Keys.ToDictionary(place => place, place => place);
        var align = at.Keys.ToDictionary(place => place, place => place);

        // Lining up: each place with the middle of what it joins in the layer before, the lower middle first where there are two.
        for (var layer = 1; layer < layers.Count; layer++)
        {
            var reached = -1;

            foreach (var place in layers[layer])
            {
                var joined = (down ? place.Above : place.Below).Where(at.ContainsKey).OrderBy(other => at[other]).ToList();
                if (joined.Count == 0) continue;

                foreach (var middle in new[] { (joined.Count - 1) / 2, joined.Count / 2 }.Distinct())
                {
                    if (align[place] != place) break;

                    var other = joined[middle];
                    var line = other.Rank < place.Rank ? (other, place) : (place, other);
                    if (yielding.Contains(line) || reached >= at[other]) continue;

                    align[other] = place;
                    root[place] = root[other];
                    align[place] = root[place];
                    reached = at[other];
                }
            }
        }

        // Packing: each block as far towards the start as the blocks before it in every layer it stands in allow.
        var sink = at.Keys.ToDictionary(place => place, place => place);
        var shift = at.Keys.ToDictionary(place => place, _ => double.PositiveInfinity);
        var x = new Dictionary<Place, double>();

        // The place before one in its own layer, the way it is being packed.
        Place? Before(Place place) => at[place] > 0 ? layers[of[place]][at[place] - 1] : null;

        foreach (var layer in layers)
            foreach (var place in layer.Where(place => root[place] == place))
                Placed(place);

        var settled = new Dictionary<Place, double>();
        foreach (var place in at.Keys)
        {
            var value = x[root[place]];
            if (shift[sink[root[place]]] < double.PositiveInfinity) value += shift[sink[root[place]]];
            settled[place] = leftwards ? value : -value;
        }

        return settled;

        void Placed(Place block)
        {
            if (x.ContainsKey(block)) return;

            x[block] = 0;
            var member = block;

            do
            {
                if (Before(member) is { } before)
                {
                    var other = root[before];
                    Placed(other);

                    if (sink[block] == block) sink[block] = sink[other];

                    var apart = ((before.Size + member.Size) / 2) + between;

                    if (sink[block] != sink[other])
                        shift[sink[other]] = Math.Min(shift[sink[other]], x[block] - x[other] - apart);
                    else
                        x[block] = Math.Max(x[block], x[other] + apart);
                }

                member = align[member];
            }
            while (member != block);
        }
    }

    private static double Width(Dictionary<Place, double> at, IEnumerable<Place> places)
    {
        var (least, most) = Edges(at, places);
        return most - least;
    }

    private static (double Least, double Most) Edges(Dictionary<Place, double> at, IEnumerable<Place> places) =>
        (places.Min(place => at[place] - (place.Size / 2)), places.Max(place => at[place] + (place.Size / 2)));
}
