using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.WindowsRegistry.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsRegistry;

/// <summary>
/// AI integration for the registry viewer: the read-only act tools (<c>registry_list_subkeys</c>,
/// <c>registry_get_values</c>) driven through <see cref="RegistryViewModel.GetClientTools"/>, plus the
/// context/security-context honesty the surface reports. Reads a stable, always-present, read-only key
/// (<c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion</c>) so nothing is mutated. The mutating tools
/// (<c>registry_set_value</c>/<c>registry_insert_value</c>) are intentionally NOT exercised here — they
/// write the live Windows registry, which is unsafe in a unit test; their in-process write path is covered
/// headlessly by <see cref="RegistryWriterTests"/> against a disposable per-test HKCU subtree.
/// </summary>
[TestClass]
public class RegistryViewModelTests
{
    // A machine-wide key present on every Windows install, readable without elevation.
    private const string StableKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion";

    [TestMethod]
    [CoversNode("win-registry-ai-act")]
    [CoversNode("win-registry-ai-context")]
    public async Task AiTools_ReadSubkeysAndValues_ThroughToolSurface()
    {
        var vm = new RegistryViewModel(Substitute.For<IShellServices>());

        // Point the view at a stable read-only key; the read tools then work relative to it.
        vm.NavigateTo(StableKey);

        // context is honest about where the view is
        StringAssert.Contains(vm.GetContext(), "Registry");
        StringAssert.Contains(vm.GetContext(), vm.CurrentKeyPath);

        // security context is non-null and reports the current key path (used by the AI surface allowlist)
        Assert.IsNotNull(vm.GetSecurityContext());
        Assert.AreEqual(vm.CurrentKeyPath, vm.GetSecurityContext());

        var tools = vm.GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "registry_list_subkeys", "registry_get_values", "registry_set_value", "registry_insert_value" },
            tools.Select(t => t.Name).ToArray(),
            "the Registry AI tool surface changed — update the tree's win-registry-ai-act leaves to match");

        // registry_list_subkeys: enumerate the child keys of the current key (Explorer is always present)
        var listSubkeys = tools.Single(t => t.Name == "registry_list_subkeys");
        var subkeys = await listSubkeys.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(subkeys.IsError, subkeys.ModelText);
        StringAssert.Contains(subkeys.ModelText, "Explorer");

        // registry_get_values: enumerate the values of the current key (ProgramFilesDir is always present)
        var getValues = tools.Single(t => t.Name == "registry_get_values");
        var values = await getValues.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(values.IsError, values.ModelText);
        StringAssert.Contains(values.ModelText, "ProgramFilesDir");
    }
}
