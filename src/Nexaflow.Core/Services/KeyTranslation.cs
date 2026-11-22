using System.Windows.Input;

using Nexaflow.Interaction;

namespace Nexaflow.Core.Services;

/// <summary>
/// WPF's keys as the input contracts name them. The one place the two vocabularies meet: the shell translates as it
/// hears a key, so no handler is ever given a WPF one.
/// </summary>
internal static class KeyTranslation
{
    /// <summary>
    /// The virtual key of <paramref name="key"/>. A key WPF reports only as standing for another — <c>System</c> under
    /// Alt, <c>ImeProcessed</c> while composing — comes to a key no handler claims, so those presses stay Windows'.
    /// </summary>
    public static ConsoleKey ToConsoleKey(this Key key) => (ConsoleKey)KeyInterop.VirtualKeyFromKey(key);

    public static KeyModifiers ToKeyModifiers(this ModifierKeys modifiers)
    {
        var held = KeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Alt))     held |= KeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Control)) held |= KeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift))   held |= KeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) held |= KeyModifiers.Windows;
        return held;
    }
}
