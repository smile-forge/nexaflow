using System;
using System.IO;
using Nexaflow.Core;
using Nexaflow.Features.Common;

namespace Nexaflow.Tests.Core.Unit.Config;

/// <summary>
/// [Secret] string properties must never reach disk in plaintext: ConfigManager writes them
/// DPAPI-encrypted ("enc:…") and decrypts on load; legacy plaintext files still load (and encrypt
/// on the next save) so existing configs migrate without a wizard re-prompt.
/// </summary>
[TestClass]
public class SecretConfigTests
{
    private sealed class FakeConfig
    {
        [Secret] public string ApiKey { get; set; } = "";
        public string BaseUrl { get; set; } = "";
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-secret-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void SaveTo_EncryptsSecret_AndLoadFrom_RoundTrips()
    {
        var dir = TempDir();
        try
        {
            var cfg = new FakeConfig { ApiKey = "sk-super-secret", BaseUrl = "https://x" };
            ConfigManager.Instance.SaveTo(dir, cfg, "fake");

            var file = Directory.GetFiles(Path.Combine(dir, "fake"), "config_*.json")[0];
            var raw  = File.ReadAllText(file);
            StringAssert.Contains(raw, "enc:");
            Assert.IsFalse(raw.Contains("sk-super-secret"), "The secret must not be on disk in plaintext.");
            StringAssert.Contains(raw, "https://x");   // non-secret fields stay readable

            var loaded = new FakeConfig();
            ConfigManager.Instance.LoadFrom(dir, loaded, "fake");
            Assert.AreEqual("sk-super-secret", loaded.ApiKey);
            Assert.AreEqual("https://x", loaded.BaseUrl);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void LoadFrom_AcceptsLegacyPlaintextSecret()
    {
        var dir = TempDir();
        try
        {
            var cfgDir = Path.Combine(dir, "fake");
            Directory.CreateDirectory(cfgDir);
            var version = typeof(FakeConfig).Assembly.GetName().Version;
            File.WriteAllText(Path.Combine(cfgDir, $"config_{version}.json"),
                """{ "ApiKey": "legacy-plain", "BaseUrl": "https://y" }""");

            var loaded = new FakeConfig();
            ConfigManager.Instance.LoadFrom(dir, loaded, "fake");
            Assert.AreEqual("legacy-plain", loaded.ApiKey);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
