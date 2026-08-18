using System.Net;
using System.Runtime.CompilerServices;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Plugins;

namespace Nexaflow.Features.Network.Ssdp;

/// <summary>
/// Discovery layer 1 — SSDP, and the first thing in the product that runs a protocol description.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here writes an octet. The M-SEARCH is built by handing values to <see cref="GraphCodec"/> against
/// <c>Protocols/ssdp.json</c>, and a reply is read the same way; what this class contributes is knowing that
/// SSDP goes to 239.255.255.250:1900, that a device answers within MX seconds, and what its headers mean
/// once they are text. That division is the whole claim being tested — if adding a second request/response
/// discovery protocol needs a second class like this one, the engine has not earned its keep.
/// </para>
/// <para>
/// <b>Light rather than Passive.</b> ARP reads a table the kernel already keeps; this puts a datagram on the
/// wire that every device on the segment will see, and the first bind will raise a firewall prompt. That is
/// a different consent, and the cost says so.
/// </para>
/// </remarks>
[Subfeature("network", "ssdp",
    DisplayName = "SSDP / UPnP",
    Description = "Asks every device on the segment to describe itself, using the UPnP discovery protocol. "
                + "Sends one small multicast datagram per adapter and listens for replies. Finds smart "
                + "plugs, TVs, printers, routers and NAS boxes that the neighbour table alone would miss.",
    Order = 1)]
public sealed class SsdpProbe : INetworkProbe
{
    /// <summary>UPnP 1.1 §1.2.1. The address and port are the protocol's, not a setting — a device that
    /// listens anywhere else is not speaking SSDP.</summary>
    private static readonly IPAddress Group = IPAddress.Parse("239.255.255.250");
    private const int Port = 1900;

    private IProbeHost? _host;
    private ProtocolFile.Loaded? _ssdp;

    /// <summary>
    /// Descriptions already fetched this run.
    /// </summary>
    /// <remarks>
    /// With <c>ssdp:all</c> a device answers once per service it hosts — a television sent thirty-six
    /// replies — and every one of them carries the same LOCATION. Fetching per reply asked one Chromecast
    /// for the same document eight times in three seconds, which is rude to the device, slow, and says the
    /// same thing eight times in the log.
    /// </remarks>
    private readonly HashSet<string> _described = new(StringComparer.OrdinalIgnoreCase);

    public string ProbeId => "network.ssdp";
    public string DisplayName => "SSDP / UPnP";
    public ProbeCost Cost => ProbeCost.Light;

    public IReadOnlyList<ProbeSetting> Settings =>
    [
        new ProbeSetting(
            "mx",
            "How many seconds devices may wait before answering. Every device picks a random moment inside "
          + "this window so they do not all reply at once, so a longer wait finds more of them on a busy "
          + "network and makes every scan take that much longer.",
            ProbeSettingType.Int, Default: "2", Min: 1, Max: 5),

        new ProbeSetting(
            "searchTarget",
            "What to ask for. 'ssdp:all' asks everything to answer; 'upnp:rootdevice' asks only for whole "
          + "devices, which is fewer replies and one per device instead of one per service.",
            ProbeSettingType.Enum, Default: "ssdp:all",
            OneOf: ["ssdp:all", "upnp:rootdevice"]),

        new ProbeSetting(
            "describe",
            "After a device answers, fetch the description document it pointed at. This is what turns an "
          + "address into a name, a make and a model — and it is one small web request per device, to a "
          + "device that just announced itself.",
            ProbeSettingType.Bool, Default: "true"),
    ];

    public void Attach(IProbeHost host) => _host = host;

    /// <summary>Any adapter that could carry a segment. Multicast on a tunnel or a down link reaches
    /// nothing, and the guard would refuse it in any case.</summary>
    public bool AppliesTo(NetworkAdapterInfo adapter) => adapter.IsUsable;

    public async IAsyncEnumerable<ProbeObservation> DiscoverAsync(
        NetworkAdapterInfo adapter, [EnumeratorCancellation] CancellationToken ct)
    {
        if (_host is null) yield break;

        var ssdp = Description();
        if (ssdp is null) yield break;

        // Per adapter, so a second sweep asks again: a device can be renamed, and a description fetched
        // an hour ago is not evidence about now.
        _described.Clear();

        int mx = Whole("mx", 2, low: 1, high: 5);
        string target = Setting("searchTarget", "ssdp:all");

        byte[] datagram;
        try
        {
            datagram = new GraphCodec(ssdp.Graph).Encode(Search(adapter, mx, target));
        }
        catch (ProtoTypeException why)
        {
            // A description that cannot write its own message is an authoring fault, and it belongs in the
            // log naming the protocol rather than as a crash in the middle of a sweep.
            _host.Log.Error($"{ProbeId}: ssdp.json could not write an M-SEARCH", why);
            yield break;
        }

        // The adapter's own IPv4 address, because a multicast has to be told which segment it is for.
        // Without it the datagram is sent from the unspecified address and reaches nothing but this
        // machine's own UPnP service, through the loopback copy — a discovery that looks like it ran.
        var via = adapter.Addresses
            .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Address;

        if (via is null)
        {
            _host.Log.Info($"{ProbeId}: {adapter.Name} has no IPv4 address to search from.");
            yield break;
        }

        var intent = new SendIntent
        {
            Target = Group,
            Port = Port,
            Layer = SendLayer.Udp,
            ByteCount = datagram.Length,
            Initiator = SendInitiator.Probe,
            Broadcast = true,
            SourceId = ProbeId,
            Via = via,
        };

        // MX is how long devices may wait, so the listen window has to outlast it — a device answering at
        // the last legal moment is answering correctly.
        var window = TimeSpan.FromSeconds(mx + 1);
        int replies = 0;

        await foreach (var reply in _host.Transport
                           .SendAndCollectAsync(intent, datagram, window, ct).ConfigureAwait(false))
        {
            replies++;

            // Windows runs a UPnP Device Host, so this machine answers its own M-SEARCH — once per service
            // it advertises. Our own reply is not a discovery, and reporting it puts the user's own machine
            // in the list of things found on the network. ArpProbe drops its own address for the same
            // reason; multicast makes it louder, because the loopback copy always arrives.
            if (Ours(reply, adapter)) continue;

            if (Read(ssdp, reply, adapter) is not { } observed) continue;

            yield return observed;

            // The reply says where to look; the document says what it is. One request per device that has
            // just announced itself, to the address it just gave us.
            if (!Flag("describe", @default: true)) continue;
            if (Described(observed, reply, adapter, ct) is not { } told) continue;

            yield return await told.ConfigureAwait(false);
        }

        _host.Log.Info($"{ProbeId}: {replies} reply/replies on {adapter.Name} "
                     + $"(asked for {target}, waited {window.TotalSeconds:0}s).");
    }

    /// <summary>Whether a reply came from this machine rather than from something on the network.</summary>
    private static bool Ours(ReceivedDatagram reply, NetworkAdapterInfo adapter)
        => IPAddress.IsLoopback(reply.From.Address)
        || adapter.Addresses.Any(a => a.Address.Equals(reply.From.Address));

    /// <summary>The M-SEARCH, as values the description turns into octets.</summary>
    /// <remarks>
    /// Header order is the caller's because it is the wire's: SSDP does not care, and a datagram that came
    /// back in a different order would be a different datagram. HOST names the group rather than the
    /// adapter — UPnP 1.1 §1.3.2 requires the multicast address there, not wherever it was sent from.
    /// </remarks>
    private static Dictionary<string, ProtoValue> Search(NetworkAdapterInfo adapter, int mx, string target)
        => new(StringComparer.Ordinal)
        {
            ["Start"] = ProtoValue.Of("M-SEARCH"),
            ["Target"] = ProtoValue.Of("*"),
            ["Version"] = ProtoValue.Of("HTTP/1.1"),
            ["Headers"] = new ProtoValue.List(
            [
                Header("HOST", $" {Group}:{Port}"),
                Header("MAN", " \"ssdp:discover\""),
                Header("MX", $" {mx}"),
                Header("ST", $" {target}"),
                Header("USER-AGENT", " Windows/10.0 UPnP/1.1 Nexaflow/1.0"),
            ]),
        };

    private static ProtoValue Header(string name, string value)
        => EvalScope.Record(("name", ProtoValue.Of(name)), ("value", ProtoValue.Of(value)));

    /// <summary>
    /// One reply, read by the same description that wrote the question, and turned into what the graph
    /// understands.
    /// </summary>
    /// <remarks>
    /// A device that answers is <b>reachable by definition</b> — the datagram came from it — which is a
    /// stronger fact than the neighbour table's, and the only identity is its address: SSDP carries no MAC,
    /// so this layer's findings correlate onto ARP's through the IP.
    /// </remarks>
    private ProbeObservation? Read(ProtocolFile.Loaded ssdp, ReceivedDatagram reply, NetworkAdapterInfo adapter)
    {
        RunGraph run;
        try
        {
            run = new GraphCodec(ssdp.Graph).Decode(reply.Payload);
        }
        catch (ProtoTypeException why)
        {
            // Refused rather than guessed at. A NOTIFY arrives on this socket too — it is SSDP's other
            // half and a different message — and reading one as a search reply would invent facts.
            _host?.Log.Warn($"{ProbeId}: {reply.From.Address} sent something this does not describe — {why.Message}");
            return null;
        }

        var headers = Headers(run);
        var now = reply.ReceivedUtc;
        var segment = adapter.SegmentId;
        var family = reply.From.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? new FactKey("net", "ipv6") : new FactKey("net", "ipv4");

        var obs = new ProbeObservation { SourceProbe = ProbeId, ObservedUtc = now };

        obs.Identities.Add(new IdentityClaim(IdentityKind.Ip, reply.From.Address.ToString(), segment,
                                             Confidence.Asserted));

        obs.Facts.Add(Fact(family, FactValue.OfAddress(reply.From.Address.ToString()), now,
                           Confidence.Asserted, "answered an M-SEARCH"));
        obs.Facts.Add(Fact(new FactKey("net", "reachable"), FactValue.OfBool(true), now,
                           Confidence.Asserted, "it replied", ttl: TimeSpan.FromMinutes(30)));
        obs.Facts.Add(Fact(new FactKey("link", "adapter"), FactValue.OfText(adapter.Name), now,
                           Confidence.Asserted, "observed via this adapter"));
        obs.Facts.Add(Fact(new FactKey("link", "segment"), FactValue.OfText(segment), now,
                           Confidence.Asserted, "adapter prefix"));

        // SERVER is a free-text banner, LOCATION is where the device's own description document lives, and
        // USN carries a UUID. All three are the device's claims about itself rather than anything observed,
        // so none of them is Asserted.
        if (Header(headers, "SERVER") is { Length: > 0 } server)
            obs.Facts.Add(Fact(new FactKey("dev", "firmware"), FactValue.OfText(server), now,
                               Confidence.Likely, "SSDP SERVER header"));

        if (Header(headers, "LOCATION") is { Length: > 0 } location)
            obs.Facts.Add(Fact(new FactKey("svc", "url"), FactValue.OfText(location), now,
                               Confidence.Strong, "SSDP LOCATION header"));

        if (Header(headers, "ST") is { Length: > 0 } serviceType)
            obs.Facts.Add(Fact(new FactKey("svc", "type"), FactValue.OfText(serviceType), now,
                               Confidence.Strong, "SSDP ST header"));

        if (Uuid(Header(headers, "USN")) is { } uuid)
            obs.Facts.Add(Fact(new FactKey("dev", "uuid"), FactValue.OfText(uuid), now,
                               Confidence.Strong, "SSDP USN header"));

        return obs;
    }

    /// <summary>
    /// Fetches and reads the description a reply pointed at, or null when there is nothing to fetch.
    /// </summary>
    /// <remarks>
    /// Returned as a task the caller awaits rather than awaited here, because this sits inside an iterator
    /// that must keep yielding: a device that takes two seconds to serve its description must not hold up
    /// the reply behind it.
    /// </remarks>
    private Task<ProbeObservation>? Described(ProbeObservation from, ReceivedDatagram reply,
                                              NetworkAdapterInfo adapter, CancellationToken ct)
    {
        var advertised = from.Facts.FirstOrDefault(f => f.Key == new FactKey("svc", "url"))?.Value.Text;

        if (advertised is null or "" || !Uri.TryCreate(advertised, UriKind.Absolute, out var where))
            return null;

        // Once per address, however many services answered from it.
        if (!_described.Add(where.ToString())) return null;

        return Fetch(where, reply, adapter, ct);
    }

    private async Task<ProbeObservation> Fetch(Uri where, ReceivedDatagram reply,
                                               NetworkAdapterInfo adapter, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var segment = adapter.SegmentId;
        var obs = new ProbeObservation { SourceProbe = ProbeId, ObservedUtc = now };

        // The same address the reply came from, so what the document says lands on the device that said it
        // rather than on a second node named after the web server.
        obs.Identities.Add(new IdentityClaim(IdentityKind.Ip, reply.From.Address.ToString(), segment,
                                             Confidence.Asserted));

        var intent = new SendIntent
        {
            Target = reply.From.Address,
            Port = where.Port,
            Layer = SendLayer.Tcp,
            ByteCount = 0,
            Initiator = SendInitiator.Probe,
            SourceId = ProbeId,
            Via = adapter.Addresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Address,
        };

        var got = await _host!.Transport.FetchAsync(intent, where, TimeSpan.FromSeconds(4), ct)
                              .ConfigureAwait(false);

        if (!got.Ok)
        {
            // Ordinary rather than exceptional: plenty of devices advertise an address they do not serve.
            _host.Log.Info($"{ProbeId}: {reply.From.Address} did not serve {where} — {got.Detail}");
            return obs;
        }

        if (DeviceDescription.Read(System.Text.Encoding.UTF8.GetString(got.Body), where) is not { } told)
        {
            _host.Log.Warn($"{ProbeId}: {where} is not a UPnP device description");
            return obs;
        }

        // Everything the device says about itself. Stated rather than observed, so none of it is Asserted:
        // a serial number is what the firmware was told to say, and a friendly name is whatever somebody
        // typed into an app once.
        Say(obs, new FactKey("name", "hostname"), told.FriendlyName, now, Confidence.Strong);
        Say(obs, new FactKey("dev", "vendor"), told.Manufacturer, now, Confidence.Strong);
        Say(obs, new FactKey("dev", "model"), told.ModelName, now, Confidence.Strong);
        Say(obs, new FactKey("dev", "modelNumber"), told.ModelNumber, now, Confidence.Likely);
        Say(obs, new FactKey("dev", "description"), told.ModelDescription, now, Confidence.Likely);
        Say(obs, new FactKey("dev", "serial"), told.SerialNumber, now, Confidence.Likely);
        Say(obs, new FactKey("dev", "class"), Kind(told.DeviceType), now, Confidence.Likely);

        // The addresses it offers, each its own key so an action can ask for exactly one of them.
        Say(obs, new FactKey("svc", "presentation"), told.PresentationUrl, now, Confidence.Strong);
        Say(obs, new FactKey("svc", "modelUrl"), told.ModelUrl, now, Confidence.Likely);
        Say(obs, new FactKey("svc", "vendorUrl"), told.ManufacturerUrl, now, Confidence.Likely);

        // The best icon it offers. One rather than all of them: a list is for choosing from, and the
        // choice — largest, then deepest — has already been made.
        if (told.Icons.Count > 0)
            Say(obs, new FactKey("dev", "icon"), told.Icons[0].Url.ToString(), now, Confidence.Strong);

        if (told.Udn is { Length: > 0 } udn)
            Say(obs, new FactKey("dev", "uuid"), udn.Replace("uuid:", ""), now, Confidence.Strong);

        _host.Log.Info($"{ProbeId}: {reply.From.Address} is "
                     + $"{(told.FriendlyName.Length > 0 ? told.FriendlyName : told.ModelName)}"
                     + $"{(told.Icons.Count > 0 ? $" ({told.Icons.Count} icon(s))" : "")}.");

        return obs;
    }

    /// <summary>The last meaningful word of a UPnP device type, which is what the thing is.</summary>
    /// <remarks>
    /// <c>urn:schemas-upnp-org:device:MediaRenderer:1</c> is a media renderer. The urn carries a namespace,
    /// the word, and a version; only the middle one says anything to a person.
    /// </remarks>
    private static string Kind(string deviceType)
    {
        if (deviceType.Length == 0) return "";

        var parts = deviceType.Split(':');
        return parts.Length >= 2 ? parts[^2] : deviceType;
    }

    private void Say(ProbeObservation obs, FactKey key, string value, DateTimeOffset now,
                     Confidence confidence)
    {
        if (value.Length == 0) return;

        obs.Facts.Add(Fact(key, FactValue.OfText(value), now, confidence, "UPnP device description"));
    }

    /// <summary>The headers a reading produced, by name, trimmed.</summary>
    /// <remarks>
    /// The description carries a value exactly as written — the space after the colon included — because
    /// that is what makes a datagram survive a round trip. Trimming is the caller's job, and this is the
    /// caller. Names fold to upper case: UPnP 1.1 §1.1.2 makes them case-insensitive and devices disagree.
    /// </remarks>
    private static Dictionary<string, string> Headers(RunGraph run)
    {
        var names = Each(run, "headerName");
        var values = Each(run, "headerValue");
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < names.Count && i < values.Count; i++)
            found[names[i].Trim()] = values[i].Trim();

        return found;
    }

    private static IReadOnlyList<string> Each(RunGraph run, string field)
        => [.. run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                        .OrderBy(n => n.Index)
                        .Select(n => n.Value.AsText())];

    private static string Header(Dictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var v) ? v : "";

    /// <summary>The UUID out of a USN, which is <c>uuid:&lt;id&gt;::&lt;service&gt;</c> or just the uuid.</summary>
    private static string? Uuid(string usn)
    {
        if (!usn.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase)) return null;

        var rest = usn["uuid:".Length..];
        int end = rest.IndexOf("::", StringComparison.Ordinal);
        var id = end < 0 ? rest : rest[..end];

        return id.Length == 0 ? null : id;
    }

    private DeviceFact Fact(FactKey key, FactValue value, DateTimeOffset now, Confidence confidence,
                            string detail, TimeSpan? ttl = null)
        => new()
        {
            Key = key, Value = value, SourceProbe = ProbeId, SourceDetail = detail,
            ObservedUtc = now, Confidence = confidence, Ttl = ttl,
            Layer = FactOntology.Describe(key).Layer,
        };

    /// <summary>
    /// The protocol description, loaded once and kept.
    /// </summary>
    /// <remarks>
    /// Beside the assembly rather than embedded in it, on purpose: the point of a description is that a
    /// person can open it, read it and disagree with it, and a resource compiled into a DLL is neither
    /// reviewable nor replaceable without a build.
    /// </remarks>
    private ProtocolFile.Loaded? Description()
    {
        if (_ssdp is not null) return _ssdp;

        var path = Path.Combine(AppContext.BaseDirectory, "Protocols", "ssdp.json");

        try
        {
            return _ssdp = ProtocolFile.Read(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or ProtoTypeException)
        {
            _host?.Log.Error($"{ProbeId}: could not load {path}", ex);
            return null;
        }
    }

    private string Setting(string name, string fallback)
        => _host?.Setting(name) is { Length: > 0 } s ? s : fallback;

    private int Whole(string name, int fallback, int low, int high)
        => int.TryParse(Setting(name, ""), out var v) ? Math.Clamp(v, low, high) : fallback;

    private bool Flag(string name, bool @default)
        => bool.TryParse(Setting(name, ""), out var v) ? v : @default;
}
