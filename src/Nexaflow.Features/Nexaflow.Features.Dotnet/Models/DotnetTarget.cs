namespace Nexaflow.Features.Dotnet.Models;

/// <summary>
/// A buildable .NET target found in a folder: a solution (<c>.slnx</c>/<c>.sln</c>) or a
/// project file (<c>.csproj</c>/<c>.fsproj</c>/<c>.vbproj</c>).
/// </summary>
public sealed record DotnetTarget(string DisplayName, string Path, bool IsSolution);
