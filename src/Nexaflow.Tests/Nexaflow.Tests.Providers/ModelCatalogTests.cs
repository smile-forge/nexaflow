using System.IO;
using System.Text;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Providers;

[TestClass]
public class ModelCatalogTests
{
    private static IReadOnlyList<CatalogModel> Read(string json) => ModelCatalog.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    private const string Hash = "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002";

    [TestMethod]
    public void AManifest_ReadsAsModelsAndTheirFiles()
    {
        var models = Read($$"""
            { "models": [ { "id": "base.en", "displayName": "Whisper base (English)",
                            "files": [ { "path": "whisper/ggml-base.en.bin", "url": "https://huggingface.co/x/ggml-base.en.bin",
                                         "size": 147964211, "sha256": "{{Hash.ToUpperInvariant()}}" } ] } ] }
            """);

        var model = models.Single();
        Assert.AreEqual("base.en", model.Id);
        Assert.AreEqual(new ModelAsset("whisper/ggml-base.en.bin", new Uri("https://huggingface.co/x/ggml-base.en.bin"), 147964211, Hash),
            model.Files.Single());
    }

    [TestMethod]
    [DataRow("""{ "path": "../outside.bin", "url": "https://h/x", "size": 1, "sha256": "{0}" }""")]
    [DataRow("""{ "path": "C:\\abs.bin", "url": "https://h/x", "size": 1, "sha256": "{0}" }""")]
    [DataRow("""{ "path": "m.bin", "url": "http://h/x", "size": 1, "sha256": "{0}" }""")]
    [DataRow("""{ "path": "m.bin", "url": "https://h/x", "size": 0, "sha256": "{0}" }""")]
    [DataRow("""{ "path": "m.bin", "url": "https://h/x", "size": 1, "sha256": "abc" }""")]
    public void AFileThatDoesNotSayWhatItMustBe_IsRefused(string file)
        => Assert.ThrowsExactly<InvalidDataException>(() =>
            Read($$"""{ "models": [ { "id": "m", "files": [ {{file.Replace("{0}", Hash)}} ] } ] }"""));

    [TestMethod]
    public void AModelWithNoFiles_IsRefused()
        => Assert.ThrowsExactly<InvalidDataException>(() => Read("""{ "models": [ { "id": "m", "files": [] } ] }"""));

    [TestMethod]
    public void AFilePackedInAnArchive_SaysWhichEntryAndWhatTheArchiveMustBe()
    {
        var file = Read($$"""
            { "models": [ { "id": "cuda", "files": [ { "path": "runtime/cudnn64_9.dll", "url": "https://pypi.test/cudnn.whl",
                "size": 270448, "sha256": "{{Hash}}",
                "archive": { "entry": "nvidia/cudnn/bin/cudnn64_9.dll", "size": 436469905, "sha256": "{{Hash.ToUpperInvariant()}}" } } ] } ] }
            """).Single().Files.Single();

        Assert.AreEqual(new ArchiveEntry("nvidia/cudnn/bin/cudnn64_9.dll", 436469905, Hash), file.Archive);
        Assert.AreEqual(new Uri("https://pypi.test/cudnn.whl"), file.Source, "the url is the archive's");
    }

    [TestMethod]
    [DataRow("""{ "size": 1, "sha256": "{0}" }""")]
    [DataRow("""{ "entry": "a.dll", "size": 0, "sha256": "{0}" }""")]
    [DataRow("""{ "entry": "a.dll", "size": 1, "sha256": "abc" }""")]
    public void AnArchiveThatIsNotFullyDescribed_IsRefused(string archive)
        => Assert.ThrowsExactly<InvalidDataException>(() => Read($$"""
            { "models": [ { "id": "m", "files": [ { "path": "a.dll", "url": "https://h/x.whl", "size": 1, "sha256": "{{Hash}}",
                                                   "archive": {{archive.Replace("{0}", Hash)}} } ] } ] }
            """));
}
