using System.Linq;
using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// The macro table held against what the parser actually does with it.
///
/// <para>
/// A row in <see cref="TexMacros"/> is a claim that a name is shorthand for something, and a claim of
/// that kind can be quietly false: add a row for a name the command table already claims and nothing
/// breaks, nothing complains, and the row simply never fires. That happened, and the corpus sweep read
/// it as "nothing changed" — which was true, and was not the good news it looked like.
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex-parse-tree")]
public class TexMacroTableTests
{
    [TestMethod]
    public void EveryMacroActuallyResolvesWhenItIsWritten()
    {
        var inert = TexMacros.All.Keys
            .Where(name => !TexParser.Parse(name).SelfAndDescendants()
                .Any(node => node.Role == TexRole.Expansion))
            .ToList();

        Assert.AreEqual(0, inert.Count,
            "these are in the macro table and resolve to nothing when written:\n  "
            + string.Join("\n  ", inert));
    }

    [TestMethod]
    public void AndResolvingOneLeavesTheSourceExactlyAsItWas()
    {
        foreach (var name in TexMacros.All.Keys)
        {
            var written = $"a {name} b";
            Assert.AreEqual(written, TexParser.Parse(written).Print(),
                $"{name} stopped printing back as it was written");
        }
    }

    [TestMethod]
    public void AndWhatItResolvesToIsItselfReadable()
    {
        foreach (var (name, definition) in TexMacros.All)
        {
            var expansion = TexParser.Parse(name).SelfAndDescendants()
                .FirstOrDefault(node => node.Role == TexRole.Expansion);

            Assert.IsNotNull(expansion, $"{name} resolved to nothing");

            // Asked of the children rather than of the expansion itself, because an expansion prints as
            // nothing on purpose — that is what keeps the round trip true. Its children are ordinary
            // pieces and print as what they are, which is the definition it was read from.
            Assert.AreEqual(definition, string.Concat(expansion.Children.Select(child => child.Print())),
                $"{name}'s expansion did not read back as the definition it was given");
        }
    }

    [TestMethod]
    public void AndTakesUpNoneOfTheSourceItWasNotWrittenIn()
    {
        // The whole reason the round trip survives: an expansion has no width, so nothing after it is
        // pushed along, and nothing inside it claims a stretch of what the writer typed.
        foreach (var name in TexMacros.All.Keys)
        {
            var root = TexParser.Parse(name);

            foreach (var expansion in root.SelfAndDescendants().Where(node => node.Role == TexRole.Expansion))
                Assert.AreEqual(0, expansion.Width, $"{name}'s expansion claims {expansion.Width} character(s)");

            Assert.AreEqual(name.Length, root.Width, $"{name} measures wrong once resolved");
        }
    }
}
