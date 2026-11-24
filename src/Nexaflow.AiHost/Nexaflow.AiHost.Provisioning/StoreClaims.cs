namespace Nexaflow.AiHost.Provisioning;

/// <summary>
/// What fetching files claims of a model store's drive, worked out the one way wherever it is asked — by the store itself, or by
/// an app from what the store last said of itself: one claim for each file published on its own, and one for each archive with
/// the entries kept from it, each less what an interrupted download of it already wrote.
/// </summary>
public static class StoreClaims
{
    /// <summary>One claim: what it will have written at its fullest, and how much of that is written already.</summary>
    public readonly record struct Claim(string Key, long Bytes, long Written)
    {
        public long Left => Math.Max(0, Bytes - Written);
    }

    /// <summary>The claims fetching <paramref name="missing"/> makes.</summary>
    /// <param name="partialOf">What an interrupted download of a file published on its own kept.</param>
    /// <param name="archivePartialOf">What an interrupted download of an archive kept.</param>
    public static IReadOnlyList<Claim> Of(IReadOnlyList<ModelAsset> missing, Func<ModelAsset, long> partialOf, Func<ArchiveEntry, long> archivePartialOf)
    {
        var claims = missing.Where(a => a.Archive is null).Select(a => new Claim(KeyOf(a), a.Size, partialOf(a))).ToList();
        foreach (var entries in missing.Where(a => a.Archive is not null).GroupBy(a => a.Archive!.ArchiveSha256, StringComparer.OrdinalIgnoreCase))
        {
            var archive = entries.First().Archive!;
            claims.Add(new Claim(KeyOf(archive), archive.ArchiveSize + entries.Sum(e => e.Size), archivePartialOf(archive)));
        }
        return claims;
    }

    /// <summary>The room <paramref name="claims"/> take on the drive beside <paramref name="underWay"/> — what the fetches under way
    /// still need, by claim — a claim one of them holds already counted once.</summary>
    public static long Weigh(IReadOnlyList<Claim> claims, IReadOnlyDictionary<string, long> underWay)
        => underWay.Values.Sum() + claims.Where(c => !underWay.ContainsKey(c.Key)).Sum(c => c.Left);

    public static string KeyOf(ModelAsset loose) => "file:" + loose.RelativePath;

    public static string KeyOf(ArchiveEntry archive) => "zip:" + archive.ArchiveSha256;
}
