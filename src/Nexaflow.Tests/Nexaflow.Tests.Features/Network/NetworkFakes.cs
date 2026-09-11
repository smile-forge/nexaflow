using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Network.Arp;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Probes;

namespace Nexaflow.Tests.Features.Network;

/// <summary>A probe host that grants settings from a dictionary and keeps what the probe said.</summary>
internal sealed class TestProbeHost(IGuardedTransport transport, Dictionary<string, string>? settings = null,
                                    params NetworkAdapterInfo[] adapters) : IProbeHost, IProbeLog
{
    private readonly Dictionary<string, string> _settings = settings ?? [];

    public List<string> Said { get; } = [];

    public IReadOnlyList<NetworkAdapterInfo> Adapters => adapters;
    public IGuardedTransport Transport => transport;
    public IProbeLog Log => this;

    public string Setting(string name) => _settings.GetValueOrDefault(name, "");
    public Task<string?> PromptAsync(ValuePrompt p, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<bool> ConfirmAsync(string t, string m, CancellationToken ct) => Task.FromResult(false);
    public Task<ElevatedResult> RunElevatedAsync(ElevatedRequest r, CancellationToken ct)
        => throw new NotSupportedException();

    public void Info(string m) { lock (Said) Said.Add(m); }
    public void Warn(string m) { lock (Said) Said.Add("warning: " + m); }
    public void Error(string m, Exception? ex = null) { lock (Said) Said.Add("error: " + m + " " + ex?.Message); }
}

/// <summary>A transport that answers each echo however a test says, and remembers every one it was asked for.</summary>
internal sealed class EchoWire : IGuardedTransport
{
    private readonly ConcurrentDictionary<IPAddress, int> _attempts = new();

    public ConcurrentQueue<SendIntent> Echoes { get; } = new();

    /// <summary>The answer to one echo, given the intent and which attempt at that address this is.
    /// Allowed and silent unless a test says otherwise.</summary>
    public Func<SendIntent, int, PingOutcome> Answer { get; init; }
        = (_, _) => new PingOutcome(GuardDecision.Allow(), false, TimeSpan.Zero);

    public Task<PingOutcome> PingAsync(SendIntent intent, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Echoes.Enqueue(intent);
        int attempt = _attempts.AddOrUpdate(intent.Target, 1, (_, n) => n + 1);
        return Task.FromResult(Answer(intent, attempt));
    }

    public Task<GuardDecision> SendUdpAsync(SendIntent i, ReadOnlyMemory<byte> p, CancellationToken ct)
        => Task.FromResult(GuardDecision.Allow());
    public IAsyncEnumerable<ReceivedDatagram> SendAndCollectAsync(SendIntent i, ReadOnlyMemory<byte> p, TimeSpan w, CancellationToken ct)
        => AsyncEnumerable.Empty<ReceivedDatagram>();
    public IAsyncEnumerable<ReceivedDatagram> ListenMulticastAsync(IPAddress g, int port, string id, CancellationToken ct)
        => AsyncEnumerable.Empty<ReceivedDatagram>();
    public Task<IProtocolStream?> ConnectAsync(SendIntent i, TimeSpan t, CancellationToken ct, Action<GuardDecision>? d = null)
        => Task.FromResult<IProtocolStream?>(null);
    public Task<FetchedDocument> FetchAsync(SendIntent i, Uri url, TimeSpan t, CancellationToken ct)
        => Task.FromResult(FetchedDocument.Nothing("no fetching in this fixture"));
    public Task<bool> TcpConnectAsync(IPAddress t, int port, TimeSpan timeout, CancellationToken ct)
        => Task.FromResult(false);
}

/// <summary>Adapters and neighbour-table rows, shaped the way the layers that read them expect.</summary>
internal static class NetworkFixtures
{
    /// <summary>An Ethernet adapter that is up, on <paramref name="ip"/>/<paramref name="prefix"/>, with the
    /// subnet's first address as its gateway.</summary>
    public static NetworkAdapterInfo Adapter(string ip = "192.168.1.10", int prefix = 24,
                                             string id = "{TEST-ADAPTER}")
    {
        var adapter = new NetworkAdapterInfo
        {
            Id = id, Name = "Test", Description = "Test adapter",
            Type = NetworkInterfaceType.Ethernet, Status = OperationalStatus.Up, MacAddress = "aa:bb:cc:dd:ee:ff",
        };

        var address = new AdapterAddress(IPAddress.Parse(ip), prefix);
        adapter.Addresses.Add(address);
        if (address.Hosts().FirstOrDefault() is { } gateway) adapter.Gateways.Add(gateway);

        return adapter;
    }

    /// <summary>One neighbour-table row. Interface 0, so it is attributed to whichever adapter asks.</summary>
    public static NeighborEntry Row(string ip, string mac, NeighborState state = NeighborState.Reachable,
                                    bool router = false)
        => new(IPAddress.Parse(ip), mac, 0, state, router);
}
