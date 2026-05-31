namespace Nexaflow.Core.Models;

/// <summary>
/// Curated palette of icons and colours for Profiles, plus a randomiser used when cloning a
/// profile so the copy is visually distinct from its source at a glance.
/// </summary>
public static class ProfileStyle
{
    private static readonly string[] Icons =
    [
        "⬡", "⬢", "◆", "●", "■", "▲", "★", "✦", "❖", "✿",
        "♦", "⬣", "⚑", "⚙", "☼", "✪", "⌬", "⬥", "◈", "▼",
    ];

    private static readonly string[] Colors =
    [
        "#5B8CFF", "#FF6B6B", "#1ABC9C", "#F39C12", "#9B59B6",
        "#2ECC71", "#E74C3C", "#3498DB", "#E67E22", "#16A085",
        "#8E44AD", "#27AE60", "#D35400", "#2980B9", "#C0392B",
    ];

    private static readonly Random Rng = new();

    /// <summary>Returns a random (icon, colour) pair from the curated palettes.</summary>
    public static (string Icon, string Color) Random()
        => (Icons[Rng.Next(Icons.Length)], Colors[Rng.Next(Colors.Length)]);
}
