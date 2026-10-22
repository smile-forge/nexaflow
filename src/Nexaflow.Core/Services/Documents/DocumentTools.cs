using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Services.Documents;

/// <summary>
/// The tools that work through a whole document — summarising it, translating it, answering a question about it — offered
/// when the workspace's Documents row has a model now.
/// </summary>
internal sealed class DocumentTools(DocumentJob job)
{
    public SummarizeDocumentTool Summary { get; } = new(job);

    public TranslateDocumentTool Translation { get; } = new(job);

    public AskDocumentTool Question { get; } = new(job);

    public IReadOnlyList<IClientTool> Tools => [Summary, Translation, Question];
}
