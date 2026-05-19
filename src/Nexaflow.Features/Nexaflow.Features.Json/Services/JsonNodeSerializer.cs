using Nexaflow.Features.Json.Models;
using System.Text;
using System.Text.Json;

namespace Nexaflow.Features.Json.Services;

internal static class JsonNodeSerializer
{
    public static string Serialize(JsonNodeModel root, bool indented = true)
    {
        var options = new JsonWriterOptions { Indented = indented };
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            WriteNode(writer, root);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonNodeModel node)
    {
        switch (node)
        {
            case JsonObjectNodeModel obj:
                writer.WriteStartObject();
                foreach (var child in obj.Children)
                {
                    writer.WritePropertyName(child.Key ?? string.Empty);
                    WriteNode(writer, child);
                }
                writer.WriteEndObject();
                break;

            case JsonArrayNodeModel arr:
                writer.WriteStartArray();
                foreach (var child in arr.Children)
                    WriteNode(writer, child);
                writer.WriteEndArray();
                break;

            case JsonValueNodeModel val:
                switch (val.ValueKind)
                {
                    case JsonValueKind.String:
                        writer.WriteStringValue(val.Value?.ToString());
                        break;
                    case JsonValueKind.Number:
                        if (val.Value is long l)        writer.WriteNumberValue(l);
                        else if (val.Value is double d) writer.WriteNumberValue(d);
                        else writer.WriteRawValue(val.Value?.ToString() ?? "0");
                        break;
                    case JsonValueKind.True:
                        writer.WriteBooleanValue(true);
                        break;
                    case JsonValueKind.False:
                        writer.WriteBooleanValue(false);
                        break;
                    default:
                        writer.WriteNullValue();
                        break;
                }
                break;

            case VirtualJsonNodeModel:
                // Should not occur during a save — VM guards against this
                writer.WriteNullValue();
                break;
        }
    }
}
