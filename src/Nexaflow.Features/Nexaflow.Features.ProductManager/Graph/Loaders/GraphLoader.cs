using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Graph.Store;

namespace Nexaflow.Features.ProductManager.Graph.Loaders;

/// <summary>
/// Reads a graph archive into a <see cref="KnowledgeGraph"/> through the same reader the builder writes
/// with, so viewer and builder stay in lock-step on the on-disk shape.
/// <para>
/// It asks for the graph alone. The archive also carries the per-file material the graph was assembled
/// from — two thirds of the file, and nothing a viewer can draw — and skipping a section costs nothing,
/// which is why the sections are there.
/// </para>
/// </summary>
public sealed class GraphLoader
{
    public KnowledgeGraph Load(string filePath) => GraphArchive.ReadGraph(filePath) ?? new KnowledgeGraph();
}
