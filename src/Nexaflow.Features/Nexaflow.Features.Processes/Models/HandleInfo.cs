namespace Nexaflow.Features.Processes.Models;

/// <summary>
/// One open kernel handle held by a process, for the details view's Handles section. Populated only via an
/// elevated <c>process.inspect</c> (there is no managed API), so it's loaded on demand rather than on the
/// refresh tick. Value/access are pre-formatted hex strings from the bridge.
/// </summary>
public sealed record HandleInfo
{
    public string Type { get; init; } = "";
    public string Name { get; init; } = "";
    public string HandleValue { get; init; } = "";   // "0x1F4"
    public string Access { get; init; } = "";          // "0x001F0FFF"

    public string RowText => $"{Type}\t{HandleValue}\t{Access}\t{Name}";
}
