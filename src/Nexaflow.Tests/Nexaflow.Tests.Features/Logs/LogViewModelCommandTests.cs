using System;
using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.ViewModels;
using NSubstitute;

namespace Nexaflow.Tests.Features.Logs;

/// <summary>
/// Headless command/state logic behind the Logs toolbar &amp; filter panel — the parts that need
/// no file load, no AvalonEdit <c>TextDocument</c> mutation and no file watcher. The tail/head reader
/// itself is covered by <see cref="LogViewModelTests"/>; the interactive controls are exercised
/// end-to-end by the UI journey. Everything here runs synchronously on the test thread.
/// </summary>
[TestClass]
public class LogViewModelCommandTests
{
    private static LogViewModel Make()
        => new("nonexistent.log", Substitute.For<IShellServices>()) { IsMonitoring = false };

    [TestMethod]
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
    public void FilterRegex_InvalidPattern_LeavesFilterInactive()
    {
        using var vm = Make();

        vm.FilterRegex = "([unterminated";   // invalid regex — swallowed, filter stays off

        Assert.IsFalse(vm.IsFilterActive);
        Assert.IsNull(vm.ActiveFilter);
    }

    [TestMethod]
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
    public void CopySelectedLines_CannotExecute_WithNoSelection()
    {
        using var vm = Make();
        Assert.AreEqual(0, vm.SelectedLineCount);
        Assert.IsFalse(vm.CopySelectedLinesCommand.CanExecute(null));
    }
}
