using System.Collections.Generic;

namespace Nexaflow.Features.Tabular.Detection;

public enum LineEndingKind { Lf, Cr, Crlf }

/// <summary>
/// A candidate interpretation of the file produced by <see cref="ShapeDetector"/>.
/// Multiple shapes may be returned with scores; if the top score is ambiguous
/// the orchestrator asks <c>IAIService.DisambiguateOptionAsync</c> to pick one.
/// </summary>
public sealed record CsvShape
{
    public required char?           Separator       { get; init; }
    public required char?           Quote           { get; init; }
    public required bool            HasHeader       { get; init; }
    public required IReadOnlyList<CsvDataType> ColumnTypes { get; init; }
    public required int             FieldCount      { get; init; }
    public required float           Score           { get; init; }
    public required string          Label           { get; init; }
    public required string          Description     { get; init; }

    /// <summary>When non-null, this shape is fixed-width and the values are the start byte column of each field.</summary>
    public IReadOnlyList<int>? FixedWidthBoundaries { get; init; }
}
