namespace Nexaflow.Features.Console.Models;

public sealed class ConsoleEnvironment
{
    public string  Name           { get; set; } = string.Empty;
    public string  TabTitle       { get; set; } = string.Empty;
    public string  LocationFilter { get; set; } = "*";
    public string? InitialCommand { get; set; }
    public bool    IsDefault      { get; set; }
}
