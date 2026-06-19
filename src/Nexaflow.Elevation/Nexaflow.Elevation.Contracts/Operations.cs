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
    public const string RegSetValue         = "reg.setValue";
    public const string RegDeleteValue      = "reg.deleteValue";
    public const string RegCreateKey        = "reg.createKey";
    public const string RegDeleteKey        = "reg.deleteKey";
    public const string RegRenameKey        = "reg.renameKey";
    public const string RegImport           = "reg.import";
    public const string ProcessKill         = "process.kill";
    public const string ProcessSetPriority  = "process.setPriority";
    public const string ProcessInspect      = "process.inspect";
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

    // ── Registry (one of the hive roots HKCU | HKLM | HKCR) ──────────────────
    /// <summary>Hive root token: "HKCU", "HKLM", or "HKCR".</summary>
    public const string RegHive     = "regHive";
    /// <summary>Key path under the hive (e.g. <c>Software\\Foo</c>); empty = the hive root.</summary>
    public const string RegPath     = "regPath";
    /// <summary>Value name; empty string means the key's default value.</summary>
    public const string RegName     = "regName";
    /// <summary><c>Microsoft.Win32.RegistryValueKind</c> name: String|ExpandString|DWord|QWord|Binary|MultiString.</summary>
    public const string RegType     = "regType";
    /// <summary>Wire-encoded value data (see the feature's registry value codec).</summary>
    public const string RegValue    = "regValue";
    /// <summary>Absolute path to a <c>.reg</c> file for import.</summary>
    public const string RegFile     = "regFile";
    /// <summary>New leaf name for a key rename.</summary>
    public const string RegNewName  = "regNewName";

    // ── Process control ──────────────────────────────────────────────────────
    /// <summary>Target process id (decimal string).</summary>
    public const string ProcessId      = "processId";
    /// <summary>"true" to also terminate the target's child process tree.</summary>
    public const string ProcessKillTree = "killTree";
    /// <summary><c>System.Diagnostics.ProcessPriorityClass</c> name: Idle|BelowNormal|Normal|AboveNormal|High|RealTime.</summary>
    public const string ProcessPriority = "priority";
    /// <summary>What <c>process.inspect</c> should gather: "all" (default — handles + modules + command line) or "handles".</summary>
    public const string InspectWhat     = "inspectWhat";
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
