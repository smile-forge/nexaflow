using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;


namespace Nexaflow.AiHost.Service;

/// <summary>
/// Starts the service's worker: this executable again (<see cref="HostProgram.WorkerArgument"/>), told which process it answers for,
/// bound to the kill-on-close job so it never outlives the service, its error output written to the service's log, and its standard
/// input the line clients are handed to it on.
/// </summary>
/// <param name="arguments">What the service was started with, passed on: where its policy file is, and its store.</param>
[SupportedOSPlatform("windows")]
internal sealed class ProcessWorkerLauncher(string hostPath, IReadOnlyList<string> arguments, HostLog log) : IWorkerLauncher
{
    public IWorkerProcess Start()
    {
        var info = new ProcessStartInfo(hostPath)
        {
            UseShellExecute       = false,
            CreateNoWindow        = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            WorkingDirectory      = Path.GetDirectoryName(hostPath)!,
        };
        info.ArgumentList.Add(HostProgram.WorkerArgument);
        info.ArgumentList.Add(HostProgram.ServeAsArgument);
        info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        var process = Process.Start(info) ?? throw new InvalidOperationException("The worker could not be started.");
        HostProcessJob.Assign(process.Handle);
        return new WorkerProcess(process, log);
    }

    private sealed class WorkerProcess : IWorkerProcess
    {
        private readonly Process _process;
        private readonly Lock _gate = new();

        public WorkerProcess(Process process, HostLog log)
        {
            _process = process;
            Id = process.Id;
            Exited = ExitOf(process);
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is { Length: > 0 } line) log.Write($"worker {Id}: {line}");
            };
            process.BeginErrorReadLine();
        }

        public int Id { get; }

        public Task<int> Exited { get; }

        public void Hand(TakenClient client)
        {
            lock (_gate) PipeHandover.Hand(client, _process.SafeHandle, _process.StandardInput);
        }

        public void Stop()
        {
            lock (_gate)
            {
                try { _process.StandardInput.Close(); }
                catch (IOException) { }   // gone already
            }
        }

        public void Kill()
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }   // gone already
        }

        public void Dispose() => _process.Dispose();

        private static async Task<int> ExitOf(Process process)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}
