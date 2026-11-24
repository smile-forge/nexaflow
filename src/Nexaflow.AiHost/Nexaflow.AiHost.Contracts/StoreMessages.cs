namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// Asks what of the files the host's catalog names are in its model store. The host alone keeps the store, reads its files and
/// fetches them, whichever app asks; an app shows the store, and weighs a fetch, by what this answers
/// (<see cref="ModelStoreDescribed"/>) and by what changes after it (<see cref="ModelFilesChanged"/>) — never by a path.
/// </summary>
public sealed record DescribeModelStore(long RequestId) : HostRequest(RequestId);

public sealed record ModelStoreDescribed(long RequestId, ModelStoreState State) : HostReply(RequestId);

/// <summary>The model store as it stood when described.</summary>
/// <param name="Files">Every file the host's catalog names: each model's and tool's, each LoRA's, the CUDA pack's.</param>
/// <param name="ArchivePartials">What each interrupted archive download kept, by the archive's SHA-256.</param>
/// <param name="UnderWay">What each fetch under way still needs, by what it fetches — a file, or an archive with the entries kept
/// from it.</param>
/// <param name="Free">What the store's drive has left; null when it would not say.</param>
/// <param name="Drive">The store's drive, as it is named to a person.</param>
/// <param name="EarlierRuntimePack">Whether the store holds an earlier CUDA pack than the host runs on: this machine had the pack, and
/// the host deletes the earlier one once the current one is whole.</param>
public sealed record ModelStoreState(IReadOnlyList<ModelFileState> Files, IReadOnlyDictionary<string, long> ArchivePartials,
                                     IReadOnlyDictionary<string, long> UnderWay, long? Free, string Drive, bool EarlierRuntimePack);

/// <summary>One file of the store, by its path in it as the catalog names it.</summary>
/// <param name="Here">Whether it is in the store, complete and verified.</param>
/// <param name="Partial">What an interrupted download of it kept; 0 for none, or one packed in an archive.</param>
/// <param name="Adoptable">Whether an earlier store kept it verified, so a fetch moves it in rather than downloading it.</param>
public sealed record ModelFileState(string Path, bool Here, long Partial, bool Adoptable);

/// <summary>
/// Fetches files into the model store — adopted, resumed or downloaded, then verified — each one the host's catalog names: the
/// host never fetches what an app points it at. It reports <see cref="Progress"/> in <see cref="Fetching"/> as bytes come; a path
/// the catalog does not name is refused (<see cref="HostErrorKind.NotFound"/>), and a fetch the store's drive has no room for is
/// answered <see cref="NoRoomForModelFiles"/> and refused before a byte comes.
/// </summary>
public sealed record FetchModelFiles(long RequestId, IReadOnlyList<string> Paths) : HostRequest(RequestId)
{
    public const string Fetching = "fetching";
}

/// <summary>The store's drive has too little room for a fetch: what it would take, beside every fetch under way, and what the drive
/// has left.</summary>
public sealed record NoRoomForModelFiles(long RequestId, long Bytes, long? Free, string Drive) : HostReply(RequestId);

/// <summary>Removes files from the model store — each one the host's catalog names — to free their room.</summary>
public sealed record RemoveModelFiles(long RequestId, IReadOnlyList<string> Paths) : HostRequest(RequestId);

/// <summary>Files arrived in the model store, verified, or left it — whichever app asked. The host says so unasked.</summary>
public sealed record ModelFilesChanged(IReadOnlyList<string> Paths) : HostMessage;
