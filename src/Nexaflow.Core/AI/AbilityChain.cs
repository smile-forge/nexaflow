using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Core.AI;

/// <summary>
/// The ability-grid columns that serve one ability, in order of preference. A request goes to the first whose
/// provider is available — and, for a page whose context is high-risk, to the first whose data stays on this
/// machine. An empty chain means the ability has no model.
/// </summary>
[JsonConverter(typeof(AbilityChainConverter))]
public sealed class AbilityChain
{
    public static AbilityChain Empty { get; } = new([]);

    public AbilityChain(IEnumerable<string> columnIds)
        => ColumnIds = [.. columnIds.Where(id => !string.IsNullOrEmpty(id)).Distinct()];

    public static AbilityChain Of(params string[] columnIds) => new(columnIds);

    public IReadOnlyList<string> ColumnIds { get; }

    public bool IsEmpty => ColumnIds.Count == 0;

    public bool SameAs(AbilityChain other) => ColumnIds.SequenceEqual(other.ColumnIds);
}

/// <summary>
/// Writes a chain as an array of column ids. Reads that, and a single id — the shape a grid saved when each
/// ability had one model — as a chain of one, so a saved grid keeps its assignments whichever version wrote it.
/// </summary>
internal sealed class AbilityChainConverter : JsonConverter<AbilityChain>
{
    public override AbilityChain Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return AbilityChain.Empty;

            case JsonTokenType.String:
                return AbilityChain.Of(reader.GetString() ?? string.Empty);

            case JsonTokenType.StartArray:
                var ids = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.String)
                        throw new JsonException($"An ability chain holds column ids, not {reader.TokenType}.");
                    ids.Add(reader.GetString() ?? string.Empty);
                }
                return new AbilityChain(ids);

            default:
                throw new JsonException($"An ability chain is a column id or an array of them, not {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, AbilityChain value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var id in value.ColumnIds) writer.WriteStringValue(id);
        writer.WriteEndArray();
    }
}
