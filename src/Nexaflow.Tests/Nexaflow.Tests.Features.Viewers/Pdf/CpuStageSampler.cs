using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.InteropServices;

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Where a piece of work spends its time, by stage: the process samples its own stacks through EventPipe (the
/// runtime's sampling profiler, about one stack per thread per millisecond) while the work runs, and each
/// sample is put to the stage of the innermost frame a caller-supplied map recognises. A sample whose stack
/// holds no recognised frame is not the work's, and is dropped.
/// </summary>
/// <remarks>
/// Work handed to other threads is still the work's: a sample on any thread counts to its stage. A thread blocked
/// inside a stage — waiting for the jobs it handed out — is told apart from one working in it, so waiting is
/// never counted as work, and the thread that ran the work is reported on its own as well, for where its wall
/// time went.
/// </remarks>
internal static class CpuStageSampler
{
    private const string Sampler = "Microsoft-DotNETCore-SampleProfiler";

    /// <summary>The sampler's mark for a thread outside managed code (1 External, 2 Managed).</summary>
    private const int ExternalCode = 1;

    public static StageProfile Sample(Action run, Func<string, string?> stageOf)
    {
        var file = TestTemp.CreateFile(".nettrace");
        var caller = (int)GetCurrentThreadId();

        try
        {
            Record(run, file);
            return Read(file, caller, stageOf);
        }
        finally
        {
            File.Delete(file);
            File.Delete(Path.ChangeExtension(file, ".etlx"));
        }
    }

    private static void Record(Action run, string file)
    {
        var providers = new List<EventPipeProvider>
        {
            new(Sampler, EventLevel.Informational),
            // Method names for the stacks: what is jitted while recording, and the rundown at the end names the rest.
            new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational,
                (long)(ClrTraceEventParser.Keywords.Jit | ClrTraceEventParser.Keywords.Loader)),
        };

        var client = new DiagnosticsClient(Environment.ProcessId);
        using var session = client.StartEventPipeSession(providers, requestRundown: true, circularBufferMB: 512);

        var copying = Task.Run(() =>
        {
            using var output = File.Create(file);
            session.EventStream.CopyTo(output);
        });

        try { run(); }
        finally
        {
            session.Stop();
            copying.Wait();
        }
    }

    private static StageProfile Read(string file, int caller, Func<string, string?> stageOf)
    {
        var work = new Dictionary<string, int>();
        var onCaller = new Dictionary<string, (int Samples, int Waiting)>();
        var hottest = new Dictionary<(string Method, string Stage), int>();
        var stages = new Dictionary<string, string?>(StringComparer.Ordinal);
        var dropped = 0;
        var callerDropped = 0;

        using var log = new TraceLog(TraceLog.CreateFromEventPipeDataFile(file, Path.ChangeExtension(file, ".etlx")));

        foreach (var sample in log.Events)
        {
            if (sample.ProviderName != Sampler) continue;

            string? stage = null;
            string? innermost = null;

            // EventPipe stacks hold managed frames only, so the innermost is managed: the work's own code, or what it
            // called into — a copy, an allocation, a wait.
            for (var frame = sample.CallStack(); frame is not null && stage is null; frame = frame.Caller)
            {
                var method = frame.CodeAddress.Method?.FullMethodName;
                if (string.IsNullOrEmpty(method)) continue;

                innermost ??= method;
                if (!stages.TryGetValue(method, out stage)) stages[method] = stage = stageOf(method);
            }

            if (stage is null)
            {
                dropped++;
                if (sample.ThreadID == caller) callerDropped++;
                continue;
            }

            // The sampler says whether the thread was running managed code or was outside it — blocked in a wait, in a
            // P/Invoke, in the collector. A thread blocked in a native wait shows only the managed frame that called it, so
            // its name alone cannot tell a wait from work.
            var waiting = sample.PayloadByName("Type") is int type && type == ExternalCode || IsWait(innermost!);
            if (sample.ThreadID == caller)
            {
                var (samples, waits) = onCaller.GetValueOrDefault(stage);
                onCaller[stage] = (samples + 1, waits + (waiting ? 1 : 0));
            }

            if (waiting) continue;

            work[stage] = work.GetValueOrDefault(stage) + 1;
            var key = (Trimmed(innermost!), stage);
            hottest[key] = hottest.GetValueOrDefault(key) + 1;
        }

        return new StageProfile(
            work.Values.Sum(),
            onCaller.Values.Sum(v => v.Samples),
            onCaller.Values.Sum(v => v.Waiting),
            dropped,
            callerDropped,
            work.OrderByDescending(s => s.Value).Select(s => new StageShare(s.Key, s.Value, 0)).ToList(),
            onCaller.OrderByDescending(s => s.Value.Samples).Select(s => new StageShare(s.Key, s.Value.Samples, s.Value.Waiting)).ToList(),
            hottest.OrderByDescending(h => h.Value).Take(40).Select(h => new MethodShare(h.Key.Method, h.Key.Stage, h.Value)).ToList());
    }

    /// <summary>A thread blocked rather than working: waiting on an event, a lock, a semaphore, or asleep.</summary>
    private static bool IsWait(string method) =>
        method.StartsWith("System.Threading.", StringComparison.Ordinal)
        && (method.Contains("Wait", StringComparison.Ordinal) || method.Contains("Sleep", StringComparison.Ordinal)
            || method.Contains("Monitor.Enter", StringComparison.Ordinal) || method.Contains("Semaphore", StringComparison.Ordinal));

    /// <summary>A method's name without its parameter list, which only makes a report wider.</summary>
    private static string Trimmed(string method)
    {
        var open = method.IndexOf('(');
        return open < 0 ? method : method[..open];
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

/// <summary>
/// What sampling found. <see cref="Work"/> is every thread's samples by stage while running managed code — where
/// the work is, wherever it ran. <see cref="OnCaller"/> is the thread that ran the work, with the samples it spent
/// blocked or outside managed code counted beside each stage — where its wall time went;
/// <see cref="CallerDropped"/> are its samples that held no frame of the work at all. <see cref="Hottest"/> are the
/// methods most often innermost while working. One sample is about a millisecond of one thread.
/// </summary>
internal sealed record StageProfile(
    int WorkSamples,
    int CallerSamples,
    int CallerWaiting,
    int DroppedSamples,
    int CallerDropped,
    IReadOnlyList<StageShare> Work,
    IReadOnlyList<StageShare> OnCaller,
    IReadOnlyList<MethodShare> Hottest);

/// <summary>One stage's samples, and how many of them were a thread blocked or outside managed code inside it.</summary>
internal sealed record StageShare(string Stage, int Samples, int Waiting);

/// <summary>A method that was the innermost managed frame of <see cref="Samples"/> samples.</summary>
internal sealed record MethodShare(string Method, string Stage, int Samples);
