using System;
using System.Collections.Generic;
using Nexaflow.Visuals.Text.Markdown.Qr;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// What an <c>aztec</c> block's <c>type:</c> means — the shared vocabulary a <c>qr</c> block has,
/// because a <c>WIFI:</c> descriptor or a vCard decodes the same from any symbol, plus GS1.
///
/// <para>
/// Aztec takes GS1 and not the pharmacy or postal formats Data Matrix carries, and the line between
/// them is not arbitrary: a PPN or a Mailmark prescribes the symbol as part of the format, so the same
/// data in an Aztec is not that format at all. GS1 does not — it names Aztec among its carriers — so it
/// belongs to both and lives in <see cref="Gs1ElementString"/> rather than in either.
/// </para>
/// </summary>
internal static class AztecPayload
{
    /// <summary>The fields each type reads. Doubles as the spell-check for a block.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> FieldsByType = Build();

    private static IReadOnlyDictionary<string, string[]> Build()
    {
        var all = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["gs1"] = ["data"],
        };
        foreach (var (type, fields) in QrPayload.FieldsByType) all.TryAdd(type, fields);
        return all;
    }

    /// <summary>Builds the encodable string and whatever the encoder must be told alongside it.</summary>
    internal static bool TryBuild(string type, IReadOnlyDictionary<string, string> fields,
                                  AztecOptions baseline,
                                  out string? payload, out AztecOptions options, out string? error)
    {
        payload = null;
        options = baseline;
        error   = null;

        if (!type.Equals("gs1", StringComparison.OrdinalIgnoreCase))
            return QrPayload.TryBuild(type, fields, out payload, out error);

        if (!fields.TryGetValue("data", out string? data) || data.Length == 0)
        {
            error = "A `gs1` symbol needs `data:` — the element string, with each AI in brackets: (01)…(17)…(10)….";
            return false;
        }

        if (!Gs1ElementString.TryParse(data, out payload, out error)) return false;

        options = baseline with { Gs1 = true };
        return true;
    }
}
