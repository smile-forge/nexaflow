using System.Collections.ObjectModel;

namespace Nexaflow.Features.Json.Models;

public sealed class JsonObjectNodeModel : JsonNodeModel
{
    public ObservableCollection<JsonNodeModel> Children { get; } = [];
}
