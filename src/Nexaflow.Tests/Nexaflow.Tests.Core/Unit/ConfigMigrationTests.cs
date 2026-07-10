using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Nexaflow.Core;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
[DoNotParallelize]   // the Register cases mutate the process-wide ConfigManager.Instance.BaseDir
[CoversNode("opt-config-migration")]
public class ConfigMigrationTests
{
    private sealed class MigrationFakeConfig
    {
        public string SomeValue { get; set; } = "default";
    }

    // Opts into the custom upgrade hook to remap a renamed field the lenient carry-over can't.
    private sealed class HookFakeConfig : Nexaflow.Features.Common.IConfigMigration
    {
        public string NewName { get; set; } = "";
        public bool   Hooked  { get; private set; }

        public void MigrateFrom(JsonObject previous, Version previousVersion)
        {
            Hooked = true;
            if (previous["OldName"] is JsonValue v) NewName = v.GetValue<string>();
        }
    }

    // The current version is the test assembly's; pick a different version for the on-disk "older" file.
    private static (Version current, Version prior) Versions<T>()
    {
        var current = typeof(T).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        var prior   = current > new Version(0, 0, 0, 0) ? new Version(0, 0, 0, 0) : new Version(99, 0, 0, 0);
        Assert.AreNotEqual(current, prior);
        return (current, prior);
    }

    [TestMethod]
    public void LoadFrom_MigratesOlderVersionForward_CarriesData_RewritesAtCurrent()
    {
        var dir = TempDir();
        try
        {
            var (current, prior) = Versions<MigrationFakeConfig>();
            var cfgDir = Path.Combine(dir, "migfake");
            Directory.CreateDirectory(cfgDir);
            var priorPath = Path.Combine(cfgDir, $"config_{prior}.json");
            File.WriteAllText(priorPath, "{\"SomeValue\":\"carried\"}");

            var cfg = new MigrationFakeConfig();
            ConfigManager.Instance.LoadFrom(dir, cfg, "migfake");

            Assert.AreEqual("carried", cfg.SomeValue);
            Assert.IsFalse(File.Exists(priorPath), "stale older file should be deleted after migration");
            Assert.IsTrue(File.Exists(Path.Combine(cfgDir, $"config_{current}.json")),
                "data should be rewritten under the current version");
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    public void LoadFrom_InvokesConfigMigrationHook_WithPreviousJson()
    {
        var dir = TempDir();
        try
        {
            var (_, prior) = Versions<HookFakeConfig>();
            var cfgDir = Path.Combine(dir, "hookfake");
            Directory.CreateDirectory(cfgDir);
            File.WriteAllText(Path.Combine(cfgDir, $"config_{prior}.json"), "{\"OldName\":\"legacy\"}");

            var cfg = new HookFakeConfig();
            ConfigManager.Instance.LoadFrom(dir, cfg, "hookfake");

            Assert.IsTrue(cfg.Hooked, "the IConfigMigration hook should run on a version mismatch");
            Assert.AreEqual("legacy", cfg.NewName, "the hook should see the raw previous JSON");
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    public void LoadFrom_MissingFile_DoesNotMigrate_KeepsDefaults()
    {
        var dir = TempDir();
        try
        {
            var cfg = new MigrationFakeConfig { SomeValue = "untouched" };
            ConfigManager.Instance.LoadFrom(dir, cfg, "absent");
            Assert.AreEqual("untouched", cfg.SomeValue);
            Assert.IsFalse(Directory.Exists(Path.Combine(dir, "absent")));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    public void Register_MigratedConfig_IsTrackedMigrated_NotDefaulted()
    {
        var dir   = TempDir();
        var saved = ConfigManager.Instance.BaseDir;
        try
        {
            ConfigManager.Instance.Initialize(dir);
            var (_, prior) = Versions<MigrationFakeConfig>();
            var name   = "regmig_" + Guid.NewGuid().ToString("N");
            var cfgDir = Path.Combine(dir, name);
            Directory.CreateDirectory(cfgDir);
            File.WriteAllText(Path.Combine(cfgDir, $"config_{prior}.json"), "{\"SomeValue\":\"x\"}");

            ConfigManager.Instance.Register(new MigrationFakeConfig(), name);

            CollectionAssert.Contains(ConfigManager.Instance.GetMigratedConfigs().ToList(), name);
            CollectionAssert.DoesNotContain(ConfigManager.Instance.GetDefaultedConfigs().ToList(), name);
        }
        finally { ConfigManager.Instance.Initialize(saved); Cleanup(dir); }
    }

    [TestMethod]
    public void Register_BrandNewConfig_IsTrackedDefaulted_NotMigrated()
    {
        var dir   = TempDir();
        var saved = ConfigManager.Instance.BaseDir;
        try
        {
            ConfigManager.Instance.Initialize(dir);
            var name = "regnew_" + Guid.NewGuid().ToString("N");

            ConfigManager.Instance.Register(new MigrationFakeConfig(), name);

            CollectionAssert.Contains(ConfigManager.Instance.GetDefaultedConfigs().ToList(), name);
            CollectionAssert.DoesNotContain(ConfigManager.Instance.GetMigratedConfigs().ToList(), name);
        }
        finally { ConfigManager.Instance.Initialize(saved); Cleanup(dir); }
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
}
