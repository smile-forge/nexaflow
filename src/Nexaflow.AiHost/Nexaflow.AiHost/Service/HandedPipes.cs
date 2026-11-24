using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.AiHost.Service;

/// <summary>
/// The clients a service's worker serves: each a pipe its supervisor took on the service's pipe and handed over
/// (<see cref="PipeHandover"/>), named on a line of <paramref name="control"/> — the worker's standard input — by its handle and the
/// client's account, which the supervisor read as it took the client on. Its end is the supervisor asking the worker to stop.
/// </summary>
internal sealed class HandedPipes(TextReader control) : IClientPipes
{
    /// <exception cref="InvalidDataException">A line named no handle and account.</exception>
    public async Task<ClientPipe?> NextAsync(CancellationToken ct)
    {
        if (await control.ReadLineAsync(ct).ConfigureAwait(false) is not { } line) return null;
        var parts = line.Split(' ');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long handle)
            || !parts[1].StartsWith("S-", StringComparison.Ordinal))
            throw new InvalidDataException($"The supervisor handed over '{line}', which names no pipe and account.");
        var pipe = new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: true, new SafePipeHandle((IntPtr)handle, ownsHandle: true));
        return new ClientPipe(pipe, new SecurityIdentifier(parts[1]));
    }
}
