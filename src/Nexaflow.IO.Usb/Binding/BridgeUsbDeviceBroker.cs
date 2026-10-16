using Nexaflow.Elevation.Contracts;
using Nexaflow.IO.Usb.Windows;

namespace Nexaflow.IO.Usb.Binding;

/// <summary>
/// <see cref="IUsbDeviceBroker"/> over the PrivilegeBridge: each call is one <see cref="ElevatedRequest"/> carrying
/// only instance ids, and the outcome is read back from the ledger the bridge wrote — the bridge's word, not ours.
/// </summary>
public sealed class BridgeUsbDeviceBroker(
    Func<ElevatedRequest, CancellationToken, Task<ElevatedResult>> runElevated,
    UsbBindingLedger ledger,
    Func<string, UsbBindingCandidate> candidates) : IUsbDeviceBroker
{
    /// <summary>The machine ledger and live devnode facts; <paramref name="runElevated"/> is the page's <c>IShellServices.RunElevatedAsync</c>.</summary>
    public static BridgeUsbDeviceBroker ForMachine(Func<ElevatedRequest, CancellationToken, Task<ElevatedResult>> runElevated) =>
        new(runElevated, UsbBindingLedger.Machine(), UsbBindingCandidates.Read);

    public string? RefusalReason(string interfaceInstanceId) => UsbBindingPolicy.RefusalReason(candidates(interfaceInstanceId));

    public Task<UsbBindingOutcome> BindGenericAsync(string interfaceInstanceId, GenericUsbDriverKind kind, CancellationToken ct)
    {
        if (RefusalReason(interfaceInstanceId) is { } reason)
            return Task.FromResult(new UsbBindingOutcome(UsbBindingOutcomeKind.Refused, $"Not taken over: {reason}.", []));
        return RunAsync(ElevatedRequest.Single(ElevatedOps.UsbBind,
            (ElevatedArgs.UsbInstanceId, interfaceInstanceId), (ElevatedArgs.UsbDriverKind, kind.ToString())), ct);
    }

    public Task<UsbBindingOutcome> RestoreAsync(string interfaceInstanceId, CancellationToken ct)
    {
        if (ledger.Active(interfaceInstanceId) is null)
            return Task.FromResult(new UsbBindingOutcome(UsbBindingOutcomeKind.Refused, "Nexaflow holds no binding for this interface.", []));
        return RunAsync(ElevatedRequest.Single(ElevatedOps.UsbRestore, (ElevatedArgs.UsbInstanceId, interfaceInstanceId)), ct);
    }

    public Task<UsbBindingOutcome> RestoreAllAsync(CancellationToken ct)
    {
        if (!ledger.Read().Any(r => r.IsActive))
            return Task.FromResult(new UsbBindingOutcome(UsbBindingOutcomeKind.Succeeded, "Nothing to restore.", []));
        return RunAsync(ElevatedRequest.Single(ElevatedOps.UsbRestoreAll), ct);
    }

    public IReadOnlyList<UsbBindingRecord> GetBindings() => ledger.Read();

    async Task<UsbBindingOutcome> RunAsync(ElevatedRequest request, CancellationToken ct)
    {
        var result = await runElevated(request, ct).ConfigureAwait(false);
        if (result.WasDeclined) return new UsbBindingOutcome(UsbBindingOutcomeKind.Declined, result.Message, []);

        var ids = result.Operations.SelectMany(o => o.Data.GetValueOrDefault(ElevatedArgs.UsbBindingIds)?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();
        List<UsbBindingRecord> records = ids.Count == 0 ? [] : ledger.Read().Where(r => ids.Contains(r.Id)).ToList();

        if (result.Success) return new UsbBindingOutcome(UsbBindingOutcomeKind.Succeeded, result.Message, records);
        bool refused = result.Operations.Any(o => o.Data.ContainsKey(ElevatedArgs.UsbRefused));
        return new UsbBindingOutcome(refused ? UsbBindingOutcomeKind.Refused : UsbBindingOutcomeKind.Failed, result.Message, records);
    }
}
