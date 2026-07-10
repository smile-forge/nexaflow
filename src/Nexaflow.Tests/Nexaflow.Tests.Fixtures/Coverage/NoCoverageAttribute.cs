namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Opts a test class out of the <c>[CoversNode]</c> requirement — for tests that map to no single product
/// node (architecture/meta guards, tooling/corpus tests, infrastructure). The <see cref="Reason"/> is
/// recorded so an opt-out is a deliberate, auditable choice rather than an oversight. Abstract test bases
/// need no opt-out (they never run directly and the guard skips them).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NoCoverageAttribute(string reason) : Attribute
{
    /// <summary>Why this test class maps to no product node.</summary>
    public string Reason { get; } = reason;
}
