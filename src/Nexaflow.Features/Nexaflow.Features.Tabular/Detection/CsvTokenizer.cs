using System.Collections.Generic;

namespace Nexaflow.Features.Tabular.Detection;

/// <summary>
/// RFC-4180-ish CSV tokenizer. When <paramref name="quote"/> is null no quoting is honored
/// (raw split on the separator). When non-null, double-quotes inside a quoted field escape
/// a literal quote (e.g. "" → ").
/// </summary>
public static class CsvTokenizer
{
    public static string[] Split(string line, char separator, char? quote)
    {
        if (quote is null)
        {
            // Simple split — keeps things explicit for un-quoted shapes.
            return line.Split(separator);
        }

        var q       = quote.Value;
        var fields  = new List<string>();
        var buf     = new System.Text.StringBuilder();
        bool inQ    = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQ)
            {
                if (c == q)
                {
                    if (i + 1 < line.Length && line[i + 1] == q) { buf.Append(q); i++; }
                    else                                          { inQ = false; }
                }
                else buf.Append(c);
            }
            else
            {
                if (c == q && buf.Length == 0)        inQ = true;
                else if (c == separator)            { fields.Add(buf.ToString()); buf.Clear(); }
                else                                  buf.Append(c);
            }
        }
        fields.Add(buf.ToString());
        return fields.ToArray();
    }
}
