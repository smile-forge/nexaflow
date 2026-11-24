using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>The AI host as a provider uses it: requests, and what the next host is started with.</summary>
public interface IAiHost
{
    /// <inheritdoc cref="AiHostClient.SendAsync"/>
    IAsyncEnumerable<HostReply> SendAsync(Func<long, HostRequest> create, TimeSpan stallLimit, CancellationToken ct = default);

    /// <inheritdoc cref="AiHostClient.OpenStreamAsync"/>
    Task<IHostStream> OpenStreamAsync(Func<long, HostRequest> create, TimeSpan stallLimit, CancellationToken ct = default);

    /// <inheritdoc cref="AiHostClient.AllocateAsync"/>
    Task<SharedBytes> AllocateAsync(int length, CancellationToken ct = default);

    /// <inheritdoc cref="AiHostClient.Environment"/>
    AiHostEnvironment Environment { get; set; }

    /// <inheritdoc cref="AiHostClient.Memory"/>
    HostMemory Memory { get; }

    /// <inheritdoc cref="AiHostClient.IsRunning"/>
    bool IsRunning { get; }

    /// <inheritdoc cref="AiHostClient.Unreachable"/>
    string? Unreachable { get; }

    /// <inheritdoc cref="AiHostClient.ReachabilityChanged"/>
    event EventHandler? ReachabilityChanged;

    /// <inheritdoc cref="AiHostClient.ModelFilesChanged"/>
    event EventHandler<IReadOnlyList<string>>? ModelFilesChanged;
}
