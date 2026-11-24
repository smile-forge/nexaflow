using System.Text.Json;
using System.Text.Json.Serialization;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

/// <summary>
/// How this machine's administrator has the host run, from <c>host.json</c> in the host's own folder,
/// <c>%ProgramData%\Smile-Forge\AiHost</c>: the share of the machine's memory models on the CPU may hold, the folder the host logs
/// to, how long a model nothing holds stays loaded once nothing is using it, and the highest priority each identity may ask for.
/// What the file leaves out — or the whole file, where there is none — is the host's own default; what an app that starts a host
/// for itself says on its command line comes before the file.
/// </summary>
/// <param name="MemoryShare">Percent of the machine's memory models on the CPU may hold; null for the default.</param>
/// <param name="LogFolder">Where the host keeps its log; null for none.</param>
/// <param name="IdleUnload">How long a model no hold holds stays loaded once nothing uses it.</param>
/// <param name="Grants">The highest priority an identity may ask for, by its account's security identifier, or that and its app
/// (<see cref="ClientIdentity.Key"/>) — the app's grant before its account's. An identity named in neither may ask for every
/// priority.</param>
internal sealed record HostPolicy(int? MemoryShare, string? LogFolder, TimeSpan IdleUnload, IReadOnlyDictionary<string, RequestPriority> Grants)
{
    /// <summary>How long a model nothing holds stays loaded idle, where the file does not say: as long as an app lets one go after.</summary>
    public static readonly TimeSpan DefaultIdleUnload = TimeSpan.FromMinutes(15);

    /// <summary>The host's own defaults: no file.</summary>
    public static HostPolicy Default { get; } = new(null, null, DefaultIdleUnload, new Dictionary<string, RequestPriority>());

    /// <summary>The host's folder on this machine, beside the model store.</summary>
    public static string DefaultFolder
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Smile-Forge", "AiHost");

    /// <summary>Where the service and its worker log where the file names no folder: a service has no app to ask. A host an app starts
    /// logs only where the app or the file says.</summary>
    public static string ServiceLogFolder => Path.Combine(DefaultFolder, "Logs");

    /// <summary>The file's name in that folder.</summary>
    public const string FileName = "host.json";

    /// <summary>The highest priority <paramref name="client"/> may ask for.</summary>
    public RequestPriority HighestFor(ClientIdentity client)
        => Grants.TryGetValue(client.Key, out var forApp) ? forApp
           : Grants.TryGetValue(client.Account, out var forAccount) ? forAccount
           : RequestPriority.Voice;

    /// <summary>The policy <paramref name="path"/> holds; the defaults where there is no such file.</summary>
    /// <exception cref="InvalidDataException">The file is there and not a policy the host reads — the host does not start on a
    /// policy it would misread.</exception>
    public static HostPolicy Read(string path)
    {
        if (!File.Exists(path)) return Default;
        PolicyFile? file;
        try
        {
            using var json = File.OpenRead(path);
            file = JsonSerializer.Deserialize(json, PolicyJson.Default.PolicyFile);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not a host policy: {ex.Message}", ex);
        }
        if (file is null) return Default;
        if (file.MemoryShare is { } share && (share < AiHostProtocol.MemoryShares.Start.Value || share > AiHostProtocol.MemoryShares.End.Value))
            throw new InvalidDataException($"{path} gives a memory share of {share}%, outside {AiHostProtocol.MemoryShares.Start.Value}–{AiHostProtocol.MemoryShares.End.Value}%.");
        if (file.IdleUnloadMinutes is < 0)
            throw new InvalidDataException($"{path} gives a negative idle time.");
        return new HostPolicy(file.MemoryShare, file.LogFolder,
                              file.IdleUnloadMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : DefaultIdleUnload,
                              file.Grants ?? new Dictionary<string, RequestPriority>());
    }

    /// <summary>The file as written: every field optional.</summary>
    internal sealed record PolicyFile(int? MemoryShare, string? LogFolder, double? IdleUnloadMinutes, Dictionary<string, RequestPriority>? Grants);
}

/// <summary>The serializer for <see cref="HostPolicy.PolicyFile"/>, generated at build time.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip,
                             AllowTrailingCommas = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(HostPolicy.PolicyFile))]
internal sealed partial class PolicyJson : JsonSerializerContext;
