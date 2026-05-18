using System.Collections.ObjectModel;

namespace Nexaflow.Features.Json.Models;

public sealed class JsonArrayNodeModel : JsonNodeModel
{
    public ObservableCollection<JsonNodeModel> Children { get; } = [];
}
