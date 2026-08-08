using System.Xml.Linq;

namespace Nexaflow.IO.Pe;

/// <summary>The UAC level the binary asks the loader for.</summary>
public enum PeExecutionLevel
{
    /// <summary>No trustInfo block — the binary is subject to installer detection and virtualisation.</summary>
    Unspecified,
    AsInvoker,
    HighestAvailable,
    RequireAdministrator,
}

/// <summary>How the process declares it handles display scaling.</summary>
public enum PeDpiAwareness { Unspecified, Unaware, System, PerMonitor, PerMonitorV2 }

/// <param name="Id">The GUID as written in the manifest.</param>
/// <param name="Name">"Windows 10 / 11", "Windows 7", … or null for one we do not recognise.</param>
public sealed record PeSupportedOs(string Id, string? Name);

/// <summary>A side-by-side dependency, e.g. Common Controls v6.</summary>
public sealed record PeManifestDependency(
    string Name, string? Version, string? ProcessorArchitecture, string? PublicKeyToken, string? Type);

/// <summary>
/// A decoded Windows application manifest — embedded (RT_MANIFEST) or a <c>.manifest</c> sidecar.
/// <para>
/// The schema is public and has been stable for two decades, so this is a real decode rather than a
/// pretty-printer: the UAC level, the supported-OS list and the DPI mode are the three things that
/// actually change how Windows runs the binary, and each is a single attribute buried in a
/// different namespace. <see cref="Other"/> keeps anything unrecognised so a manifest using a newer
/// setting still shows it rather than silently losing it.
/// </para>
/// </summary>
public sealed record AppManifest
{
    public static readonly AppManifest Empty = new();

    public bool IsEmpty => RawXml is null;

    /// <summary>The manifest source, pretty-printed. Null when there was no manifest.</summary>
    public string? RawXml { get; init; }

    /// <summary>Set when the manifest came from a <c>&lt;file&gt;.manifest</c> sidecar rather than
    /// from the RT_MANIFEST resource.</summary>
    public bool IsExternal { get; init; }

    // Identity
    public string? AssemblyName          { get; init; }
    public string? AssemblyVersion       { get; init; }
    public string? ProcessorArchitecture { get; init; }
    public string? AssemblyType          { get; init; }
    public string? PublicKeyToken        { get; init; }
    public string? Description           { get; init; }

    // Trust
    public PeExecutionLevel ExecutionLevel { get; init; } = PeExecutionLevel.Unspecified;
    public bool             UiAccess       { get; init; }

    /// <summary>True when the binary always triggers a UAC prompt.</summary>
    public bool RequiresElevation => ExecutionLevel == PeExecutionLevel.RequireAdministrator;

    // Compatibility
    public IReadOnlyList<PeSupportedOs> SupportedOs { get; init; } = [];

    /// <summary>No Windows 10/11 entry means the compatibility shims apply — the binary sees a
    /// faked-down OS version. Only meaningful when the manifest declares <em>some</em> OS support.</summary>
    public bool RunsUnderCompatibilityShims
        => SupportedOs.Count > 0 && !SupportedOs.Any(o => o.Id.Equals(Windows10Id, StringComparison.OrdinalIgnoreCase));

    public string? MaxVersionTested { get; init; }

    // Windows settings
    public PeDpiAwareness DpiAwareness { get; init; } = PeDpiAwareness.Unspecified;
    public bool           LongPathAware { get; init; }
    public string?        ActiveCodePage { get; init; }

    /// <summary>Every <c>windowsSettings</c> child, decoded or not, in document order.</summary>
    public IReadOnlyDictionary<string, string> WindowsSettings { get; init; }
        = new Dictionary<string, string>();

    // Dependencies + COM
    public IReadOnlyList<PeManifestDependency> Dependencies { get; init; } = [];

    public int ComClassCount   { get; init; }
    public int TypeLibCount    { get; init; }
    public int WindowClassCount { get; init; }
    public int ProxyStubCount  { get; init; }

    /// <summary>The manifest registers COM without touching the registry.</summary>
    public bool HasRegistrationFreeCom
        => ComClassCount + TypeLibCount + WindowClassCount + ProxyStubCount > 0;

    /// <summary>Top-level elements the decoder does not model, so nothing is silently dropped.</summary>
    public IReadOnlyList<string> Other { get; init; } = [];

    /// <summary>Set when the XML would not parse — the raw text is still available.</summary>
    public string? ParseError { get; init; }

    internal const string Windows10Id = "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}";

    /// <summary>
    /// Decodes manifest XML. Never throws: malformed XML yields an <see cref="AppManifest"/> that
    /// carries <see cref="ParseError"/> and the original text, because being able to look at a
    /// broken manifest is exactly when you most want to.
    /// </summary>
    public static AppManifest Parse(string? xml, bool isExternal = false)
    {
        if (string.IsNullOrWhiteSpace(xml)) return Empty;

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException e)
        {
            return new AppManifest { RawXml = xml, IsExternal = isExternal, ParseError = e.Message };
        }

        var root = document.Root;
        if (root is null) return new AppManifest { RawXml = xml, IsExternal = isExternal };

        // Matching on local names throughout: the settings namespace has been re-versioned on nearly
        // every Windows release (…/2005/WindowsSettings through …/2020/…), and a namespace-exact
        // match would quietly miss whichever ones a given manifest happens to use.
        var identity = Find(root, "assemblyIdentity");
        var level    = Find(root, "requestedExecutionLevel");
        var settings = Descend(root, "windowsSettings").ToList();

        var settingValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in settings.SelectMany(s => s.Elements()))
            settingValues[setting.Name.LocalName] = setting.Value.Trim();

        return new AppManifest
        {
            RawXml     = Prettify(document, xml),
            IsExternal = isExternal,

            AssemblyName          = Attr(identity, "name"),
            AssemblyVersion       = Attr(identity, "version"),
            ProcessorArchitecture = Attr(identity, "processorArchitecture"),
            AssemblyType          = Attr(identity, "type"),
            PublicKeyToken        = Attr(identity, "publicKeyToken"),
            Description           = Find(root, "description")?.Value.Trim() is { Length: > 0 } d ? d : null,

            ExecutionLevel = ParseLevel(Attr(level, "level")),
            UiAccess       = string.Equals(Attr(level, "uiAccess"), "true", StringComparison.OrdinalIgnoreCase),

            SupportedOs = [.. Descend(root, "supportedOS")
                              .Select(e => Attr(e, "Id"))
                              .Where(id => id is { Length: > 0 })
                              .Select(id => new PeSupportedOs(id!, OsName(id!)))],
            MaxVersionTested = Attr(Find(root, "maxversiontested"), "Id"),

            DpiAwareness   = ParseDpi(settingValues),
            LongPathAware  = settingValues.TryGetValue("longPathAware", out var lp) &&
                             lp.Equals("true", StringComparison.OrdinalIgnoreCase),
            ActiveCodePage = settingValues.GetValueOrDefault("activeCodePage"),
            WindowsSettings = settingValues,

            Dependencies = [.. Descend(root, "dependentAssembly")
                               .Select(e => Find(e, "assemblyIdentity"))
                               .Where(i => i is not null)
                               .Select(i => new PeManifestDependency(
                                   Attr(i, "name") ?? "(unnamed)", Attr(i, "version"),
                                   Attr(i, "processorArchitecture"), Attr(i, "publicKeyToken"),
                                   Attr(i, "type")))],

            ComClassCount    = Descend(root, "comClass").Count(),
            TypeLibCount     = Descend(root, "typelib").Count(),
            WindowClassCount = Descend(root, "windowClass").Count(),
            ProxyStubCount   = Descend(root, "comInterfaceProxyStub").Count() +
                               Descend(root, "comInterfaceExternalProxyStub").Count(),

            Other = [.. root.Elements()
                        .Select(e => e.Name.LocalName)
                        .Where(n => !Modelled.Contains(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)],
        };
    }

    /// <summary>Top-level element names this decoder already surfaces; anything else lands in
    /// <see cref="Other"/>.</summary>
    private static readonly HashSet<string> Modelled = new(StringComparer.OrdinalIgnoreCase)
    {
        "assemblyIdentity", "trustInfo", "compatibility", "application", "dependency",
        "description", "file", "noInherit", "noInheritable",
    };

    private static XElement? Find(XElement? root, string localName)
        => root?.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);

    private static IEnumerable<XElement> Descend(XElement root, string localName)
        => root.Descendants().Where(e => e.Name.LocalName == localName);

    private static string? Attr(XElement? element, string name)
        => element?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

    private static PeExecutionLevel ParseLevel(string? level) => level?.ToLowerInvariant() switch
    {
        "asinvoker"            => PeExecutionLevel.AsInvoker,
        "highestavailable"     => PeExecutionLevel.HighestAvailable,
        "requireadministrator" => PeExecutionLevel.RequireAdministrator,
        _                      => PeExecutionLevel.Unspecified,
    };

    /// <summary>
    /// <c>dpiAwareness</c> (Windows 10+) wins over the older boolean <c>dpiAware</c> when both are
    /// present, matching how the loader itself resolves them. Both may list several
    /// comma-separated values, most-preferred first.
    /// </summary>
    private static PeDpiAwareness ParseDpi(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue("dpiAwareness", out var modern) && modern.Length > 0)
        {
            string first = modern.Split(',')[0].Trim().ToLowerInvariant();
            if (first is "permonitorv2") return PeDpiAwareness.PerMonitorV2;
            if (first is "permonitor")   return PeDpiAwareness.PerMonitor;
            if (first is "system")       return PeDpiAwareness.System;
            if (first is "unaware")      return PeDpiAwareness.Unaware;
        }

        if (settings.TryGetValue("dpiAware", out var legacy) && legacy.Length > 0)
        {
            string first = legacy.Split(',')[0].Trim().ToLowerInvariant();
            if (first is "true/pm" or "per monitor") return PeDpiAwareness.PerMonitor;
            if (first is "true")                     return PeDpiAwareness.System;
            if (first is "false")                    return PeDpiAwareness.Unaware;
        }
        return PeDpiAwareness.Unspecified;
    }

    private static string? OsName(string id) => id.Trim().ToLowerInvariant() switch
    {
        "{e2011457-1546-43c5-a5fe-008deee3d3f0}" => "Windows Vista",
        "{35138b9a-5d96-4fbd-8e2d-a2440225f93a}" => "Windows 7",
        "{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}" => "Windows 8",
        "{1f676c76-80e1-4239-95bb-83d0f6d0da78}" => "Windows 8.1",
        "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" => "Windows 10 / 11",
        _                                        => null,
    };

    private static string Prettify(XDocument document, string original)
    {
        try   { return document.ToString(SaveOptions.None); }
        catch { return original; }
    }
}
