using System.Text;
using Nexaflow.Providers.AiHost;

namespace Nexaflow.Tests.Providers;

/// <summary>The catalog of models Nexaflow runs itself.</summary>
[TestClass]
public class OwnedModelsTests
{
    [TestMethod]
    public void TheShippedCatalog_SaysHowEachModelIsRun_AndKeepsItsFilesInItsFolder()
    {
        var gemma = OwnedModels.All.Single(m => m.Id == "gemma-4-e4b-it");

        Assert.AreEqual(("models/gemma-4-e4b-it", "gemma4-onnx-community-q4f16"), (gemma.Folder, gemma.Adapter));
        Assert.IsTrue(gemma is { MemoryBytes: > 0, ContextTokens: > 0, Images: true, Audio: true });
        CollectionAssert.IsSubsetOf(new[] { "models/gemma-4-e4b-it/genai_config.native.json", "models/gemma-4-e4b-it/chat_template.jinja" },
                                    gemma.Catalog.Files.Select(f => f.RelativePath).ToArray(), "what the adapter and the host read");
    }

    private const string Hash = "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002";

    [TestMethod]
    [DataRow("""{ "id": "m", "folder": "models/m", "memoryBytes": 1, "contextTokens": 1, "files": [{0}] }""")]
    [DataRow("""{ "id": "m", "folder": "models/m", "adapter": "a", "memoryBytes": 1, "contextTokens": 1, "files": [{1}] }""")]
    public void AModelThatDoesNotSayHowItIsRun_OrStraysFromItsFolder_IsRefused(string model)
    {
        string inFolder  = $$"""{ "path": "models/m/w.bin", "url": "https://h/w", "size": 1, "sha256": "{{Hash}}" }""";
        string outFolder = $$"""{ "path": "elsewhere/w.bin", "url": "https://h/w", "size": 1, "sha256": "{{Hash}}" }""";
        var json = $$"""{ "models": [ {{model.Replace("{0}", inFolder).Replace("{1}", outFolder)}} ] }""";

        Assert.ThrowsExactly<InvalidDataException>(() => OwnedModels.Read(Encoding.UTF8.GetBytes(json)));
    }
}
