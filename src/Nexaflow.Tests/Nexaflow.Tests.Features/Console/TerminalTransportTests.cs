using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.ViewModels;
using NSubstitute;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// The terminal over its transport: it starts the shell through its launcher where the tab opened, reads the shell's
/// bytes with one decoder so a character split between two reads still arrives whole, keeps what was typed before the
/// shell was there, and tells the shell the size the console has room for.
/// </summary>
[TestClass]
[CoversNode("ioterm-transport")]
public sealed class TerminalTransportTests
{
    private sealed class TestTerminal(FakeTerminal transport, IShellServices shell) : TerminalViewModel(transport, shell, Substitute.For<IAIService>())
    {
        protected override IReadOnlyList<TerminalEnvironment> Environments => [];
        protected override string? FindBoundEnvName(string folderPath) => null;
        protected override void PersistFolderBinding(string folderPath, string envName) { }

        public Task Start(TerminalEnvironment? env = null) => Start(env, TestTemp.Root);

        public Task Start(TerminalEnvironment? env, string path)
        {
            SetupInitialState(env, path);
            return ShellStarted;
        }
    }

    /// <summary>A shell whose UI marshal runs at once, so what the terminal renders can be read straight after.</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    private readonly FakeTerminal _transport = new();

    private TestTerminal Terminal() => new(_transport, RunningShell());

    [TestMethod]
    public async Task TheShellStartsWhereTheTabOpened_WithTheEnvironmentsVariables()
    {
        var env = new TerminalEnvironment { Name = "Build", EnvOverrides = new() { ["NEXA_MODE"] = "build" } };
        var folder = TestTemp.CreateSubdirectory("nexa-term-").FullName;

        await Terminal().Start(env, folder);

        Assert.AreEqual(folder, _transport.Launched!.WorkingDirectory);
        Assert.AreEqual("build", _transport.Launched.Environment!["NEXA_MODE"]);
    }

    [TestMethod]
    public async Task ACharacterSplitBetweenTwoReadsArrivesWhole()
    {
        var terminal = Terminal();
        await terminal.Start();

        _transport.Emit((byte)'c', (byte)'a', (byte)'f', 0xC3);   // "caf" and the first byte of 'é'
        _transport.Emit(0xA9);

        StringAssert.Contains(terminal.ScreenText, "café");
    }

    [TestMethod]
    public async Task WhatTheShellSaidBeforeTheTerminalListenedIsNotLost()
    {
        _transport.Emit("Microsoft Windows"u8.ToArray());

        var terminal = Terminal();
        await terminal.Start();

        StringAssert.Contains(terminal.ScreenText, "Microsoft Windows");
    }

    [TestMethod]
    public async Task KeysTypedBeforeTheShellIsThereReachItOnceItIs()
    {
        var terminal = Terminal();
        terminal.SendText("dir");
        Assert.AreEqual(string.Empty, _transport.Written, "there is no shell to send to yet");

        await terminal.Start();

        Assert.AreEqual("dir", _transport.Written);
    }

    [TestMethod]
    public async Task TheShellIsToldTheSizeTheConsoleHasRoomFor_WithinBounds()
    {
        var terminal = Terminal();
        await terminal.Start();

        terminal.Resize(132, 40);
        Assert.AreEqual((132, 40), _transport.Size);

        terminal.Resize(3, 1);
        Assert.AreEqual((20, 5), _transport.Size, "a collapsed pane still leaves the shell room to print");
    }

    [TestMethod]
    public async Task ASizeSettledBeforeTheShellStartedIsTheOneItGets()
    {
        var terminal = Terminal();
        terminal.Resize(100, 30);

        await terminal.Start();

        Assert.AreEqual((100, 30), _transport.Size);
    }

    [TestMethod]
    public async Task CtrlCInterrupts_AndCtrlBreakEndsTheForegroundProgram()
    {
        var terminal = Terminal();
        await terminal.Start();

        terminal.SendCtrlC();
        terminal.HardStop();

        Assert.AreEqual(1, _transport.Interrupts);
        Assert.AreEqual(1, _transport.ForegroundEnds);
    }

    [TestMethod]
    public async Task ClosingTheTabClosesTheTransport()
    {
        var terminal = Terminal();
        await terminal.Start();

        terminal.Dispose();

        Assert.IsTrue(_transport.Disposed);
    }
}
