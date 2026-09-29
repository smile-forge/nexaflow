using System.Globalization;
using Nexaflow.IO.Hdf5;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Hdf5;

/// <summary>
/// The reader against a file libhdf5 wrote: every object kind, every datatype family the fixture holds, and
/// windowed reads in one, two and three dimensions. Values are asserted as the viewer shows them.
/// </summary>
[TestClass]
[CoversNode("hdf5-reader")]
public sealed class PureHdfSourceTests
{
    private static IHdf5Source Open(string name = "experiment.h5") => Hdf5Source.Open(TestSampleData.Path("hdf5", name));

    [TestMethod]
    public void Root_ListsGroupsDatasetsLinksAndNamedTypes_ByKind()
    {
        using var src = Open();
        var listing = src.ListChildren("/", 0, int.MaxValue);

        Assert.IsNull(listing.Problem);
        Assert.IsFalse(listing.HasMore);
        var kinds = listing.Items.ToDictionary(o => o.Name, o => o.Kind);
        Assert.AreEqual(Hdf5ObjectKind.Group, kinds["calibration"]);
        Assert.AreEqual(Hdf5ObjectKind.Group, kinds["measurements"]);
        Assert.AreEqual(Hdf5ObjectKind.Group, kinds["metadata"]);
        Assert.AreEqual(Hdf5ObjectKind.Dataset, kinds["latest"], "a soft link to a dataset is followed");
        Assert.AreEqual(Hdf5ObjectKind.UnresolvedLink, kinds["dangling"]);
        Assert.AreEqual(Hdf5ObjectKind.UnresolvedLink, kinds["elsewhere"]);
        Assert.AreEqual(Hdf5ObjectKind.NamedDatatype, kinds["typedef"]);
        Assert.AreEqual("/measurements", listing.Items.Single(o => o.Name == "measurements").Path);
        Assert.IsFalse(string.IsNullOrEmpty(listing.Items.Single(o => o.Name == "dangling").Problem));
    }

    [TestMethod]
    public void ListChildren_Pages_WithHasMoreUntilTheLastPage()
    {
        using var src = Open();
        var all   = src.ListChildren("/metadata", 0, int.MaxValue).Items.Select(o => o.Name).ToList();
        var first = src.ListChildren("/metadata", 0, 3);
        var rest  = src.ListChildren("/metadata", 3, int.MaxValue);

        Assert.AreEqual(3, first.Items.Count);
        Assert.IsTrue(first.HasMore);
        Assert.IsFalse(rest.HasMore);
        CollectionAssert.AreEqual(all, first.Items.Concat(rest.Items).Select(o => o.Name).ToList());
    }

    [TestMethod]
    public void ListChildren_OnADataset_Throws()
    {
        using var src = Open();
        Assert.ThrowsExactly<Hdf5Exception>(() => src.ListChildren("/calibration/gain", 0, 10));
    }

    [TestMethod]
    public void ListChildren_KeepsWhatItReadBeforeAMemberItCannotDecode()
    {
        using var src = Open("latest-format.h5");
        var listing = src.ListChildren("/", 0, int.MaxValue);

        Assert.IsNotNull(listing.Problem, "the enum in the newest format is not decodable, and says so");
        Assert.IsFalse(listing.Items.Any(o => o.Name == "state"));
    }

    [TestMethod]
    public void Stat_DescribesAChunkedResizableDataset()
    {
        using var src = Open();
        var o = src.Stat("/measurements/temperature/");

        Assert.IsNotNull(o);
        Assert.AreEqual("/measurements/temperature", o.Path);
        Assert.AreEqual("temperature", o.Name);
        var ds = o.Dataset!;
        Assert.AreEqual("float64", ds.Type.DisplayName);
        CollectionAssert.AreEqual(new ulong[] { 500 }, ds.Space.Shape.ToArray());
        Assert.IsNull(ds.Space.MaxShape[0], "maxshape None is unlimited");
        Assert.IsTrue(ds.Space.IsResizable);
        Assert.AreEqual(Hdf5Layout.Chunked, ds.Layout);
        CollectionAssert.AreEqual(new ulong[] { 100 }, ds.ChunkShape.ToArray());
    }

    [TestMethod]
    public void Stat_ReportsLayoutsAndSpacesAcrossTheFile()
    {
        using var src = Open();
        Assert.AreEqual(Hdf5Layout.Contiguous, src.Stat("/calibration/gain")!.Dataset!.Layout);
        Assert.AreEqual(0, src.Stat("/calibration/gain")!.Dataset!.ChunkShape.Count);
        Assert.AreEqual(Hdf5SpaceKind.Scalar, src.Stat("/metadata/scalar")!.Dataset!.Space.Kind);
        Assert.AreEqual(0UL, src.Stat("/metadata/empty")!.Dataset!.Space.ElementCount);
        CollectionAssert.AreEqual(new ulong[] { 4, 16, 16 }, src.Stat("/measurements/frames")!.Dataset!.Space.Shape.ToArray());
    }

    [TestMethod]
    public void Stat_OfNothing_IsNull_AndTheRootIsAGroup()
    {
        using var src = Open();
        Assert.IsNull(src.Stat("/no/such/thing"));
        Assert.AreEqual(Hdf5ObjectKind.Group, src.Stat("/")!.Kind);
    }

    [TestMethod]
    public void Types_AreNamedInLanguageNeutralNotation()
    {
        using var src = Open();
        string TypeOf(string path) => src.Stat(path)!.Dataset!.Type.DisplayName;

        Assert.AreEqual("float32", TypeOf("/calibration/gain"));
        Assert.AreEqual("uint16", TypeOf("/measurements/frames"));
        Assert.AreEqual("int32", TypeOf("/measurements/counter"));
        Assert.AreEqual("{t: float64, ch: int16, energy: float32}", TypeOf("/measurements/events"));
        Assert.AreEqual("string[8]", TypeOf("/metadata/labels"));
        Assert.AreEqual("string", TypeOf("/metadata/notes"));
        Assert.AreEqual("enum<int8>{IDLE=0, RUN=1, FAULT=2}", TypeOf("/metadata/state"));
        Assert.AreEqual("enum<int8>{FALSE=0, TRUE=1}", TypeOf("/metadata/flags"));
        Assert.AreEqual("opaque[4]", TypeOf("/metadata/blob"));
    }

    [TestMethod]
    public void Attributes_AreReadWithTheirValues()
    {
        using var src = Open();
        var attrs = src.Attributes("/measurements/temperature").ToDictionary(a => a.Name);

        Assert.AreEqual("K", attrs["units"].Value);
        Assert.AreEqual("PT100", attrs["sensor"].Value);
        Assert.AreEqual("1000", attrs["sample_rate"].Value);
        Assert.AreEqual("[1, 0.5, 0.25]", attrs["calibration"].Value);
        Assert.AreEqual(Hdf5SpaceKind.Simple, attrs["calibration"].Space!.Kind);
        Assert.IsTrue(attrs.Values.All(a => a.Problem is null && !a.IsTruncated));

        var root = src.Attributes("/").ToDictionary(a => a.Name);
        Assert.AreEqual("42", root["experiment"].Value);
        Assert.AreEqual("int32", root["experiment"].Type!.DisplayName);
    }

    [TestMethod]
    public void Attributes_OfAnUnresolvedLink_AreNone()
    {
        using var src = Open();
        Assert.AreEqual(0, src.Attributes("/dangling").Count);
    }

    [TestMethod]
    public void ReadBlock_ReadsOnlyTheWindow_In1D()
    {
        using var src = Open();
        var block = src.ReadBlock("/measurements/counter", new([1500], [3]));

        Assert.AreEqual(3, block.Count);
        CollectionAssert.AreEqual(new[] { "1500", "1501", "1502" }, Enumerable.Range(0, 3).Select(i => block.Format(i)).ToArray());
        var t = double.Parse(src.ReadBlock("/measurements/temperature", new([10], [1])).Format(0), CultureInfo.InvariantCulture);
        Assert.AreEqual(293.15 + 2.1 * Math.Sin(10 / 30.0), t, 1e-9);
    }

    [TestMethod]
    public void ReadBlock_ReadsARectangle_In2DAnd3D_InCOrder()
    {
        using var src = Open();
        var m = src.ReadBlock("/measurements/matrix", new([10, 5], [2, 3]));
        CollectionAssert.AreEqual(new[] { "3005", "3006", "3007", "3305", "3306", "3307" },
            Enumerable.Range(0, 6).Select(i => m.Format(i)).ToArray());

        var f = src.ReadBlock("/measurements/frames", new([2, 0, 3], [1, 16, 1]));
        Assert.AreEqual(16, f.Count);
        Assert.AreEqual(((2 * 256 + 0 * 16 + 3) % 1000).ToString(CultureInfo.InvariantCulture), f.Format(0));
        Assert.AreEqual(((2 * 256 + 15 * 16 + 3) % 1000).ToString(CultureInfo.InvariantCulture), f.Format(15));
    }

    [TestMethod]
    public void ReadBlock_Compound_FormatsEachFieldAndTheWholeRecord()
    {
        using var src = Open();
        var block = src.ReadBlock("/measurements/events", new([2], [2]));

        CollectionAssert.AreEqual(new[] { "t", "ch", "energy" }, block.Fields.ToArray());
        Assert.AreEqual("0.002", block.Format(0, 0));
        Assert.AreEqual("2", block.Format(0, 1));
        Assert.AreEqual("513", block.Format(0, 2));
        Assert.AreEqual("{t: 0.003, ch: 3, energy: 514}", block.Format(1));
    }

    [TestMethod]
    public void ReadBlock_StringsEnumsOpaqueAndScalars_FormatAsTheyRead()
    {
        using var src = Open();
        string[] All(string path) { var b = src.ReadBlock(path, Hdf5Selection.All(src.Stat(path)!.Dataset!.Space)); return [.. Enumerable.Range(0, (int)b.Count).Select(i => b.Format(i))]; }

        CollectionAssert.AreEqual(new[] { "alpha", "beta", "gamma" }, All("/metadata/labels"));
        CollectionAssert.AreEqual(new[] { "first", "second line\nwrapped", "third" }, All("/metadata/notes"));
        CollectionAssert.AreEqual(new[] { "IDLE", "RUN", "RUN", "FAULT", "IDLE", "RUN" }, All("/metadata/state"));
        CollectionAssert.AreEqual(new[] { "TRUE", "FALSE", "TRUE" }, All("/metadata/flags"));
        CollectionAssert.AreEqual(new[] { "0x01020304" }, All("/metadata/blob"));
        CollectionAssert.AreEqual(new[] { "3.25" }, All("/metadata/scalar"));
        CollectionAssert.AreEqual(new[] { "{\"bins\": 4096}" }, All("/metadata/config"));
        Assert.AreEqual(0, All("/metadata/empty").Length);
    }

    [TestMethod]
    public void ReadBlock_RefusesASelectionOutsideTheDataset_AndAGroup()
    {
        using var src = Open();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => src.ReadBlock("/measurements/counter", new([1999], [2])));
        Assert.ThrowsExactly<ArgumentException>(() => src.ReadBlock("/measurements/matrix", new([0], [1])));
        Assert.ThrowsExactly<Hdf5Exception>(() => src.ReadBlock("/measurements", Hdf5Selection.Scalar));
    }

    [TestMethod]
    public void Open_FromAStream_ReadsTheSameFile()
    {
        using var stream = new MemoryStream(File.ReadAllBytes(TestSampleData.Path("hdf5", "experiment.h5")));
        using var src = Hdf5Source.Open(stream);
        Assert.AreEqual("float32", src.Stat("/calibration/gain")!.Dataset!.Type.DisplayName);
    }

    [TestMethod]
    public void Open_OnSomethingThatIsNotHdf5_ThrowsTheReadersReason()
    {
        using var stream = new MemoryStream("not an hdf5 file"u8.ToArray());
        Assert.ThrowsExactly<Hdf5Exception>(() => Hdf5Source.Open(stream));
    }

    [TestMethod]
    public void AnOpenSource_NeverStopsTheFileBeingReplaced()
    {
        var copy = Path.Combine(Path.GetTempPath(), $"nexaflow-hdf5-{Guid.NewGuid():N}.h5");
        File.Copy(TestSampleData.Path("hdf5", "experiment.h5"), copy);
        using var src = Hdf5Source.Open(copy);
        File.Delete(copy);
        Assert.IsFalse(File.Exists(copy));
    }
}
