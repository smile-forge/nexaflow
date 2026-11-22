using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Interaction;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Dropping;

namespace Nexaflow.Tests.Visuals.Dropping;

/// <summary>The formats other applications drag in, read as the kinds targets name.</summary>
[TestClass]
[CoversNode("vcommon-drag-payload")]
public class WindowsDragPayloadTests
{
    /// <summary>A FILEGROUPDESCRIPTORW naming <paramref name="name"/>, <paramref name="size"/> bytes long.</summary>
    private static MemoryStream Descriptor(string name, uint size)
    {
        var table = new byte[4 + 592];
        BitConverter.GetBytes(1).CopyTo(table, 0);
        BitConverter.GetBytes(0x40u).CopyTo(table, 4);
        BitConverter.GetBytes(size).CopyTo(table, 4 + 68);
        Encoding.Unicode.GetBytes(name).CopyTo(table, 4 + 72);
        return new MemoryStream(table);
    }

    [TestMethod]
    public void Files_AreTheirPaths() => UiThread.Run(() =>
    {
        var payload = new WindowsDragPayload(new DataObject(DataFormats.FileDrop, new[] { @"C:\a.txt" }));

        Assert.IsTrue(payload.CarriesFiles());
        CollectionAssert.AreEqual(new[] { @"C:\a.txt" }, payload.Files().ToArray());
    });

    [TestMethod]
    public void AFileOfContentOnly_IsNamedOnHover_AndReadWhenWanted() => UiThread.Run(() =>
    {
        var content = Encoding.UTF8.GetBytes("an attachment's content");
        var data = new DataObject();
        data.SetData("FileGroupDescriptorW", Descriptor("attached.txt", (uint)content.Length));
        data.SetData("FileContents", new MemoryStream(content));

        var payload = new WindowsDragPayload(data);

        Assert.IsTrue(payload.Contains(DragKinds.FileContents));
        Assert.AreEqual("attached.txt", payload.FileNames().Single());
        using var read = new MemoryStream();
        payload.Contents()[0].CopyTo(read);
        CollectionAssert.AreEqual(content, read.ToArray(), "to the size the table gives, not the rounded block");
    });

    [TestMethod]
    public void ATableThatClaimsMoreThanItHolds_IsNoFiles() => UiThread.Run(() =>
    {
        var lying = new byte[16];
        BitConverter.GetBytes(int.MaxValue).CopyTo(lying, 0);

        Assert.IsFalse(new WindowsDragPayload(new DataObject("FileGroupDescriptorW", new MemoryStream(lying))).CarriesFiles());
    });

    [TestMethod]
    public void ABrowsersLink_IsALink_NotTheUrlFileItAlsoOffers() => UiThread.Run(() =>
    {
        var data = new DataObject();
        data.SetData("FileGroupDescriptorW", Descriptor("Example.url", 0));
        data.SetData("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes("https://example.com\0")));

        var payload = new WindowsDragPayload(data);

        Assert.AreEqual("https://example.com", payload.Link());
        Assert.IsFalse(payload.CarriesFiles());
    });

    [TestMethod]
    public void WordsThatAreOneAddress_AreALinkAsWell() => UiThread.Run(() =>
    {
        var payload = new WindowsDragPayload(new DataObject(DataFormats.UnicodeText, " https://example.com/a "));

        Assert.AreEqual("https://example.com/a", payload.Link());
        Assert.IsTrue(payload.Contains(DragKinds.Text));
    });

    [TestMethod]
    public void APicture_IsPngBytes() => UiThread.Run(() =>
    {
        var data = new DataObject();
        data.SetImage(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8));

        var png = new WindowsDragPayload(data).Image();

        Assert.IsNotNull(png);
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    });

    [TestMethod]
    public void TheAppsOwnKind_IsItselfAndNothingShared() => UiThread.Run(() =>
    {
        var payload = new WindowsDragPayload(new DataObject("Nexaflow.Tests.Kind", "value"));

        Assert.AreEqual("value", payload.Read("Nexaflow.Tests.Kind"));
        Assert.IsFalse(payload.IsShareable());
    });
}
