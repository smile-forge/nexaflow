using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nexaflow.Core;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.Logging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A stored value written by a build where its property had another shape — the installed release reading a dev
/// build's <c>"Language": "en"</c> into what is, for it, still an enum — keeps the property's default and goes on
/// record, rather than throwing out of <see cref="ConfigManager.Register"/> and stopping startup.
/// </summary>
[TestClass]
[CoversNode("opt-config-migration")]
public class ConfigUnreadableValueTests
{
    private enum Language { English, French }

    private sealed class ShapeChangedConfig
    {
        public Language Language     { get; set; } = Language.English;
        public int      TextFontSize { get; set; } = 13;
        public string   Theme        { get; set; } = "Dark";
    }

    // Recovers, from the raw old JSON, the value the carry-over cannot read.
    private sealed class SelfMigratingConfig : Nexaflow.Features.Common.IConfigMigration
    {
        public Language Language { get; set; } = Language.English;

        public void MigrateFrom(JsonObject previous, Version previousVersion)
        {
            if (previous["Language"] is JsonValue v && v.GetValue<string>() == "fr") Language = Language.French;
        }
    }

    private static Version Current => typeof(ConfigUnreadableValueTests).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    private static Version Prior   => Current > new Version(0, 0, 0, 0) ? new Version(0, 0, 0, 0) : new Version(99, 0, 0, 0);

    private string        _dir    = string.Empty;
    private CrashLogProvider _log = null!;
    private ConfigManager _config = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(TestTemp.Root, "nexaflow-unreadable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var now = DateTimeOffset.Now;   // one clock reading, so the log a test reads is the day it was written
        _log    = new CrashLogProvider(() => Path.Combine(_dir, "logs"), () => now, "test");
        _config = TestRuntimes.ConfigIn(_dir, new LoggerFactory([_log]).CreateLogger<ConfigManager>());
    }

    [TestCleanup]
    public void Teardown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void Register_ValueOfAnotherShape_KeepsItsDefault_AndTheRestLoads()
    {
        var name = UniqueName();
        WriteConfig(name, Current, """{ "Language": "fr", "TextFontSize": 15, "Theme": "Light" }""");

        var cfg = (ShapeChangedConfig)_config.Register(new ShapeChangedConfig(), name);

        Assert.AreEqual(Language.English, cfg.Language, "a value that does not read leaves its property at the default");
        Assert.AreEqual(15, cfg.TextFontSize, "the values around it still load");
        Assert.AreEqual("Light", cfg.Theme);
    }

    [TestMethod]
    public void Register_ValueOfAnotherShape_IsRecorded_WithoutTheValue()
    {
        var name = UniqueName();
        var path = WriteConfig(name, Current, """{ "Language": "fr", "TextFontSize": 15 }""");

        _config.Register(new ShapeChangedConfig(), name);

        Assert.IsTrue(File.Exists(_log.CurrentPath), "the fallback to a default goes on record");
        var entry = File.ReadAllText(_log.CurrentPath);
        StringAssert.Contains(entry, path, "the entry names the file");
        StringAssert.Contains(entry, "ShapeChangedConfig.Language", "…the property that fell back");
        StringAssert.Contains(entry, "System.Text.Json.JsonException", "…and why it could not be read");
        Assert.IsFalse(entry.Contains("\"fr\""), "the stored value itself stays out of a log users send with reports");
    }

    [TestMethod]
    public void Register_EveryValueReadable_RecordsNothing()
    {
        var name = UniqueName();
        WriteConfig(name, Current, """{ "Language": "French", "TextFontSize": 15 }""");

        var cfg = (ShapeChangedConfig)_config.Register(new ShapeChangedConfig(), name);

        Assert.AreEqual(Language.French, cfg.Language);
        Assert.IsFalse(File.Exists(_log.CurrentPath), "a clean load leaves no entry");
    }

    [TestMethod]
    public void LoadFrom_MigratingAValueOfAnotherShape_KeepsItsDefault_RecordsIt_AndRewritesAtCurrent()
    {
        var name      = UniqueName();
        var priorPath = WriteConfig(name, Prior, """{ "Language": "fr", "TextFontSize": 15 }""");

        var cfg = new ShapeChangedConfig();
        _config.LoadFrom(_dir, cfg, name);

        Assert.AreEqual(Language.English, cfg.Language);
        Assert.AreEqual(15, cfg.TextFontSize, "the readable values carry over");
        Assert.IsTrue(File.Exists(Path.Combine(_dir, name, $"config_{Current}.json")), "the migration still completes");
        Assert.IsFalse(File.Exists(priorPath), "…and removes the older file");
        StringAssert.Contains(File.ReadAllText(_log.CurrentPath), priorPath, "the value the migration dropped is on record");
    }

    [TestMethod]
    public void LoadFrom_SelfMigratingConfig_RecoversTheValueFromTheOldJson_AndRecordsNothing()
    {
        var name = UniqueName();
        WriteConfig(name, Prior, """{ "Language": "fr" }""");

        var cfg = new SelfMigratingConfig();
        _config.LoadFrom(_dir, cfg, name);

        Assert.AreEqual(Language.French, cfg.Language, "the hook still runs after the carry-over skipped the value");
        Assert.IsFalse(File.Exists(_log.CurrentPath), "a value the hook owns is not a fault");
    }

    // ── A whole file that cannot be read ──

    [TestMethod]
    public void Register_AFileThatIsNotJson_FallsBackToDefaults_RatherThanThrowing()
    {
        var name = UniqueName();
        WriteConfig(name, Current, "{ this is not json");

        var cfg = (ShapeChangedConfig)_config.Register(new ShapeChangedConfig(), name);

        Assert.AreEqual(Language.English, cfg.Language, "a damaged file must not stop the app starting");
        Assert.AreEqual(13, cfg.TextFontSize);
        CollectionAssert.Contains(_config.GetUnreadableConfigs().ToList(), name,
            "reported as lost, which is what the user is told and the wizard re-asks on");
        CollectionAssert.Contains(_config.GetDefaultedConfigs().ToList(), name,
            "and as defaulted, because that is what it now holds");
        StringAssert.Contains(File.ReadAllText(_log.CurrentPath), "could not be read");
    }

    [TestMethod]
    public void Register_AFileThatIsNotJson_IsKeptAside_SoTheNextLaunchStartsClean()
    {
        var name = UniqueName();
        const string damaged = "{ this is not json";
        var path = WriteConfig(name, Current, damaged);

        _config.Register(new ShapeChangedConfig(), name);

        Assert.IsFalse(File.Exists(path), "the file that cannot be read is moved out of the way");
        var kept = Directory.GetFiles(Path.Combine(_dir, name), "config_*.json.unreadable-*").Single();
        Assert.AreEqual(damaged, File.ReadAllText(kept), "with its bytes intact, to recover by hand");
    }

    [TestMethod]
    public void LoadFrom_AMigrationSourceThatIsNotJson_FallsBackToDefaults()
    {
        var name = UniqueName();
        WriteConfig(name, Prior, "not json either");

        var cfg = new ShapeChangedConfig();
        _config.LoadFrom(_dir, cfg, name);

        Assert.AreEqual(13, cfg.TextFontSize, "an unreadable older file is a fallback, not a crash");
        StringAssert.Contains(File.ReadAllText(_log.CurrentPath), "could not be read");
    }

    [TestMethod]
    public void Saving_ClearsTheUnreadableMark()
    {
        var name = UniqueName();
        WriteConfig(name, Current, "{ this is not json");
        var cfg = _config.Register(new ShapeChangedConfig(), name);

        _config.Save(cfg, name);

        CollectionAssert.DoesNotContain(_config.GetUnreadableConfigs().ToList(), name,
            "once the user's answers are written there is nothing left to re-ask");
    }

    private static string UniqueName() => "unreadable_" + Guid.NewGuid().ToString("N");

    private string WriteConfig(string name, Version version, string json)
    {
        var dir = Path.Combine(_dir, name);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"config_{version}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
