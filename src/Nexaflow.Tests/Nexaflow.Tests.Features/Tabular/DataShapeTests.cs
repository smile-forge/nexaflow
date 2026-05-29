using System.Linq;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Streaming;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
public class DataShapeTests
{
    private static CsvShape MakeShape(bool hasHeader, int fields, CsvDataType[]? types = null) => new()
    {
        Separator   = ',',
        Quote       = null,
        HasHeader   = hasHeader,
        ColumnTypes = types ?? Enumerable.Repeat(CsvDataType.String, fields).ToArray(),
        FieldCount  = fields,
        Score       = 1f,
        Label       = "test",
        Description = "test",
    };

    [TestMethod]
    public void NoHeader_EffectiveColumns_NumberedColumnN()
    {
        var data = new DataShape(MakeShape(hasHeader: false, fields: 3), rawHeaderCells: null);
        var cols = data.EffectiveColumns;
        CollectionAssert.AreEqual(
            new[] { "Column 1", "Column 2", "Column 3" },
            cols.Select(c => c.Header).ToArray());
    }

    [TestMethod]
    public void NoHeader_AfterMergeWithPrevious_RenumbersSequentially()
    {
        var data = new DataShape(MakeShape(hasHeader: false, fields: 4), rawHeaderCells: null);
        data.AddTransform(new MergeWithPreviousTransform(2, ","));
        var cols = data.EffectiveColumns;
        // 3 columns, sequentially renumbered.
        CollectionAssert.AreEqual(
            new[] { "Column 1", "Column 2", "Column 3" },
            cols.Select(c => c.Header).ToArray());
    }

    [TestMethod]
    public void HasHeader_AfterMergeWithPrevious_ReinterpretsHeaderRow()
    {
        // Raw header tokens as the tokenizer produces them (untrimmed) for "First, Last, Suffix, Age".
        var raw = new[] { "First", " Last", " Suffix", " Age" };
        var data = new DataShape(MakeShape(hasHeader: true, fields: 4), rawHeaderCells: raw);

        // Merge col 2 (Suffix) with col 1 (Last) — treat the separator between them as data.
        data.AddTransform(new MergeWithPreviousTransform(2, ","));
        var cols = data.EffectiveColumns;
        // Effective: "First", "Last, Suffix" (whitespace from raw preserved through the rejoin), "Age".
        Assert.AreEqual(3,             cols.Length);
        Assert.AreEqual("First",       cols[0].Header);
        Assert.AreEqual("Last, Suffix", cols[1].Header);
        Assert.AreEqual("Age",         cols[2].Header);
    }

    [TestMethod]
    public void HydrateRow_AppliesTransformChainInOrder()
    {
        var data = new DataShape(MakeShape(hasHeader: false, fields: 4), null);
        data.AddTransform(new MergeWithPreviousTransform(2, ","));        // 4 → 3 cols
        data.AddTransform(new EvaluateAsTransform(0, CsvDataType.Integer)); // no-op on cells

        var cells = data.HydrateRow(["1", "a", "b", "9"]);
        CollectionAssert.AreEqual(new[] { "1", "a,b", "9" }, cells);
    }

    [TestMethod]
    public void EffectiveColumns_AfterEvaluateAs_ChangesType()
    {
        var data = new DataShape(MakeShape(hasHeader: false, fields: 2, types:
            [CsvDataType.String, CsvDataType.String]), null);
        data.AddTransform(new EvaluateAsTransform(0, CsvDataType.Integer));
        var cols = data.EffectiveColumns;
        Assert.AreEqual(CsvDataType.Integer, cols[0].Type);
        Assert.AreEqual(CsvDataType.String,  cols[1].Type);
    }

    [TestMethod]
    public void HasHeader_SplitByOnHeaderWithoutSeparator_KeepsApplyToColumnsDefault()
    {
        // Header "StartDateTime" has no space, so the arity-2 split pads the right half
        // with "". DataShape must keep the SplitBy ApplyToColumns default ("StartDateTime 2")
        // for the new column instead of writing the empty header in.
        var raw = new[] { "State", "StartDateTime", "Price" };
        var data = new DataShape(MakeShape(hasHeader: true, fields: 3), rawHeaderCells: raw);
        data.AddTransform(new SplitByTransform(1, ' ', 2));
        var cols = data.EffectiveColumns;
        Assert.AreEqual(4,                 cols.Length);
        Assert.AreEqual("State",           cols[0].Header);
        Assert.AreEqual("StartDateTime",   cols[1].Header);
        Assert.AreEqual("StartDateTime 2", cols[2].Header);
        Assert.AreEqual("Price",           cols[3].Header);
    }

    [TestMethod]
    public void HasHeader_RenameColumn_SurvivesRawHeaderReTokenize()
    {
        // Even though the file has a real header line, an explicit rename should win.
        var raw = new[] { "First", "Last", "Age" };
        var data = new DataShape(MakeShape(hasHeader: true, fields: 3), rawHeaderCells: raw);
        data.AddTransform(new RenameColumnTransform(1, "Family"));
        var cols = data.EffectiveColumns;
        CollectionAssert.AreEqual(
            new[] { "First", "Family", "Age" },
            cols.Select(c => c.Header).ToArray());
    }

    [TestMethod]
    public void NoHeader_RenameColumn_NotClobberedByRenumber()
    {
        var data = new DataShape(MakeShape(hasHeader: false, fields: 3), rawHeaderCells: null);
        data.AddTransform(new RenameColumnTransform(1, "Special"));
        var cols = data.EffectiveColumns;
        CollectionAssert.AreEqual(
            new[] { "Column 1", "Special", "Column 3" },
            cols.Select(c => c.Header).ToArray());
    }

    [TestMethod]
    public void Split_PropagatesLeftSeparator_OntoNewColumns()
    {
        var raw = new[] { "Phrase", "Age" };
        var data = new DataShape(MakeShape(hasHeader: true, fields: 2), rawHeaderCells: raw);
        data.AddTransform(new SplitByTransform(0, ' ', 3));
        var cols = data.EffectiveColumns;

        // First split column inherits the file-separator boundary; parts 2 and 3 know
        // they were created by a space-split — so a later MergeWithPrevious rejoins on space.
        Assert.AreEqual(",",  cols[0].LeftSeparator);
        Assert.AreEqual(" ",  cols[1].LeftSeparator);
        Assert.AreEqual(" ",  cols[2].LeftSeparator);
        Assert.AreEqual(",",  cols[3].LeftSeparator); // the original "Age" column
    }

    [TestMethod]
    public void SplitThenMergeBack_RestoresOriginalSeparator()
    {
        // Pin the user's reported bug: split a column on space, then merge with previous
        // — the merge should rejoin using the split character, not the file separator.
        // Going through SplitByTransform.Apply then MergeWithPreviousTransform.Apply
        // (with the right separator) on a body row should produce the input.
        var split = new SplitByTransform(0, ' ', 3);
        var afterSplit = split.Apply(["One two three", "next"]);
        CollectionAssert.AreEqual(new[] { "One", "two", "three", "next" }, afterSplit);

        // Reading LeftSeparator off the split-produced columns tells us to rejoin on " ".
        var raw = new[] { "Phrase", "next" };
        var data = new DataShape(MakeShape(hasHeader: true, fields: 2), rawHeaderCells: raw);
        data.AddTransform(split);
        var cols = data.EffectiveColumns;
        Assert.AreEqual(" ", cols[1].LeftSeparator);
        Assert.AreEqual(" ", cols[2].LeftSeparator);

        // Two merges (col 2 then col 1) recreate the original by re-joining on space.
        // The order matters: merges are applied to the chain, and each merge takes the
        // CURRENT column index of its target.
        data.AddTransform(new MergeWithPreviousTransform(2, " "));
        data.AddTransform(new MergeWithPreviousTransform(1, " "));
        var rejoined = data.HydrateRow(["One two three", "next"]);
        CollectionAssert.AreEqual(new[] { "One two three", "next" }, rejoined);
    }

    [TestMethod]
    public void ClearTransforms_RestoresRawShape()
    {
        var data = new DataShape(MakeShape(hasHeader: false, fields: 3), null);
        data.AddTransform(new MergeWithPreviousTransform(2, ","));
        Assert.AreEqual(2, data.EffectiveColumns.Length);

        data.ClearTransforms();
        Assert.AreEqual(3, data.EffectiveColumns.Length);
    }
}
