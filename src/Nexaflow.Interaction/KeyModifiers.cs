namespace Nexaflow.Interaction;

/// <summary>
/// The modifier keys held with a key press or a drag. A key itself is a <see cref="ConsoleKey"/>: the base library's
/// name for a Windows virtual key, so no UI framework's key type is in a contract.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None    = 0,
    Alt     = 1,
    Control = 2,
    Shift   = 4,
    Windows = 8,
}
