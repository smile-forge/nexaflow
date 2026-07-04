using System.Text.Json;
using Nexaflow.Providers.Aria;
using Nexaflow.Providers.Claude;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Gemini;
using Nexaflow.Providers.Ollama;
using Nexaflow.Providers.OpenAI;

namespace Nexaflow.Tests.Providers;

/// <summary>Defaults, identity, and JSON round-trip for each provider's <see cref="IProviderConfig"/>.</summary>
[TestClass]
public class ProviderConfigTests
{
    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    // ── Claude ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Claude_Defaults()
    {
        var c = new ClaudeConfig();
        Assert.AreEqual("claude", c.ConfigName);
        Assert.AreEqual("Claude", c.FriendlyName);
        Assert.AreEqual("", c.ApiKey);
        Assert.AreEqual("https://api.anthropic.com", c.BaseUrl);
    }

    [TestMethod]
    public void Claude_RoundTrips()
    {
        var c = RoundTrip(new ClaudeConfig { ApiKey = "sk-test", BaseUrl = "https://proxy.example/v1" });
        Assert.AreEqual("sk-test", c.ApiKey);
        Assert.AreEqual("https://proxy.example/v1", c.BaseUrl);
    }

    // ── OpenAI ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void OpenAI_Defaults()
    {
        var c = new OpenAIConfig();
        Assert.AreEqual("openai", c.ConfigName);
        Assert.AreEqual("OpenAI", c.FriendlyName);
        Assert.AreEqual("", c.ApiKey);
        Assert.AreEqual("", c.BaseUrl);
    }

    [TestMethod]
    public void OpenAI_RoundTrips()
    {
        var c = RoundTrip(new OpenAIConfig { ApiKey = "sk-oai", BaseUrl = "http://localhost:1234/v1" });
        Assert.AreEqual("sk-oai", c.ApiKey);
        Assert.AreEqual("http://localhost:1234/v1", c.BaseUrl);
    }

    // ── Gemini ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Gemini_Defaults()
    {
        var c = new GeminiConfig();
        Assert.AreEqual("gemini", c.ConfigName);
        Assert.AreEqual("Gemini", c.FriendlyName);
        Assert.AreEqual("", c.ApiKey);
    }

    [TestMethod]
    public void Gemini_RoundTrips()
    {
        var c = RoundTrip(new GeminiConfig { ApiKey = "g-key" });
        Assert.AreEqual("g-key", c.ApiKey);
    }

    // ── Aria ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Aria_Defaults()
    {
        var c = new AriaConfig();
        Assert.AreEqual("aria", c.ConfigName);
        Assert.AreEqual("Aria", c.FriendlyName);
        Assert.AreEqual("", c.ApiKey);
    }

    [TestMethod]
    public void Aria_RoundTrips()
    {
        var c = RoundTrip(new AriaConfig { ApiKey = "aria-key" });
        Assert.AreEqual("aria-key", c.ApiKey);
    }

    // ── Ollama (richest: identity, defaults, computed keep-alive) ─────────────

    [TestMethod]
    public void Ollama_Defaults()
    {
        var c = new OllamaConfig();
        Assert.AreEqual("ollama", c.ConfigName);
        Assert.AreEqual("Ollama", c.FriendlyName);
        Assert.AreEqual("http://localhost:11434", c.Url);
        Assert.IsFalse(c.KeepWhileRunning);
        Assert.AreEqual(0, c.KeepAliveMinutes);
    }

    [TestMethod]
    public void Ollama_KeepAliveValue_IsNull_WhenNothingSet()
        => Assert.IsNull(new OllamaConfig().KeepAliveValue);

    [TestMethod]
    public void Ollama_KeepAliveValue_IsMinusOne_WhenKeepWhileRunning()
        => Assert.AreEqual("-1", new OllamaConfig { KeepWhileRunning = true }.KeepAliveValue);

    [TestMethod]
    public void Ollama_KeepAliveValue_FormatsMinutes()
        => Assert.AreEqual("7m", new OllamaConfig { KeepAliveMinutes = 7 }.KeepAliveValue);

    [TestMethod]
    public void Ollama_KeepAliveValue_KeepWhileRunning_WinsOverMinutes()
        => Assert.AreEqual("-1", new OllamaConfig { KeepWhileRunning = true, KeepAliveMinutes = 7 }.KeepAliveValue);

    [TestMethod]
    public void Ollama_RoundTrips_AndRecomputesKeepAlive()
    {
        var c = RoundTrip(new OllamaConfig { Url = "http://box:11434", KeepAliveMinutes = 10, ThinkingMode = true });
        Assert.AreEqual("http://box:11434", c.Url);
        Assert.AreEqual(10, c.KeepAliveMinutes);
        Assert.IsTrue(c.ThinkingMode);
        Assert.AreEqual("10m", c.KeepAliveValue);   // [JsonIgnore] — recomputed, not deserialized
    }

    [TestMethod]
    public void Ollama_KeepAliveValue_IsNotSerialized()
    {
        var json = JsonSerializer.Serialize(new OllamaConfig { KeepWhileRunning = true });
        StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("KeepAliveValue"));
    }
}
