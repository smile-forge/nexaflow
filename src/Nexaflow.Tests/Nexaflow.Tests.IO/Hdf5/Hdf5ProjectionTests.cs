using System.Buffers.Binary;
using System.Text;
using Nexaflow.IO.Hdf5;
using Nexaflow.IO.Hdf5.Projection;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Hdf5;

/// <summary>
/// Datasets as files: a fixed-size dataset is a well-formed NumPy .npy whose length is known before a byte is
/// read, and variable-length data is escaped text, one element per line.
/// </summary>
[TestClass]
[CoversNode("hdf5-projection")]
public sealed class Hdf5ProjectionTests
{
    private static IHdf5Source Open() => Hdf5Source.Open(TestSampleData.Path("hdf5", "experiment.h5"));

    private static byte[] Project(IHdf5Source src, string path)
    {
        using var stream = Hdf5Projection.Open(src, src.Stat(path)!);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static (string Dict, byte[] Data) SplitNpy(byte[] npy)
    {
        CollectionAssert.AreEqual(new byte[] { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 1, 0 }, npy[..8]);
        int headerLen = BinaryPrimitives.ReadUInt16LittleEndian(npy.AsSpan(8));
        Assert.AreEqual(0, (10 + headerLen) % 64, "data starts on a 64-byte boundary");
        Assert.AreEqual((byte)'\n', npy[10 + headerLen - 1]);
        return (Encoding.Latin1.GetString(npy, 10, headerLen).TrimEnd(), npy[(10 + headerLen)..]);
    }

    [TestMethod]
    [DataRow("/measurements/temperature", "'<f8'", "(500,)", 500 * 8)]
    [DataRow("/measurements/frames", "'<u2'", "(4, 16, 16)", 4 * 16 * 16 * 2)]
    [DataRow("/measurements/events", "[('t', '<f8'), ('ch', '<i2'), ('energy', '<f4')]", "(50,)", 50 * 14)]
    [DataRow("/metadata/labels", "'|S8'", "(3,)", 3 * 8)]
    [DataRow("/metadata/state", "'|i1'", "(6,)", 6)]
    [DataRow("/metadata/blob", "'|V4'", "()", 4)]
    [DataRow("/metadata/scalar", "'<f8'", "()", 8)]
    [DataRow("/metadata/empty", "'<f4'", "(0,)", 0)]
    public void Npy_HeaderNamesDtypeAndShape_AndLengthIsExact(string path, string descr, string shape, int dataBytes)
    {
        using var src = Open();
        var npy = Project(src, path);
        var (dict, data) = SplitNpy(npy);

        Assert.AreEqual($"{{'descr': {descr}, 'fortran_order': False, 'shape': {shape}, }}", dict);
        Assert.AreEqual(dataBytes, data.Length);
        Assert.AreEqual(npy.Length, Hdf5Projection.Length(src, src.Stat(path)!));
    }

    [TestMethod]
    public void Npy_Data_IsTheElementsPackedLittleEndian()
    {
        using var src = Open();
        var (_, temps) = SplitNpy(Project(src, "/measurements/temperature"));
        Assert.AreEqual(293.15 + 2.1 * Math.Sin(123 / 30.0), BinaryPrimitives.ReadDoubleLittleEndian(temps.AsSpan(123 * 8)), 1e-9);

        var (_, events) = SplitNpy(Project(src, "/measurements/events"));
        var record = events.AsSpan(7 * 14, 14);
        Assert.AreEqual(0.007, BinaryPrimitives.ReadDoubleLittleEndian(record), 1e-12);
        Assert.AreEqual((short)7, BinaryPrimitives.ReadInt16LittleEndian(record[8..]));
        Assert.AreEqual(518f, BinaryPrimitives.ReadSingleLittleEndian(record[10..]));

        var (_, labels) = SplitNpy(Project(src, "/metadata/labels"));
        Assert.AreEqual("beta", Encoding.ASCII.GetString(labels, 8, 8).TrimEnd('\0'));
    }

    [TestMethod]
    public void Text_IsOneEscapedElementPerLine_AndLengthIsExact()
    {
        using var src = Open();
        var notes = Project(src, "/metadata/notes");
        Assert.AreEqual("first\nsecond line\\nwrapped\nthird\n", Encoding.UTF8.GetString(notes));
        Assert.AreEqual(notes.Length, Hdf5Projection.Length(src, src.Stat("/metadata/notes")!));
        Assert.AreEqual("{\"bins\": 4096}\n", Encoding.UTF8.GetString(Project(src, "/metadata/config")));
    }

    [TestMethod]
    public void EntryNames_RoundTripToTheirDataset()
    {
        using var src = Open();
        Assert.AreEqual("temperature.npy", Hdf5Projection.EntryName("temperature", src.Stat("/measurements/temperature")!.Dataset!.Type));
        Assert.AreEqual("notes.txt", Hdf5Projection.EntryName("notes", src.Stat("/metadata/notes")!.Dataset!.Type));

        Assert.AreEqual("/a/b", Hdf5Projection.DatasetPathOf("/a/b.npy", out var npy));
        Assert.AreEqual(Hdf5ProjectionFormat.Npy, npy);
        Assert.AreEqual("/a/b.npy", Hdf5Projection.DatasetPathOf("/a/b.npy.txt", out var txt));
        Assert.AreEqual(Hdf5ProjectionFormat.Text, txt);
        Assert.IsNull(Hdf5Projection.DatasetPathOf("/a/b", out _));
        Assert.IsNull(Hdf5Projection.DatasetPathOf("/a/.npy", out _), "an extension alone names nothing");
    }

    [TestMethod]
    public void TextLine_EscapesTheCharactersThatWouldBreakALine() =>
        Assert.AreEqual(@"a\\b\nc\rd\te", Hdf5Projection.TextLine("a\\b\nc\rd\te"));
}
