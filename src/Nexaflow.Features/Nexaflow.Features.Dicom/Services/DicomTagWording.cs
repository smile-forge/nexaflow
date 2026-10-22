using System.Globalization;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Dicom.Services;

/// <summary>
/// The words <see cref="DicomTagReader"/> writes into a row in place of a value it does not print as-is — a tag
/// the dictionary has no name for, a masked identifier, a sequence, binary data. The tag drawer reads them in the
/// shown language (<see cref="ForDisplay"/>); the assistant's tools read them in English (<see cref="Assistant"/>),
/// because what a model reads stays English.
/// </summary>
internal sealed class DicomTagWording
{
    private readonly string _sequenceFormat;

    private DicomTagWording(string unknownName, string hiddenValue, string binaryValue, string sequenceFormat)
    {
        UnknownName = unknownName;
        HiddenValue = hiddenValue;
        BinaryValue = binaryValue;
        _sequenceFormat = sequenceFormat;
    }

    /// <summary>The English wording the assistant's tools read.</summary>
    public static DicomTagWording Assistant { get; } =
        new("Private / Unknown", "••• (hidden)", "‹binary›", "‹sequence: {0} item(s)›");

    /// <summary>The shown language's wording, for the tag drawer — looked up per read, so a language change is
    /// picked up by the next one.</summary>
    public static DicomTagWording ForDisplay() => new(
        Str.Get("Dicom.Tags.UnknownName"),
        Str.Get("Dicom.Tags.Hidden"),
        Str.Get("Dicom.Tags.Binary"),
        Str.Get("Dicom.Tags.SequenceFormat"));

    /// <summary>The name of a private tag, or one the DICOM dictionary does not know.</summary>
    public string UnknownName { get; }

    /// <summary>The value shown for an identifying tag while patient info is hidden.</summary>
    public string HiddenValue { get; }

    /// <summary>The value shown for an element that does not read as text.</summary>
    public string BinaryValue { get; }

    /// <summary>The value shown for a sequence of <paramref name="itemCount"/> items.</summary>
    public string SequenceValue(int itemCount) => string.Format(CultureInfo.CurrentCulture, _sequenceFormat, itemCount);
}
