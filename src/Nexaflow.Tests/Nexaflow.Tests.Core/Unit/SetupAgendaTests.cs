using System.ComponentModel.DataAnnotations;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// What a first run or an update asks: what is new, on a first run or an update until the run is recorded; an app-wide config
/// that says it must be set up, when it is new or was carried forward without what it now requires, never one carried forward
/// whole; and the startup workspace's AI, when it has no language model ready to answer.
/// </summary>
[TestClass]
[CoversNode("opt-setup-wizard")]
[CoversNode("opt-wizard-mandatory-configs")]
public class SetupAgendaTests
{
    [MandatorySetup]
    private sealed class Mandatory : IFeatureConfig
    {
        public string ConfigName   => "mandatory-test";
        public string FriendlyName => "Mandatory";
        [Required] public string Key { get; set; } = "";
    }

    private string _root = "";

    [TestInitialize]
    public void Init() => _root = TestTemp.CreateSubdirectory("nexaflow-agenda-").FullName;

    [TestCleanup]
    public void Done() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private static SetupAgenda Agenda(ConfigManager config, AppRuns runs, Workspace startup)
        => SetupAgenda.Of(startup, config, runs, TestRuntimes.Providers, TestRuntimes.SavedListIn(config, startup));

    [TestMethod]
    public void AnUpdate_IsWelcomed_UntilTheRunIsRecorded_AndAnUnchangedRelaunchIsNot()
    {
        var config = TestRuntimes.ConfigIn(_root);
        var shell  = new ShellConfig { LastRunVersion = "1.0" };
        var runs   = new AppRuns(shell, config, "2.0");
        Assert.IsTrue(runs.IsUpdate);

        runs.Stamp();

        Assert.IsFalse(runs.IsUpdate);
        Assert.AreEqual("2.0", shell.LastRunVersion);
        Assert.IsFalse(new AppRuns(shell, config, "2.0").IsUpdate, "the same version run again is no update");
    }

    [TestMethod]
    public void ANewMandatoryConfig_IsAskedFor_ButOneCarriedForwardWhole_IsNot()
    {
        var first = TestRuntimes.ConfigIn(_root);
        var asked = new Mandatory();
        first.Register(asked, asked.ConfigName);
        var runs  = new AppRuns(new ShellConfig(), first, "1.0");

        CollectionAssert.Contains(Agenda(first, runs, new Workspace { Name = "W" }).MandatoryConfigs.ToList(), asked);

        asked.Key = "sk-1";
        first.Save(asked, asked.ConfigName);
        var again = TestRuntimes.ConfigIn(_root);
        again.Register(new Mandatory(), asked.ConfigName);

        Assert.AreEqual(0, Agenda(again, new AppRuns(new ShellConfig(), again, "1.0"), new Workspace { Name = "W" })
                               .MandatoryConfigs.OfType<Mandatory>().Count());
    }

    [TestMethod]
    public void AFirstRun_IsWelcomed_AndAStartupWorkspaceWithNoModelReady_IsSetUp()
    {
        var config = TestRuntimes.ConfigIn(_root);
        var agenda = Agenda(config, new AppRuns(new ShellConfig(), config, "1.0"), new Workspace { Name = "W" });

        Assert.IsTrue(agenda.Welcomes, "nothing was read: the app has never run here");
        Assert.IsTrue(agenda.SetsUpWorkspace);
        Assert.AreEqual(0, agenda.AwaitedDownloads.Count, "models are offered after an update, not on a first run");
    }
}
