using System;
using System.IO;
using System.Text;
using Nexaflow.Features.WindowsFileSystem.FileActions;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
public class ShellNewContentResolverTests
{
    [TestMethod]
    public void NullFile_ResolvesToEmpty()
    {
        var c = ShellNewContentResolver.Resolve(new ShellNewSpec(ShellNewKind.NullFile));

        Assert.IsNull(c.Bytes);
        Assert.IsNull(c.TemplatePath);
    }

    [TestMethod]
    public void Data_Bytes_ResolvesToBytes()
    {
        var bytes = new byte[] { 9, 8, 7 };

        var c = ShellNewContentResolver.Resolve(new ShellNewSpec(ShellNewKind.Data, Data: bytes));

        CollectionAssert.AreEqual(bytes, c.Bytes);
        Assert.IsNull(c.TemplatePath);
    }

    [TestMethod]
    public void Data_String_ResolvesToUtf8Bytes()
    {
        var c = ShellNewContentResolver.Resolve(new ShellNewSpec(ShellNewKind.Data, DataString: "hi"));

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("hi"), c.Bytes);
    }

    [TestMethod]
    public void FileName_AbsoluteExisting_ResolvesToPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexasnr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tpl = Path.Combine(dir, "t.dat");
            File.WriteAllText(tpl, "x");

            var c = ShellNewContentResolver.Resolve(new ShellNewSpec(ShellNewKind.FileName, FileName: tpl));

            Assert.AreEqual(tpl, c.TemplatePath);
            Assert.IsNull(c.Bytes);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void FileName_Missing_ResolvesToEmpty()
    {
        var c = ShellNewContentResolver.Resolve(
            new ShellNewSpec(ShellNewKind.FileName, FileName: @"Z:\nope\missing.xyz"));

        Assert.IsNull(c.TemplatePath);
        Assert.IsNull(c.Bytes);
    }
}
