using FellowOakDicom;

namespace Nexaflow.Features.Dicom.Models;

/// <summary>
/// Reads the small set of DICOM attributes the viewer surfaces, and draws the line between
/// <b>identifying</b> (patient PHI) and <b>non-identifying</b> (study/series/geometry) data. The PHI
/// split is the single source of truth for two rules: the <c>Hide patient info</c> toggle masks the
/// identifying fields, and the AI (<c>GetContext</c> / client tools) is only ever shown the
/// non-identifying fields.
/// </summary>
internal static class DicomTags
{
    /// <summary>Reads a single string value, trimming, with a friendly fallback for absent/blank.</summary>
    public static string Str(DicomDataset ds, DicomTag tag, string fallback = "")
    {
        try
        {
            var v = ds.GetSingleValueOrDefault(tag, string.Empty);
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }
        catch { return fallback; }
    }

    public static double? Double(DicomDataset ds, DicomTag tag)
        => ds.TryGetSingleValue(tag, out double v) ? v : null;

    public static int Int(DicomDataset ds, DicomTag tag, int fallback = 0)
        => ds.TryGetSingleValue(tag, out int v) ? v : fallback;

    /// <summary>
    /// DICOM <c>PatientName</c> is stored as <c>Family^Given^Middle^Prefix^Suffix</c>. Turn it into a
    /// readable "Given Family" for display.
    /// </summary>
    public static string PersonName(DicomDataset ds, DicomTag tag, string fallback = "(unknown)")
    {
        var raw = Str(ds, tag);
        if (string.IsNullOrEmpty(raw)) return fallback;
        var parts = raw.Split('^');
        var family = parts.Length > 0 ? parts[0].Trim() : string.Empty;
        var given = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        var name = string.Join(' ', new[] { given, family }.Where(s => !string.IsNullOrEmpty(s)));
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    /// <summary>Formats a DICOM date (<c>YYYYMMDD</c>) as <c>YYYY-MM-DD</c>, else the raw/fallback.</summary>
    public static string Date(DicomDataset ds, DicomTag tag, string fallback = "")
    {
        var raw = Str(ds, tag);
        if (raw.Length == 8 && raw.All(char.IsDigit))
            return $"{raw[..4]}-{raw.Substring(4, 2)}-{raw.Substring(6, 2)}";
        return string.IsNullOrEmpty(raw) ? fallback : raw;
    }
}
