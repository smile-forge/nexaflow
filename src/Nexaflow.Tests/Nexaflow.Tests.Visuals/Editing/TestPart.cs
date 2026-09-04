using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// A part of a parse tree that was never parsed — what a layout tree built by hand was drawn from.
///
/// <para>
/// The trees in these tests stand in for a typeset formula without a typesetter, so they need something
/// to have been drawn from. This is that something, and it is deliberately the whole of what the seam
/// asks a part for: where it is written. A piece drawn from nothing anybody wrote — a fraction's bar, a
/// decoration — is given no part at all rather than one naming no characters, because that is the
/// distinction being tested.
/// </para>
/// </summary>
internal sealed record TestPart(int Start, int Length) : ISourcePart;
