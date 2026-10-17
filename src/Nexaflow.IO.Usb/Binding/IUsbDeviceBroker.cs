namespace Nexaflow.IO.Usb.Binding;

public enum UsbBindingOutcomeKind
{
    Succeeded,
    /// <summary>The policy refused the target before anything was asked of the user.</summary>
    Refused,
    /// <summary>The UAC prompt was declined; nothing changed.</summary>
    Declined,
    Failed,
}

/// <summary>The result of a broker call, with the ledger records as they stand after it.</summary>
public sealed record UsbBindingOutcome(UsbBindingOutcomeKind Kind, string Message, IReadOnlyList<UsbBindingRecord> Records)
{
    public bool Succeeded => Kind == UsbBindingOutcomeKind.Succeeded;
}

/// <summary>
/// Driver take-over and restore. Every change happens in the elevated bridge (one UAC prompt per call); the
/// client passes only a device instance id and never a path, an INF or a ledger record.
/// </summary>
public interface IUsbDeviceBroker
{
    /// <summary>Why <paramref name="interfaceInstanceId"/> may not be taken over, or null — answered without elevation.</summary>
    string? RefusalReason(string interfaceInstanceId);

    Task<UsbBindingOutcome> BindGenericAsync(string interfaceInstanceId, GenericUsbDriverKind kind, CancellationToken ct);

    Task<UsbBindingOutcome> RestoreAsync(string interfaceInstanceId, CancellationToken ct);

    /// <summary>Restores every active binding in one elevated request — one prompt, the uninstall path too.</summary>
    Task<UsbBindingOutcome> RestoreAllAsync(CancellationToken ct);

    /// <summary>The ledger, read directly; no elevation.</summary>
    IReadOnlyList<UsbBindingRecord> GetBindings();
}
