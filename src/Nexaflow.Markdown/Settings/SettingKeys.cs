using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Settings;

/// <summary>
/// The names a block's settings are written under, and reading them off a tree.
///
/// <para>
/// <strong>The settings are the lines above the data, and the first row closes them.</strong> That rule
/// is what lets a block hold ordinary words — a cloud counting <c>shape</c> and <c>scale</c>, a plot
/// with a column headed <c>size</c> — and it works only because a key is a setting when it is one of a
/// known few. So the known few, and how a written key is matched against them, live here rather than
/// being spelt out again by each block.
/// </para>
/// </summary>
public static class SettingKeys
{
    /// <summary>
    /// A key as it is compared: lower case, and without the hyphens a reader may write it with, so
    /// <c>min-size</c>, <c>minSize</c> and <c>MINSIZE</c> are one key.
    /// </summary>
    public static string Plain(string? key) =>
        (key ?? string.Empty).Replace("-", string.Empty).Trim().ToLowerInvariant();

    /// <summary>Whether a written key names one of <paramref name="known"/>.</summary>
    public static bool Is(string? key, IReadOnlyList<string> known)
    {
        var name = Plain(key);

        foreach (var one in known)
            if (Plain(one) == name) return true;

        return false;
    }

    /// <summary>
    /// The settings a block's tree carries, by their plain names.
    /// </summary>
    /// <param name="setting">The kind a setting's line is read into.</param>
    /// <param name="value">The role its value carries.</param>
    public static Dictionary<string, string> Written(ContentNode root, string setting, string value)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in root.SelfAndDescendants())
            if (node.Kind == setting)
                fields[Plain(node.Part(Roles.Name)?.Text)] = node.Part(value)?.Text ?? string.Empty;

        return fields;
    }
}
