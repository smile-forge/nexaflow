using System.Text.Json.Serialization;

namespace Nexaflow.Features.ProductManager.Model;

/// <summary>
/// A typed, <em>loose</em> connection from a node to another entity — it records an intended alignment
/// but never hard-breaks if the target drifts. A snaplink can carry its own <see cref="Status"/>.
/// </summary>
/// <remarks>
/// Tolerant by design: only the fields a given <see cref="Type"/> uses are populated (the rest are
/// omitted on save), so new link types can be added without a schema migration.
/// <list type="bullet">
///   <item><c>markdown</c> → <see cref="Doc"/> + <see cref="TitlePath"/> (heading path, not a line number).</item>
///   <item><c>code</c> → <see cref="Class"/> + optional <see cref="Method"/>.</item>
///   <item>other → <see cref="Target"/> (a free string, e.g. a URL).</item>
/// </list>
/// </remarks>
public sealed class Snaplink
{
    public string Type { get; set; } = "markdown";
    public Status? Status { get; set; }

    // markdown
    public string? Doc { get; set; }
    public List<string>? TitlePath { get; set; }

    // code
    public string? Class { get; set; }
    public string? Method { get; set; }
    /// <summary>Stable structure path (from CodeStructureExtractor) for precise navigation; survives edits.</summary>
    public string? Ast { get; set; }

    // other (url, …)
    public string? Target { get; set; }

    /// <summary>A short human label for the link target, for the details list.</summary>
    [JsonIgnore]
    public string Display => Type switch
    {
        "markdown" => TitlePath is { Count: > 0 } ? $"{Doc} › {string.Join(" › ", TitlePath)}" : Doc ?? "(markdown)",
        "code"     => Method is { Length: > 0 } ? $"{Class}.{Method}" : Class ?? Doc ?? "(code)",
        _          => Target ?? Type
    };
}
