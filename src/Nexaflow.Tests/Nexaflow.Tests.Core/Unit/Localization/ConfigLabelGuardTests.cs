using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Every word the Options grid shows reads from the string table. A row's label and an enum value's name are keys a
/// [ConfigDisplayName] names — without one a row shows its property's code name in every language — and a section's
/// title is its config's FriendlyName, looked up the same way everywhere but a provider, whose section is the
/// provider's own name. <see cref="LocalizationContentGuardTests"/> checks each key is in English under its project's
/// area; this checks none is missing, and none is English text left where a key belongs.
/// </summary>
[TestClass]
[DoNotParallelize]   // reads FriendlyName with no table behind Str, and Str.Source is process-wide
[NoCoverage("repo guard over the config types Options reflects on, not a product behaviour")]
public partial class ConfigLabelGuardTests
{
    [TestMethod]
    public void EveryRowTheGridShows_NamesItsLabelByKey()
    {
        var problems = new List<string>();
        foreach (var type in ConfigTypes().Where(ShowsAGrid))
        {
            foreach (var pi in ConfigEditViewModel.EditableProperties(type))
            {
                Check(problems, $"{type.FullName}.{pi.Name}", PropertyEditViewModel.LabelKey(pi));

                var enumType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
                if (!enumType.IsEnum) continue;
                foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
                    Check(problems, $"{type.FullName}.{pi.Name} = {enumType.Name}.{field.Name}", PropertyEditViewModel.LabelKey(field));
            }
        }

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    [TestMethod]
    public void EverySectionTitle_ButAProvidersOwnName_IsReadFromTheStringTable()
    {
        var previous = Str.Source;
        try
        {
            Str.Source = null;   // with no table behind it, a title read through Str comes back as its key
            var problems = ConfigTypes()
                .Where(t => !IsProvider(t))
                .Select(t => (Type: t, Title: FriendlyName(t)))
                .Where(x => !Key().IsMatch(x.Title))
                .Select(x => $"{x.Type.FullName}: FriendlyName is \"{x.Title}\" - return Str.Get(\"<Area>.Config.<Section>\")")
                .ToList();

            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        }
        finally
        {
            Str.Source = previous;
        }
    }

    /// <summary>Nothing found is not nothing wrong: the scan reaches Core's, the features' and the providers' configs.</summary>
    [TestMethod]
    public void TheScan_ReachesCoreFeatureAndProviderConfigs()
    {
        var types = ConfigTypes().ToList();

        Assert.IsTrue(types.Any(t => t.Assembly == typeof(Nexaflow.Core.SecurityConfig).Assembly), "no Core config found");
        Assert.IsTrue(types.Any(t => t.Assembly.GetName().Name!.StartsWith("Nexaflow.Features.", StringComparison.Ordinal)),
                      "no feature config found");
        Assert.IsTrue(types.Any(IsProvider), "no provider config found");
    }

    private static void Check(List<string> problems, string what, string? key)
    {
        if (key is null)
            problems.Add($"{what}: no [ConfigDisplayName] - the row shows its code name in every language");
        else if (!Key().IsMatch(key))
            problems.Add($"{what}: [ConfigDisplayName(\"{key}\")] is text, not a string-table key");
    }

    /// <summary>Every concrete config type the app ships: Core's, and every feature's and provider's beside it.</summary>
    private static IEnumerable<Type> ConfigTypes()
        => Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.*.dll")
            .Where(f => !Path.GetFileName(f).StartsWith("Nexaflow.Tests.", StringComparison.Ordinal) && IsManaged(f))
            .Select(Assembly.LoadFrom)
            .Append(typeof(Nexaflow.Core.SecurityConfig).Assembly)   // Core ships as Nexaflow.dll, outside the pattern
            .SelectMany(LoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && (typeof(IFeatureConfig).IsAssignableFrom(t) || typeof(IProviderConfig).IsAssignableFrom(t)))
            .Distinct();

    private static bool IsManaged(string file)
    {
        try { AssemblyName.GetAssemblyName(file); return true; }
        catch (BadImageFormatException) { return false; }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); }
    }

    /// <summary>A section with a custom control shows that control, not a grid; its words are its XAML's.</summary>
    private static bool ShowsAGrid(Type type)
        => type.GetCustomAttribute<Nexaflow.Features.Common.CustomControlAttribute>() is null
           && type.GetCustomAttribute<Nexaflow.Providers.Common.CustomControlAttribute>() is null;

    private static bool IsProvider(Type type)
        => type.Assembly.GetName().Name!.StartsWith("Nexaflow.Providers.", StringComparison.Ordinal);

    private static string FriendlyName(Type type) => Activator.CreateInstance(type, nonPublic: true) switch
    {
        IFeatureConfig feature   => feature.FriendlyName,
        IProviderConfig provider => provider.FriendlyName,
        _                        => throw new InvalidOperationException($"{type.FullName} is not a config"),
    };

    [GeneratedRegex(@"^[A-Za-z]\w*(\.\w+){2,}$")]
    private static partial Regex Key();
}
