using Nexaflow.Markdown.Export;

namespace Nexaflow.IO.Documents;

/// <summary>
/// A document spelled in one format. A writer only spells: what each block and run is, and what it looks like where that is
/// not the reader's to choose, are already in the document it is handed — so a new format is a writer and nothing else.
/// </summary>
public interface IDocumentWriter
{
    /// <summary><paramref name="document"/>, in this writer's format, onto <paramref name="into"/>.</summary>
    void Write(ExportDocument document, Stream into);
}
