namespace Nexaflow.Features.Processes.Models;

/// <summary>One loaded module (DLL / the EXE), for the details view's Modules section.</summary>
public sealed record ModuleInfo
{
    public required string Name { get; init; }
    public string Path { get; init; } = "";
    public string Version { get; init; } = "";
    public string Company { get; init; } = "";
    public long Size { get; init; }
}
