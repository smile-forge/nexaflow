using System.Linq;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Streaming;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
public class TransformSerializerTests
{
    [TestMethod]
    public void EmptyChain_RoundTrips()
    {
        var json = TransformSerializer.Serialize(System.Array.Empty<IColumnTransform>());
        var back = TransformSerializer.Deserialize(json);
        Assert.AreEqual(0, back.Count);
    }

    [TestMethod]
    public void MergeWithPrevious_RoundTrips()
    {
        var original = new IColumnTransform[] { new MergeWithPreviousTransform(3, ",") };
        var json = TransformSerializer.Serialize(original);
        var back = TransformSerializer.Deserialize(json);
        Assert.AreEqual(1, back.Count);
        var m = (MergeWithPreviousTransform)back[0];
        Assert.AreEqual(3,   m.MergeIndex);
        Assert.AreEqual(",", m.MergeJoinWith);
    }

    [TestMethod]
    public void SplitBy_RoundTrips_PreservesArity()
    {
        var original = new IColumnTransform[] { new SplitByTransform(2, ' ', 5) };
        var json = TransformSerializer.Serialize(original);
        var back = TransformSerializer.Deserialize(json);
        var s = (SplitByTransform)back[0];
        Assert.AreEqual(2,   s.SplitIndex);
        Assert.AreEqual(' ', s.SplitSeparator);
        Assert.AreEqual(5,   s.SplitArity);
    }

    [TestMethod]
    public void EvaluateAs_RoundTrips()
    {
        var original = new IColumnTransform[] { new EvaluateAsTransform(0, CsvDataType.DateTime) };
        var json = TransformSerializer.Serialize(original);
        var back = TransformSerializer.Deserialize(json);
        var e = (EvaluateAsTransform)back[0];
        Assert.AreEqual(0,                  e.EvalIndex);
        Assert.AreEqual(CsvDataType.DateTime, e.EvalTarget);
    }

    [TestMethod]
    public void RenameColumn_RoundTrips()
    {
        var original = new IColumnTransform[] { new RenameColumnTransform(1, "Customer Name") };
        var json = TransformSerializer.Serialize(original);
        var back = TransformSerializer.Deserialize(json);
        var r = (RenameColumnTransform)back[0];
        Assert.AreEqual(1,               r.RenameIndex);
        Assert.AreEqual("Customer Name", r.RenameTo);
    }

    [TestMethod]
    public void FullChain_RoundTrips_PreservingOrder()
    {
        IColumnTransform[] original = new IColumnTransform[]
        {
            new RenameColumnTransform(0, "Id"),
            new SplitByTransform(4, ' ', 2),
            new EvaluateAsTransform(5, CsvDataType.Time),
            new MergeWithPreviousTransform(2, ";"),
        };
        var json = TransformSerializer.Serialize(original);
        var back = TransformSerializer.Deserialize(json);
        Assert.AreEqual(4, back.Count);
        Assert.IsInstanceOfType(back[0], typeof(RenameColumnTransform));
        Assert.IsInstanceOfType(back[1], typeof(SplitByTransform));
        Assert.IsInstanceOfType(back[2], typeof(EvaluateAsTransform));
        Assert.IsInstanceOfType(back[3], typeof(MergeWithPreviousTransform));
    }

    [TestMethod]
    public void Malformed_DeserializeReturnsEmpty_NoThrow()
    {
        var back = TransformSerializer.Deserialize("not valid json");
        Assert.AreEqual(0, back.Count);
    }

    [TestMethod]
    public void DataShape_ReplayedTransforms_ProduceSameEffectiveColumns()
    {
        // Build a chain on one DataShape, serialize, replay on a fresh DataShape,
        // assert the effective columns match.
        var raw = new[] { "First", "Last", "Phrase", "Age" };
        var rawShape = new CsvShape
        {
            Separator   = ',', Quote = null, HasHeader = true,
            ColumnTypes = new[] { CsvDataType.String, CsvDataType.String, CsvDataType.String, CsvDataType.Integer },
            FieldCount  = 4,   Score = 1f, Label = "test", Description = "test",
        };

        var a = new DataShape(rawShape, raw);
        a.AddTransform(new RenameColumnTransform(0, "Id"));
        a.AddTransform(new SplitByTransform(2, ' ', 3));

        var json = TransformSerializer.Serialize(a.Transforms);
        var b = new DataShape(rawShape, raw);
        foreach (var t in TransformSerializer.Deserialize(json)) b.AddTransform(t);

        CollectionAssert.AreEqual(
            a.EffectiveColumns.Select(c => c.Header).ToArray(),
            b.EffectiveColumns.Select(c => c.Header).ToArray());
    }
}
