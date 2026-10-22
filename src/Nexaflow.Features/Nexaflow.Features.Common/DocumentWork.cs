using System.ComponentModel;

namespace Nexaflow.Features.Common;

/// <summary>What whole-document work does to a document (<see cref="IAIService.WatchDocumentWork"/>).</summary>
public enum DocumentWorkKind
{
    /// <summary>Summarising it, section by section.</summary>
    Summary,

    /// <summary>Translating it into another language.</summary>
    Translation,

    /// <summary>Answering a question about it.</summary>
    Question,
}

/// <summary>One piece of whole-document work under way on a document now, whoever set it going — the user from its page, or the assistant.</summary>
/// <param name="Kind">What it is doing to the document.</param>
/// <param name="Language">The language it translates into, as it was asked; null for work that is not a translation.</param>
/// <param name="Progress">How far it has got, as its background task says it — the step and the count — or null before it has said.</param>
public sealed record DocumentWorkUnderway(DocumentWorkKind Kind, string? Language, string? Progress);

/// <summary>
/// The whole-document work under way on one document, kept current on the thread that asked for it, for a page to show on
/// the controls that set such work going. Disposed, it stops following.
/// </summary>
public interface IDocumentWorkWatch : INotifyPropertyChanged, IDisposable
{
    /// <summary>The work under way on the document now, in the order it was set going.</summary>
    IReadOnlyList<DocumentWorkUnderway> Underway { get; }
}
