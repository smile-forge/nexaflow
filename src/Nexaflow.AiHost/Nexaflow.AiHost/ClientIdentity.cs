using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

/// <summary>
/// Who a request is from: the account the OS says opened its connection — read by acting as the pipe's client, never taken from
/// what the client says — and the app it says it is, which tells apart several apps run by one account. What one client's requests
/// leave behind — the conversations a model keeps, the openings it shares — is kept apart from another's, each with its share of
/// the room; turns on a device go round the clients waiting at each priority, and admission takes requests in the order they came,
/// whoever sent them.
/// </summary>
/// <param name="Account">The account's security identifier, as the OS gives it.</param>
/// <param name="App">The app's name for itself, as its hello gave it.</param>
internal sealed record ClientIdentity(string Account, string App)
{
    private static readonly AsyncLocal<ClientIdentity?> Serving = new();

    /// <summary>The host itself, for what it does on no client's behalf — a model loaded again after a picture, a test driving
    /// its parts in process.</summary>
    public static ClientIdentity Host { get; } = new("host", "host");

    /// <summary>The client the code running now serves; <see cref="Host"/> outside any request.</summary>
    public static ClientIdentity Current => Serving.Value ?? Host;

    /// <summary>The highest priority the client may ask for (<see cref="HostPolicy.Grants"/>): every priority, unless the host's policy
    /// says less.</summary>
    public RequestPriority Highest { get; init; } = RequestPriority.Voice;

    /// <summary>The priority the client is served at, asking for <paramref name="asked"/>: what it asked, or its grant where it asked for
    /// more.</summary>
    public RequestPriority Granted(RequestPriority asked) => asked < Highest ? Highest : asked;

    /// <summary>The key what a client leaves behind is kept under, and its turns counted by.</summary>
    public string Key { get; } = $"{Account}/{App}";

    /// <summary>Makes this the client everything from here on in the caller's flow serves.</summary>
    public void Enter() => Serving.Value = this;

    public override string ToString() => Key;
}
