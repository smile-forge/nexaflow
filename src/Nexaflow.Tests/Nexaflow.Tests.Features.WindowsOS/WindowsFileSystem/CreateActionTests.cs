using System;
using System.IO;
using Nexaflow.Features.Text.FileActions;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
[CoversNode("winfs-create-shellnew")]
public class CreateActionTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexacreate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void NewFolder_Create_MakesDirectory()
    {
        var dir = TempDir();
        try
        {
            var action = new NewFolderCreateAction();
            Assert.AreEqual(string.Empty, action.FileExtension);

            var created = action.Create(dir, "My Folder");

            Assert.IsNotNull(created);
            Assert.IsTrue(Directory.Exists(Path.Combine(dir, "My Folder")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void BlankText_Create_WritesEmptyFile()
    {
        var dir = TempDir();
        try
        {
            var action = new BlankTextCreateAction();
            Assert.AreEqual(".txt", action.FileExtension);

            var created = action.Create(dir, "notes.txt");

            Assert.IsNotNull(created);
            var path = Path.Combine(dir, "notes.txt");
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(string.Empty, File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void Template_Create_CopiesTemplateContent()
    {
        var dir = TempDir();
        try
        {
            var tpl = Path.Combine(dir, "src.md");
            File.WriteAllText(tpl, "TEMPLATE BODY");
            var action = new TemplateCreateAction("Doc", "📌", ".md", tpl);

            var created = action.Create(dir, "out.md");

            Assert.IsNotNull(created);
            Assert.AreEqual("TEMPLATE BODY", File.ReadAllText(Path.Combine(dir, "out.md")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void Template_Create_MissingTemplate_FallsBackToEmptyFile()
    {
        var dir = TempDir();
        try
        {
            var action = new TemplateCreateAction("Doc", "📌", ".md", Path.Combine(dir, "missing.md"));

            var created = action.Create(dir, "out.md");

            Assert.IsNotNull(created);
            Assert.AreEqual(string.Empty, File.ReadAllText(Path.Combine(dir, "out.md")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void ShellNew_Create_NullFile_MakesEmptyFile()
    {
        var dir = TempDir();
        try
        {
            var entry  = new ShellNewEntry(".txt", "Text Document", null, new ShellNewSpec(ShellNewKind.NullFile));
            var action = new ShellNewCreateAction(entry);

            action.Create(dir, "new.txt");

            var path = Path.Combine(dir, "new.txt");
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(0, new FileInfo(path).Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void ShellNew_Create_Data_WritesBytes()
    {
        var dir = TempDir();
        try
        {
            var bytes  = new byte[] { 1, 2, 3, 4 };
            var entry  = new ShellNewEntry(".bin", "Binary", null, new ShellNewSpec(ShellNewKind.Data, Data: bytes));
            var action = new ShellNewCreateAction(entry);

            action.Create(dir, "x.bin");

            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(dir, "x.bin")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void ShellNew_Create_FileName_CopiesTemplate()
    {
        var dir = TempDir();
        try
        {
            var tpl = Path.Combine(dir, "template.dat");
            File.WriteAllText(tpl, "SEED");
            var entry  = new ShellNewEntry(".dat", "Data", null, new ShellNewSpec(ShellNewKind.FileName, FileName: tpl));
            var action = new ShellNewCreateAction(entry);

            action.Create(dir, "fresh.dat");

            Assert.AreEqual("SEED", File.ReadAllText(Path.Combine(dir, "fresh.dat")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
