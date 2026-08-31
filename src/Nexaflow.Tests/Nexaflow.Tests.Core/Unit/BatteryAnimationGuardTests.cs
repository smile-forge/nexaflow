using Nexaflow.Core;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The shell half of "disable background animations when on battery": the setting and the machine's
/// power state are combined here and pushed onto <see cref="BackgroundAnimationPolicy"/>, which is what
/// a <c>ThemedRegion</c> actually consults.
/// <para>These run on whatever machine CI happens to use, so they assert only what holds on both a
/// desktop and a laptop: with the setting off scenes are always allowed, and with it on the answer
/// tracks the power state rather than being stuck. The scene side of the contract is pinned by
/// <c>ThemedRegionScenePolicyTests</c> in Tests.Visuals, which can flip the policy directly.</para>
/// </summary>
[TestClass]
[DoNotParallelize]   // the policy it drives is process-wide
[CoversNode("theme-scene-battery-policy")]
public class BatteryAnimationGuardTests
{
    [TestCleanup]
    public void Cleanup() => BatteryAnimationGuard.SetDisableOnBattery(false);

    [TestMethod]
    public void Unit_SettingOff_AlwaysAllowsScenes()
    {
        BatteryAnimationGuard.SetDisableOnBattery(true);
        BatteryAnimationGuard.SetDisableOnBattery(false);

        Assert.IsTrue(BackgroundAnimationPolicy.ScenesEnabled,
            "With the setting off the power state is irrelevant - scenes must always be allowed.");
    }

    /// <summary>
    /// A desktop reports "no battery" and must be unaffected by the setting; a laptop on mains is
    /// likewise unaffected. Only a machine actually running off its battery suppresses scenes - so the
    /// invariant that holds everywhere is that turning the setting on never suppresses a plugged-in
    /// machine, and turning it back off always restores.
    /// </summary>
    [TestMethod]
    public void Unit_SettingOn_TracksPowerStateAndIsReversible()
    {
        BatteryAnimationGuard.SetDisableOnBattery(true);
        bool suppressedOnBattery = !BackgroundAnimationPolicy.ScenesEnabled;

        BatteryAnimationGuard.SetDisableOnBattery(false);
        Assert.IsTrue(BackgroundAnimationPolicy.ScenesEnabled,
            "Turning the setting back off must restore scenes whatever the power state was.");

        BatteryAnimationGuard.SetDisableOnBattery(true);
        Assert.AreEqual(suppressedOnBattery, !BackgroundAnimationPolicy.ScenesEnabled,
            "The guard must be a pure function of (setting, power state) - re-applying it can't drift.");
    }

    [TestMethod]
    public void Unit_ShellConfig_DefaultsToSavingBatteryByDefault()
    {
        Assert.IsTrue(new ShellConfig().DisableAnimationsOnBattery,
            "A laptop should save power out of the box; on a desktop the setting is a no-op either way.");
    }
}
