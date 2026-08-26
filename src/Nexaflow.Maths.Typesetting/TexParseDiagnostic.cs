namespace XamlMath;

/// <summary>
/// A part of the input the parser could not read, and why.
/// <para>
/// Produced only by <see cref="TexFormulaParser.ParseWithRecovery(SourceSpan, string?)"/>, which carries
/// on past what it cannot understand instead of giving up on the whole formula. A formula that comes back
/// with any of these is still a formula and still draws — but the stretches named here were not
/// interpreted, they were merely shown, so anything reading meaning out of the result should treat them
/// as the parser's best guess rather than as structure.
/// </para>
/// </summary>
/// <param name="Message">What went wrong, in the parser's own words.</param>
/// <param name="At">The characters given up on.</param>
public sealed record TexParseDiagnostic(string Message, SourceSpan At);
