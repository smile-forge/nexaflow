using System.IO;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Dicom.Services;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>
/// DICOM content read from inside a <c>.zip</c> (a virtual path) loads and renders the same as on-disk content —
/// the loader and renderer go through the shell's VFS, so a study delivered zipped just works.
/// </summary>
[TestClass]
[CoversNode("dicom-load")]
public class DicomVfsTests
{
    private string _tmp = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        // The loader/renderer read through the process-wide VFS, so the zip handler must be on the singleton.
        VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());
        _tmp = Path.Combine(TestTemp.Root, "nexa-dicom-vfs-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    [TestMethod]
    public async Task Load_ZipOfInstances_EnumeratesImages()
    {
        var zip = DicomTestFiles.WriteZip(_tmp, "study.zip", instanceCount: 3);

        // Opening the .zip as a DICOM container browses its entries through the VFS.
        var container = await DicomContainerLoader.LoadAsync(new DicomIo(VirtualFileSystem.Instance), [zip]);

        Assert.AreEqual(3, container.Images.Count, "all three zipped instances load as images");
        Assert.AreEqual(0, container.Reports.Count);
    }

    [TestMethod]
    public async Task Render_ImageFromInsideZip_ProducesBitmap()
    {
        var zip = DicomTestFiles.WriteZip(_tmp, "study.zip", instanceCount: 1);
        var container = await DicomContainerLoader.LoadAsync(new DicomIo(VirtualFileSystem.Instance), [zip]);
        var virtualPath = container.Images[0].FilePath!;

        Assert.IsFalse(File.Exists(virtualPath), "the instance path is virtual (inside the zip), not on disk");

        var renderer = await DicomRenderer.OpenAsync(new DicomIo(VirtualFileSystem.Instance), virtualPath);
        var bmp = renderer.Render(0);
        Assert.AreEqual(8, bmp.PixelWidth);
        Assert.AreEqual(8, bmp.PixelHeight);
    }
}
