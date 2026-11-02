using System.Linq;
using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// A document's outline (<see cref="ContentOutline"/>): every heading promoted by <see cref="WithOutline"/> and numbered over the whole
/// document, its sections answered from an index of units — and, kept a unit at a time through every change, the outline of the
/// document it became, outlined whole.
/// </summary>
[TestClass]
[CoversNode("markdown-sections")]
public class MarkdownOutlineTests
{
    /// <summary>What headings are written saying — some alike, one written as another's repeat would be numbered.</summary>
    private static readonly string[] Said = ["Notes", "Notes", "Intro", "Notes 1", "Usage", "Notes-1", "Intro"];

    [TestMethod]
    [CoversNode("markdown-anchors")]
    public void TwoHeadingsSayingTheSame_AreToldApartByTheOrderTheyAreWrittenIn()
    {
        var tree = MarkdownParser.Worked("# Notes\n\n## Notes\n\n### `Notes` & *more*\n");

        CollectionAssert.AreEqual(new[] { "notes", "notes-1", "notes--more" }, Names(ContentOutline.Of(tree)));
        Assert.AreEqual(3, tree.SelfAndDescendants().Count(node => OutlineMark.Of(node) is not null), "each heading is marked once");
    }

    [TestMethod]
    [CoversNode("markdown-anchors")]
    public void TypographyAndEmojiInAHeading_MoveNoAnchor_ItIsNamedByWhatWasWritten()
    {
        // GitHub names a heading by what was written, so a link to one keeps working whether or not it is drawn curled.
        var tree = MarkdownParser.Worked("# Don't \"panic\" -- :rocket: launch\n");

        CollectionAssert.AreEqual(new[] { "dont-panic----rocket-launch" }, Names(ContentOutline.Of(tree)));
    }

    [TestMethod]
    [CoversNode("markdown-anchors")]
    public void ANameWrittenAsARepeatWouldBeNumbered_IsTakenLikeAnyOther()
    {
        CollectionAssert.AreEqual(new[] { "a", "a-1", "a-2" }, Names(ContentOutline.Of(MarkdownParser.Worked("# A\n\n# A-1\n\n# A\n"))));
        CollectionAssert.AreEqual(new[] { "a", "a-1", "a-1-1" }, Names(ContentOutline.Of(MarkdownParser.Worked("# A\n\n# A\n\n# A-1\n"))));
    }

    [TestMethod]
    public void AHeadingInAQuote_IsNamed_ButHeadsNothing()
    {
        var outline = ContentOutline.Of(MarkdownParser.Worked("# Top\n\n> # Quoted\n\ntext\n"));

        CollectionAssert.AreEqual(new[] { "top", "quoted" }, Names(outline));
        Assert.IsNull(outline.Section(0, 1), "a heading in a quote heads no section");
        Assert.AreEqual(2, outline.Section(0, 0)!.Behind, "and the heading above it holds the quote and the words after it");
    }

    [TestMethod]
    public void KeptAUnitAtATime_ItIsTheOutlineOfTheDocumentItBecame()
    {
        var random = new Random(7);
        var units = Enumerable.Range(0, 80).Select(_ => Unit(random)).ToList();

        var outline = new ContentOutline();
        outline.Replaced(0, 0, Gathered(MarkdownParser.Worked(string.Concat(units)), units, 0, units.Count));
        Alike(outline, units, "as first read");

        for (var step = 0; step < 400; step++)
        {
            var at = random.Next(units.Count);
            var (from, to, made) = random.Next(5) switch
            {
                0 when units.Count > 1 => Removed(units, at),
                1 => Inserted(units, at, Unit(random)),
                2 when at + 1 < units.Count => Merged(units, at),
                _ => Rewritten(units, at, Unit(random)),
            };

            var tree = MarkdownParser.Worked(string.Concat(units));
            outline.Replaced(from, to, Gathered(tree, units, from, from + made));

            Alike(outline, units, $"after step {step}");
        }
    }

    [TestMethod]
    [CoversNode("markdown-anchors")]
    public void TypingInAHeading_RenamesOnlyTheUnitsHoldingOneLikeIt()
    {
        List<string> units = ["# Notes\n\nwords\n\n", "# Intro\n\n", "# Notes\n\n", "# Usage\n\n## Intro\n\n"];

        var outline = new ContentOutline();
        outline.Replaced(0, 0, Gathered(MarkdownParser.Worked(string.Concat(units)), units, 0, units.Count));
        var repeat = outline[2].Unit;

        Rewritten(units, 0, "# Notesx\n\nwords\n\n");
        var renamed = outline.Replaced(0, 1, Gathered(MarkdownParser.Worked(string.Concat(units)), units, 0, 1));

        CollectionAssert.AreEquivalent(new[] { repeat }, renamed.ToArray(), "the repeat takes the name the heading let go of, and nothing else changes");
        Assert.AreEqual("notes", outline.Name(2, 0));
        Assert.AreEqual("intro-1", outline.Name(3, 1), "a unit holding headings like none changed keeps what it answered to");

        Rewritten(units, 0, "# Notesxy\n\nwords\n\n");
        renamed = outline.Replaced(0, 1, Gathered(MarkdownParser.Worked(string.Concat(units)), units, 0, 1));

        Assert.AreEqual(0, renamed.Count, "typing on in a heading no other is like renames nothing");
    }

    // ── The oracle ──────────────────────────────────────────────────────────

    /// <summary>A unit of a document: a few blocks, each closed by a blank line, so no block runs on into the unit after.</summary>
    private static string Unit(Random random)
    {
        var unit = new StringBuilder();

        for (var block = random.Next(1, 5); block > 0; block--)
            unit.Append(random.Next(6) switch
            {
                0 or 1 or 2 => $"{new string('#', random.Next(1, 4))} {Said[random.Next(Said.Length)]}\n\n",
                3 => $"> # {Said[random.Next(Said.Length)]}\n\n",
                _ => "some words\n\n",
            });

        return unit.ToString();
    }

    private static (int From, int To, int Made) Removed(List<string> units, int at)
    {
        units.RemoveAt(at);
        return (at, at + 1, 0);
    }

    private static (int From, int To, int Made) Inserted(List<string> units, int at, string unit)
    {
        units.Insert(at, unit);
        return (at, at, 1);
    }

    private static (int From, int To, int Made) Merged(List<string> units, int at)
    {
        units[at] += units[at + 1];
        units.RemoveAt(at + 1);
        return (at, at + 2, 1);
    }

    private static (int From, int To, int Made) Rewritten(List<string> units, int at, string unit)
    {
        units[at] = unit;
        return (at, at + 1, 1);
    }

    /// <summary>What the units from <paramref name="from"/> up to <paramref name="to"/> say to the outline, from the parts of <paramref name="tree"/> each stands for.</summary>
    private static List<OutlineUnit> Gathered(ContentNode tree, List<string> units, int from, int to)
    {
        var start = units.Take(from).Sum(unit => unit.Length);
        var gathered = new List<OutlineUnit>();

        for (var index = from; index < to; index++)
        {
            var end = start + units[index].Length;
            var parts = tree.Children.Where(part => !part.IsDerived && part.Offset >= start && part.Offset < end).ToList();

            gathered.Add(OutlineUnit.Of(parts, units[index].Length, start));
            start = end;
        }

        return gathered;
    }

    /// <summary>Holds <paramref name="outline"/>, kept a unit at a time, to the outline of the document <paramref name="units"/> make, outlined whole.</summary>
    private static void Alike(ContentOutline outline, List<string> units, string when)
    {
        var document = string.Concat(units);
        var tree = MarkdownParser.Worked(document);
        var whole = ContentOutline.Of(tree);

        Assert.AreEqual(units.Count, outline.Count, $"one unit for each, {when}");
        Assert.AreEqual(document.Length, outline.Width, $"standing for every character, {when}");
        CollectionAssert.AreEqual(Names(whole), Names(outline), $"every heading answers to the same name, {when}");

        var kept = Entries(outline).ToList();
        var read = Entries(whole).ToList();

        for (var at = 0; at < kept.Count; at++)
            Assert.AreEqual(whole.Section(read[at].Unit, read[at].Entry), outline.Section(kept[at].Unit, kept[at].Entry),
                            $"entry {at} heads the same section, {when}");

        foreach (var block in tree.Children.Where(part => !part.IsDerived && part.Role != Roles.Trivia))
            CollectionAssert.AreEqual(whole.Holding(block.Offset!.Value).ToList(), outline.Holding(block.Offset!.Value).ToList(),
                                      $"the block at {block.Offset} stands in the same sections, {when}");
    }

    private static IEnumerable<(int Unit, int Entry)> Entries(ContentOutline outline)
    {
        for (var unit = 0; unit < outline.Count; unit++)
            for (var entry = 0; entry < outline[unit].Unit.Entries.Count; entry++)
                yield return (unit, entry);
    }

    private static string[] Names(ContentOutline outline) => [.. Entries(outline).Select(at => outline.Name(at.Unit, at.Entry))];
}
