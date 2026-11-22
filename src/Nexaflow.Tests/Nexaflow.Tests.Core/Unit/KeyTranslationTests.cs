using System;
using System.Windows.Input;
using Nexaflow.Core.Services;

using Nexaflow.Interaction;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>The keys handlers claim, as the shell hands them over: WPF's press, the contracts' name for it.</summary>
[TestClass]
[CoversNode("interaction")]
public class KeyTranslationTests
{
    [TestMethod]
    [DataRow(Key.A, ConsoleKey.A)]
    [DataRow(Key.C, ConsoleKey.C)]
    [DataRow(Key.V, ConsoleKey.V)]
    [DataRow(Key.X, ConsoleKey.X)]
    [DataRow(Key.Enter, ConsoleKey.Enter)]
    [DataRow(Key.Delete, ConsoleKey.Delete)]
    [DataRow(Key.Tab, ConsoleKey.Tab)]
    [DataRow(Key.F5, ConsoleKey.F5)]
    [DataRow(Key.Up, ConsoleKey.UpArrow)]
    [DataRow(Key.Down, ConsoleKey.DownArrow)]
    [DataRow(Key.Add, ConsoleKey.Add)]
    [DataRow(Key.Subtract, ConsoleKey.Subtract)]
    [DataRow(Key.D0, ConsoleKey.D0)]
    public void AKey_IsItsVirtualKey(Key pressed, ConsoleKey expected)
        => Assert.AreEqual(expected, pressed.ToConsoleKey());

    [TestMethod]
    [DataRow(Key.System)]
    [DataRow(Key.ImeProcessed)]
    public void AKeyStandingForAnother_IsNoneAHandlerClaims(Key pressed)
    {
        var translated = pressed.ToConsoleKey();
        Assert.IsFalse(translated is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Delete,
            $"{pressed} came to {translated}");
    }

    [TestMethod]
    public void EveryCombinationOfModifiers_CarriesAcross()
    {
        for (var bits = 0; bits < 16; bits++)
        {
            var held = ((ModifierKeys)bits).ToKeyModifiers();
            Assert.AreEqual((bits & (int)ModifierKeys.Alt) != 0, held.HasFlag(KeyModifiers.Alt));
            Assert.AreEqual((bits & (int)ModifierKeys.Control) != 0, held.HasFlag(KeyModifiers.Control));
            Assert.AreEqual((bits & (int)ModifierKeys.Shift) != 0, held.HasFlag(KeyModifiers.Shift));
            Assert.AreEqual((bits & (int)ModifierKeys.Windows) != 0, held.HasFlag(KeyModifiers.Windows));
        }
    }
}
