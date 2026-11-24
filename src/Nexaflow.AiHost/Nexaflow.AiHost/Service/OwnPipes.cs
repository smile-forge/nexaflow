using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Service;

/// <summary>
/// The pipe a host an app started for itself opens under the name it was given: only the host's own account may open it, and the
/// first instance is refused where the name is taken already, so nothing else can stand in for the host.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OwnPipes(string pipeName) : IClientPipes
{
    /// <summary>Buffered both ways: unbuffered, every write would wait for the other end to read it.</summary>
    private const int PipeBuffer = SharedBytes.InlineLimit;

    private bool _first = true;

    public async Task<ClientPipe?> NextAsync(CancellationToken ct)
    {
        var pipe = Create(_first);
        _first = false;
        try
        {
            await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            return new ClientPipe(pipe, Account: null);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private NamedPipeServerStream Create(bool first)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                                               PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None),
                                               PipeBuffer, PipeBuffer, security);
    }
}
