using System.IO.Pipes;
using System.Security.Principal;

namespace Nexaflow.AiHost.Service;

/// <summary>Where a host's clients come from: each one's pipe, connected.</summary>
internal interface IClientPipes
{
    /// <summary>The next client, connected; null once no more will come.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before a client came.</exception>
    Task<ClientPipe?> NextAsync(CancellationToken ct);
}

/// <summary>A client's pipe, connected, and who the client is where that was read already.</summary>
/// <param name="Account">The client's account, as the process that connected the pipe read it — the service's supervisor, since only
/// that process can act as the pipe's client; null where the host reads it from the pipe itself.</param>
internal sealed record ClientPipe(NamedPipeServerStream Pipe, SecurityIdentifier? Account);
