using Nexaflow.Features.Processes.Models;
using Nexaflow.Features.Processes.Services;

namespace Nexaflow.Tests.Features.Processes;

/// <summary>An in-memory <see cref="IProcessSource"/> the view-model tests drive directly — no live system.</summary>
internal sealed class FakeProcessSource : IProcessSource
{
    public ProcessSnapshot Next { get; set; } = ProcessSnapshot.Empty;
    public Func<int, ProcessDetail?>? DetailFunc { get; set; }

    public ProcessSnapshot Snapshot() => Next;
    public ProcessDetail? ReadDetail(int pid) => DetailFunc?.Invoke(pid);

    // ── Builders ──────────────────────────────────────────────────────────────
    public static ProcessSnapshot Snap(params (int pid, int parent, string name)[] procs) =>
        new()
        {
            Processes      = procs.Select(p => new ProcessSample
            {
                Pid = p.pid, ParentPid = p.parent, Name = p.name,
            }).ToList(),
            ProcessorCount = 4,
        };

    public static ProcessSample Sample(int pid, string name, double cpu = 0, long ws = 0, long priv = 0, int parent = 0) =>
        new() { Pid = pid, ParentPid = parent, Name = name, CpuPercent = cpu, WorkingSet = ws, PrivateBytes = priv };
}
