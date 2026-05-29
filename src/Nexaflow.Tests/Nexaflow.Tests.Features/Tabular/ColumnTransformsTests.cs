using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Streaming;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
public class ColumnTransformsTests
{
    [TestMethod]
    public void Merge_TwoColumns_ConcatsAndDropsSource()
    {
        var t = new MergeColumnsTransform([0, 1], " ");
        var result = t.Apply(["first", "last", "33"]);
        CollectionAssert.AreEqual(new[] { "first last", "33" }, result);
    }

    [TestMethod]
    public void Merge_NonContiguousColumns_PreservesUnmergedInPlace()
    {
        var t = new MergeColumnsTransform([0, 2], "/");
        var result = t.Apply(["a", "b", "c", "d"]);
        CollectionAssert.AreEqual(new[] { "a/c", "b", "d" }, result);
    }

    [TestMethod]
    public void Merge_SingleIndex_NoOp()
    {
        var t = new MergeColumnsTransform([1], " ");
        var result = t.Apply(["a", "b", "c"]);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, result);
    }

    [TestMethod]
    public void Split_ArityN_ProducesExactlyNCells_WithOverflowInLast()
    {
        // arity 3 over "a-b-c-d-e" → ["a", "b", "c-d-e"]; the final cell holds the rest.
        var t = new SplitByTransform(1, '-', 3);
        var result = t.Apply(["x", "a-b-c-d-e", "y"]);
        CollectionAssert.AreEqual(new[] { "x", "a", "b", "c-d-e", "y" }, result);
    }

    [TestMethod]
    public void Split_FiveWaySplit_FivePartsExactly()
    {
        // The user's example: "One two three four five" split on space arity 5 → 5 cells.
        var t = new SplitByTransform(0, ' ', 5);
        var result = t.Apply(["One two three four five", "next"]);
        CollectionAssert.AreEqual(new[] { "One", "two", "three", "four", "five", "next" }, result);
    }

    [TestMethod]
    public void Split_DateTimeBySpace_ArityTwo_DateAndTime()
    {
        var t = new SplitByTransform(0, ' ', 2);
        var result = t.Apply(["21.05.2004 15:32:18", "active"]);
        CollectionAssert.AreEqual(new[] { "21.05.2004", "15:32:18", "active" }, result);
    }

    [TestMethod]
    public void Split_UnderArityRow_PadsTrailingCellsWithEmpty()
    {
        // Body shorter than arity → trailing cells are "" so headers stay aligned.
        var t = new SplitByTransform(0, ' ', 5);
        var result = t.Apply(["only one", "next"]);
        CollectionAssert.AreEqual(new[] { "only", "one", "", "", "", "next" }, result);
    }

    [TestMethod]
    public void Split_ApplyToColumns_FirstKeepsOriginal_RestNumbered()
    {
        var t = new SplitByTransform(1, ' ', 3);
        var cols = t.ApplyToColumns([
            new ColumnDescriptor("First",  CsvDataType.String),
            new ColumnDescriptor("Phrase", CsvDataType.String),
            new ColumnDescriptor("Last",   CsvDataType.String)]);
        Assert.AreEqual(5,            cols.Length);
        Assert.AreEqual("First",      cols[0].Header);
        Assert.AreEqual("Phrase",     cols[1].Header);   // original kept on first split part
        Assert.AreEqual("Phrase 2",   cols[2].Header);
        Assert.AreEqual("Phrase 3",   cols[3].Header);
        Assert.AreEqual("Last",       cols[4].Header);
    }

    [TestMethod]
    public void Rename_SetsHeader_AndMarksAsExplicit()
    {
        var t = new RenameColumnTransform(1, "Customer Name");
        var cols = t.ApplyToColumns([
            new ColumnDescriptor("a", CsvDataType.String),
            new ColumnDescriptor("b", CsvDataType.String)]);
        Assert.AreEqual("Customer Name", cols[1].Header);
        Assert.IsTrue(cols[1].IsHeaderExplicit);
        Assert.IsFalse(cols[0].IsHeaderExplicit);
    }

    [TestMethod]
    public void EvaluateAs_DoesNotAlterCells_ChangesColumnTypeOnly()
    {
        var t = new EvaluateAsTransform(0, CsvDataType.Decimal);
        var result = t.Apply(["1", "abc"]);
        CollectionAssert.AreEqual(new[] { "1", "abc" }, result);

        var cols = t.ApplyToColumns([
            new ColumnDescriptor("Col 1", CsvDataType.String),
            new ColumnDescriptor("Col 2", CsvDataType.String)]);
        Assert.AreEqual(CsvDataType.Decimal, cols[0].Type);
        Assert.AreEqual(CsvDataType.String,  cols[1].Type);
    }

    [TestMethod]
    public void MergeWithPrevious_JoinsWithDetectedSeparatorAsData()
    {
        // Simulates the user marking the boundary between cols 1 and 2 as a false split,
        // so the original separator (',') belongs to the data in col 1.
        var t      = new MergeWithPreviousTransform(2, ",");
        var result = t.Apply(["first", "Smith", "Jr.", "30"]);
        CollectionAssert.AreEqual(new[] { "first", "Smith,Jr.", "30" }, result);
    }

    [TestMethod]
    public void MergeWithPrevious_AtFirstColumn_NoOp()
    {
        var t      = new MergeWithPreviousTransform(0, ",");
        var result = t.Apply(["a", "b", "c"]);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, result);
    }

    [TestMethod]
    public void MergeWithPrevious_ApplyToColumns_ShrinksColumnCount()
    {
        var t = new MergeWithPreviousTransform(2, ",");
        var cols = t.ApplyToColumns([
            new ColumnDescriptor("First", CsvDataType.String),
            new ColumnDescriptor("Last",  CsvDataType.String),
            new ColumnDescriptor("Sfx",   CsvDataType.String),
            new ColumnDescriptor("Age",   CsvDataType.Integer)]);
        Assert.AreEqual(3,           cols.Length);
        Assert.AreEqual("Last,Sfx",  cols[1].Header);
        Assert.AreEqual("Age",       cols[2].Header);
    }

    [TestMethod]
    public void Merge_ApplyToColumns_BuildsCombinedHeader()
    {
        var t = new MergeColumnsTransform([0, 1], " ");
        var cols = t.ApplyToColumns([
            new ColumnDescriptor("First", CsvDataType.String),
            new ColumnDescriptor("Last",  CsvDataType.String),
            new ColumnDescriptor("Age",   CsvDataType.Integer)]);
        Assert.AreEqual(2,                cols.Length);
        Assert.AreEqual("First Last",     cols[0].Header);
        Assert.AreEqual("Age",            cols[1].Header);
    }
}
