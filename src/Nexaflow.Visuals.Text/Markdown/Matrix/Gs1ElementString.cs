using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Matrix;

/// <summary>
/// A GS1 element string, written the way people write it — <c>(01)04150123456782(17)261231(10)LOT7</c> —
/// and turned into the form a symbol actually carries.
///
/// <para>
/// Shared by every symbology that can be a GS1 carrier, which is Data Matrix and Aztec here, because
/// the element string is a property of the data and not of the symbol it is drawn as. The part worth
/// having in one place is the separator rule: an application identifier whose data is a fixed length
/// needs nothing after it, and any other must be closed by a group separator unless it is last. Get
/// that wrong and the symbol scans perfectly and decodes to the wrong fields.
/// </para>
/// </summary>
internal static class Gs1ElementString
{
    /// <summary>The group separator that closes a variable-length element.</summary>
    internal const char Separator = '\u001D';

    /// <summary>How a separator is shown when the payload is put in front of a person.</summary>
    internal const string ReadableSeparator = "⟨GS⟩";

    /// <summary>
    /// Application identifiers whose data is a fixed length, and so take no separator after them. Every
    /// other AI is variable and is closed by a separator when another follows.
    /// </summary>
    private static readonly Dictionary<string, int> FixedLength = new()
    {
        ["00"] = 18, ["01"] = 14, ["02"] = 14, ["03"] = 14,
        ["11"] = 6,  ["12"] = 6,  ["13"] = 6,  ["15"] = 6, ["16"] = 6, ["17"] = 6,
        ["20"] = 2,
        ["410"] = 13, ["411"] = 13, ["412"] = 13, ["413"] = 13,
        ["414"] = 13, ["415"] = 13, ["416"] = 13, ["417"] = 13,
    };

    /// <summary>Reads a bracketed element string and returns the wire form, or says where it stopped making sense.</summary>
    internal static bool TryParse(string data, out string? payload, out string? error)
    {
        payload = null;

        var elements = Read(data, out error);
        if (elements is null) return false;

        payload = Join(elements);
        return true;
    }

    /// <summary>Splits <c>(ai)value(ai)value…</c> into pairs, or explains where it stopped being that.</summary>
    internal static List<(string Ai, string Value)>? Read(string data, out string? error)
    {
        error = null;
        var elements = new List<(string, string)>();
        int at = 0;

        while (at < data.Length)
        {
            if (data[at] != '(')
            {
                error = $"GS1 data must be a run of (AI)value pairs; at '{data[at..]}' there is no opening bracket.";
                return null;
            }

            int close = data.IndexOf(')', at);
            if (close < 0)
            {
                error = "A GS1 application identifier's bracket is never closed.";
                return null;
            }

            string ai = data[(at + 1)..close];
            if (ai.Length is < 2 or > 4 || !ai.All(char.IsAsciiDigit))
            {
                error = $"'{ai}' is not a GS1 application identifier — two to four digits.";
                return null;
            }

            int next = data.IndexOf('(', close);
            string value = next < 0 ? data[(close + 1)..] : data[(close + 1)..next];

            if (value.Length == 0)
            {
                error = $"AI ({ai}) has no value.";
                return null;
            }

            if (FixedLength.TryGetValue(ai, out int length) && value.Length != length)
            {
                error = $"AI ({ai}) takes exactly {length} characters; '{value}' is {value.Length}.";
                return null;
            }

            elements.Add((ai, value));
            at = next < 0 ? data.Length : next;
        }

        return elements;
    }

    /// <summary>The wire form: brackets off, and a separator after each variable-length element bar the last.</summary>
    internal static string Join(IReadOnlyList<(string Ai, string Value)> elements)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < elements.Count; i++)
        {
            var (ai, value) = elements[i];
            sb.Append(ai).Append(value);

            bool last = i == elements.Count - 1;
            if (!last && !FixedLength.ContainsKey(ai)) sb.Append(Separator);
        }
        return sb.ToString();
    }

    /// <summary>The payload with its separators shown, since a GS is invisible and is the point of the format.</summary>
    internal static string Readable(string payload) =>
        payload.Replace(Separator.ToString(), ReadableSeparator);
}
