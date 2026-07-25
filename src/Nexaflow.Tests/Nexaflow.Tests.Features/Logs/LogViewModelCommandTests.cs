using System;
using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Logs;

/// <summary>
/// The filter side panel: the regex pattern box and its clear button, and the time-range Apply / Clear
/// pair. These decide which lines the user can still see, so a silently-dropped bound or a filter that
/// survives its own clear is a correctness bug, not a cosmetic one.
/// <para>
/// Everything here runs synchronously with no file load, no <c>TextDocument</c> mutation and no watcher.
/// The tail/head reader is covered by <see cref="LogViewModelTests"/>, the toolbar and status bar by
/// <see cref="LogSurfaceTests"/>, and the controls end-to-end by the UI journey.
/// </para>
/// </summary>
[TestClass]
public class LogViewModelCommandTests
{
    private static LogViewModel Make()
        => new("nonexistent.log", Substitute.For<IShellServices>()) { IsMonitoring = false };

    [TestMethod]
    [CoversNode("log-viewer-filter")]
    public void FilterRegex_ValidPattern_ActivatesFilter()
    {
        using var vm = Make();
        Assert.IsFalse(vm.IsFilterActive);

        vm.FilterRegex = "ERROR|WARN";

        Assert.IsTrue(vm.IsFilterActive);
        Assert.IsNotNull(vm.ActiveFilter);
        Assert.IsTrue(vm.ActiveFilter!.IsMatch("2024 ERROR boom"));
    }

    [TestMethod]
    [CoversNode("log-viewer-filter")]
    public void FilterRegex_InvalidPattern_LeavesFilterInactive()
    {
        using var vm = Make();

        vm.FilterRegex = "([unterminated";   // invalid regex — swallowed, filter stays off

        Assert.IsFalse(vm.IsFilterActive);
        Assert.IsNull(vm.ActiveFilter);
    }

    [TestMethod]
    [CoversNode("log-viewer-filter")]
    public void FilterRegex_Empty_ClearsFilter()
    {
        using var vm = Make();
        vm.FilterRegex = "INFO";
        Assert.IsTrue(vm.IsFilterActive);

        vm.FilterRegex = string.Empty;

        Assert.IsFalse(vm.IsFilterActive);
        Assert.IsNull(vm.ActiveFilter);
    }

    [TestMethod]
    [CoversNode("log-viewer-filter-clear")]
    public void ClearFilter_ResetsRegexAndActiveState()
    {
        using var vm = Make();
        vm.FilterRegex = "INFO";
        Assert.IsTrue(vm.IsFilterActive);

        vm.ClearFilterCommand.Execute(null);

        Assert.AreEqual(string.Empty, vm.FilterRegex);
        Assert.IsFalse(vm.IsFilterActive);
        Assert.IsNull(vm.ActiveFilter);
    }

    [TestMethod]
    [CoversNode("log-viewer-time-apply")]
    public void ApplyTimeFilter_WithDateAndTime_ComputesBounds()
    {
        using var vm = Make();
        vm.FilterStartDate = new DateTime(2024, 1, 1);
        vm.FilterStartTime = "08:30:00";
        vm.FilterEndDate   = new DateTime(2024, 1, 2);
        vm.FilterEndTime   = "17:45:00";

        vm.ApplyTimeFilterCommand.Execute(null);

        Assert.AreEqual(new DateTime(2024, 1, 1, 8, 30, 0), vm.FilterStart);
        Assert.AreEqual(new DateTime(2024, 1, 2, 17, 45, 0), vm.FilterEnd);
    }

    [TestMethod]
    [CoversNode("log-viewer-time-apply")]
    public void ApplyTimeFilter_EndDateWithoutTime_ExpandsToEndOfDay()
    {
        using var vm = Make();
        vm.FilterEndDate = new DateTime(2024, 1, 1);
        vm.FilterEndTime = string.Empty;   // no time → filter should cover the whole day

        vm.ApplyTimeFilterCommand.Execute(null);

        Assert.IsNotNull(vm.FilterEnd);
        Assert.AreEqual(new DateTime(2024, 1, 2).AddTicks(-1), vm.FilterEnd);
    }

    [TestMethod]
    [CoversNode("log-viewer-time-clear")]
    public void ClearTimeFilter_ResetsAllTimeFields()
    {
        using var vm = Make();
        vm.FilterStartDate = new DateTime(2024, 1, 1);
        vm.FilterStartTime = "08:00:00";
        vm.FilterEndDate   = new DateTime(2024, 1, 2);
        vm.FilterEndTime   = "09:00:00";
        vm.ApplyTimeFilterCommand.Execute(null);

        vm.ClearTimeFilterCommand.Execute(null);

        Assert.IsNull(vm.FilterStartDate);
        Assert.AreEqual(string.Empty, vm.FilterStartTime);
        Assert.IsNull(vm.FilterEndDate);
        Assert.AreEqual(string.Empty, vm.FilterEndTime);
        Assert.IsNull(vm.FilterStart);
        Assert.IsNull(vm.FilterEnd);
    }

    [TestMethod]
    [CoversNode("log-viewer-watch")]
    public void ToggleMonitoring_FlipsFlag_AndIsSafeWithoutFile()
    {
        using var vm = Make();   // path doesn't exist → StartMonitoring is a guarded no-op
        Assert.IsFalse(vm.IsMonitoring);

        vm.ToggleMonitoringCommand.Execute(null);
        Assert.IsTrue(vm.IsMonitoring);

        vm.ToggleMonitoringCommand.Execute(null);
        Assert.IsFalse(vm.IsMonitoring);
    }

    [TestMethod]
    [CoversNode("log-viewer-copy-selected")]
    public void CopySelectedLines_CannotExecute_WithNoSelection()
    {
        using var vm = Make();
        Assert.AreEqual(0, vm.SelectedLineCount);
        Assert.IsFalse(vm.CopySelectedLinesCommand.CanExecute(null));
    }
}
