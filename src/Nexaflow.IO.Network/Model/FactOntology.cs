namespace Nexaflow.IO.Network.Model;

/// <summary>Declared metadata for one fact key: how to show it, what it means, whether a device may
/// legitimately have several.</summary>
/// <param name="Key">The key being described.</param>
/// <param name="DisplayName">Human label for the card row.</param>
/// <param name="Kind">The value kind probes are expected to assert.</param>
/// <param name="Layer">Default card grouping when a probe doesn't set one.</param>
/// <param name="MultiValued">
/// True when several simultaneous values are normal and correct — a device really does have many open
/// ports and many service instances. For these, <c>Best()</c> is meaningless and the UI lists them all.
/// </param>
/// <param name="Unit">Optional unit suffix for display (<c>ms</c>, <c>Mbit/s</c>).</param>
public readonly record struct FactDef(
    FactKey Key,
    string DisplayName,
    FactValueKind Kind,
    string Layer,
    bool MultiValued = false,
    string Unit = "");

/// <summary>
/// The declared vocabulary of facts. A probe asserting a key that isn't here is <b>not rejected</b> — the
/// key is surfaced under an "Other" grouping with its raw name. Dropping it would hide real information;
/// blessing it would let any probe invent first-class vocabulary. Neither is acceptable, so we do the third
/// thing and show it as what it is.
/// </summary>
public static class FactOntology
{
    private static readonly Dictionary<FactKey, FactDef> _defs = Build();

    public const string LayerLink = "link";
    public const string LayerName = "name";
    public const string LayerService = "service";
    public const string LayerMgmt = "mgmt";
    public const string LayerIot = "iot";
    public const string LayerOther = "other";

    /// <summary>The declared definition, or a synthesised "unknown key" one that renders under
    /// <see cref="LayerOther"/>.</summary>
    public static FactDef Describe(FactKey key)
        => _defs.TryGetValue(key, out var d)
            ? d
            : new FactDef(key, key.ToString(), FactValueKind.Text, LayerOther);

    /// <summary>True if the key is part of the declared vocabulary.</summary>
    public static bool IsKnown(FactKey key) => _defs.ContainsKey(key);

    /// <summary>Every declared definition, for the settings/help surface and the AI's schema view.</summary>
    public static IReadOnlyCollection<FactDef> All => _defs.Values;

    private static Dictionary<FactKey, FactDef> Build()
    {
        FactDef[] defs =
        [
            // ── link layer: what the segment itself tells us ─────────────────────
            new(new("link", "mac"),        "MAC address",     FactValueKind.Text,      FactOntology.LayerLink),
            new(new("link", "vendor"),     "Vendor (OUI)",    FactValueKind.Text,      FactOntology.LayerLink),
            new(new("link", "adapter"),    "Seen via adapter",FactValueKind.Text,      FactOntology.LayerLink, MultiValued: true),
            new(new("link", "segment"),    "L2 segment",      FactValueKind.Text,      FactOntology.LayerLink),

            // ── network layer ────────────────────────────────────────────────────
            new(new("net", "ipv4"),        "IPv4 address",    FactValueKind.Address,   FactOntology.LayerLink, MultiValued: true),
            new(new("net", "ipv6"),        "IPv6 address",    FactValueKind.Address,   FactOntology.LayerLink, MultiValued: true),
            new(new("net", "reachable"),   "Reachable",       FactValueKind.Bool,      FactOntology.LayerLink),
            new(new("net", "rtt"),         "Round-trip time", FactValueKind.Number,    FactOntology.LayerLink, Unit: "ms"),
            new(new("net", "ttl"),         "IP TTL",          FactValueKind.Number,    FactOntology.LayerLink),
            new(new("net", "port.open"),   "Open port",       FactValueKind.Number,    FactOntology.LayerService, MultiValued: true),

            // ── naming ───────────────────────────────────────────────────────────
            new(new("name", "hostname"),   "Hostname",        FactValueKind.Text,      FactOntology.LayerName),
            new(new("name", "fqdn"),       "FQDN",            FactValueKind.Text,      FactOntology.LayerName),
            new(new("name", "netbios"),    "NetBIOS name",    FactValueKind.Text,      FactOntology.LayerName),

            // ── services ─────────────────────────────────────────────────────────
            new(new("svc", "instance"),    "Service instance",FactValueKind.Text,      FactOntology.LayerService, MultiValued: true),
            new(new("svc", "type"),        "Service type",    FactValueKind.Text,      FactOntology.LayerService, MultiValued: true),
            new(new("svc", "txt"),         "Service TXT",     FactValueKind.Text,      FactOntology.LayerService, MultiValued: true),
            new(new("svc", "url"),         "Service URL",     FactValueKind.Text,      FactOntology.LayerService, MultiValued: true),

            // ── device identity ──────────────────────────────────────────────────
            new(new("dev", "vendor"),      "Manufacturer",    FactValueKind.Text,      FactOntology.LayerName),
            new(new("dev", "model"),       "Model",           FactValueKind.Text,      FactOntology.LayerName),
            new(new("dev", "serial"),      "Serial number",   FactValueKind.Text,      FactOntology.LayerName),
            new(new("dev", "class"),       "Device type",     FactValueKind.Text,      FactOntology.LayerName),
            new(new("dev", "firmware"),    "Firmware",        FactValueKind.Text,      FactOntology.LayerMgmt),
            new(new("dev", "uuid"),        "Device UUID",     FactValueKind.Text,      FactOntology.LayerName),

            // UPnP describes a device in a document with a schema, so these are declared rather than
            // landing under "Other" as raw keys. A vocabulary that a whole protocol family fills is worth
            // naming; one probe's private notion would not be.
            new(new("dev", "modelNumber"), "Model number",    FactValueKind.Text,      FactOntology.LayerName),
            new(new("dev", "description"), "Description",     FactValueKind.Text,      FactOntology.LayerName),
            new(new("dev", "icon"),        "Icon",            FactValueKind.Bytes,     FactOntology.LayerName),
            new(new("svc", "presentation"),"Web interface",   FactValueKind.Text,      FactOntology.LayerService),
            new(new("svc", "modelUrl"),    "Model page",      FactValueKind.Text,      FactOntology.LayerService),
            new(new("svc", "vendorUrl"),   "Manufacturer page", FactValueKind.Text,    FactOntology.LayerService),

            // ── management plane ─────────────────────────────────────────────────
            new(new("snmp", "sysName"),    "SNMP sysName",    FactValueKind.Text,      FactOntology.LayerMgmt),
            new(new("snmp", "sysDescr"),   "SNMP sysDescr",   FactValueKind.Text,      FactOntology.LayerMgmt),
            new(new("snmp", "sysObjectID"),"SNMP sysObjectID",FactValueKind.Text,      FactOntology.LayerMgmt),
            new(new("snmp", "uptime"),     "Uptime",          FactValueKind.Number,    FactOntology.LayerMgmt, Unit: "s"),
        ];

        var map = new Dictionary<FactKey, FactDef>();
        foreach (var d in defs) map[d.Key] = d;
        return map;
    }
}
