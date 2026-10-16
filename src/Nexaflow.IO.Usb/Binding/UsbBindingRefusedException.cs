namespace Nexaflow.IO.Usb.Binding;

/// <summary>A take-over or restore the policy or the ledger refused before anything on the system changed.</summary>
public sealed class UsbBindingRefusedException(string reason) : Exception($"Not taken over: {reason}.")
{
    public string Reason => reason;
}
