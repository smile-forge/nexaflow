using System;

namespace Nexaflow.Markdown.Binding;

/// <summary>
/// What a binding standing on a line of its own (<c>{{Path}}</c>) can be bound to beyond plain text: content that says what
/// it is now, can be told that part of it was opened or closed, and says when it has changed.
///
/// <para>
/// <strong>So whatever shows the content keeps no logic for growing it.</strong> A diagram of something too big to write
/// out whole — a binary's dependencies — is bound to one of these: a chip pressed on a node tells it which node was opened,
/// it works out what that shows, and says it has changed; the content is read again where the binding stands, and nothing
/// else on the page is.
/// </para>
/// </summary>
public interface IBoundContent
{
    /// <summary>What it says now — the text read where the binding stands.</summary>
    string Text { get; }

    /// <summary>
    /// Opens the part of it <paramref name="key"/> names, or closes it again — what a chip pressed on a node of a diagram
    /// means. A key it knows nothing of changes nothing.
    /// </summary>
    void Expand(string key, bool open);

    /// <summary>Raised when what it says has changed — on whatever thread the change landed on.</summary>
    event EventHandler? Changed;
}
