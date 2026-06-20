using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Visuals.Terminal.Models;

/// <summary>A name/value pair from the environment, shown in the terminal's Environment sub-tab.</summary>
public partial class EnvVar : ObservableObject
{
    public string Name  { get; init; } = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
}
