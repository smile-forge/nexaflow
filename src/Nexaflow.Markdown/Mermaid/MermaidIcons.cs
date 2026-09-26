using Nexaflow.Icons;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// The icon a diagram names, as Mermaid's diagrams write one, read into an icon the app draws — one of the Fluent UI System
/// Icons the ribbon draws with, or an emoji.
///
/// <para>
/// Mermaid takes Font Awesome's classes (<c>fa fa-book</c>, <c>fa-solid fa-book</c>), an icon pack's <c>pack:name</c>
/// (<c>fa:fa-user</c>, <c>mdi:account</c>, <c>logos:aws</c>) and, in an architecture diagram, five of its own
/// (<c>cloud</c>, <c>database</c>, <c>disk</c>, <c>internet</c>, <c>server</c>). None of those packs is here, so a name is
/// read as the Fluent icon drawing the same thing: the Fluent icon of that name, or the one another pack's name stands for
/// where they differ. The app's own <c>fluent:name</c> and <c>fluent-filled:name</c> name one directly. A name nothing here
/// draws is no icon, and what was written is what is drawn.
/// </para>
/// </summary>
public static class MermaidIcons
{
    /// <summary>Other packs' names for the Fluent icon that draws the same thing, written as Fluent writes its names.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user"] = "person", ["account"] = "person", ["user_circle"] = "person_circle", ["circle_user"] = "person_circle",
        ["users"] = "people", ["user_group"] = "people_team", ["users_group"] = "people_team", ["account_group"] = "people_team",
        ["group"] = "people_team", ["team"] = "people_team",
        ["gear"] = "settings", ["gears"] = "settings", ["cog"] = "settings", ["cogs"] = "settings", ["sliders"] = "options",
        ["envelope"] = "mail", ["email"] = "mail", ["inbox"] = "mail",
        ["trash"] = "delete", ["trash_can"] = "delete", ["trash_alt"] = "delete",
        ["pen"] = "edit", ["pencil"] = "edit", ["pen_to_square"] = "edit",
        ["check"] = "checkmark", ["xmark"] = "dismiss", ["times"] = "dismiss", ["close"] = "dismiss",
        ["magnifying_glass"] = "search", ["magnify"] = "search",
        ["lock"] = "lock_closed", ["unlock"] = "lock_open",
        ["file"] = "document", ["file_alt"] = "document_text", ["file_lines"] = "document_text", ["file_document"] = "document_text",
        ["bell"] = "alert", ["comments"] = "chat", ["message"] = "chat", ["phone"] = "call", ["cellphone"] = "phone", ["mobile"] = "phone",
        ["picture"] = "image", ["photo"] = "image",
        ["chart_bar"] = "data_bar_vertical", ["chart_line"] = "data_line", ["chart_pie"] = "data_pie",
        ["shopping_cart"] = "cart", ["credit_card"] = "payment", ["dollar"] = "money", ["dollar_sign"] = "money",
        ["map_marker"] = "location", ["location_dot"] = "location", ["map_pin"] = "location",
        ["exclamation_triangle"] = "warning", ["triangle_exclamation"] = "warning",
        ["info_circle"] = "info", ["circle_info"] = "info", ["question_circle"] = "question", ["circle_question"] = "question",
        ["plus"] = "add", ["minus"] = "subtract", ["code_branch"] = "branch", ["git"] = "branch",
        ["internet"] = "globe", ["disk"] = "hard_drive", ["hdd"] = "hard_drive", ["harddisk"] = "hard_drive",
        ["robot"] = "bot", ["graduation_cap"] = "hat_graduation", ["bolt"] = "flash", ["sun"] = "weather_sunny", ["moon"] = "weather_moon",
        ["thumbs_up"] = "thumb_like", ["thumbs_down"] = "thumb_dislike", ["flask"] = "beaker", ["puzzle"] = "puzzle_piece",
        ["bullseye"] = "target", ["crosshairs"] = "target", ["download"] = "arrow_download", ["upload"] = "arrow_upload",
    };

    /// <summary>The icon written — or null where nothing the app draws is called that.</summary>
    public static IconRef? Of(string? written)
    {
        // Metadata may quote it either way.
        var said = (written ?? string.Empty).Trim().Trim('"', '\'').Trim();
        if (said.Length == 0) return null;

        if (said.StartsWith(IconRef.FluentPrefix, StringComparison.OrdinalIgnoreCase)
            || said.StartsWith(IconRef.FluentFilledPrefix, StringComparison.OrdinalIgnoreCase))
            return Drawn(IconRef.Parse(said));

        // Nothing but what no keyboard types is an emoji, which draws itself.
        if (said.All(character => !char.IsAscii(character) || char.IsWhiteSpace(character))) return IconRef.Emoji(said);

        // The last class written is the icon's own — "fa fa-book", "fa-solid fa-book" — and what follows a pack's colon is its name.
        var name = said[(said.LastIndexOfAny([' ', '\t']) + 1)..];
        name = name[(name.LastIndexOf(':') + 1)..];
        foreach (var pack in (ReadOnlySpan<string>)["fa-", "mdi-"])
            if (name.StartsWith(pack, StringComparison.OrdinalIgnoreCase)) name = name[pack.Length..];

        var fluent = name.Replace('-', '_').ToLowerInvariant();
        return Drawn(IconRef.Fluent(Aliases.GetValueOrDefault(fluent, fluent)));
    }

    /// <summary>The icon, where the font has a glyph for it.</summary>
    private static IconRef? Drawn(IconRef icon) => FluentGlyphs.Of(icon) is null ? null : icon;
}
