using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

public enum AiHostFailure
{
    /// <summary>The host served the request and failed it; <see cref="AiHostException.Kind"/> says how.</summary>
    Request,
    /// <summary>The host died or stopped answering while the request ran. The next request starts a new one.</summary>
    Lost,
    /// <summary>No host could be had: not installed, would not start, or crashing too often to be restarted yet.</summary>
    Unavailable,
}

/// <summary>A request to the AI host that did not complete.</summary>
public sealed class AiHostException(AiHostFailure failure, string message, HostErrorKind? kind = null) : Exception(message)
{
    public AiHostFailure Failure { get; } = failure;

    /// <summary>The host's own error kind, for <see cref="AiHostFailure.Request"/>.</summary>
    public HostErrorKind? Kind { get; } = kind;
}
