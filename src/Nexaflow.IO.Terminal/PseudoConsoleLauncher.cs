using Nexaflow.IO.Common.Terminal;

namespace Nexaflow.IO.Terminal;

/// <summary>Starts a program on this computer in a ConPTY pseudo-console — cmd.exe unless another is named — in the folder
/// and with the variables the launch asks for, and hands over its <see cref="PseudoConsoleHostService"/> running.</summary>
/// <param name="executable">The program, by name on the path or in full.</param>
/// <param name="arguments">Its arguments, quoted as the program reads its command line.</param>
public sealed class PseudoConsoleLauncher(string executable, string arguments) : ITerminalLauncher
{
    /// <summary>A launcher for cmd.exe.</summary>
    public PseudoConsoleLauncher() : this("cmd.exe", string.Empty) { }

    public ValueTask<ITerminalTransport> LaunchAsync(TerminalLaunch launch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var host = new PseudoConsoleHostService
        {
            ShellExecutable      = executable,
            ShellArguments       = arguments,
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
