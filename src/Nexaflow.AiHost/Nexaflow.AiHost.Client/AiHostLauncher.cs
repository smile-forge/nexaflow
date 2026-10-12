using System.Diagnostics;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Processes;

namespace Nexaflow.AiHost.Client;

/// <summary>Starts a host that will connect to <c>pipeName</c> and prove itself with <c>token</c>.</summary>
public interface IAiHostLauncher
{
    IAiHostProcess Start(string pipeName, string token, AiHostEnvironment environment);
}

/// <summary>A started host, as the client watches it.</summary>
public interface IAiHostProcess : IDisposable
{
    int Id { get; }

    /// <summary>Completes when the process has exited, with its exit code.</summary>
    Task<int> Exited { get; }

    void Kill();
}

/// <summary>Starts <c>Nexaflow.AiHost.exe</c>, hands it the token on standard input, and binds it to the
/// kill-on-close job so it can never outlive the app.</summary>
public sealed class ProcessAiHostLauncher(string hostPath, string aiDir) : IAiHostLauncher
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
            WorkingDirectory       = Path.GetDirectoryName(hostPath)!,
        };
        info.ArgumentList.Add(AiHostProtocol.PipeArgument);
        info.ArgumentList.Add(pipeName);
        info.ArgumentList.Add(AiHostProtocol.AiDirArgument);
        info.ArgumentList.Add(aiDir);
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
        return new HostProcess(process);
    }

    private sealed class HostProcess(Process process) : IAiHostProcess
    {
        public int Id => process.Id;

        public Task<int> Exited { get; } = WaitAsync(process);

        public void Kill()
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already gone */ }
        }

        public void Dispose() => process.Dispose();

        private static async Task<int> WaitAsync(Process process)
        {
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
    }
}
