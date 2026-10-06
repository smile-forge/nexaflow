using System.IO;
using Nexaflow.Features.Hdf5;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Hdf5;

/// <summary>
/// Splitting a path that continues into an HDF5 file into the file and the object, so a tab opened on such a path
/// shows the right file at the right place.
/// </summary>
[TestClass]
[CoversNode("hdf5-open-actions")]
public sealed class Hdf5LocationTests
{
    private static readonly string Sample = TestSampleData.Path("hdf5", "experiment.h5");

    [TestMethod]
    public void TheFileItself_HasNoObject() =>
        Assert.AreEqual((Sample, (string?)null), Hdf5Location.Split(Sample, VirtualFileSystem.Instance));

    [TestMethod]
    public void APathIntoTheFile_SplitsAtTheFile()
    {
        var (file, obj) = Hdf5Location.Split(Path.Combine(Sample, "measurements", "temperature.npy"), VirtualFileSystem.Instance);
        Assert.AreEqual(Sample, file);
        Assert.AreEqual("/measurements/temperature.npy", obj);
    }

    [TestMethod]
    public void AGroupNamedLikeAFile_StaysInsideItsFile()
    {
        var (file, obj) = Hdf5Location.Split(Path.Combine(Sample, "inner.h5", "x"), VirtualFileSystem.Instance);
        Assert.AreEqual(Sample, file);
        Assert.AreEqual("/inner.h5/x", obj);
    }

    [TestMethod]
    public void APathThatNamesNoFile_IsLeftWhole() =>
        Assert.AreEqual((@"C:\nowhere\a.txt", (string?)null), Hdf5Location.Split(@"C:\nowhere\a.txt", VirtualFileSystem.Instance));

    [TestMethod]
    [DataRow("run.h5", true)]
    [DataRow("RUN.HDF5", true)]
    [DataRow("swath.he5", true)]
    [DataRow("notes.h5.txt", false)]
    public void Extensions_AreRecognisedWhateverTheirCase(string name, bool expected) =>
        Assert.AreEqual(expected, Hdf5Location.HasHdf5Extension(name));
}
