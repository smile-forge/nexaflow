using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Chemistry.Stages;

/// <summary>
/// Pairs each ring-closure digit with the one that closes it, and hangs under both the atom at the other end.
///
/// <para>
/// A digit is a promise that the atom it follows is bonded to whichever atom next writes the same digit — so a
/// ring bond is a fact about two places in the string at once, and neither of them says it. By the time anything
/// counts an atom's bonds, that has to have an answer.
/// </para>
/// <para>
/// A number is free again once it closes, which is how <c>C1CC1C1CC1</c> is two rings. What cannot be paired is
/// said where it was written: a digit that never closes, one that closes onto its own atom or onto an atom it is
/// already bonded to, and a pair whose two ends name different bonds.
/// </para>
/// </summary>
public sealed class ConnectAtoms : IAstStage
{
    public string Name => "smiles:rings";

    public ContentNode Run(ContentNode tree) => SmilesRewrite.Molecules(tree, Connect);

    private sealed record Closure(int Ring, int Atom, int Partner, string? Trouble);

    private static ContentNode Connect(ContentNode molecule)
    {
        var closures = new Dictionary<int, Closure>();
        var open = new Dictionary<int, (int Ring, int Atom, string? Bond)>();
        var bonded = new HashSet<(int, int)>();
        var atoms = 0;
        var rings = 0;

        Walk(molecule, previous: -1);

        foreach (var (number, (ring, atom, _)) in open)
            closures[ring] = new Closure(ring, atom, -1, $"Ring {number} is never closed.");

        if (closures.Count == 0) return molecule;

        return SmilesRewrite.RingBonds(molecule, (index, node) =>
        {
            if (!closures.TryGetValue(index, out var closure)) return node;

            var said = closure.Partner >= 0
                ? node.Saying(SmilesKinds.Fact, SmilesRoles.Partner, closure.Partner.ToString())
                : node;

            return SmilesRewrite.Troubled(said, closure.Trouble);
        });

        int Walk(ContentNode chain, int previous)
        {
            foreach (var item in chain.Children)
            {
                switch (item.Kind)
                {
                    case SmilesKinds.Atom:
                        if (previous >= 0) bonded.Add(Key(previous, atoms));
                        previous = atoms++;
                        break;

                    case SmilesKinds.Dot:
                        previous = -1;
                        break;

                    case SmilesKinds.Branch:
                        Walk(item, previous);
                        break;

                    case SmilesKinds.RingBond:
                    {
                        var ring = rings++;
                        if (item.Trouble is not null || previous < 0) break;

                        var number = int.Parse(item.Children.First(child => child.Kind == SmilesKinds.RingNumber).Text.TrimStart('%'));
                        var bond = item.Children.FirstOrDefault(child => child.Kind == SmilesKinds.Bond)?.Text;

                        if (!open.Remove(number, out var start))
                        {
                            open[number] = (ring, previous, bond);
                            break;
                        }

                        var trouble =
                            start.Atom == previous ? $"Ring {number} closes onto the atom that opened it."
                            : bonded.Contains(Key(start.Atom, previous)) ? $"Ring {number} joins two atoms that are already bonded."
                            : Disagree(start.Bond, bond) ? $"The two ends of ring {number} name different bonds."
                            : null;

                        if (trouble is not null)
                        {
                            // The opening end is left as it was: it is the closing digit that asked for the impossible.
                            closures[ring] = new Closure(ring, previous, -1, trouble);
                            break;
                        }

                        bonded.Add(Key(start.Atom, previous));
                        closures[start.Ring] = new Closure(start.Ring, start.Atom, previous, null);
                        closures[ring] = new Closure(ring, previous, start.Atom, null);
                        break;
                    }
                }
            }

            return previous;
        }
    }

    private static (int, int) Key(int a, int b) => (Math.Min(a, b), Math.Max(a, b));

    /// <summary>
    /// Whether two ends name different bonds. One end saying nothing agrees with anything, and <c>/</c> against
    /// <c>\</c> is the same single bond seen from either end.
    /// </summary>
    private static bool Disagree(string? one, string? other)
    {
        if (one is null || other is null || one == other) return false;
        return !(one is "/" or "\\" or "-" && other is "/" or "\\" or "-");
    }
}
