using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsApps.Models;
using Nexaflow.Features.WindowsApps.Services;
using Nexaflow.Features.WindowsApps.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsApps;

/// <summary>
/// The Installed Apps tab's list chrome and row actions, driven headlessly off an in-memory app source —
/// no registry scan, no package manager, nothing uninstalled. What matters here is the safety shape: both
/// destructive row actions are confirmation-gated and must do nothing when declined, "Remove from list" is
/// refused outright for an app that isn't orphaned, and Open Location refuses a path that isn't there.
/// The sort/filter behaviour is the list's own, asserted through the collection view the ListView binds.
///
/// The AI surface is covered by <see cref="WindowsAppsViewModelTests"/>.
/// </summary>
[TestClass]
public class WindowsAppsSurfaceTests
{
    // ── Harness ───────────────────────────────────────────────────────────────

    /// <summary>A VM whose scan runs inline off <paramref name="apps"/>, so the list is populated on return.</summary>
    private static WindowsAppsViewModel BuildLoaded(IShellServices shell, params InstalledApp[] apps)
    {
        shell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci =>
             {
                 var task = ci.Arg<IBackgroundTask>();
                 task.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                 ci.Arg<Action<bool>>()?.Invoke(true);
             });

        return new WindowsAppsViewModel(shell, new InstalledAppsService([new FakeSource(apps)]));
    }

    private static WindowsAppsViewModel BuildLoaded(params InstalledApp[] apps)
        => BuildLoaded(Substitute.For<IShellServices>(), apps);

    private sealed class FakeSource(IReadOnlyList<InstalledApp> apps) : IInstalledAppSource
    {
        public AppSource Source => AppSource.Win32;
        public Task<IReadOnlyList<InstalledApp>> EnumerateAsync(CancellationToken ct) => Task.FromResult(apps);
        public Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct) =>
            Task.FromResult(UninstallResult.Ok);
    }

    private static InstalledApp App(string name, string? publisher = null, long? size = null,
                                    AppSource source = AppSource.Win32, string? location = null,
                                    string? uninstall = "x.exe")
        => new()
        {
            Name = name, Publisher = publisher, Version = "1.0", SizeBytes = size,
            Source = source, InstallLocation = location, UninstallString = uninstall,
        };

    /// <summary>The collection view the ListView actually renders — where sort and filter land.</summary>
    private static IEnumerable<InstalledAppItem> Visible(WindowsAppsViewModel vm)
        => CollectionViewSource.GetDefaultView(vm.Apps).Cast<InstalledAppItem>();

    // ── Header bar ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("windowsapps-loading-status")]
    public void LoadingIndicator_ClearsOnceTheScanFinishes_AndTheCountIsTheListLength()
    {
        var vm = BuildLoaded(App("Alpha"), App("Beta"));

        Assert.IsFalse(vm.IsLoading, "the 'Discovering…' indicator gives way to the count");
        Assert.AreEqual(2, vm.Apps.Count);
    }

    [TestMethod]
    [CoversNode("windowsapps-refresh")]
    public void Refresh_RerunsTheScan_AndReplacesTheListRatherThanAppending()
    {
        var vm = BuildLoaded(App("Alpha"), App("Beta"));
        Assert.AreEqual(2, vm.Apps.Count);

        vm.RefreshCommand.Execute(null);

        Assert.AreEqual(2, vm.Apps.Count, "a rescan replaces the list — it must not double it");
        Assert.IsFalse(vm.IsLoading);
    }

    // ── List columns + sort ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("windowsapps-columns")]
    public void Columns_RenderTheirValues_WithPlaceholdersForWhatAnAppDidntReport()
    {
        var vm = BuildLoaded(
            new InstalledApp { Name = "Contoso", Publisher = "Contoso Ltd", Version = "2.1",
                               InstallDate = new DateTime(2026, 3, 4), SizeBytes = 1024 * 1024,
                               Source = AppSource.Store },
            App("Bare"));

        var contoso = vm.Apps.Single(a => a.Name == "Contoso");
        Assert.AreEqual("Contoso Ltd", contoso.Publisher);
        Assert.AreEqual("2.1", contoso.Version);
        Assert.AreEqual("2026-03-04", contoso.InstallDateText);
        Assert.AreEqual("Store", contoso.SourceLabel);

        var bare = vm.Apps.Single(a => a.Name == "Bare");
        Assert.AreEqual(string.Empty, bare.Publisher, "a missing publisher renders blank, not 'null'");
        Assert.AreEqual("—", bare.InstallDateText, "a missing install date renders as a dash");
        Assert.AreEqual("Win32", bare.SourceLabel);
    }

    [TestMethod]
    [CoversNode("windowsapps-sort")]
    public void SortBy_OrdersTheList_AndClickingTheSameColumnAgainReversesIt()
    {
        var vm = BuildLoaded(App("Charlie"), App("Alpha"), App("Bravo"));
        Assert.AreEqual(nameof(InstalledAppItem.Name), vm.SortColumn, "the list opens sorted by name");
        Assert.IsTrue(vm.SortAscending);

        vm.SortByCommand.Execute(nameof(InstalledAppItem.Name));   // clicking the active header flips it
        Assert.IsFalse(vm.SortAscending);
        CollectionAssert.AreEqual(new[] { "Charlie", "Bravo", "Alpha" },
                                  Visible(vm).Select(a => a.Name).ToArray());

        vm.SortByCommand.Execute(nameof(InstalledAppItem.Name));
        Assert.IsTrue(vm.SortAscending, "the same header toggles back");
        CollectionAssert.AreEqual(new[] { "Alpha", "Bravo", "Charlie" },
                                  Visible(vm).Select(a => a.Name).ToArray());
    }

    [TestMethod]
    [CoversNode("windowsapps-sort")]
    public void SortBy_ADifferentColumn_StartsAscendingAgain()
    {
        var vm = BuildLoaded(App("Alpha", size: 30), App("Bravo", size: 10));

        vm.SortByCommand.Execute(nameof(InstalledAppItem.Name));
        vm.SortByCommand.Execute(nameof(InstalledAppItem.Name));      // now descending
        vm.SortByCommand.Execute(nameof(InstalledAppItem.SizeBytes)); // switch column

        Assert.AreEqual(nameof(InstalledAppItem.SizeBytes), vm.SortColumn);
        Assert.IsTrue(vm.SortAscending, "a new column resets to ascending rather than inheriting the flip");
    }

    [TestMethod]
    [CoversNode("windowsapps-sort")]
    public void SortBy_AnEmptyColumn_IsIgnored()
    {
        var vm = BuildLoaded(App("Alpha"));
        var before = vm.SortColumn;

        vm.SortByCommand.Execute(string.Empty);

        Assert.AreEqual(before, vm.SortColumn);
    }

    // ── Name filter (driven by the shell input bar) ───────────────────────────

    [TestMethod]
    [CoversNode("windowsapps-filter")]
    public void Filter_NarrowsTheListCaseInsensitively_AndClearingItRestoresEverything()
    {
        var vm = BuildLoaded(App("Contoso Editor"), App("Fabrikam Player"), App("Contoso Viewer"));

        Assert.IsNull(vm.SetFilter("contoso"), "the query handler swallows the input silently");
        CollectionAssert.AreEquivalent(new[] { "Contoso Editor", "Contoso Viewer" },
                                       Visible(vm).Select(a => a.Name).ToArray());

        vm.SetFilter("   ");
        Assert.AreEqual(3, Visible(vm).Count(), "a blank filter shows everything again");
    }

    [TestMethod]
    [CoversNode("windowsapps-filter")]
    public void Filter_MatchesOnNameOnly_NotPublisher()
    {
        var vm = BuildLoaded(App("Editor", publisher: "Contoso Ltd"), App("Contoso Player"));

        vm.SetFilter("Contoso");

        CollectionAssert.AreEqual(new[] { "Contoso Player" }, Visible(vm).Select(a => a.Name).ToArray());
    }

    // ── Row actions ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("windowsapps-openlocation")]
    public void OpenLocation_OpensAFileExplorerTabThere_AndIgnoresAMissingFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaapps_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var shell = Substitute.For<IShellServices>();
            var vm = BuildLoaded(shell, App("Real", location: dir), App("Gone", location: @"C:\no\such\folder"));

            vm.OpenLocationCommand.Execute(vm.Apps.Single(a => a.Name == "Gone"));
            shell.DidNotReceiveWithAnyArgs().OpenTab(default!, default, default, default);

            vm.OpenLocationCommand.Execute(vm.Apps.Single(a => a.Name == "Real"));
            shell.Received(1).OpenTab("FileSystem",
                Arg.Is<Dictionary<string, string>>(p => p["mode"] == "path" && p["path"] == dir),
                Arg.Any<IPageView?>());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("elevated-uninstaller")]
    public async Task Uninstall_AsksFirst_AndDoesNothingWhenDeclined()
    {
        var shell = Substitute.For<IShellServices>();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));                       // the user backs out
        var vm = BuildLoaded(shell, App("Contoso Editor"));
        shell.ClearReceivedCalls();

        await vm.UninstallCommand.ExecuteAsync(vm.Apps.Single());

        await shell.Received().ConfirmAsync(Arg.Is<string>(t => t.Contains("Contoso Editor")),
                                            Arg.Any<string>(), Arg.Any<CancellationToken>());
        shell.DidNotReceiveWithAnyArgs().QueueBackgroundTask(default!, default, default);
    }

    [TestMethod]
    [CoversNode("elevated-uninstaller")]
    public async Task Uninstall_WarnsDifferentlyForAStoreApp_ThanForAWin32Uninstaller()
    {
        var shell = Substitute.For<IShellServices>();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));
        var vm = BuildLoaded(shell, App("Store App", source: AppSource.Store), App("Win32 App"));
        shell.ClearReceivedCalls();

        await vm.UninstallCommand.ExecuteAsync(vm.Apps.Single(a => a.Name == "Store App"));
        await shell.Received().ConfirmAsync(Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("Store app")), Arg.Any<CancellationToken>());

        await vm.UninstallCommand.ExecuteAsync(vm.Apps.Single(a => a.Name == "Win32 App"));
        await shell.Received().ConfirmAsync(Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("own uninstaller")), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [CoversNode("elevated-uninstaller")]
    public async Task Uninstall_WithNoRow_IsANoOp()
    {
        var shell = Substitute.For<IShellServices>();
        var vm = BuildLoaded(shell, App("Contoso"));
        shell.ClearReceivedCalls();

        await vm.UninstallCommand.ExecuteAsync(null);

        await shell.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default!, default);
    }

    [TestMethod]
    [CoversNode("cleanup-broken")]
    public async Task RemoveFromList_OnlyOffersItselfForAnOrphanedEntry()
    {
        var shell = Substitute.For<IShellServices>();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));
        // A healthy Win32 entry (has an uninstaller) is not a leftover — the action must refuse it.
        var vm = BuildLoaded(shell, App("Healthy", location: Path.GetTempPath()));
        shell.ClearReceivedCalls();

        await vm.DeleteRecordCommand.ExecuteAsync(vm.Apps.Single());

        Assert.IsFalse(vm.Apps.Single().IsOrphaned, "precondition: this entry is not a leftover");
        await shell.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default!, default);
    }

    [TestMethod]
    [CoversNode("cleanup-broken")]
    public async Task RemoveFromList_ForALeftover_ConfirmsAndSaysItDeletesNoFiles()
    {
        var shell = Substitute.For<IShellServices>();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));                       // declined — nothing is removed
        // No uninstaller and a location that doesn't exist ⇒ a leftover registry entry.
        var vm = BuildLoaded(shell, App("Ghost", location: @"C:\no\such\folder", uninstall: null));
        shell.ClearReceivedCalls();
        var ghost = vm.Apps.Single();
        Assert.IsTrue(ghost.IsOrphaned, "precondition: this entry is a leftover");

        await vm.DeleteRecordCommand.ExecuteAsync(ghost);

        await shell.Received().ConfirmAsync(
            Arg.Is<string>(t => t.Contains("Ghost")),
            Arg.Is<string>(m => m.Contains("does not delete any files")),
            Arg.Any<CancellationToken>());
        shell.DidNotReceiveWithAnyArgs().QueueBackgroundTask(default!, default, default);
    }

    // ── Discovery sources + size pass ─────────────────────────────────────────

    [TestMethod]
    [CoversNode("windows-store-apps")]
    [CoversNode("registry-apps")]
    public void SourceLabel_DistinguishesStoreAppsFromRegistryApps()
    {
        var vm = BuildLoaded(App("Packaged", source: AppSource.Store), App("Classic"));

        Assert.AreEqual("Store", vm.Apps.Single(a => a.Name == "Packaged").SourceLabel);
        Assert.AreEqual("Win32", vm.Apps.Single(a => a.Name == "Classic").SourceLabel);
    }

    /// <summary>
    /// Pass 2 measures on-disk size only for rows that reported none <i>and</i> have a folder to walk —
    /// a row with no install location must never be queued (there is nothing to measure), and a row that
    /// already reported a size must be left alone rather than re-walked.
    /// </summary>
    [TestMethod]
    [CoversNode("windowsapps-size-pass")]
    public void SizePass_MeasuresOnlyTheRowsWithAFolderAndNoReportedSize()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaapps_size_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "payload.bin"), new byte[3000]);
        try
        {
            var vm = BuildLoaded(
                App("Reported", size: 4096, location: dir),
                App("Measurable", location: dir),
                App("NoFolder"));

            Assert.AreEqual(4096, vm.Apps.Single(a => a.Name == "Reported").SizeBytes,
                            "a self-reported size is kept, not re-measured");
            Assert.AreEqual(3000, vm.Apps.Single(a => a.Name == "Measurable").SizeBytes,
                            "pass 2 fills in the missing size from the folder");

            var noFolder = vm.Apps.Single(a => a.Name == "NoFolder");
            Assert.IsFalse(noFolder.HasLocation);
            Assert.IsNull(noFolder.SizeBytes, "nothing to walk ⇒ the row stays blank rather than reading 0");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("windowsapps-size-pass")]
    public void SizePass_PatchedSize_RaisesAChangeSoTheCellRedraws()
    {
        var vm = BuildLoaded(App("Bare"));
        var item = vm.Apps.Single();
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.SizeBytes = 2048;

        Assert.AreEqual("2.0 KB", item.SizeText);
        CollectionAssert.Contains(raised, nameof(InstalledAppItem.SizeText));
    }
}
