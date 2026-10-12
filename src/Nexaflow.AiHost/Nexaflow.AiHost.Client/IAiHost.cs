using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>The AI host as a provider uses it: requests, and what the next host is started with.</summary>
public interface IAiHost
{
    /// <inheritdoc cref="AiHostClient.SendAsync"/>
    IAsyncEnumerable<HostReply> SendAsync(Func<long, HostRequest> create, TimeSpan stallLimit, CancellationToken ct = default);

    /// <inheritdoc cref="AiHostClient.Environment"/>
    AiHostEnvironment Environment { get; set; }
}
