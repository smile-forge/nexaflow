using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Features.Processes.ViewModels;
using NSubstitute;
using static Nexaflow.Tests.Features.Processes.FakeProcessSource;

namespace Nexaflow.Tests.Features.Processes;

[TestClass]
public class ProcessToolsTests
{
    private static (ProcessesViewModel vm, FakeProcessSource src) NewVm()
    {
        var src = new FakeProcessSource();
        return (new ProcessesViewModel(Substitute.For<IShellServices>(), src), src);
    }

    private static IClientTool Tool(ProcessesViewModel vm, string name) =>
        vm.GetClientTools().First(t => t.Name == name);

    [TestMethod]
    public void ReadToolsAreReadOnly_MutationsRequireApproval()
    {
        var (vm, _) = NewVm();
        using (vm)
        {
            var tools = vm.GetClientTools().ToDictionary(t => t.Name);
            Assert.AreEqual(ToolSafety.ReadOnly, tools["list_processes"].Safety);
            Assert.AreEqual(ToolSafety.ReadOnly, tools["get_process_details"].Safety);
            Assert.AreEqual(ToolSafety.RequiresApproval, tools["kill_process"].Safety);
            Assert.AreEqual(ToolSafety.RequiresApproval, tools["set_process_priority"].Safety);
        }
    }

    [TestMethod]
    public async Task ListProcesses_ListsNames()
    {
        var (vm, _) = NewVm();
        using (vm)
        {
            vm.ApplySnapshot(Snap((1, 0, "a.exe"), (2, 0, "b.exe")));
            var res = await Tool(vm, "list_processes").InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsTrue(res.Success);
            StringAssert.Contains(res.ModelText, "a.exe");
            StringAssert.Contains(res.ModelText, "b.exe");
        }
    }

    [TestMethod]
    public async Task TopConsumers_ByMemory_ReturnsBiggest()
    {
        var (vm, _) = NewVm();
        using (vm)
        {
            vm.ApplySnapshot(new ProcessSnapshot
            {
                Processes      = [Sample(1, "small.exe", ws: 100), Sample(2, "huge.exe", ws: 9_000_000)],
                ProcessorCount = 4,
            });
            var args = new JsonObject { ["metric"] = "memory", ["count"] = 1 };
            var res = await Tool(vm, "get_top_consumers").InvokeAsync(args, CancellationToken.None);
            StringAssert.Contains(res.ModelText, "huge.exe");
            Assert.IsFalse(res.ModelText.Contains("small.exe"), "count=1 should return only the top consumer");
        }
    }

    [TestMethod]
    public async Task GetDetails_UnknownProcess_IsError()
    {
        var (vm, _) = NewVm();
        using (vm)
        {
            vm.ApplySnapshot(Snap((1, 0, "a.exe")));
            var res = await Tool(vm, "get_process_details")
                .InvokeAsync(new JsonObject { ["process"] = "nope" }, CancellationToken.None);
            Assert.IsTrue(res.IsError);
        }
    }

    [TestMethod]
    public async Task GetDetails_KnownProcess_IncludesFields()
    {
        var (vm, src) = NewVm();
        using (vm)
        {
            vm.ApplySnapshot(Snap((1, 0, "a.exe")));
            src.DetailFunc = pid => new ProcessDetail
            {
                Pid = pid, Name = "a.exe", Description = "App A", ThreadCount = 3, HandleCount = 9,
            };
            var res = await Tool(vm, "get_process_details")
                .InvokeAsync(new JsonObject { ["process"] = "a.exe" }, CancellationToken.None);
            Assert.IsTrue(res.Success);
            StringAssert.Contains(res.ModelText, "App A");
        }
    }

    [TestMethod]
    public async Task GetTree_IndentsChildren()
    {
        var (vm, _) = NewVm();
        using (vm)
        {
            vm.ApplySnapshot(Snap((1, 0, "a"), (2, 1, "b")));
            var res = await Tool(vm, "get_process_tree").InvokeAsync(new JsonObject(), CancellationToken.None);
            StringAssert.Contains(res.ModelText, "a (PID 1)");
            StringAssert.Contains(res.ModelText, "  b (PID 2)");   // child indented under parent
        }
    }
}
