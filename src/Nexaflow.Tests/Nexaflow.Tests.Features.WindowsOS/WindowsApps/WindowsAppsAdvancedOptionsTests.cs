using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsApps.Models;
using Nexaflow.Features.WindowsApps.Services;
using Nexaflow.Features.WindowsApps.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsApps;

/// <summary>
/// The row actions Windows' "Add or remove programs" offers beyond Uninstall — Modify for a Win32
/// program, and Move / Advanced options for a Store package — plus everything inside the Advanced
/// options pane.
///
/// Nothing here touches a real installed app: the package backend is a fake
/// <see cref="IStoreAppOperations"/> that records what it was asked to do, and the background-execution
/// policy is an in-memory <see cref="IBackgroundAppAccess"/>. What matters is the shape: an action is
/// offered only where it applies, the destructive ones (Reset, removing an add-on) are
/// confirmation-gated and do nothing when declined, and a failure from the backend is reported rather
/// than swallowed.
///
/// The one exception is <see cref="RegistryBackgroundAppAccess_RoundTripsEachMode"/>, which exercises the
/// real registry mapping against a synthetic package family no app owns, and deletes it afterwards.
/// </summary>
[TestClass]
public class WindowsAppsAdvancedOptionsTests
{
    // ── Harness ───────────────────────────────────────────────────────────────

    /// <summary>Records what it was asked to do and hands back whatever the test staged.</summary>
    private sealed class FakeStoreSource(params InstalledApp[] apps)
        : IInstalledAppSource, IStoreAppOperations
    {
        public AppSource Source => AppSource.Store;

        public List<string> Calls { get; } = [];
        public AppOperationResult Result { get; set; } = AppOperationResult.Ok;
        public IReadOnlyList<AppVolume> Volumes { get; set; } = [];
        public IReadOnlyList<AppAddOn> AddOns { get; set; } = [];
        public int RunningProcesses { get; set; }

        public Task<IReadOnlyList<InstalledApp>> EnumerateAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InstalledApp>>(apps);

        public Task<AppOperationResult> UninstallAsync(InstalledApp app, CancellationToken ct)
        {
            Calls.Add($"uninstall:{app.Name}");
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<AppVolume>> GetVolumesAsync(CancellationToken ct) =>
            Task.FromResult(Volumes);

        public Task<AppOperationResult> MoveAsync(InstalledApp app, AppVolume target, CancellationToken ct)
        {
            Calls.Add($"move:{app.Name}→{target.MountPoint}");
            return Task.FromResult(Result);
        }

        public Task<AppOperationResult> RepairAsync(InstalledApp app, CancellationToken ct)
        {
            Calls.Add($"repair:{app.Name}");
            return Task.FromResult(Result);
        }

        public Task<AppOperationResult> ResetAsync(InstalledApp app, CancellationToken ct)
        {
            Calls.Add($"reset:{app.Name}");
            return Task.FromResult(Result);
        }

        public Task<int> TerminateAsync(InstalledApp app, CancellationToken ct)
        {
            Calls.Add($"terminate:{app.Name}");
            return Task.FromResult(RunningProcesses);
        }

        public Task<IReadOnlyList<AppAddOn>> GetAddOnsAsync(InstalledApp app, CancellationToken ct) =>
            Task.FromResult(AddOns);

        public Task<AppOperationResult> RemoveAddOnAsync(AppAddOn addOn, CancellationToken ct)
        {
            Calls.Add($"remove-addon:{addOn.Name}");
            return Task.FromResult(Result);
        }
    }

    /// <summary>A Win32 source that records the Modify it was asked to run instead of launching one.</summary>
    private sealed class FakeWin32Source(params InstalledApp[] apps) : IInstalledAppSource
    {
        public AppSource Source => AppSource.Win32;
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<InstalledApp>> EnumerateAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InstalledApp>>(apps);

        public Task<AppOperationResult> UninstallAsync(InstalledApp app, CancellationToken ct) =>
            Task.FromResult(AppOperationResult.Ok);

        public Task<AppOperationResult> ModifyAsync(InstalledApp app, CancellationToken ct)
        {
            Calls.Add($"modify:{app.Name}");
            return Task.FromResult(AppOperationResult.Ok);
        }
    }

    private sealed class FakeBackgroundAccess : IBackgroundAppAccess
    {
        private readonly Dictionary<string, BackgroundAppMode> _modes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Set false to make every write fail, as a locked-down policy store would.</summary>
        public bool Writable { get; set; } = true;

        public BackgroundAppMode Get(string packageFamilyName) =>
            _modes.TryGetValue(packageFamilyName, out var mode) ? mode : BackgroundAppMode.PowerOptimized;

        public bool Set(string packageFamilyName, BackgroundAppMode mode)
        {
            if (!Writable) return false;
            _modes[packageFamilyName] = mode;
            return true;
        }
    }

    /// <summary>A shell whose background queue runs inline, so a queued task has landed by the time we return.</summary>
    private static IShellServices InlineShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci =>
             {
                 ci.Arg<IBackgroundTask>().RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                 ci.Arg<Action<bool>>()?.Invoke(true);
             });
        return shell;
    }

    private static WindowsAppsViewModel Build(IShellServices shell,
                                              IReadOnlyList<IInstalledAppSource> sources,
                                              IBackgroundAppAccess? access = null)
        => new(shell, new InstalledAppsService(sources, access ?? new FakeBackgroundAccess()));

    private static InstalledApp StoreApp(string name = "Contoso Notes",
                                         string location = @"C:\Program Files\WindowsApps\Contoso")
        => new()
        {
            Name = name, Publisher = "Contoso Ltd", Version = "3.2.0.0",
            Source = AppSource.Store, InstallLocation = location,
            PackageFullName   = $"{name}_3.2.0.0_x64__abc123",
            PackageFamilyName = $"{name}_abc123",
        };

    private static InstalledApp Win32App(string name = "Fabrikam Suite",
                                         string? modifyPath = @"C:\Fabrikam\setup.exe /modify",
                                         bool modifyBlocked = false)
        => new()
        {
            Name = name, Version = "1.0", Source = AppSource.Win32,
            UninstallString = @"C:\Fabrikam\setup.exe /uninstall",
            ModifyPath = modifyPath, ModifyBlocked = modifyBlocked,
        };

    private static AppVolume Volume(string mount, bool system = false) =>
        new(Name: $"\\\\?\\Volume{{{mount[0]}}}\\", MountPoint: mount,
            PackageStorePath: mount + @"Program Files\WindowsApps",
            IsSystem: system, FreeBytes: 100L * 1024 * 1024 * 1024);

    /// <summary>Opens the pane for the single Store row and hands it back.</summary>
    private static AppAdvancedOptionsViewModel OpenPane(WindowsAppsViewModel vm)
    {
        vm.ShowAdvancedOptionsCommand.Execute(vm.Apps.Single(a => a.IsStore));
        Assert.IsNotNull(vm.AdvancedOptions, "precondition: the pane opened");
        return vm.AdvancedOptions!;
    }

    // ── Modify (Win32) ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("windowsapps-modify")]
    public void Modify_IsOfferedOnlyWhereTheVendorRegisteredAMaintenanceCommand()
    {
        var source = new FakeWin32Source(
            Win32App("Changeable"),
            Win32App("NoCommand", modifyPath: null),
            Win32App("Blocked", modifyBlocked: true));
        var vm = Build(InlineShell(), [source]);

        Assert.IsTrue(vm.Apps.Single(a => a.Name == "Changeable").CanModify);
        Assert.IsFalse(vm.Apps.Single(a => a.Name == "NoCommand").CanModify,
                       "no ModifyPath ⇒ there is nothing to launch");
        Assert.IsFalse(vm.Apps.Single(a => a.Name == "Blocked").CanModify,
                       "NoModify is the vendor saying don't offer this");
    }

    [TestMethod]
    [CoversNode("windowsapps-modify")]
    public void Modify_RunsTheVendorsCommand_AndRefusesTheEntriesThatDontOfferOne()
    {
        var source = new FakeWin32Source(Win32App("Changeable"), Win32App("Blocked", modifyBlocked: true));
        var vm = Build(InlineShell(), [source]);

        vm.ModifyCommand.Execute(vm.Apps.Single(a => a.Name == "Blocked"));
        CollectionAssert.DoesNotContain(source.Calls, "modify:Blocked");

        vm.ModifyCommand.Execute(vm.Apps.Single(a => a.Name == "Changeable"));
        CollectionAssert.Contains(source.Calls, "modify:Changeable");
    }

    [TestMethod]
    [CoversNode("windowsapps-modify")]
    public void Modify_IsNotOfferedForAStoreApp_WhichHasNoInstallerToReopen()
    {
        var vm = Build(InlineShell(), [new FakeStoreSource(StoreApp())]);

        Assert.IsFalse(vm.Apps.Single().CanModify);
    }

    // ── Opening the pane ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("windowsapps-advanced")]
    public void AdvancedOptions_OpenForAStorePackage_AndAreRefusedForAWin32Program()
    {
        var vm = Build(InlineShell(), [new FakeWin32Source(Win32App()), new FakeStoreSource(StoreApp())]);

        vm.ShowAdvancedOptionsCommand.Execute(vm.Apps.Single(a => !a.IsStore));
        Assert.IsFalse(vm.IsAdvancedOpen, "a Win32 program has no package to show advanced options for");

        vm.ShowAdvancedOptionsCommand.Execute(vm.Apps.Single(a => a.IsStore));
        Assert.IsTrue(vm.IsAdvancedOpen);
        Assert.AreEqual("Contoso Notes", vm.AdvancedOptions!.Item.Name);

        vm.CloseAdvancedOptionsCommand.Execute(null);
        Assert.IsFalse(vm.IsAdvancedOpen, "closing hands the width back to the list");
    }

    [TestMethod]
    [CoversNode("windowsapps-advanced")]
    public void AdvancedOptions_CloseButtonInThePane_DismissesItWithoutReachingPastItsOwnDataContext()
    {
        var vm = Build(InlineShell(), [new FakeStoreSource(StoreApp())]);
        var pane = OpenPane(vm);

        pane.CloseCommand.Execute(null);

        Assert.IsFalse(vm.IsAdvancedOpen);
    }

    [TestMethod]
    [CoversNode("windowsapps-move")]
    public void Move_OpensTheSamePane_WithItsMoveCardCalledOut()
    {
        var vm = Build(InlineShell(), [new FakeStoreSource(StoreApp())]);

        vm.ShowMoveCommand.Execute(vm.Apps.Single());

        Assert.IsTrue(vm.IsAdvancedOpen, "Move is the Advanced options pane, focused on its drive picker");
        Assert.IsTrue(vm.AdvancedOptions!.MoveHighlighted);
    }

    [TestMethod]
    [CoversNode("windowsapps-advanced")]
    public void AdvancedOptions_SurviveARescan_ButCloseOnceTheAppIsGone()
    {
        var app = StoreApp();
        var source = new FakeStoreSource(app);
        var vm = Build(InlineShell(), [source]);

        var pane = OpenPane(vm);
        var itemBeforeRescan = pane.Item;

        vm.RefreshCommand.Execute(null);
        Assert.IsTrue(vm.IsAdvancedOpen, "a rescan the app survived must not close the pane");
        Assert.AreNotSame(itemBeforeRescan, vm.AdvancedOptions!.Item,
                          "the pane re-points at the fresh row rather than holding a detached one");

        // Now the app is gone — which is what a successful uninstall looks like from here.
        var vanishing = new VanishingStoreSource(app);
        var vm2 = Build(InlineShell(), [vanishing]);
        OpenPane(vm2);
        vanishing.Vanish();
        vm2.RefreshCommand.Execute(null);

        Assert.IsFalse(vm2.IsAdvancedOpen, "the pane can't describe an app that is no longer installed");
    }

    /// <summary>A Store source whose single app disappears on demand, to model a completed uninstall.</summary>
    private sealed class VanishingStoreSource(InstalledApp app) : IInstalledAppSource, IStoreAppOperations
    {
        private bool _gone;
        public void Vanish() => _gone = true;

        public AppSource Source => AppSource.Store;

        public Task<IReadOnlyList<InstalledApp>> EnumerateAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InstalledApp>>(_gone ? [] : [app]);

        public Task<AppOperationResult> UninstallAsync(InstalledApp a, CancellationToken ct) =>
            Task.FromResult(AppOperationResult.Ok);
        public Task<IReadOnlyList<AppVolume>> GetVolumesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AppVolume>>([]);
        public Task<AppOperationResult> MoveAsync(InstalledApp a, AppVolume v, CancellationToken ct) =>
            Task.FromResult(AppOperationResult.Ok);
        public Task<AppOperationResult> RepairAsync(InstalledApp a, CancellationToken ct) =>
            Task.FromResult(AppOperationResult.Ok);
        public Task<AppOperationResult> ResetAsync(InstalledApp a, CancellationToken ct) =>
            Task.FromResult(AppOperationResult.Ok);
        public Task<int> TerminateAsync(InstalledApp a, CancellationToken ct) => Task.FromResult(0);
        public Task<IReadOnlyList<AppAddOn>> GetAddOnsAsync(InstalledApp a, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AppAddOn>>([]);
        public Task<AppOperationResult> RemoveAddOnAsync(AppAddOn a, CancellationToken ct) =>
            Task.FromResult(AppOperationResult.Ok);
    }

    // ── Background execution ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("windowsapps-advanced-background")]
    public void BackgroundPermission_OpensOnTheStoredChoice_AndWritesTheUsersBack()
    {
        var access = new FakeBackgroundAccess();
        access.Set("Contoso Notes_abc123", BackgroundAppMode.Never);
        var vm = Build(InlineShell(), [new FakeStoreSource(StoreApp())], access);

        var pane = OpenPane(vm);
        Assert.AreEqual(BackgroundAppMode.Never, pane.SelectedBackgroundMode!.Mode,
                        "the dropdown opens showing what is actually stored");

        pane.SelectedBackgroundMode = BackgroundModeOption.For(BackgroundAppMode.Always);

        Assert.AreEqual(BackgroundAppMode.Always, access.Get("Contoso Notes_abc123"),
                        "picking a mode writes it through immediately — there is no Apply button");
    }

    [TestMethod]
    [CoversNode("windowsapps-advanced-background")]
    public void BackgroundPermission_ReadingTheStoredChoice_IsNotWrittenStraightBack()
    {
        var access = new WriteCountingAccess();
        var vm = Build(InlineShell(), [new FakeStoreSource(StoreApp())], access);

        OpenPane(vm);

        Assert.AreEqual(0, access.Writes,
                        "loading the pane must not look like the user chose the value it just read");
    }

    private sealed class WriteCountingAccess : IBackgroundAppAccess
    {
        public int Writes { get; private set; }
        public BackgroundAppMode Get(string packageFamilyName) => BackgroundAppMode.PowerOptimized;
        public bool Set(string packageFamilyName, BackgroundAppMode mode) { Writes++; return true; }
    }

    [TestMethod]
    [CoversNode("windowsapps-advanced-background")]
    public void BackgroundPermission_ThatCouldntBeWritten_IsReportedRatherThanAssumed()
    {
        var shell = InlineShell();
        var access = new FakeBackgroundAccess { Writable = false };
        var vm = Build(shell, [new FakeStoreSource(StoreApp())], access);

        var pane = OpenPane(vm);
        shell.ClearReceivedCalls();

        pane.SelectedBackgroundMode = BackgroundModeOption.For(BackgroundAppMode.Never);

        shell.Received().ShowError(Arg.Is<string>(m => m.Contains("Contoso Notes")));
        Assert.IsNull(pane.Status, "a failed write must not leave a success message behind");
    }

    /// <summary>
    /// The real policy store, exercised against a package family no app owns so nothing on the machine
    /// is retuned. Deleted again in <c>finally</c>.
    /// </summary>
    [TestMethod]
    [CoversNode("windowsapps-advanced-background")]
    public void RegistryBackgroundAppAccess_RoundTripsEachMode()
    {
        const string root = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
        var family = "NexaflowTest.Synthetic_" + Guid.NewGuid().ToString("N")[..12];
        var access = new RegistryBackgroundAppAccess();
        try
        {
            Assert.AreEqual(BackgroundAppMode.PowerOptimized, access.Get(family),
                            "an app the user never touched is on the Windows-decides default");

            foreach (var mode in new[] { BackgroundAppMode.Always, BackgroundAppMode.Never,
                                         BackgroundAppMode.PowerOptimized })
            {
                Assert.IsTrue(access.Set(family, mode), $"HKCU write for {mode} should succeed");
                Assert.AreEqual(mode, access.Get(family), $"{mode} should read back as itself");
            }
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(root, writable: true);
            key?.DeleteSubKeyTree(family, throwOnMissingSubKey: false);
        }
    }

    // ── Terminate / Repair / Reset ────────────────────────────────────────────

    [TestMethod]
    [CoversNode("windowsapps-advanced-terminate")]
    public void Terminate_SaysHowManyProcessesItStopped_AndSaysSoWhenThereWereNone()
    {
        var source = new FakeStoreSource(StoreApp()) { RunningProcesses = 3 };
        var vm = Build(InlineShell(), [source]);
        var pane = OpenPane(vm);

        pane.TerminateCommand.Execute(null);
        CollectionAssert.Contains(source.Calls, "terminate:Contoso Notes");
        StringAssert.Contains(pane.Status, "3");

        source.RunningProcesses = 0;
        pane.TerminateCommand.Execute(null);
        StringAssert.Contains(pane.Status, "wasn't running");
    }

    [TestMethod]
    [CoversNode("windowsapps-advanced-repair")]
    public void Repair_RunsWithoutAConfirmation_BecauseItLeavesTheAppsDataAlone()
    {
        var shell = InlineShell();
        var source = new FakeStoreSource(StoreApp());
        var vm = Build(shell, [source]);
        var pane = OpenPane(vm);
        shell.ClearReceivedCalls();

        pane.RepairCommand.Execute(null);

        CollectionAssert.Contains(source.Calls, "repair:Contoso Notes");
        shell.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default!, default);
        StringAssert.Contains(pane.Status, "repaired");
    }

    [TestMethod]
    [CoversNode("windowsapps-advanced-repair")]
    public void Repair_ThatFailed_IsReportedRatherThanClaimedAsSuccess()
    {
        var shell = InlineShell();
        var source = new FakeStoreSource(StoreApp())
        {
            Result = AppOperationResult.Fail("The package is in use."),
        };
        var vm = Build(shell, [source]);
        var pane = OpenPane(vm);
        shell.ClearReceivedCalls();

        pane.RepairCommand.Execute(null);

        shell.Received().ShowError(Arg.Is<string>(m => m.Contains("The package is in use.")));
        Assert.IsNull(pane.Status);
        Assert.IsFalse(pane.IsBusy, "the pane must not stay locked after a failure");
    }

    [TestMethod]
    [CoversNode("windowsapps-advanced-reset")]
    public async Task Reset_AsksFirst_AndDoesNothingWhenDeclined()
    {
        var shell = InlineShell();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                           Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));
        var source = new FakeStoreSource(StoreApp());
        var vm = Build(shell, [source]);
        var pane = OpenPane(vm);

        await pane.ResetCommand.ExecuteAsync(null);

        await shell.Received().ConfirmAsync(
            Arg.Is<string>(t => t.Contains("Contoso Notes")),
            Arg.Is<string>(m => m.Contains("permanently deletes")),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        CollectionAssert.DoesNotContain(source.Calls, "reset:Contoso Notes");
    }

    [TestMethod]
    [CoversNode("windowsapps-advanced-reset")]
    public async Task Reset_OnceConfirmed_WipesTheAppsDataAndReRegistersIt()
    {
        var shell = InlineShell();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                           Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(true));
        var source = new FakeStoreSource(StoreApp());
        var vm = Build(shell, [source]);
        var pane = OpenPane(vm);

        await pane.ResetCommand.ExecuteAsync(null);

        CollectionAssert.Contains(source.Calls, "reset:Contoso Notes");
        StringAssert.Contains(pane.Status, "reset");
    }

    // ── Move ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("windowsapps-move")]
    public void Move_OpensOnTheDriveTheAppIsOn_AndWontMoveItThere()
    {
        var source = new FakeStoreSource(StoreApp()) { Volumes = [Volume(@"C:\", system: true), Volume(@"D:\")] };
        var vm = Build(InlineShell(), [source]);

        var pane = OpenPane(vm);

        Assert.AreEqual(@"C:\", pane.SelectedVolume!.MountPoint, "the dropdown reads as 'where it is now'");
        Assert.IsFalse(pane.CanMove, "moving an app to the drive it already lives on is a no-op");
        Assert.IsTrue(pane.HasMoveTarget);

        // What the dropdown actually renders (DisplayMemberPath="Display").
        Assert.AreEqual("C: (system) — 100.0 GB free", pane.SelectedVolume.Display);
        Assert.AreEqual("D: — 100.0 GB free", pane.Volumes.Single(v => v.MountPoint == @"D:\").Display);
    }

    [TestMethod]
    [CoversNode("windowsapps-move")]
    public void Move_ToAnotherDrive_RelocatesThePackage()
    {
        var source = new FakeStoreSource(StoreApp()) { Volumes = [Volume(@"C:\", system: true), Volume(@"D:\")] };
        var vm = Build(InlineShell(), [source]);
        var pane = OpenPane(vm);

        pane.SelectedVolume = pane.Volumes.Single(v => v.MountPoint == @"D:\");
        Assert.IsTrue(pane.CanMove);

        pane.MoveCommand.Execute(null);

        CollectionAssert.Contains(source.Calls, @"move:Contoso Notes→D:\");
        StringAssert.Contains(pane.Status, "D:");
    }

    [TestMethod]
    [CoversNode("windowsapps-move")]
    public void Move_OnAOneDriveMachine_SaysThereIsNowhereToMoveTo()
    {
        var source = new FakeStoreSource(StoreApp()) { Volumes = [Volume(@"C:\", system: true)] };
        var vm = Build(InlineShell(), [source]);

        var pane = OpenPane(vm);

        Assert.IsFalse(pane.HasMoveTarget, "one volume ⇒ the picker is replaced by an explanation");
        Assert.IsFalse(pane.CanMove);
    }

    // ── Add-ons ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("windowsapps-advanced-addons")]
    public void AddOns_AreListedForTheApp_AndEmptinessIsOnlyClaimedAfterTheScan()
    {
        var source = new FakeStoreSource(StoreApp())
        {
            AddOns =
            [
                new AppAddOn("Extra Levels", "Contoso Ltd", "1.2", "Extra_1.2_x64__abc123", 5_242_880),
                new AppAddOn("Voice Pack", null, null, "Voice_1.0_x64__abc123", null),
            ],
        };
        var vm = Build(InlineShell(), [source]);

        var pane = OpenPane(vm);

        Assert.IsTrue(pane.AddOnsScanned, "the empty state must not be claimed before we have looked");
        CollectionAssert.AreEqual(new[] { "Extra Levels", "Voice Pack" },
                                  pane.AddOns.Select(a => a.Name).ToArray());
        Assert.AreEqual("5.0 MB", pane.AddOns[0].SizeText);
        Assert.AreEqual("Contoso Ltd · 1.2", pane.AddOns[0].Subtitle);
        Assert.AreEqual("—", pane.AddOns[1].SizeText, "an unmeasured add-on shows a dash, not 0 bytes");
        Assert.AreEqual(string.Empty, pane.AddOns[1].Subtitle);
    }

    [TestMethod]
    [CoversNode("windowsapps-advanced-addons")]
    public async Task RemovingAnAddOn_AsksFirst_AndSaysTheAppItselfStays()
    {
        var shell = InlineShell();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                           Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));
        var addOn = new AppAddOn("Extra Levels", "Contoso Ltd", "1.2", "Extra_1.2_x64__abc123", 1024);
        var source = new FakeStoreSource(StoreApp()) { AddOns = [addOn] };
        var vm = Build(shell, [source]);
        var pane = OpenPane(vm);

        await pane.RemoveAddOnCommand.ExecuteAsync(addOn);

        await shell.Received().ConfirmAsync(
            Arg.Is<string>(t => t.Contains("Extra Levels")),
            Arg.Is<string>(m => m.Contains("Contoso Notes") && m.Contains("stays installed")),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        CollectionAssert.DoesNotContain(source.Calls, "remove-addon:Extra Levels");
        Assert.AreEqual(1, pane.AddOns.Count, "a declined removal leaves the list alone");
    }

    [TestMethod]
    [CoversNode("windowsapps-advanced-addons")]
    public async Task RemovingAnAddOn_OnceConfirmed_DropsItFromTheListButKeepsTheApp()
    {
        var shell = InlineShell();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                           Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(true));
        var addOn = new AppAddOn("Extra Levels", "Contoso Ltd", "1.2", "Extra_1.2_x64__abc123", 1024);
        var source = new FakeStoreSource(StoreApp()) { AddOns = [addOn] };
        var vm = Build(shell, [source]);
        var pane = OpenPane(vm);

        await pane.RemoveAddOnCommand.ExecuteAsync(addOn);

        CollectionAssert.Contains(source.Calls, "remove-addon:Extra Levels");
        Assert.AreEqual(0, pane.AddOns.Count);
        Assert.AreEqual(1, vm.Apps.Count, "the app the add-on extended is untouched");
    }
}
