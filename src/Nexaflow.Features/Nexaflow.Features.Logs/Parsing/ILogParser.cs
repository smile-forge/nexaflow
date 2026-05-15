namespace Nexaflow.Features.Logs.Parsing;

public interface ILogParser
{
    string FormatName { get; }

    /// <summary>
    /// Returns a confidence score [0, 1] for this parser given the file header bytes
    /// and the first decoded line. Higher wins; 0 means "cannot handle this file".
    /// </summary>
    float Confidence(ReadOnlySpan<byte> headerBytes, string firstLine);

    LogLine ParseLine(string rawText);
}
