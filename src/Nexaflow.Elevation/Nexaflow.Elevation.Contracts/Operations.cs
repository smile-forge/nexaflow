namespace Nexaflow.Elevation.Contracts;

/// <summary>
/// Operation identifiers understood by the privilege bridge. A feature names one of these when it
/// builds an <see cref="ElevatedRequest"/>; the bridge maps it to a handler. Adding a new elevated
/// action = add a handler in the bridge and a constant here.
/// </summary>
public static class ElevatedOps
{
    public const string ServiceStart        = "service.start";
    public const string ServiceStop         = "service.stop";
    public const string ServiceRestart      = "service.restart";
    public const string ServicePause        = "service.pause";
    public const string ServiceContinue     = "service.continue";
    public const string ServiceSetStartMode = "service.setStartMode";
    public const string EnvSet              = "env.set";
    public const string EnvDelete           = "env.delete";
}

/// <summary>Well-known argument keys carried in <see cref="ElevatedOperation.Args"/>.</summary>
public static class ElevatedArgs
{
    public const string ServiceName = "serviceName";
    /// <summary>One of <see cref="ServiceStartMode"/>: Automatic | AutomaticDelayed | Manual | Disabled.</summary>
    public const string StartMode   = "startMode";
    public const string EnvName     = "name";
    public const string EnvValue    = "value";
    /// <summary>Only "Machine" reaches the bridge; User/Process are handled in-process by the feature.</summary>
    public const string EnvTarget   = "target";
}

/// <summary>Canonical startup-mode values exchanged over the wire (plural to avoid colliding with the
/// BCL <see cref="System.ServiceProcess.ServiceStartMode"/> enum).</summary>
public static class ServiceStartModes
{
    public const string Automatic        = "Automatic";
    public const string AutomaticDelayed = "AutomaticDelayed";
    public const string Manual           = "Manual";
    public const string Disabled         = "Disabled";
}

/// <summary>Shared protocol tunables for the launcher and the bridge.</summary>
public static class BridgeProtocol
{
    /// <summary>How long the launcher waits for the elevated bridge to connect — covers slow UAC consent.</summary>
    public const int ConnectTimeoutMs = 120_000;
    /// <summary>How long the launcher waits for the request/result exchange once connected.</summary>
    public const int IoTimeoutMs = 90_000;
}
