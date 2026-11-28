using System;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// The keys the engine gives a meaning of its own — moving through what is written, picking it out, writing and taking it back — named as
/// every toolkit names them. What shows content says which of its toolkit's keys was pressed as one of these; a key that is none of them
/// means nothing here, and is left to whatever else wants it.
/// </summary>
public enum EditingKey
{
    /// <summary>A key the engine gives no meaning of its own.</summary>
    None,

    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    PageUp,
    PageDown,

    /// <summary>Backspace.</summary>
    Back,
    Delete,
    Insert,
    Enter,
    Tab,
    Space,
    Escape,

    A,
    B,
    C,
    I,
    V,
    X,
    Y,
    Z,
}

/// <summary>The keys held while another is pressed or the pointer is: any of them together, as every toolkit counts them.</summary>
[Flags]
public enum HeldKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// What the pointer says can be done where it is — what the engine decides, and whatever shows content draws in its toolkit's own pointers.
/// </summary>
public enum PointerShape
{
    /// <summary>Nothing to write in: drawing nobody types into, or content only read.</summary>
    Arrow,

    /// <summary>Somewhere to write: a bar where the caret would go.</summary>
    Text,

    /// <summary>Something that leads somewhere, or a button to press.</summary>
    Hand,

    /// <summary>An edge carried sideways — a boundary dragged to another place along a line.</summary>
    ResizeAcross,
}
