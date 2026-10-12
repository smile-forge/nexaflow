using System.Net;

namespace Nexaflow.Providers.Common;

/// <summary>Where the content of a request goes when a provider is asked something.</summary>
public enum DataResidency
{
    /// <summary>It leaves this machine — a cloud API, or a server elsewhere on the network.</summary>
    LeavesMachine,

    /// <summary>It stays on this machine — a model served from here.</summary>
    StaysOnMachine,
}

public static class EndpointResidency
{
    /// <summary>
    /// The residency of requests sent to <paramref name="endpoint"/>: a loopback address (<c>localhost</c>,
    /// <c>127.0.0.0/8</c>, <c>::1</c>) keeps them on this machine; any other host takes them off it. An endpoint
    /// left empty is the provider's own default, whose residency is <paramref name="whenUnset"/>.
    /// </summary>
    public static DataResidency Of(string? endpoint, DataResidency whenUnset)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return whenUnset;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return DataResidency.LeavesMachine;

        if (uri.IsLoopback) return DataResidency.StaysOnMachine;
        return IPAddress.TryParse(uri.DnsSafeHost, out var ip) && IPAddress.IsLoopback(ip)
            ? DataResidency.StaysOnMachine
            : DataResidency.LeavesMachine;
    }
}
