using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;

namespace Nexaflow.Services.Initiatives.Cli.Building;

/// <summary>
/// The installed SDK's MSBuild, loaded into this process. <c>Microsoft.Build</c> is compiled against but never shipped
/// beside nfi: the engine that schedules a build has to be the one the SDK's worker processes speak to, and the SDK a
/// tree builds with is the one its <c>global.json</c> selects.
/// <para>
/// An engine loads once per process, so a process serves the trees that select the same SDK. Each tree's choice is
/// asked once (it runs <c>dotnet --info</c>) and remembered for the life of the process.
/// </para>
/// </summary>
internal static class MSBuildRuntime
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string?> Selected = new(StringComparer.OrdinalIgnoreCase);
    private static string? _loaded;

    /// <summary>Null once the SDK the tree at <paramref name="codeRoot"/> selects is the MSBuild loaded here — loading
    /// it if none is yet — otherwise why it cannot be.</summary>
    public static string? Register(string codeRoot)
    {
        lock (Gate)
        {
            if (!Selected.TryGetValue(codeRoot, out var wanted))
                Selected[codeRoot] = wanted = Select(codeRoot);

            if (wanted is null) return $"no .NET SDK resolves for {codeRoot} (`dotnet --info` there names none)";
            if (_loaded is null)
            {
                MSBuildLocator.RegisterMSBuildPath(wanted);
                _loaded = wanted;

                // Worker processes are started with the host this names; unset, MSBuild would start them with nfi.exe.
                if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is not { Length: > 0 }
                    && Path.GetFullPath(Path.Combine(wanted, "..", "..", "dotnet.exe")) is var host && File.Exists(host))
                    Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", host);
                return null;
            }

            return string.Equals(Path.TrimEndingDirectorySeparator(_loaded), Path.TrimEndingDirectorySeparator(wanted),
                                 StringComparison.OrdinalIgnoreCase)
                ? null
                : $"{codeRoot} builds with the SDK in {wanted}, and nfi's resident process has the one in {_loaded} loaded — "
                + "`nfi daemon stop`, then build again";
        }
    }

    private static string? Select(string codeRoot) =>
        MSBuildLocator.QueryVisualStudioInstances(new VisualStudioInstanceQueryOptions
        {
            DiscoveryTypes   = DiscoveryType.DotNetSdk,
            WorkingDirectory = codeRoot,
        }).FirstOrDefault()?.MSBuildPath;
}

/// <summary>
/// The <see cref="IBuildEngine"/> that is the SDK's MSBuild. Nothing here names a <c>Microsoft.Build</c> type: those
/// are only loadable once <see cref="MSBuildRuntime"/> has found the SDK, so the engine proper —
/// <see cref="MSBuildDriver"/> — is created on the first build a tree is admitted for, and not before.
/// </summary>
/// <param name="name">What its scheduler is called — one per pool, each with its own sessions.</param>
internal sealed class HostedMSBuild(string name, int nodes, bool reuseNodes) : IBuildEngine
{
    private IBuildEngine? _driver;

    public int Nodes => nodes;

    public string? Admit(string codeRoot) => MSBuildRuntime.Register(codeRoot);

    public void BeginSession() => Driver.BeginSession();

    public Task<BuildReport> Start(BuildOrder order) => Driver.Start(order);

    public void CancelSession() => _driver?.CancelSession();

    public void EndSession() => _driver?.EndSession();

    public void ReleaseNodes() => _driver?.ReleaseNodes();

    public void Dispose() => _driver?.Dispose();

    private IBuildEngine Driver => _driver ??= CreateDriver();

    // Its own method, never inlined: compiling the caller must not be what first loads Microsoft.Build.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private IBuildEngine CreateDriver() => new MSBuildDriver(name, nodes, reuseNodes);
}
