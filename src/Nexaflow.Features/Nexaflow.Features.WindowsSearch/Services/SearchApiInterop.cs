using System.Runtime.InteropServices;

namespace Nexaflow.Features.WindowsSearch.Services;

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// Windows Search COM, transcribed from the SDK's own searchapi.h — the MIDL-generated header, which
// is the authoritative statement of vtable layout.
//
// READ THIS BEFORE EDITING. A COM interface is a vtable: .NET builds it purely from the ORDER these
// methods are declared in. Get the order wrong and the wrong function pointer is called — an access
// violation that takes the process down, with no managed exception and nothing pointing here.
//
//   * The published docs on learn.microsoft.com list members ALPHABETICALLY, not in vtable order.
//     Never transcribe from them. The order below came from
//     %WindowsSdkDir%\Include\<ver>\um\SearchAPI.h (the `…Vtbl` structs).
//   * Most slots below are never called; they exist ONLY to hold their position so the ones we DO
//     call land on the right pointer. They are declared with no parameters — enough for layout, not
//     enough to call. Never call one. If you need it, transcribe its real signature from the header.
//   * Never insert, reorder or delete a member. Append-only, and only from the header.
//
// InterfaceIsIUnknown means the three IUnknown slots are implicit, so the first member declared here
// is vtable slot 3 — matching the header, where QueryInterface/AddRef/Release head every Vtbl.
// ─────────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Values for <see cref="ISearchQueryHelper.put_QuerySyntax"/> (<c>_SEARCH_QUERY_SYNTAX</c>).</summary>
internal enum SearchQuerySyntax
{
    /// <summary>Treat the query as literal text — no operators, no property constraints.</summary>
    None = 0,
    /// <summary>Advanced Query Syntax: <c>kind:document</c>, <c>size:&gt;1mb</c>, <c>author:john</c>.</summary>
    Advanced = 1,
    /// <summary>Natural query syntax — AQS plus phrasing like "documents modified last week".</summary>
    Natural = 2,
}

/// <summary>Entry point to the indexer. Co-created from <c>CLSID_CSearchManager</c>.</summary>
[ComImport]
[Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF69")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISearchManager
{
    // Slots 3-9 — position only, never called.
    void GetIndexerVersionStr();
    void GetIndexerVersion();
    void GetParameter();
    void SetParameter();
    void get_ProxyName();
    void get_BypassList();
    void SetProxy();

    /// <summary>Slot 10. The named catalog — always <c>"SystemIndex"</c> for us.</summary>
    [return: MarshalAs(UnmanagedType.Interface)]
    ISearchCatalogManager GetCatalog([MarshalAs(UnmanagedType.LPWStr)] string pszCatalog);
}

/// <summary>One indexed catalog. We use it only to reach the query helper.</summary>
[ComImport]
[Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF50")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISearchCatalogManager
{
    // Slots 3-24 — position only, never called.
    void get_Name();
    void GetParameter();
    void SetParameter();
    void GetCatalogStatus();
    void Reset();
    void Reindex();
    void ReindexMatchingURLs();
    void ReindexSearchRoot();
    void put_ConnectTimeout();
    void get_ConnectTimeout();
    void put_DataTimeout();
    void get_DataTimeout();
    void NumberOfItems();
    void NumberOfItemsToIndex();
    void URLBeingIndexed();
    void GetURLIndexingState();
    void GetPersistentItemsChangedSink();
    void RegisterViewForNotification();
    void GetItemsChangedSink();
    void UnregisterViewForNotification();
    void SetExtensionClusion();
    void EnumerateExcludedExtensions();

    /// <summary>Slot 25. The AQS parser.</summary>
    [return: MarshalAs(UnmanagedType.Interface)]
    ISearchQueryHelper GetQueryHelper();

    // Slots 26-27 — position only, never called.
    void put_DiacriticSensitivity();
    void get_DiacriticSensitivity();

    /// <summary>Slot 28. Which locations the indexer is configured to cover.</summary>
    [return: MarshalAs(UnmanagedType.Interface)]
    ISearchCrawlScopeManager GetCrawlScopeManager();
}

/// <summary>
/// What the indexer has been told to cover. Read-only here on purpose: this tells the user why a search
/// found nothing, it does not reconfigure their machine.
/// </summary>
[ComImport]
[Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF55")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISearchCrawlScopeManager
{
    // Slots 3-9 — position only, never called. All of them MUTATE the machine's indexing configuration;
    // none should ever be given a real signature here without a deliberate decision to allow that.
    void AddDefaultScopeRule();
    void AddRoot();
    void RemoveRoot();
    void EnumerateRoots();
    void AddHierarchicalScope();
    void AddUserScopeRule();
    void RemoveScopeRule();

    /// <summary>Slot 10. Every include/exclude rule, which is what explains a coverage answer.</summary>
    [return: MarshalAs(UnmanagedType.Interface)]
    IEnumSearchScopeRules EnumerateScopeRules();

    // Slot 11 — position only.
    void HasParentScopeRule();

    /// <summary>
    /// Slot 12. True when something BELOW this path carries its own rule — which is what separates a
    /// wholly-covered folder from one with a hole in it.
    /// </summary>
    [PreserveSig]
    int HasChildScopeRule([MarshalAs(UnmanagedType.LPWStr)] string pszURL,
                          [MarshalAs(UnmanagedType.Bool)] out bool pfHasChildRule);

    /// <summary>Slot 13. Whether the indexer covers this path at all.</summary>
    [PreserveSig]
    int IncludedInCrawlScope([MarshalAs(UnmanagedType.LPWStr)] string pszURL,
                             [MarshalAs(UnmanagedType.Bool)] out bool pfIsIncluded);

    // RevertToDefaultScopes, SaveAll, GetParentScopeVersionId, RemoveDefaultScopeRule follow.
}

[ComImport]
[Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF54")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumSearchScopeRules
{
    [PreserveSig]
    int Next(uint celt,
             [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)]
             ISearchScopeRule?[] pprgelt,
             out uint pceltFetched);

    [PreserveSig] int Skip(uint celt);
    void Reset();
    void Clone(out IEnumSearchScopeRules ppenum);
}

/// <summary>One include or exclude rule in the indexer's crawl scope.</summary>
[ComImport]
[Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF53")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISearchScopeRule
{
    [PreserveSig]
    int get_PatternOrURL([MarshalAs(UnmanagedType.LPWStr)] out string? ppszPatternOrURL);

    [PreserveSig]
    int get_IsIncluded([MarshalAs(UnmanagedType.Bool)] out bool pfIsIncluded);

    [PreserveSig]
    int get_IsDefault([MarshalAs(UnmanagedType.Bool)] out bool pfIsDefault);

    [PreserveSig]
    int get_FollowFlags(out uint pFollowFlags);
}

/// <summary>
/// Windows' own AQS parser. <c>GenerateSQLFromUserQuery</c> is the whole reason we are here: it turns
/// what a user typed in Explorer's search box into SQL against SystemIndex, including the property
/// vocabulary, the locale-correct date handling and the size suffixes we would otherwise re-implement.
/// </summary>
[ComImport]
[Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF63")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISearchQueryHelper
{
    // Slots 3-9 — position only, never called.
    void get_ConnectionString();
    void put_QueryContentLocale();
    void get_QueryContentLocale();
    void put_QueryKeywordLocale();
    void get_QueryKeywordLocale();
    void put_QueryTermExpansion();
    void get_QueryTermExpansion();

    /// <summary>Slot 10. Must be set to <see cref="SearchQuerySyntax.Advanced"/> — the default is
    /// natural-language, which would read a property constraint as prose.</summary>
    void put_QuerySyntax(SearchQuerySyntax querySyntax);

    // Slots 11-16 — position only, never called.
    void get_QuerySyntax();
    void put_QueryContentProperties();
    void get_QueryContentProperties();
    void put_QuerySelectColumns();
    void get_QuerySelectColumns();

    /// <summary>Slot 17. Cleared before generating, so the emitted WHERE is only the user's own
    /// constraint and nothing this helper was carrying.</summary>
    void put_QueryWhereRestrictions([MarshalAs(UnmanagedType.LPWStr)] string? pszRestrictions);

    // Slots 18-20 — position only, never called.
    void get_QueryWhereRestrictions();
    void put_QuerySorting();
    void get_QuerySorting();

    /// <summary>Slot 21. The full <c>SELECT … FROM SystemIndex WHERE …</c> for a user query.</summary>
    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GenerateSQLFromUserQuery([MarshalAs(UnmanagedType.LPWStr)] string pszQuery);

    // WriteProperties and the QueryMaxResults pair follow; not declared — see the note above.
}

/// <summary>Co-creation for <c>CSearchManager</c>.</summary>
[ComImport]
[Guid("7D096C5F-AC08-4F1F-BEB7-5C22C517CE39")]
internal class CSearchManager;
