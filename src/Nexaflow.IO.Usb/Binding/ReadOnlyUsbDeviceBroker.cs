using Nexaflow.IO.Usb.Windows;

namespace Nexaflow.IO.Usb.Binding;

/// <summary>
/// A broker that answers but never changes anything: the policy and the ledger, read as the real one reads them, and
/// every bind or restore refused. For a host that must ask nothing of the user — the one debug details are taken
/// through — where a tool may say what it would do but a UAC prompt must never appear.
/// </summary>
public sealed class ReadOnlyUsbDeviceBroker(UsbBindingLedger ledger, Func<string, UsbBindingCandidate> candidates) : IUsbDeviceBroker
{
    public static ReadOnlyUsbDeviceBroker ForMachine() => new(UsbBindingLedger.Machine(), UsbBindingCandidates.Read);

    public string? RefusalReason(string interfaceInstanceId) => UsbBindingPolicy.RefusalReason(candidates(interfaceInstanceId));

    public Task<UsbBindingOutcome> BindGenericAsync(string interfaceInstanceId, GenericUsbDriverKind kind, CancellationToken ct) => Refuse();

    public Task<UsbBindingOutcome> RestoreAsync(string interfaceInstanceId, CancellationToken ct) => Refuse();

    public Task<UsbBindingOutcome> RestoreAllAsync(CancellationToken ct) => Refuse();

    public IReadOnlyList<UsbBindingRecord> GetBindings() => ledger.Read();

    static Task<UsbBindingOutcome> Refuse() =>
        Task.FromResult(new UsbBindingOutcome(UsbBindingOutcomeKind.Refused, "Drivers are not changed from here.", []));
}
