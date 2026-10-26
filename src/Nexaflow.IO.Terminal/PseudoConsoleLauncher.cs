using Nexaflow.IO.Common.Terminal;

namespace Nexaflow.IO.Terminal;

/// <summary>Starts a shell on this computer in a ConPTY pseudo-console, in the folder and with the variables the launch
/// asks for, and hands over its <see cref="PseudoConsoleHostService"/> running.</summary>
public sealed class PseudoConsoleLauncher : ITerminalLauncher
{
    public ValueTask<ITerminalTransport> LaunchAsync(TerminalLaunch launch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var host = new PseudoConsoleHostService
        {
            StartDirectory       = launch.WorkingDirectory,
            EnvironmentOverrides = launch.Environment,
        };
        try { host.Start(launch.Columns, launch.Rows); }
        catch
        {
            host.Dispose();
            throw;
        }
        return ValueTask.FromResult<ITerminalTransport>(host);
    }
}
