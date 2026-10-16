using Nexaflow.Elevation.Contracts;
using Nexaflow.IO.Usb.Binding;
using Nexaflow.IO.Usb.Windows;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// USB driver take-over (ADR-001 Strategy A). The client names a devnode and nothing else: the policy is checked
/// again here, the INF is Windows' own, and the ledger is the machine one, read and written only by this process.
/// </summary>
internal abstract class UsbBindingOperation : IElevatedOperation
{
    public abstract string Id { get; }

    public ElevatedOperationResult Execute(IReadOnlyDictionary<string, string> args)
    {
        try
        {
            var records = Run(args, UsbBindingLedger.MachineForBridge());
            bool clean = records.All(r => r.State is UsbBindingState.Bound or UsbBindingState.Restored);
            var result = clean
                ? ElevatedOperationResult.Ok(Id, Describe(records))
                : ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, Describe(records));
            result.Data[ElevatedArgs.UsbBindingIds] = string.Join(',', records.Select(r => r.Id));
            return result;
        }
        catch (UsbBindingRefusedException e)
        {
            var refused = ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, e.Message);
            refused.Data[ElevatedArgs.UsbRefused] = "true";
            return refused;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or TimeoutException or ArgumentException)
        {
            return ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, e.Message);
        }
    }

    protected abstract IReadOnlyList<UsbBindingRecord> Run(IReadOnlyDictionary<string, string> args, UsbBindingLedger ledger);

    protected static string InstanceId(IReadOnlyDictionary<string, string> args) =>
        args.TryGetValue(ElevatedArgs.UsbInstanceId, out var id) && !string.IsNullOrWhiteSpace(id)
            ? id
            : throw new ArgumentException("Missing USB instance id.");

    static string Describe(IReadOnlyList<UsbBindingRecord> records) => records.Count switch
    {
        0 => "Nothing to restore.",
        1 => $"{records[0].InterfaceInstanceId}: {records[0].State}. {records[0].Log.LastOrDefault()}",
        _ => string.Join("; ", records.Select(r => $"{r.InterfaceInstanceId}: {r.State}")),
    };
}

internal sealed class UsbBindOperation : UsbBindingOperation
{
    public override string Id => ElevatedOps.UsbBind;

    protected override IReadOnlyList<UsbBindingRecord> Run(IReadOnlyDictionary<string, string> args, UsbBindingLedger ledger)
    {
        var kind = args.TryGetValue(ElevatedArgs.UsbDriverKind, out var name) && Enum.TryParse<GenericUsbDriverKind>(name, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown generic driver '{name}'.");
        return [InboxWinUsbBinder.Bind(InstanceId(args), kind, ledger)];
    }
}

internal sealed class UsbRestoreOperation : UsbBindingOperation
{
    public override string Id => ElevatedOps.UsbRestore;

    protected override IReadOnlyList<UsbBindingRecord> Run(IReadOnlyDictionary<string, string> args, UsbBindingLedger ledger) =>
        [InboxWinUsbBinder.Restore(InstanceId(args), ledger)];
}

internal sealed class UsbRestoreAllOperation : UsbBindingOperation
{
    public override string Id => ElevatedOps.UsbRestoreAll;

    protected override IReadOnlyList<UsbBindingRecord> Run(IReadOnlyDictionary<string, string> args, UsbBindingLedger ledger) =>
        InboxWinUsbBinder.RestoreAll(ledger);
}
