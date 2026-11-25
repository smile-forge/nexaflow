using Nexaflow.AiHost.Contracts;
using Nexaflow.Core;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Core.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A setting decided outside the app shows in Settings as decided there — its value, read-only, and why — and saving leaves the app's
/// own value in the config, for when the app decides again. The machine's AI host service decides the share of memory models on the CPU
/// may hold while the app is joined to it.
/// </summary>
[TestClass]
[CoversNode("opt-save-validate")]
public class ConfigRulingsTests
{
    private sealed class Rated
    {
        public int Share { get; set; } = 70;
        public int Other { get; set; } = 3;
    }

    private sealed class RulesShare(int value, string why) : IConfigRulings
    {
        public ConfigRuling? RulingFor(object config, string propertyName)
            => config is Rated && propertyName == nameof(Rated.Share) ? new ConfigRuling(value, why) : null;
    }

    /// <summary>A configuration of the test's own, so a ruling it adds rules on no other test's settings.</summary>
    private static ConfigManager NewConfig() => TestRuntimes.ConfigIn(TestTemp.CreateSubdirectory("rulings-").FullName);

    [TestMethod]
    public void ARuledSetting_ShowsTheRuling_ReadOnly_SayingWhy_AndSavingKeepsTheAppsOwn()
    {
        var config = NewConfig();
        using var ruled = config.AddRulings(new RulesShare(40, "set by the service"));
        var real = new Rated();
        var section = new ConfigEditViewModel(real, "test-rated", "Rated", config);
        var share = section.Properties.Single(p => p.PropertyName == nameof(Rated.Share));
        var other = section.Properties.Single(p => p.PropertyName == nameof(Rated.Other));

        Assert.AreEqual((40, false, "set by the service"), (share.Value, share.IsEnabled, share.Note));
        Assert.AreEqual((true, false), (other.IsEnabled, other.IsRuled), "what the app decides stays the user's");
        Assert.IsFalse(section.HasChanges, "showing a ruling changes nothing");

        other.Value = "5";
        section.ApplyToReal();
        Assert.AreEqual((70, 5), (real.Share, real.Other), "the app's own share is kept for when it decides again");
    }

    [TestMethod]
    public void ARulingTakenAway_LeavesTheSettingTheUsersAgain()
    {
        var config = NewConfig();
        var ruled = config.AddRulings(new RulesShare(40, "set by the service"));
        ruled.Dispose();

        var share = new ConfigEditViewModel(new Rated(), "test-rated", "Rated", config).Properties.Single(p => p.PropertyName == nameof(Rated.Share));

        Assert.AreEqual((70, true, false), (share.Value, share.IsEnabled, share.IsRuled));
    }

    [TestMethod]
    public void JoinedToTheService_TheShareOfMemoryIsTheServices_ElseTheApps()
    {
        HostMemory? service = HostMemory.Of(64L << 30, 35);
        var rulings = new AiHostSettingRulings(() => service);
        var preload = new ModelPreloadConfig();

        var ruling = rulings.RulingFor(preload, nameof(ModelPreloadConfig.MemoryForModelsPercent));
        Assert.AreEqual(35, ruling?.Value, "the share the service holds its models to");
        Assert.IsFalse(string.IsNullOrWhiteSpace(ruling?.Reason));
        Assert.IsNull(rulings.RulingFor(preload, nameof(ModelPreloadConfig.IdleMinutes)), "the app still decides when it lets a model go");

        service = null;   // a host of the app's own
        Assert.IsNull(rulings.RulingFor(preload, nameof(ModelPreloadConfig.MemoryForModelsPercent)));
    }
}
