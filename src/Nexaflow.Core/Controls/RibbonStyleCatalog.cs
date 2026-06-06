namespace Nexaflow.Core.Controls;

/// <summary>
/// Shared icon + colour-swatch banks for ribbon styling. Used by both the full
/// <see cref="RibbonEditor"/> and the per-button right-click style flyouts on
/// <see cref="RibbonBar"/> so every styling surface offers the identical palette.
/// </summary>
internal static class RibbonStyleCatalog
{
    /// <summary>Emoji glyphs offered as button icons.</summary>
    public static readonly string[] Icons =
    [
        "🖥","📁","📂","💾","🗂","📄","📝","📋","🗒","🗃",
        "🔍","🔎","🔬","🔭","📡","🛰","🌐","🔗","📎","📌",
        "✂","🖊","🖋","✒","🖌","🖍","📐","📏","📌","📍",
        "💬","🗨","🗯","📢","📣","🔔","🔕","📯","🎵","🎶",
        "⌨","🖱","🖨","📠","📟","📺","📻","🎙","🎚","🎛",
        "⚙","🔧","🔨","🪛","🔩","🪝","🧰","🪜","🧲","💡",
        "🔋","🔌","💻","🖥","📱","⌚","🕹","🎮","🗜","💿",
        "📦","🧳","🗺","🗓","📅","📆","🗑","📤","📥","📬",
        "🏠","🏢","🏗","🏭","🏦","🏥","🏛","🎓","🏆","⭐",
        "❤","💙","💚","💛","🟥","🟦","🟩","🟨","⬛","⬜",
        "➕","➖","✖","➗","🟰","✅","❌","⚠","ℹ","🔒",
        "🔓","👤","👥","🤖","🐛","🦋","🌟","💫","⚡","🔥",
    ];

    /// <summary>
    /// Categorical swatch-bank keys (resolved to hex against the live theme at paint time) so the
    /// ribbon pickers draw from the same curated, theme-tuned palette as every other component.
    /// </summary>
    public static readonly string[] SwatchKeys =
    [
        "Swatch.Blue",  "Swatch.Cyan",   "Swatch.Teal",  "Swatch.Green",
        "Swatch.Lime",  "Swatch.Yellow", "Swatch.Amber", "Swatch.Orange",
        "Swatch.Red",   "Swatch.Pink",   "Swatch.Purple","Swatch.Slate",
    ];
}
