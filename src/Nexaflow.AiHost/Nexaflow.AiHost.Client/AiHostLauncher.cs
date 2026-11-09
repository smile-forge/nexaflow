using System.Diagnostics;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Processes;

namespace Nexaflow.AiHost.Client;

/// <summary>Starts a host that will connect to <c>pipeName</c> and prove itself with <c>token</c>.</summary>
public interface IAiHostLauncher
{
    IAiHostProcess Start(string pipeName, string token, AiHostEnvironment environment);

    /// <summary>The share of this machine's memory the models on a host it starts may hold, as that host finds it.</summary>
    HostMemory Memory { get; }
}

/// <summary>A started host, as the client watches it.</summary>
public interface IAiHostProcess : IDisposable
{
    int Id { get; }

    /// <summary>Completes when the process has exited, with its exit code.</summary>
    Task<int> Exited { get; }

    /// <summary>The last lines the process wrote to its error output, oldest first — a native runtime's last words before
    /// a crash.</summary>
    IReadOnlyList<string> LastErrors { get; }

    void Kill();
}

/// <summary>Starts <c>Nexaflow.AiHost.exe</c>, hands it the token on standard input, and binds it to the
/// kill-on-close job so it can never outlive the app.</summary>
/// <param name="memoryShare">The share of this machine's memory, in percent, models on the host's CPU may hold.</param>
/// <param name="logDirectory">Asked at each start for the folder the host keeps its log in, and its error output is written
/// to beside it (<see cref="HostErrorOutput"/>); null, or a null answer, and neither is kept — only the error output's last
/// lines, in memory, for the record of the host's end. Asked rather than fixed, so a host started after the setting
/// changes follows it.</param>
public sealed class ProcessAiHostLauncher(string hostPath, string aiDir, int memoryShare = AiHostProtocol.DefaultMemoryShare,
                                          Func<string?>? logDirectory = null) : IAiHostLauncher
{
    public IAiHostProcess Start(string pipeName, string token, AiHostEnvironment environment)
    {
        if (!File.Exists(hostPath))
            throw new AiHostException(AiHostFailure.Unavailable, $"The AI host is not installed ({hostPath}).");

        var info = new ProcessStartInfo(hostPath)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardInput  = true,
            RedirectStandardError  = true,
            WorkingDirectory       = Path.GetDirectoryName(hostPath)!,
        };
        info.ArgumentList.Add(AiHostProtocol.PipeArgument);
        info.ArgumentList.Add(pipeName);
        info.ArgumentList.Add(AiHostProtocol.AiDirArgument);
        info.ArgumentList.Add(aiDir);
        info.ArgumentList.Add(AiHostProtocol.MemoryShareArgument);
        info.ArgumentList.Add(memoryShare.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string? logs = logDirectory?.Invoke();
        if (logs is not null)
        {
            info.ArgumentList.Add(AiHostProtocol.LogDirArgument);
            info.ArgumentList.Add(logs);
        }
        if (environment.RuntimePack is { } pack)
        {
            info.ArgumentList.Add(AiHostProtocol.RuntimePackArgument);
            info.ArgumentList.Add(pack);
        }

        var process = Process.Start(info)
            ?? throw new AiHostException(AiHostFailure.Unavailable, "The AI host could not be started.");
        KillOnCloseJob.Assign(process.Handle);
        process.StandardInput.WriteLine(token);
        process.StandardInput.Close();
        return new HostProcess(process, logs);
    }

    private sealed class HostProcess : IAiHostProcess
    {
        private readonly Process _process;
        private readonly HostErrorOutput _errors;

        public HostProcess(Process process, string? logDirectory)
        {
            _process = process;
            _errors  = new HostErrorOutput(logDirectory, process.Id);
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is { Length: > 0 } line) _errors.Take(line);
            };
            process.BeginErrorReadLine();
            Exited = WaitAsync(process);
        }

        public int Id => _process.Id;

        /// <summary>Completes once the process has exited and its error output has been read to the end.</summary>
        public Task<int> Exited { get; }

        public IReadOnlyList<string> LastErrors => _errors.Last;

        public void Kill()
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already gone */ }
        }

        public void Dispose()
        {
            _process.Dispose();
            _errors.Dispose();
        }

        private static async Task<int> WaitAsync(Process process)
        {
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
    }

    /// <summary>The share the host takes of the memory .NET says this process has — the host's own reading of it.</summary>
    public HostMemory Memory { get; } = HostMemory.Of(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, memoryShare);
}
