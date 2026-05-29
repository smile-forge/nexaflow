using System;
using System.Collections.Generic;
using System.Text.Json;
using Nexaflow.Features.Tabular.Detection;

namespace Nexaflow.Features.Tabular.Streaming;

/// <summary>
/// JSON round-trip for the user-applied transform chain. The serialized form is stored in
/// ribbon pin <c>PageParams</c> so that re-opening a pinned tab restores every merge,
/// split, evaluate-as and rename the user had applied to it.
/// </summary>
public static class TransformSerializer
{
    public static string Serialize(IReadOnlyList<IColumnTransform> transforms)
    {
        if (transforms.Count == 0) return "[]";
        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (var t in transforms)
            {
                switch (t)
                {
                    case MergeWithPreviousTransform m:
                        w.WriteStartObject();
                        w.WriteString("kind",  "MergePrev");
                        w.WriteNumber("index", m.MergeIndex);
                        w.WriteString("join",  m.MergeJoinWith);
                        w.WriteEndObject();
                        break;
                    case SplitByTransform s:
                        w.WriteStartObject();
                        w.WriteString("kind",  "Split");
                        w.WriteNumber("index", s.SplitIndex);
                        w.WriteString("sep",   s.SplitSeparator.ToString());
                        w.WriteNumber("arity", s.SplitArity);
                        w.WriteEndObject();
                        break;
                    case EvaluateAsTransform e:
                        w.WriteStartObject();
                        w.WriteString("kind",  "Eval");
                        w.WriteNumber("index", e.EvalIndex);
                        w.WriteString("type",  e.EvalTarget.ToString());
                        w.WriteEndObject();
                        break;
                    case RenameColumnTransform r:
                        w.WriteStartObject();
                        w.WriteString("kind",  "Rename");
                        w.WriteNumber("index", r.RenameIndex);
                        w.WriteString("name",  r.RenameTo);
                        w.WriteEndObject();
                        break;
                    case MergeColumnsTransform mc:
                        w.WriteStartObject();
                        w.WriteString("kind", "MergeMany");
                        w.WriteString("join", mc.MergedJoinWith);
                        w.WritePropertyName("indices");
                        w.WriteStartArray();
                        foreach (var i in mc.MergedIndices) w.WriteNumberValue(i);
                        w.WriteEndArray();
                        w.WriteEndObject();
                        break;
                }
            }
            w.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public static List<IColumnTransform> Deserialize(string? json)
    {
        var result = new List<IColumnTransform>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var kind = el.GetProperty("kind").GetString();
                switch (kind)
                {
                    case "MergePrev":
                        result.Add(new MergeWithPreviousTransform(
                            el.GetProperty("index").GetInt32(),
                            el.GetProperty("join").GetString() ?? " "));
                        break;
                    case "Split":
                        var sepStr = el.GetProperty("sep").GetString();
                        if (string.IsNullOrEmpty(sepStr)) break;
                        result.Add(new SplitByTransform(
                            el.GetProperty("index").GetInt32(),
                            sepStr![0],
                            el.GetProperty("arity").GetInt32()));
                        break;
                    case "Eval":
                        if (Enum.TryParse<CsvDataType>(el.GetProperty("type").GetString(), out var ty))
                            result.Add(new EvaluateAsTransform(el.GetProperty("index").GetInt32(), ty));
                        break;
                    case "Rename":
                        result.Add(new RenameColumnTransform(
                            el.GetProperty("index").GetInt32(),
                            el.GetProperty("name").GetString() ?? string.Empty));
                        break;
                    case "MergeMany":
                        var indices = new List<int>();
                        foreach (var x in el.GetProperty("indices").EnumerateArray())
                            indices.Add(x.GetInt32());
                        result.Add(new MergeColumnsTransform(
                            indices.ToArray(),
                            el.GetProperty("join").GetString() ?? " "));
                        break;
                }
            }
        }
        catch { /* malformed payload — drop silently rather than crash the page open */ }
        return result;
    }
}
