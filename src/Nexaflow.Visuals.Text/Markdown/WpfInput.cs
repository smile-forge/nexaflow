using System.Windows.Input;

using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// WPF's keys, held keys and pointers said in the engine's own words (<see cref="EditingKey"/>, <see cref="HeldKeys"/>,
/// <see cref="PointerShape"/>), and back — the one place this toolkit's input is translated for the engine.
/// </summary>
internal static class WpfInput
{
    /// <summary>The key the engine knows <paramref name="key"/> as — <see cref="EditingKey.None"/> where it gives it no meaning.</summary>
    public static EditingKey Editing(Key key) => key switch
    {
        Key.Left => EditingKey.Left,
        Key.Right => EditingKey.Right,
        Key.Up => EditingKey.Up,
        Key.Down => EditingKey.Down,
        Key.Home => EditingKey.Home,
        Key.End => EditingKey.End,
        Key.PageUp => EditingKey.PageUp,
        Key.PageDown => EditingKey.PageDown,
        Key.Back => EditingKey.Back,
        Key.Delete => EditingKey.Delete,
        Key.Insert => EditingKey.Insert,
        Key.Enter => EditingKey.Enter,
        Key.Tab => EditingKey.Tab,
        Key.Space => EditingKey.Space,
        Key.Escape => EditingKey.Escape,
        Key.A => EditingKey.A,
        Key.B => EditingKey.B,
        Key.C => EditingKey.C,
        Key.I => EditingKey.I,
        Key.V => EditingKey.V,
        Key.X => EditingKey.X,
        Key.Y => EditingKey.Y,
        Key.Z => EditingKey.Z,
        _ => EditingKey.None,
    };

    /// <summary>The keys held, as the engine counts them: the same flags, which both say as every toolkit does.</summary>
    public static HeldKeys Held(ModifierKeys modifiers) => (HeldKeys)(int)modifiers;

    /// <summary>The pointer this toolkit draws for what the engine says can be done.</summary>
    public static Cursor Pointer(PointerShape shape) => shape switch
    {
        PointerShape.Text => Cursors.IBeam,
        PointerShape.Hand => Cursors.Hand,
        PointerShape.ResizeAcross => Cursors.SizeWE,
        _ => Cursors.Arrow,
    };
}
