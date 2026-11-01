using Nexaflow.IO.Common.Logging;

namespace Nexaflow.AiHost;

/// <summary>
/// The host's own log, kept only while the app's safety logging is on: the app then names a folder
/// (<see cref="Nexaflow.AiHost.Contracts.AiHostProtocol.LogDirArgument"/>) and the host writes a file of its own there
/// beside the shell's, under the same retention and cap. The host has no window and the app only sees its pipe, so this
/// is where a load failure or a crash's last words end up. With no folder named it keeps nothing.
/// </summary>
internal sealed class HostLog : IDisposable
{
    /// <summary>Names the host's files in the shared logs folder.</summary>
    public const string FilePrefix = "aihost";

    private readonly SessionLogFile? _file;

    public HostLog(string? directory) => _file = directory is null ? null : new SessionLogFile(directory, FilePrefix);

    public void Write(string line) => _file?.Write(line);

    public void Dispose() => _file?.Dispose();
}
