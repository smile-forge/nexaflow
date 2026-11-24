namespace Nexaflow.AiHost.Provisioning;

/// <summary>
/// A model store as it stood when read (<see cref="ModelAssetStore.Read"/>), for the files asked about: what an app shows the
/// store by and weighs a fetch with — never a path.
/// </summary>
/// <param name="ArchivePartials">What each interrupted archive download kept, by the archive's SHA-256.</param>
/// <param name="UnderWay">What each fetch under way still needs, by its claim (<see cref="StoreClaims"/>).</param>
/// <param name="Free">What the store's drive has left for the store's user; null when it would not say.</param>
/// <param name="Drive">The store's drive, as it is named to a person.</param>
public sealed record StoreReading(IReadOnlyList<FileReading> Files, IReadOnlyDictionary<string, long> ArchivePartials,
                                  IReadOnlyDictionary<string, long> UnderWay, long? Free, string Drive);

/// <summary>One file of a <see cref="StoreReading"/>.</summary>
/// <param name="Path">Its path in the store, as the catalog names it.</param>
/// <param name="Here">Whether it is in the store, complete and verified.</param>
/// <param name="Partial">What an interrupted download of it kept; 0 for none, or one packed in an archive.</param>
/// <param name="Adoptable">Whether an earlier version kept it verified, so a fetch moves it in rather than downloading it.</param>
public sealed record FileReading(string Path, bool Here, long Partial, bool Adoptable);
