using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace Nexaflow.Features.Json.Models;

public sealed class JsonBreadcrumbItem : ObservableObject
{
    public string         Label           { get; init; } = string.Empty;
    public JsonNodeModel? Node            { get; init; }
    public ICommand       NavigateCommand { get; init; } = null!;
}
