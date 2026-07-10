using System.IO;
using System.Linq;
using System.Text;
using Nexaflow.Features.Email.Vfs;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Email;

/// <summary>The email-as-container path: the archive handler/session expose parts as entries, and a virtual
/// <c>mail.eml\attachment</c> path round-trips through the VFS to the decoded bytes — the mechanism behind
/// opening an attachment in its own viewer.</summary>
[TestClass]
[CoversNode("email-attachments")]
public class EmailVfsTests
{
    private static string SimpleEml => TestSampleData.Path("email", "simple.eml");

    [TestMethod]
    public void Handler_Recognises_EmlAndMsg_ButNotZip()
    {
        var h = new EmailArchiveHandler();
        Assert.IsTrue(h.CanHandle("message.eml"));
        Assert.IsTrue(h.CanHandle("message.msg"));
        Assert.IsFalse(h.CanHandle("archive.zip"));
    }

    [TestMethod]
    public void Session_Entry_MatchesAttachmentName()
    {
        using var fs = File.OpenRead(SimpleEml);
        using var session = new EmailArchiveHandler().Open(fs, "simple.eml");

        var names = session.Entries.Select(e => e.Name).ToList();
        CollectionAssert.Contains(names, "notes.txt");
    }

    [TestMethod]
    public void OpenEntry_ReturnsDecodedBytes()
    {
        using var fs = File.OpenRead(SimpleEml);
        using var session = new EmailArchiveHandler().Open(fs, "simple.eml");

        using var stream = session.OpenEntry("notes.txt");
        using var reader = new StreamReader(stream);
        StringAssert.Contains(reader.ReadToEnd(), "revenue up 12%");
    }

    [TestMethod]
    public void OpenEntry_Unknown_Throws()
    {
        using var fs = File.OpenRead(SimpleEml);
        using var session = new EmailArchiveHandler().Open(fs, "simple.eml");
        Assert.ThrowsExactly<FileNotFoundException>(() => session.OpenEntry("does-not-exist.bin"));
    }

    [TestMethod]
    public void Vfs_RoundTrips_AttachmentPath()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new EmailArchiveHandler());

        var virtualPath = Path.Combine(SimpleEml, "notes.txt");
        Assert.IsTrue(vfs.Exists(virtualPath), "attachment should resolve as a VFS entry");

        var bytes = vfs.ReadAllBytes(virtualPath);
        StringAssert.Contains(Encoding.UTF8.GetString(bytes), "revenue up 12%");
    }

    [TestMethod]
    public void Vfs_EmlIsAContainer()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new EmailArchiveHandler());
        Assert.IsTrue(vfs.IsContainer(SimpleEml));
    }
}
