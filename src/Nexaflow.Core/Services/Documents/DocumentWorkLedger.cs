using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Services.Documents;

/// <summary>
/// The whole-document work under way in a workspace, by document: what each job is doing to which document and how far it has
/// got, whoever set it going — a page's button or the assistant. A job enters itself as it starts and leaves as it ends
/// (<see cref="Begin"/>); a page follows its document's (<see cref="Watch"/>). The same work is never under way twice on one
/// document — a second summary, a second translation into the same language — while any number of questions may be asked of it.
/// </summary>
internal sealed class DocumentWorkLedger
{
    private readonly Lock _gate = new();
    private readonly List<Underway> _underway = [];

    /// <summary>Raised with the document's full path whenever work on it starts, moves on or ends — on the job's thread.</summary>
    public event Action<string>? Changed;

    /// <summary>
    /// Enters <paramref name="kind"/> of work on <paramref name="path"/>, translating into <paramref name="language"/> where it is
    /// a translation; null where the same work is under way on it already.
    /// </summary>
    public Underway? Begin(string path, DocumentWorkKind kind, string? language)
    {
        var document = Path.GetFullPath(path);
        Underway entered;

        lock (_gate)
        {
            if (kind != DocumentWorkKind.Question && _underway.Any(work => work.Is(document, kind, language))) return null;

            entered = new Underway(this, document, kind, language);
            _underway.Add(entered);
        }

        Changed?.Invoke(document);
        return entered;
    }

    /// <summary>The work under way on <paramref name="path"/>, in the order it was set going.</summary>
    public IReadOnlyList<DocumentWorkUnderway> On(string path)
    {
        var document = Path.GetFullPath(path);
        lock (_gate) return [.. _underway.Where(work => work.Document.Equals(document, StringComparison.OrdinalIgnoreCase)).Select(work => work.Said)];
    }

    /// <summary>A watch on the work under way on <paramref name="path"/>, kept current on the thread that asks for it.</summary>
    public IDocumentWorkWatch Watch(string path) => new DocumentWorkWatch(this, Path.GetFullPath(path));

    private void End(Underway work)
    {
        bool ended;
        lock (_gate) ended = _underway.Remove(work);
        if (ended) Changed?.Invoke(work.Document);
    }

    /// <summary>One job's work on one document, entered while it runs: it says how far it has got, and once disposed it is over.</summary>
    internal sealed class Underway(DocumentWorkLedger ledger, string document, DocumentWorkKind kind, string? language) : IDisposable
    {
        private volatile string? _progress;

        /// <summary>The document's full path.</summary>
        public string Document => document;

        /// <summary>It, as a page is told it.</summary>
        public DocumentWorkUnderway Said => new(kind, language, _progress);

        /// <summary>Whether it is <paramref name="other"/> work of this kind, into this language.</summary>
        public bool Is(string other, DocumentWorkKind otherKind, string? otherLanguage) =>
            document.Equals(other, StringComparison.OrdinalIgnoreCase) && kind == otherKind
            && string.Equals(language?.Trim(), otherLanguage?.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Says how far it has got, as its background task does.</summary>
        public void Report(string progress)
        {
            _progress = progress;
            ledger.Changed?.Invoke(document);
        }

        public void Dispose() => ledger.End(this);
    }
}

/// <summary>
/// <see cref="IDocumentWorkWatch"/> over a workspace's <see cref="DocumentWorkLedger"/>: read again whenever work on its document
/// changes, on the thread that made it.
/// </summary>
internal sealed partial class DocumentWorkWatch : ObservableObject, IDocumentWorkWatch
{
    private readonly DocumentWorkLedger _ledger;
    private readonly string _document;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    public DocumentWorkWatch(DocumentWorkLedger ledger, string document)
    {
        _ledger   = ledger;
        _document = document;
        _ledger.Changed += OnChanged;
        Refresh();
    }

    [ObservableProperty] private IReadOnlyList<DocumentWorkUnderway> _underway = [];

    private void OnChanged(string document)
    {
        if (!document.Equals(_document, StringComparison.OrdinalIgnoreCase)) return;

        if (_ui.CheckAccess()) Refresh();
        else _ui.InvokeAsync(Refresh);
    }

    private void Refresh() => Underway = _ledger.On(_document);

    public void Dispose() => _ledger.Changed -= OnChanged;
}
