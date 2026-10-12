using System.Runtime.Intrinsics.X86;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Whisper;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>Whisper on this machine: the models it offers, and fetching one when it is first assigned.</summary>
[TestClass]
public class WhisperProviderTests
{
    /// <summary>A store that has nothing until asked, then has whatever it was asked for.</summary>
    private sealed class Store : IModelAssetStore
    {
        public readonly List<ModelAsset> Fetched = [];
        public event EventHandler<ModelAsset>? Changed;
        public string? Locate(ModelAsset asset) => Fetched.Contains(asset) ? @"C:\models\" + asset.RelativePath : null;
        public Task<string> EnsureAsync(ModelAsset asset, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(new AssetProgress(asset.Size, asset.Size));
            Fetched.Add(asset);
            Changed?.Invoke(this, asset);
            return Task.FromResult(Locate(asset)!);
        }
    }

    [TestMethod]
    public async Task ItOffersEveryModelInItsCatalog_EachWithAVerifiableFile()
    {
        var models = await new WhisperSpeechToTextProvider(new FakeActivityManager(), new Store(), new ProviderModel(""))
            .GetAvailableModelsAsync();

        CollectionAssert.AreEquivalent(
            new[] { "base.en", "base", "small.en", "small", "medium.en", "medium", "large-v3-turbo", "large-v3" }, models.ToArray());
        Assert.IsTrue(WhisperSpeechToTextProvider.Models.All(m => m.Files.Single().Sha256.Length == 64));
    }

    [TestMethod]
    public async Task AModelFirstAssigned_IsFetched_ThenAvailable()
    {
        var store    = new Store();
        var provider = new WhisperSpeechToTextProvider(new FakeActivityManager(), store, new ProviderModel("base.en"));
        var changed  = 0;
        provider.AvailabilityChanged += (_, _) => changed++;
        Assert.IsFalse(provider.IsAvailable, "nothing to listen with before the model is on disk");

        await provider.WarmupAsync();

        Assert.AreEqual("whisper/ggml-base.en.bin", store.Fetched.Single().RelativePath);
        Assert.AreEqual(1, changed);
        Assert.AreEqual(Avx.IsSupported, provider.IsAvailable);
    }

    [TestMethod]
    public async Task ListeningWithoutTheModel_SaysWhy()
        => await Assert.ThrowsExactlyAsync<LlmProviderException>(() =>
            new WhisperSpeechToTextProvider(new FakeActivityManager(), new Store(), new ProviderModel("base.en"))
                .StartSessionAsync(new SpeechToTextOptions()));
}
