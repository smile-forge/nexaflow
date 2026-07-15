using System.Collections.Generic;
using System.Linq;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The matching + keyboard-nav core behind the AI input's "/" quick-open. Extracted from
/// <see cref="ShellViewModel"/> precisely so it's testable without standing up the shell.
/// </summary>
[TestClass]
[CoversNode("aibar-slash-palette")]
public class SlashCommandPaletteTests
{
    private int _opened;

    private SlashCommandItem Item(string label, string category = "Page")
        => new()
        {
            Icon     = "•",
            Label    = label,
            Category = category,
            Invoke   = () => _opened++,
        };

    private IReadOnlyList<SlashCommandItem> Candidates(params string[] labels)
        => labels.Select(l => Item(l)).ToList();

    // ── Matching ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Update_FiltersToMatchesAndOpens()
    {
        var p = new SlashCommandPalette();

        p.Update("serv", Candidates("Services", "Fonts", "System Info"));

        Assert.IsTrue(p.IsOpen);
        CollectionAssert.AreEqual(new[] { "Services" }, p.Items.Select(i => i.Label).ToList());
    }

    [TestMethod]
    public void Update_NoMatches_ClosedAndEmpty()
    {
        var p = new SlashCommandPalette();

        p.Update("zzz", Candidates("Services", "Fonts"));

        Assert.IsFalse(p.IsOpen);
        Assert.AreEqual(0, p.Items.Count);
    }

    [TestMethod]
    public void Update_EmptyQuery_ListsEverything_UpToTheCap()
    {
        var p = new SlashCommandPalette();
        var many = Enumerable.Range(0, SlashCommandPalette.MaxItems + 5).Select(i => $"Page{i:00}").ToArray();

        p.Update("", Candidates(many));

        Assert.AreEqual(SlashCommandPalette.MaxItems, p.Items.Count, "a bare '/' must not dump the whole catalog");
    }

    [TestMethod]
    public void Update_RanksExactThenPrefixThenWordStartThenSubstring()
    {
        var p = new SlashCommandPalette();

        // For "info": "Info" exact(0), "Info Panel" prefix(1), "System Info" word-start(2),
        // "Reinforce" mid-word substring(3).
        p.Update("info", Candidates("Reinforce", "System Info", "Info Panel", "Info"));

        CollectionAssert.AreEqual(new[] { "Info", "Info Panel", "System Info", "Reinforce" },
            p.Items.Select(i => i.Label).ToList());
    }

    [TestMethod]
    public void Update_DedupsByLabel_FirstCandidateWins()
    {
        var p = new SlashCommandPalette();

        // A page and a ribbon item can share a label; the page (listed first) should win, once.
        var candidates = new List<SlashCommandItem> { Item("Services", "Page"), Item("Services", "Ribbon") };
        p.Update("services", candidates);

        Assert.AreEqual(1, p.Items.Count);
        Assert.AreEqual("Page", p.Items[0].Category);
    }

    // ── Selection + nav ─────────────────────────────────────────────────────

    [TestMethod]
    public void Update_SelectsFirstRow_AndHighlightsIt()
    {
        var p = new SlashCommandPalette();

        p.Update("", Candidates("Alpha", "Beta"));

        Assert.AreEqual(0, p.SelectedIndex);
        Assert.IsTrue(p.Items[0].IsHighlighted);
        Assert.IsFalse(p.Items[1].IsHighlighted);
    }

    [TestMethod]
    public void MoveDown_And_MoveUp_Wrap()
    {
        var p = new SlashCommandPalette();
        p.Update("", Candidates("Alpha", "Beta", "Gamma"));

        p.MoveDown(); Assert.AreEqual(1, p.SelectedIndex);
        p.MoveDown(); Assert.AreEqual(2, p.SelectedIndex);
        p.MoveDown(); Assert.AreEqual(0, p.SelectedIndex, "down past the end wraps to the top");

        p.MoveUp();   Assert.AreEqual(2, p.SelectedIndex, "up past the top wraps to the bottom");
    }

    [TestMethod]
    public void MovingSelection_MovesTheHighlight()
    {
        var p = new SlashCommandPalette();
        p.Update("", Candidates("Alpha", "Beta"));

        p.MoveDown();

        Assert.IsFalse(p.Items[0].IsHighlighted);
        Assert.IsTrue(p.Items[1].IsHighlighted);
    }

    [TestMethod]
    public void Selected_TracksTheIndex()
    {
        var p = new SlashCommandPalette();
        p.Update("", Candidates("Alpha", "Beta"));

        Assert.AreEqual("Alpha", p.Selected?.Label);
        p.MoveDown();
        Assert.AreEqual("Beta", p.Selected?.Label);
    }

    [TestMethod]
    public void Close_EmptiesAndResets()
    {
        var p = new SlashCommandPalette();
        p.Update("", Candidates("Alpha"));

        p.Close();

        Assert.IsFalse(p.IsOpen);
        Assert.AreEqual(0, p.Items.Count);
        Assert.AreEqual(-1, p.SelectedIndex);
        Assert.IsNull(p.Selected);
    }

    // ── Rank helper ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Rank_ClassifiesMatches()
    {
        Assert.AreEqual(0, SlashCommandPalette.Rank("Fonts", "fonts"));   // exact (case-insensitive)
        Assert.AreEqual(1, SlashCommandPalette.Rank("Fonts", "fon"));     // prefix
        Assert.AreEqual(2, SlashCommandPalette.Rank("System Info", "info")); // word-start
        Assert.AreEqual(3, SlashCommandPalette.Rank("Comfort", "omf"));   // mid-word substring
        Assert.IsNull(SlashCommandPalette.Rank("Fonts", "xyz"));          // no match
    }
}
