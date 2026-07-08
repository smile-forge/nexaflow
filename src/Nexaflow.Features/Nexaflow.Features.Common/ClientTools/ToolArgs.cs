using System.Text.Json.Nodes;

namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// Tolerant readers for the loosely-typed <see cref="JsonObject"/> arguments the model emits for a
/// client tool. Shared so every feature reads tool arguments the same forgiving way (typed value
/// first, then string/number coercion) instead of re-implementing it per feature.
/// </summary>
public static class ToolArgs
{
    /// <summary>First present, non-blank argument among <paramref name="keys"/>, read as a trimmed string.</summary>
    public static string? Str(JsonObject args, params string[] keys)
    {
        foreach (var k in keys)
            if (args.TryGetPropertyValue(k, out var n) && n is not null)
            {
                var s = n is JsonValue v && v.TryGetValue<string>(out var sv) ? sv : n.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
        return null;
    }

    /// <summary>Reads a boolean (JSON bool or "true"/"false" string); <paramref name="fallback"/> if absent/invalid.</summary>
    public static bool Bool(JsonObject args, string key, bool fallback = false)
    {
        if (args.TryGetPropertyValue(key, out var n) && n is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b)) return b;
            if (v.TryGetValue<string>(out var s) && bool.TryParse(s, out var bs)) return bs;
        }
        return fallback;
    }

    /// <summary>Reads an integer (JSON number or numeric string); <paramref name="fallback"/> if absent/invalid.</summary>
    public static int Int(JsonObject args, string key, int fallback)
        => IntOrNull(args, key) ?? fallback;

    /// <summary>Reads an integer (JSON number or numeric string); null if absent/invalid — for tools
    /// where "not given" means something different from any default.</summary>
    public static int? IntOrNull(JsonObject args, string key)
    {
        if (args.TryGetPropertyValue(key, out var n) && n is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<double>(out var d)) return (int)d;
            if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var p)) return p;
        }
        return null;
    }
}
