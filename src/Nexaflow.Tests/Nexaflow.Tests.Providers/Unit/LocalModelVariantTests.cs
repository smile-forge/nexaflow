using Nexaflow.Providers.Local.Catalog;

namespace Nexaflow.Tests.Providers.Unit;

[TestClass]
public class LocalModelVariantTests
{
    [TestMethod]
    public void DownloadUrl_BuildsHuggingFaceResolvePath()
    {
        var v = new LocalModelVariant
        {
            Repo  = "unsloth/gemma-4-12b-it-GGUF",
            Files = ["gemma-4-12b-it-Q4_K_M.gguf"],
        };

        Assert.AreEqual(
            "https://huggingface.co/unsloth/gemma-4-12b-it-GGUF/resolve/main/gemma-4-12b-it-Q4_K_M.gguf?download=true",
            v.DownloadUrlFor("gemma-4-12b-it-Q4_K_M.gguf"));
    }

    [TestMethod]
    public void DownloadUrl_BaseUrlOverrideWins()
    {
        var v = new LocalModelVariant { Repo = "x/y", BaseUrl = "https://example.com/models/", Files = ["a.gguf", "b.gguf"] };
        Assert.AreEqual("https://example.com/models/b.gguf", v.DownloadUrlFor("b.gguf"));
    }

    [TestMethod]
    public void DownloadUrl_ExplicitUrlForSingleFile()
    {
        var v = new LocalModelVariant { Repo = "x/y", Url = "https://host/file.gguf", Files = ["file.gguf"] };
        Assert.AreEqual("https://host/file.gguf", v.DownloadUrlFor("file.gguf"));
    }

    [TestMethod]
    public void FamilyKind_MapsCaseInsensitively()
    {
        Assert.AreEqual(ModelFamily.Qwen,  new LocalModelVariant { Family = "qwen"  }.FamilyKind);
        Assert.AreEqual(ModelFamily.Qwen,  new LocalModelVariant { Family = "QWEN"  }.FamilyKind);
        Assert.AreEqual(ModelFamily.Gemma, new LocalModelVariant { Family = "gemma" }.FamilyKind);
        Assert.AreEqual(ModelFamily.Gemma, new LocalModelVariant { Family = "other" }.FamilyKind);
    }

    [TestMethod]
    public void PrimaryFile_IsFirstShard()
    {
        var v = new LocalModelVariant { Files = ["part-1.gguf", "part-2.gguf"] };
        Assert.AreEqual("part-1.gguf", v.PrimaryFile);
    }
}
