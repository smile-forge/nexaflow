using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>One real packet, with a breakdown that must account for every byte.</summary>
public sealed class CorpusCapture
{
    public string Label { get; set; } = "";
    public string Hex { get; set; } = "";

    /// <summary><c>offset:length:name:value</c>, one per line.</summary>
    public string FieldBreakdown { get; set; } = "";

    public string? Notes { get; set; }

    /// <summary>The packet bytes.</summary>
    public byte[] Bytes => Convert.FromHexString(Hex.Replace(" ", "").Replace("\n", "").Replace("\r", ""));

    /// <summary>The breakdown rows, parsed. Rows that do not start <c>&lt;int&gt;:&lt;int&gt;:</c> are
    /// commentary and are skipped — the breakdowns are prose-annotated by design.</summary>
    public IReadOnlyList<(int Offset, int Length, string Label)> Fields()
    {
        List<(int, int, string)> rows = [];
        foreach (var line in FieldBreakdown.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0) continue;

            var parts = t.Split(':', 3);
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0], out var off) || !int.TryParse(parts[1], out var len)) continue;

            rows.Add((off, len, parts[2]));
        }
        return rows;
    }
}

/// <summary>What one protocol's stress analysis produced.</summary>
public sealed class CorpusProtocol
{
    public string Protocol { get; set; } = "";
    public string Slug { get; set; } = "";
    public List<CorpusCapture> Captures { get; set; } = [];
    public string? Statefulness { get; set; }
    public List<CorpusGap> Gaps { get; set; } = [];

    [JsonPropertyName("v0RoundTrips")] public bool V0RoundTrips { get; set; }
}

/// <summary>Something the v0 grammar could not express, and the proposed fix.</summary>
public sealed class CorpusGap
{
    public string Severity { get; set; } = "";
    public string What { get; set; } = "";
    public string Why { get; set; } = "";
    public string FixKind { get; set; } = "";
    public string ProposedFix { get; set; } = "";
}

/// <summary>
/// Loads the checked-in protocol corpus — ten real wire formats chosen so that each defeats the grammar in
/// a different way (nested BER lengths, DNS name compression, accumulating CoAP option deltas, MQTT
/// varints, BACnet's three stacked sub-protocols, and so on).
///
/// <para>
/// This is the bar the DynamicProtocol engine is built against, and it exists <b>before</b> the engine on
/// purpose: the first version of the grammar failed all ten, and finding that on paper cost a day instead
/// of a rewrite.
/// </para>
/// </summary>
public static class ProtocolCorpus
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static string CorpusDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Protocol", "Corpus");

    public static IReadOnlyList<CorpusProtocol> All()
    {
        if (!Directory.Exists(CorpusDirectory)) return [];

        List<CorpusProtocol> all = [];
        foreach (var file in Directory.GetFiles(CorpusDirectory, "*.json").OrderBy(f => f))
            if (JsonSerializer.Deserialize<CorpusProtocol>(File.ReadAllText(file), Json) is { } p)
                all.Add(p);

        return all;
    }

    public static CorpusProtocol Get(string slug)
        => All().FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException(
               $"No corpus entry '{slug}'. Present: {string.Join(", ", All().Select(p => p.Slug))}");
}
