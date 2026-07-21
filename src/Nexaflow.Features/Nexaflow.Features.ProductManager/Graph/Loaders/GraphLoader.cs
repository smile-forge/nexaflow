using System.IO;
using System.Text.Json;
using Nexaflow.IO.Common;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Features.ProductManager.Graph.Loaders;

/// <summary>
/// Reads a <c>graph.json</c> into a <see cref="KnowledgeGraph"/> using the same <c>ProductJson.Options</c> the
/// builder writes with (snake_case) — so viewer and builder stay in lock-step on the on-disk shape.
/// </summary>
public sealed class GraphLoader
{
    public KnowledgeGraph Load(string filePath) =>
        JsonSerializer.Deserialize<KnowledgeGraph>(
            VirtualFileSystem.Instance.ReadAllText(filePath), ProductJson.Options) ?? new KnowledgeGraph();
}
