using System.ServiceProcess;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

internal sealed class ServiceSetStartModeOperation : ServiceOperationBase
{
    public override string Id => ElevatedOps.ServiceSetStartMode;

    protected override ElevatedOperationResult Run(
        ServiceController sc, string name, IReadOnlyDictionary<string, string> args)
    {
        var mode = args.GetValueOrDefault(ElevatedArgs.StartMode);
        if (string.IsNullOrWhiteSpace(mode))
            return Fail("Missing start mode.");

        // Map the canonical mode → WMI mode + the registry delayed-autostart flag.
        (string wmiMode, bool delayed) = mode switch
        {
            ServiceStartModes.Automatic        => ("Automatic", false),
            ServiceStartModes.AutomaticDelayed => ("Automatic", true),
            ServiceStartModes.Manual           => ("Manual",    false),
            ServiceStartModes.Disabled         => ("Disabled",  false),
            _ => ("", false),
        };
        if (wmiMode.Length == 0)
            return Fail($"Unknown start mode '{mode}'.");

        var (ok, message) = ServiceWmi.ChangeStartMode(name, wmiMode);
        if (!ok)
            return Fail(message);

        // Only meaningful for Automatic, but clearing it for the others keeps the key honest.
        ServiceRegistry.SetDelayedAutostart(name, delayed);

        var result = Ok($"Set {name} startup to {mode}.");
        result.Data["startMode"] = mode;
        return result;
    }
}
