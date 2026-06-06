using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Elevation.Contracts;

/// <summary>
/// The single JSON configuration both ends of the bridge use, so the wire format never drifts.
/// Enums travel as strings; property matching is case-insensitive (mirrors the Aria pipe convention).
/// </summary>
public static class ElevationJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
