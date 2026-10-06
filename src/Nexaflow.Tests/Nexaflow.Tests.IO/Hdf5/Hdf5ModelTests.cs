using Nexaflow.IO.Hdf5;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Hdf5;

/// <summary>The backend-neutral model: path arithmetic, shape notation and what makes a type fixed-size.</summary>
[TestClass]
[CoversNode("hdf5-reader")]
public sealed class Hdf5ModelTests
{
    [TestMethod]
    [DataRow(null, "/")]
    [DataRow("", "/")]
    [DataRow("/", "/")]
    [DataRow("a", "/a")]
    [DataRow("a//b/", "/a/b")]
    [DataRow("/a/b", "/a/b")]
    public void Normalize_GivesOneLeadingSlashAndNoTrailingOne(string? input, string expected) =>
        Assert.AreEqual(expected, Hdf5Path.Normalize(input));

    [TestMethod]
    public void Combine_Parent_And_NameOf_AreInverse()
    {
        Assert.AreEqual("/a", Hdf5Path.Combine("/", "a"));
        Assert.AreEqual("/a/b", Hdf5Path.Combine("/a", "b"));
        Assert.AreEqual("/a", Hdf5Path.Parent("/a/b"));
        Assert.AreEqual("/", Hdf5Path.Parent("/a"));
        Assert.AreEqual("b", Hdf5Path.NameOf("/a/b"));
        Assert.AreEqual("/", Hdf5Path.NameOf("/"));
    }

    [TestMethod]
    public void FormatDims_PrintsUnlimitedAsInfinity() =>
        Assert.AreEqual("500 × ∞", Hdf5Dataspace.FormatDims([500UL, null]));

    [TestMethod]
    public void IsFixedSize_HoldsForFlatTypes_NotForVariableLengthOnesOrCompoundsHoldingThem()
    {
        var f8   = new Hdf5Type(Hdf5TypeClass.Float, 8);
        var text = new Hdf5Type(Hdf5TypeClass.VariableLength, 16) { Base = new(Hdf5TypeClass.Integer, 1) };
        Assert.IsTrue(f8.IsFixedSize);
        Assert.IsFalse(text.IsFixedSize);
        Assert.IsTrue(text.IsVariableLengthString);
        Assert.IsTrue(new Hdf5Type(Hdf5TypeClass.Compound, 8) { Fields = [new("x", 0, f8)] }.IsFixedSize);
        Assert.IsFalse(new Hdf5Type(Hdf5TypeClass.Compound, 24) { Fields = [new("x", 0, f8), new("s", 8, text)] }.IsFixedSize);
        Assert.AreEqual("float32[3]", new Hdf5Type(Hdf5TypeClass.Array, 12) { Base = new(Hdf5TypeClass.Float, 4) }.DisplayName);
    }
}
