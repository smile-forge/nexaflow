namespace Nexaflow.Elevation.Contracts;

/// <summary>
/// First frame the elevated bridge sends after connecting back to the launcher's private pipe. The
/// launcher rejects the connection unless <see cref="Token"/> matches the one-time token it passed on
/// the command line — this defeats a lower-privilege process squatting the pipe name.
/// </summary>
public sealed class BridgeHello
{
    public string Token { get; set; } = "";
}
