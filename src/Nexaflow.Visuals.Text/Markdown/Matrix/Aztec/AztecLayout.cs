using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// Where an Aztec symbol's bits go: the bullseye at the middle, the orientation marks and mode message
/// wrapped around it, the reference grid a full symbol needs, and the spiral the data runs along.
///
/// <para>
/// Aztec puts its finder in the centre rather than the corners, which is what lets one symbology span
/// fifteen modules to a hundred and fifty-one with no version table: every size is the same core with
/// more rings around it. Everything here follows from that, and every number in it was checked against
/// two reference symbols from different generators, module for module.
/// </para>
/// </summary>
internal static class AztecLayout
{
    /// <summary>Modules from the middle to the mode-message ring: five compact, seven full.</summary>
    internal static int CoreRadius(bool compact) => compact ? 5 : 7;

    /// <summary>
    /// The data grid's width in modules, which for a full symbol is not the symbol's width — the
    /// reference-grid lines sit between the data positions and are counted separately.
    /// </summary>
    internal static int BaseSize(bool compact, int layers) => (compact ? 11 : 14) + 4 * layers;

    /// <summary>
    /// Modules on a side. A compact symbol is its data grid; a full one also carries the reference grid —
    /// the line through the middle, and another every sixteen modules out from it in both directions.
    /// </summary>
    internal static int Size(bool compact, int layers)
    {
        int data = BaseSize(compact, layers);
        return compact ? data : data + 1 + 2 * ((data / 2 - 1) / 15);
    }

    /// <summary>
    /// The bits a symbol's layers hold. Each layer is a two-module-thick ring, so the count is the area
    /// between the outer edge and the core: <c>(2n)² − (2n−4)²</c> summed over the layers, which comes
    /// out as a straight quadratic.
    /// </summary>
    internal static int CapacityBits(bool compact, int layers) =>
        ((compact ? 88 : 112) + 16 * layers) * layers;

    /// <summary>Builds the symbol's modules from its mode message and its already-encoded data stream.</summary>
    internal static bool[,] Build(bool compact, int layers,
                                  IReadOnlyList<bool> modeBits, IReadOnlyList<bool> dataBits)
    {
        int size     = Size(compact, layers);
        int centre   = size / 2;
        int radius   = CoreRadius(compact);
        int baseSize = BaseSize(compact, layers);
        var modules  = new bool[size, size];

        Bullseye(modules, centre, radius);
        if (!compact) ReferenceGrid(modules, size, centre);
        OrientationMarks(modules, centre, radius);

        int at = 0;
        foreach (var (row, col) in ModeCells(centre, radius, compact))
        {
            modules[row, col] = modeBits[at];
            at++;
        }

        var cells = DataCells(compact, layers);
        for (int i = 0; i < cells.Count; i++)
            if (dataBits[i]) modules[cells[i].Row, cells[i].Col] = true;

        return modules;
    }

    /// <summary>
    /// Concentric squares out from the middle, dark on the even radii. The core is what a scanner finds
    /// first, and its alternating rings are why Aztec needs no quiet zone worth the name.
    /// </summary>
    private static void Bullseye(bool[,] modules, int centre, int radius)
    {
        for (int r = 0; r < radius; r += 2)
            for (int d = -r; d <= r; d++)
            {
                modules[centre - r, centre + d] = true;
                modules[centre + r, centre + d] = true;
                modules[centre + d, centre - r] = true;
                modules[centre + d, centre + r] = true;
            }
    }

    /// <summary>
    /// A full symbol's reference grid: whole rows and columns of alternating modules through the middle
    /// and every sixteen out from it, which give a reader something to re-register against when a large
    /// symbol is printed on something that bends. The alternation is the same rule the bullseye follows,
    /// so the lines pass through the core without disturbing it.
    /// </summary>
    private static void ReferenceGrid(bool[,] modules, int size, int centre)
    {
        for (int line = centre; line >= 0; line -= 16) Line(modules, size, line);
        for (int line = centre + 16; line < size; line += 16) Line(modules, size, line);
    }

    private static void Line(bool[,] modules, int size, int line)
    {
        for (int other = 0; other < size; other++)
        {
            bool dark = (line + other) % 2 == 0;
            modules[line, other] = dark;
            modules[other, line] = dark;
        }
    }

    /// <summary>
    /// The four corners of the mode-message ring, each the corner module plus its neighbour on either
    /// side. Clockwise from the top left they carry three dark modules, then two, one and none, so a
    /// reader that has found the core can tell which way up the symbol is and whether it is mirrored.
    /// </summary>
    private static void OrientationMarks(bool[,] modules, int centre, int radius)
    {
        int lo = centre - radius, hi = centre + radius;

        modules[lo, lo]     = true;   modules[lo, lo + 1]  = true;   modules[lo + 1, lo] = true;
        modules[lo, hi]     = true;   modules[lo, hi - 1]  = false;  modules[lo + 1, hi] = true;
        modules[hi, hi]     = false;  modules[hi, hi - 1]  = false;  modules[hi - 1, hi] = true;
        modules[hi, lo]     = false;  modules[hi, lo + 1]  = false;  modules[hi - 1, lo] = false;
    }

    /// <summary>
    /// The mode message's cells, clockwise from the top-left corner, skipping the two modules at each
    /// end of every side because those belong to the orientation marks. A full symbol also skips the
    /// middle module of each side, which is where its reference grid crosses the ring — which is the
    /// whole reason a full mode message is ten bits a side rather than eleven.
    /// </summary>
    internal static IEnumerable<(int Row, int Col)> ModeCells(int centre, int radius, bool compact)
    {
        int lo = centre - radius, hi = centre + radius;
        int grid = compact ? int.MinValue : centre;

        for (int col = lo + 2; col <= hi - 2; col++) if (col != grid) yield return (lo, col);
        for (int row = lo + 2; row <= hi - 2; row++) if (row != grid) yield return (row, hi);
        for (int col = hi - 2; col >= lo + 2; col--) if (col != grid) yield return (hi, col);
        for (int row = hi - 2; row >= lo + 2; row--) if (row != grid) yield return (row, lo);
    }

    /// <summary>
    /// Data-grid coordinate to module coordinate. They are the same in a compact symbol; in a full one
    /// the reference-grid lines occupy positions the data never uses, so walking outward from the middle
    /// gives up a place each time fifteen have been taken.
    /// </summary>
    private static int[] DataMap(bool compact, int baseSize, int centre)
    {
        var map = new int[baseSize];

        if (compact)
        {
            for (int i = 0; i < baseSize; i++) map[i] = i;
            return map;
        }

        int middle = baseSize / 2;
        for (int i = 0; i < middle; i++)
        {
            int offset = i + i / 15;
            map[middle - i - 1] = centre - offset - 1;
            map[middle + i]     = centre + offset + 1;
        }
        return map;
    }

    /// <summary>
    /// The data cells in the order the message fills them — the spiral. It starts at the outer
    /// top-left, runs down the left-hand pair of columns, and turns counter-clockwise at each corner,
    /// taking two modules at a time outer before inner. The outermost layer comes first and the
    /// innermost last, so the message ends against the core.
    ///
    /// <para>
    /// The four sides of a layer are congruent under a quarter turn, so one arm is described and the
    /// other three are that arm rotated — which is both shorter and how the symbol is actually built.
    /// Returned as a list rather than walked in place so that a reader can follow the same order
    /// backwards; there is only one right answer here and both directions should be reading it.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<(int Row, int Col)> DataCells(bool compact, int layers)
    {
        int size     = Size(compact, layers);
        int baseSize = BaseSize(compact, layers);
        var map      = DataMap(compact, baseSize, size / 2);
        var cells    = new List<(int Row, int Col)>(CapacityBits(compact, layers));

        for (int layer = 0; layer < layers; layer++)
        {
            int lo   = 2 * layer;
            int side = baseSize - 4 * layer;

            for (int arm = 0; arm < 4; arm++)
                for (int along = 0; along < side - 2; along++)
                    for (int depth = 0; depth < 2; depth++)
                    {
                        var (row, col) = arm switch
                        {
                            0 => (lo + along, lo + depth),
                            1 => (lo + side - 1 - depth, lo + along),
                            2 => (lo + side - 1 - along, lo + side - 1 - depth),
                            _ => (lo + depth, lo + side - 1 - along),
                        };

                        cells.Add((map[row], map[col]));
                    }
        }

        return cells;
    }

}
