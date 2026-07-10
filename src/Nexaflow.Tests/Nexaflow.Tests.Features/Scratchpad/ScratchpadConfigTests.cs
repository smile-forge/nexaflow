using Nexaflow.Features.Scratchpad;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Scratchpad;

[TestClass]
[CoversNode("scratchpad-config")]
public class ScratchpadConfigTests
{
    [TestMethod]
    public void Defaults_MatchSpec()
    {
        var cfg = new ScratchpadConfig();
        Assert.AreEqual("scratchpad", cfg.ConfigName);
        Assert.AreEqual("Scratchpad", cfg.FriendlyName);
        Assert.AreEqual("2 hours", cfg.NoteLifetime);
        Assert.AreEqual("30 days", cfg.RecycleBinRetention);
    }

    [TestMethod]
    [DataRow("30 minutes", 30)]
    [DataRow("1 hour", 60)]
    [DataRow("2 hours", 120)]
    [DataRow("4 hours", 240)]
    [DataRow("8 hours", 480)]
    [DataRow("24 hours", 1440)]
    public void GetNoteLifetime_KnownValues(string label, int expectedMinutes)
    {
        var cfg = new ScratchpadConfig { NoteLifetime = label };
        Assert.AreEqual(TimeSpan.FromMinutes(expectedMinutes), cfg.GetNoteLifetime());
    }

    [TestMethod]
    public void GetNoteLifetime_UnknownValue_DefaultsTo2Hours()
    {
        var cfg = new ScratchpadConfig { NoteLifetime = "garbage" };
        Assert.AreEqual(TimeSpan.FromHours(2), cfg.GetNoteLifetime());
    }

    [TestMethod]
    [DataRow("None", 0)]
    [DataRow("1 day", 1)]
    [DataRow("15 days", 15)]
    [DataRow("30 days", 30)]
    [DataRow("60 days", 60)]
    [DataRow("90 days", 90)]
    public void GetRetentionDays_KnownValues(string label, int expected)
    {
        var cfg = new ScratchpadConfig { RecycleBinRetention = label };
        Assert.AreEqual(expected, cfg.GetRetentionDays());
    }

    [TestMethod]
    public void GetRetentionDays_Infinite_ReturnsNull()
    {
        var cfg = new ScratchpadConfig { RecycleBinRetention = "Infinite" };
        Assert.IsNull(cfg.GetRetentionDays());
    }

    [TestMethod]
    public void GetRetentionDays_UnknownValue_DefaultsTo30()
    {
        var cfg = new ScratchpadConfig { RecycleBinRetention = "garbage" };
        Assert.AreEqual(30, cfg.GetRetentionDays());
    }

    [TestMethod]
    public void GetLifetimeOptions_ContainsExpectedLabels()
    {
        var opts = ScratchpadConfig.GetLifetimeOptions().ToList();
        CollectionAssert.Contains(opts, "30 minutes");
        CollectionAssert.Contains(opts, "24 hours");
    }

    [TestMethod]
    public void GetRetentionOptions_ContainsExpectedLabels()
    {
        var opts = ScratchpadConfig.GetRetentionOptions().ToList();
        CollectionAssert.Contains(opts, "None");
        CollectionAssert.Contains(opts, "Infinite");
    }
}
