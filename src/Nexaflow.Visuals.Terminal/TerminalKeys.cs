using System.Windows.Input;

namespace Nexaflow.Visuals.Terminal;

/// <summary>
/// Maps special (non-text) keys to the byte sequences a terminal sends to the shell. Printable characters
/// are handled via WPF <c>TextInput</c> (layout/IME aware), not here. <see cref="Key.Enter"/> is
/// intentionally not mapped — the terminal intercepts it for command/query routing.
/// </summary>
public static class TerminalKeys
{
    /// <summary>The sequence for a special key, or null if it isn't one the terminal forwards.</summary>
    public static string? Encode(Key key, ModifierKeys mods)
    {
        bool ctrl = (mods & ModifierKeys.Control) != 0;

        // Ctrl+letter → control code (Ctrl+C = 0x03 interrupt, Ctrl+D = 0x04, etc.)
        if (ctrl && key is >= Key.A and <= Key.Z)
            return ((char)(key - Key.A + 1)).ToString();

        return key switch
        {
            Key.Back     => "\x7f",
            Key.Tab      => "\t",
            Key.Escape   => "\x1b",
            Key.Up       => "\x1b[A",
            Key.Down     => "\x1b[B",
            Key.Right    => "\x1b[C",
            Key.Left     => "\x1b[D",
            Key.Home     => "\x1b[H",
            Key.End      => "\x1b[F",
            Key.Delete   => "\x1b[3~",
            Key.Insert   => "\x1b[2~",
            Key.PageUp   => "\x1b[5~",
            Key.PageDown => "\x1b[6~",
            _ => null,
        };
    }
}
