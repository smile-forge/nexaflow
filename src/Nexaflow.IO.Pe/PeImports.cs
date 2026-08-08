namespace Nexaflow.IO.Pe;

/// <summary>How a module's imports were declared.</summary>
public enum PeImportKind
{
    /// <summary>The normal import directory — resolved by the loader before entry.</summary>
    Standard,
    /// <summary>The delay-load directory — resolved on first call by a helper stub.</summary>
    DelayLoad,
    /// <summary>The bound-import directory — a cached snapshot of a previous bind.</summary>
    Bound,
}

/// <summary>
/// One imported function. Exactly one of <see cref="Name"/> / <see cref="Ordinal"/> is meaningful:
/// an import by ordinal carries no name at all, which is common in stripped or deliberately
/// obfuscated binaries and must render as "#123" rather than blank.
/// </summary>
public sealed record PeImportFunction(string? Name, ushort? Ordinal, ushort Hint, uint IatRva, ulong ThunkValue)
{
    public bool IsByOrdinal => Name is null && Ordinal is not null;

    /// <summary>Display form — the name, or "#ordinal" when imported by ordinal.</summary>
    public string Display => Name ?? (Ordinal is { } o ? $"#{o}" : "(unnamed)");
}

/// <summary>One imported module and everything this image pulls from it.</summary>
public sealed record PeImportModule(
    string                            Name,
    PeImportKind                      Kind,
    uint                              OriginalFirstThunkRva,
    uint                              FirstThunkRva,
    uint                              TimeDateStamp,
    IReadOnlyList<PeImportFunction>   Functions)
{
    public bool IsDelayLoad => Kind == PeImportKind.DelayLoad;

    /// <summary>An API set (<c>api-ms-win-*</c> / <c>ext-ms-*</c>) — a virtual name the loader
    /// redirects through the API set schema rather than a real DLL on disk.</summary>
    public bool IsApiSet => IsApiSetName(Name);

    public static bool IsApiSetName(string name) =>
        name.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("ext-ms-",     StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Name} ({Functions.Count} imports, {Kind})";
}
