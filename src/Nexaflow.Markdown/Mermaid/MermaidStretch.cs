using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// A statement written across several lines rather than one — a note written until its <c>end note</c>, a label written until its
/// closing quotes.
///
/// <para>
/// <strong>The parser finds the stretch and the grammar reads it.</strong> <see cref="MermaidParser"/> takes the line that opens
/// one, every line after it and the line that ends it, and stitches what this reads of each back together with the space between
/// them — so the whole stretch is one line of the tree and prints as exactly the characters written, and no diagram has to walk the
/// lines itself to find where its own block constructs end.
/// </para>
/// </summary>
/// <param name="Kind">The kind the whole stretch is read as.</param>
/// <param name="Opens">A line read as opening one, or null where it opens none — which is how the parser knows a stretch starts here.</param>
/// <param name="Ends">Whether a line ends the stretch that is open.</param>
/// <param name="Inside">A line inside it, read: whatever it would say on its own, here it says what the stretch says.</param>
/// <param name="Ended">The line that ends it, read.</param>
/// <param name="Unclosed">What is wrong where nothing ends it, which leaves the line that opened it a statement on its own.</param>
public sealed record MermaidStretch(
    string Kind,
    Func<string, ContentNode?> Opens,
    Func<string, bool> Ends,
    Func<string, ContentNode> Inside,
    Func<string, ContentNode> Ended,
    string Unclosed);
