using Microsoft.Win32;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsRegistry.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsRegistry;

/// <summary>
/// The gate in front of every registry delete: the view-model asks the shell's confirmation first, and a
/// declined one writes nothing. The only honest way to show that "declined" means "not written" is a real
/// key, so these run under a private, per-test <c>HKCU\Software\Nexaflow.Tests\{guid}</c> subtree — the
/// same fixture as <see cref="RegistryWriterTests"/>, deleted in cleanup.
/// </summary>
[TestClass]
[CoversNode("registry-confirmation")]
public class RegistryDeleteConfirmationTests
{
    private string _sub = "";

    [TestInitialize]
    public void Setup()
    {
        _sub = $@"Software\Nexaflow.Tests\{Guid.NewGuid():N}";
        using var key = Registry.CurrentUser.CreateSubKey(_sub);
        key.SetValue("Doomed", "x");
        key.CreateSubKey("Child").Dispose();
    }

    [TestCleanup]
    public void Teardown()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_sub, throwOnMissingSubKey: false); } catch { }
    }

    /// <summary>A view-model parked on <paramref name="sub"/> whose shell answers every confirmation <paramref name="answer"/>.</summary>
    private static RegistryViewModel At(string sub, bool answer)
    {
        var shell = Substitute.For<IShellServices>();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                           Arg.Any<CancellationToken>())
             .Returns(answer);
        var vm = new RegistryViewModel(shell);
        vm.NavigateTo($@"HKCU\{sub}");
        return vm;
    }

    private static RegistryValue Doomed => new("Doomed", RegistryValueKind.String, "x");

    [TestMethod]
    public async Task DeclinedDeleteValue_LeavesTheValue()
    {
        await At(_sub, answer: false).DeleteValueCommand.ExecuteAsync(Doomed);

        using var key = Registry.CurrentUser.OpenSubKey(_sub)!;
        Assert.AreEqual("x", key.GetValue("Doomed"), "a declined confirmation writes nothing");
    }

    [TestMethod]
    public async Task ConfirmedDeleteValue_RemovesIt()
    {
        await At(_sub, answer: true).DeleteValueCommand.ExecuteAsync(Doomed);

        using var key = Registry.CurrentUser.OpenSubKey(_sub)!;
        Assert.IsNull(key.GetValue("Doomed"));
    }

    [TestMethod]
    public async Task DeclinedDeleteKey_LeavesTheKey()
    {
        await At($@"{_sub}\Child", answer: false).DeleteKeyCommand.ExecuteAsync(null);

        using var child = Registry.CurrentUser.OpenSubKey($@"{_sub}\Child");
        Assert.IsNotNull(child, "a declined confirmation writes nothing");
    }
}
