namespace Nexaflow.Features.Logs.Parsing;

public interface ILogParser
{
    /// <summary>The format's invariant name — what the assistant's page context reads. Never shown translated.</summary>
    string FormatName { get; }

    /// <summary>The format's name as the status bar shows it to a person, in the interface language.</summary>
    string FormatDisplayName { get; }

    /// <summary>
    /// Returns a confidence score [0, 1] for this parser given the file header bytes
    /// and the first decoded line. Higher wins; 0 means "cannot handle this file".
    /// </summary>
    float Confidence(ReadOnlySpan<byte> headerBytes, string firstLine);

    LogLine ParseLine(string rawText);
}
