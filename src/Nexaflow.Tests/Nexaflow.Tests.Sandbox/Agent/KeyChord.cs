using FlaUI.Core.WindowsAPI;

namespace Nexaflow.Tests.Sandbox.Agent;

/// <summary>
/// A key or a chord as typed — <c>enter</c>, <c>f5</c>, <c>ctrl+shift+p</c>, <c>alt+f4</c> — as the keys pressed together, the
/// last one while the others are held. Anything not named here is tried as a virtual-key name (<c>OEM_PLUS</c>).
/// </summary>
internal static class KeyChord
{
    private static readonly Dictionary<string, VirtualKeyShort> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = VirtualKeyShort.CONTROL,
        ["control"] = VirtualKeyShort.CONTROL,
        ["shift"] = VirtualKeyShort.SHIFT,
        ["alt"] = VirtualKeyShort.ALT,
        ["win"] = VirtualKeyShort.LWIN,
        ["enter"] = VirtualKeyShort.RETURN,
        ["return"] = VirtualKeyShort.RETURN,
        ["esc"] = VirtualKeyShort.ESCAPE,
        ["escape"] = VirtualKeyShort.ESCAPE,
        ["tab"] = VirtualKeyShort.TAB,
        ["space"] = VirtualKeyShort.SPACE,
        ["backspace"] = VirtualKeyShort.BACK,
        ["del"] = VirtualKeyShort.DELETE,
        ["delete"] = VirtualKeyShort.DELETE,
        ["ins"] = VirtualKeyShort.INSERT,
        ["insert"] = VirtualKeyShort.INSERT,
        ["home"] = VirtualKeyShort.HOME,
        ["end"] = VirtualKeyShort.END,
        ["pgup"] = VirtualKeyShort.PRIOR,
        ["pageup"] = VirtualKeyShort.PRIOR,
        ["pgdn"] = VirtualKeyShort.NEXT,
        ["pagedown"] = VirtualKeyShort.NEXT,
        ["left"] = VirtualKeyShort.LEFT,
        ["right"] = VirtualKeyShort.RIGHT,
        ["up"] = VirtualKeyShort.UP,
        ["down"] = VirtualKeyShort.DOWN,
        ["menu"] = VirtualKeyShort.APPS,
    };

    public static VirtualKeyShort[] Parse(string chord)
    {
        var parts = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new SandboxException("key: name a key, such as enter or ctrl+s.");
        return parts.Select(part => KeyOf(part, chord)).ToArray();
    }

    private static VirtualKeyShort KeyOf(string part, string chord)
    {
        if (Named.TryGetValue(part, out var named)) return named;
        if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0])
            && Enum.TryParse<VirtualKeyShort>($"KEY_{char.ToUpperInvariant(part[0])}", out var character))
            return character;
        if (Enum.TryParse<VirtualKeyShort>(part, ignoreCase: true, out var virtualKey) && !int.TryParse(part, out _))
            return virtualKey;
        throw new SandboxException($"key: '{part}' in '{chord}' is not a key this knows.");
    }
}
