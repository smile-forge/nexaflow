using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.UI;

/// <summary>
/// End-to-end first-run setup test: launches a fresh Nexaflow against an isolated config dir
/// (NEXAFLOW_CONFIG_DIR), drives the whole setup wizard configuring every step, then reads the
/// config the wizard wrote to disk and asserts it matches what was entered.
/// Excluded from headless/CI: --filter "TestCategory!=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
public class SetupWizardTests
{
    private string          _configDir = null!;
    private Application      _app       = null!;
    private UIA3Automation   _automation = null!;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [TestInitialize]
    public void Setup()
    {
        _configDir  = Path.Combine(Path.GetTempPath(), "nexaflow-uitest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);

        _automation = new UIA3Automation();

        var psi = new ProcessStartInfo(FindAppExe()) { UseShellExecute = false };
        psi.EnvironmentVariables["NEXAFLOW_CONFIG_DIR"] = _configDir;   // isolated, fresh => first-run wizard
        _app = Application.Launch(psi);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { _app?.Kill(); } catch { /* already exited */ }
        _automation?.Dispose();

        // Surface any unhandled UI-thread exception the wizard logged (crash.log lives in the isolated dir).
        string? crash = null;
        var crashLog = Path.Combine(_configDir, "crash.log");
        try { if (File.Exists(crashLog)) crash = File.ReadAllText(crashLog); } catch { }

        try { if (Directory.Exists(_configDir)) Directory.Delete(_configDir, recursive: true); } catch { }

        if (!string.IsNullOrWhiteSpace(crash))
            Assert.Fail($"The app logged an unhandled exception during the setup wizard:{Environment.NewLine}{crash}");
    }

    [TestMethod]
    [CoversNode("opt-ai-persona")]
    [CoversNode("opt-section-shell")]
    public void FirstRunWizard_ConfiguresEveryStep_AndPersistsToDisk()
    {
        const string chosenTheme   = "Ocean";               // not the default (Dark)
        const string apiKey        = "sk-test-ABC123";
        const string baseUrl       = "https://example.test/anthropic";
        const string chosenModel   = "claude-sonnet-4-6";   // a non-first entry in Claude's static list
        const string personaName   = "TestBot";
        const string personaPrompt = "You are a test assistant.";
        var          projectDir    = _configDir;            // an existing directory (passes folder validation)

        var wizard = WaitForWindow("Nexaflow Setup");

        // ── Step 1: What's New (welcome) — no input ──
        ClickNext(wizard);

        // ── Step 2: Shell settings (custom control) — pick a theme; leave language + start-with-windows ──
        var themeCombo = WaitFor(() => wizard.FindFirstDescendant(cf => cf.ByAutomationId("ShellThemeCombo"))?.AsComboBox());
        themeCombo.Select(chosenTheme);
        ClickNext(wizard);

        // ── Step 3: pick the provider ──
        var providerList = WaitFor(() => wizard.FindFirstDescendant(cf => cf.ByAutomationId("WizardProviderList"))?.AsListBox());
        providerList.Select("Claude");
        ClickNext(wizard);

        // ── Step 4: provider config (Claude API key + base URL) ──
        SetText(wizard, "cfg_ApiKey", apiKey, propertyChanged: true);
        SetText(wizard, "cfg_BaseUrl", baseUrl, propertyChanged: true);
        ClickNext(wizard);

        // ── Step 5: pick a model (the list loads asynchronously) ──
        var modelList = WaitFor(() =>
        {
            var lb = wizard.FindFirstDescendant(cf => cf.ByAutomationId("WizardModelList"))?.AsListBox();
            return lb is { } box && box.Items.Length > 0 ? box : null;
        });
        modelList.Select(chosenModel);
        ClickNext(wizard);

        // ── Step 6: persona (custom control; fields are NameBox / PromptBox by x:Name) ──
        SetText(wizard, "NameBox",   personaName,   propertyChanged: true);
        SetText(wizard, "PromptBox", personaPrompt, propertyChanged: true);
        ClickNext(wizard);

        // ── Step 7: projects — enable, then set the directory. The step hosts the feature's own
        // ProjectsConfigEditorControl (Projects 2.0), so drive its named elements, not cfg_* auto-ids. ──
        var enableToggle = WaitFor(() => wizard.FindFirstDescendant(cf => cf.ByAutomationId("EnableProjectsCheck"))?.AsCheckBox());
        if (enableToggle.ToggleState != ToggleState.On) enableToggle.Toggle();

        var dirBox = WaitFor(() =>
        {
            var tb = wizard.FindFirstDescendant(cf => cf.ByAutomationId("ProjectDirectoryBox"))?.AsTextBox();
            return tb is { IsEnabled: true } ? tb : null;   // enabled only once projects is on
        });
        dirBox.Focus();
        dirBox.Text = projectDir;              // PropertyChanged-bound → commits without a focus change
        ClickNext(wizard);                     // Finish

        // ── Wizard closes, app writes config + opens the main window ──
        WaitUntil(() => GetWindow("Nexaflow Setup") is null);
        Thread.Sleep(750);

        // ── Verify the GLOBAL shell config (written to {root}\shell, not per-workspace) ──
        var shell = ReadConfig(_configDir, "shell");
        Assert.AreEqual(chosenTheme, shell.GetProperty("Theme").GetString(), "Shell theme not persisted.");
        Assert.IsFalse(shell.GetProperty("PrestartAtLogin").GetBoolean(), "PrestartAtLogin should remain off.");

        // ── Verify what landed on disk under Contexts\Default ──
        var defaultDir = Path.Combine(_configDir, "Contexts", "Default");

        var claude = ReadConfig(defaultDir, "claude");
        // The ApiKey is [Secret]: on disk it must be DPAPI-encrypted ("enc:…"), never plaintext —
        // and must decrypt back to what the wizard collected (same user, so we can verify here).
        var storedKey = claude.GetProperty("ApiKey").GetString()!;
        StringAssert.StartsWith(storedKey, "enc:", "Claude ApiKey must be encrypted at rest.");
        Assert.IsFalse(storedKey.Contains(apiKey), "Claude ApiKey must not be plaintext on disk.");
        var decrypted = System.Text.Encoding.UTF8.GetString(
            System.Security.Cryptography.ProtectedData.Unprotect(
                Convert.FromBase64String(storedKey["enc:".Length..]), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser));
        Assert.AreEqual(apiKey, decrypted, "Claude ApiKey did not round-trip through DPAPI.");
        Assert.AreEqual(baseUrl, claude.GetProperty("BaseUrl").GetString(), "Claude BaseUrl not persisted.");

        var ai = ReadConfig(defaultDir, "ai-abilities");
        var column = ai.GetProperty("Columns").EnumerateArray().Single();
        Assert.AreEqual("Claude",     column.GetProperty("ProviderName").GetString(), "AI column provider wrong.");
        Assert.AreEqual(chosenModel,  column.GetProperty("Model").GetString(),        "AI column model wrong.");
        var columnId = column.GetProperty("Id").GetString();
        var assignments = ai.GetProperty("Assignments");
        Assert.IsTrue(assignments.EnumerateObject().Any(), "No ability assignments written.");
        foreach (var a in assignments.EnumerateObject())
            Assert.AreEqual(columnId, a.Value.GetString(), $"Ability '{a.Name}' not assigned to the chosen model.");

        var persona = ReadConfig(defaultDir, "ai-persona");
        Assert.AreEqual(personaName,   persona.GetProperty("Name").GetString(),         "Persona name not persisted.");
        Assert.AreEqual(personaPrompt, persona.GetProperty("SystemPrompt").GetString(), "Persona prompt not persisted.");

        var projects = ReadConfig(defaultDir, "projects");
        Assert.IsTrue(projects.GetProperty("EnableProjects").GetBoolean(), "EnableProjects not persisted.");
        Assert.AreEqual(projectDir, projects.GetProperty("ProjectDirectory").GetString(), "Project directory not persisted.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Sets a text field by automation id. For LostFocus-bound fields pass propertyChanged:false
    /// and tab away afterwards; here all callers are PropertyChanged-bound or handle focus themselves.</summary>
    private void SetText(Window wizard, string automationId, string value, bool propertyChanged)
    {
        var box = WaitFor(() => wizard.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.AsTextBox());
        box.Focus();
        box.Text = value;
        if (!propertyChanged) Keyboard.Press(VirtualKeyShort.TAB);
    }

    /// <summary>Clicks Next/Finish once it's enabled (its enabled-state tracks the step's validity).</summary>
    private void ClickNext(Window wizard)
    {
        var next = WaitFor(() =>
        {
            var btn = wizard.FindFirstDescendant(cf => cf.ByAutomationId("WizardNextButton"))?.AsButton();
            return btn is { IsEnabled: true } ? btn : null;
        });
        next.Invoke();
    }

    private Window WaitForWindow(string title)
    {
        Window? found = null;
        WaitUntil(() => (found = GetWindow(title)) is not null);
        return found!;
    }

    private Window? GetWindow(string title)
    {
        try
        {
            return _app.GetAllTopLevelWindows(_automation)
                       .FirstOrDefault(w => string.Equals(w.Title, title, StringComparison.Ordinal));
        }
        catch { return null; }
    }

    private static T WaitFor<T>(Func<T?> probe) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Timeout)
        {
            T? result = null;
            try { result = probe(); } catch { /* element not ready yet */ }
            if (result is not null) return result;
            Thread.Sleep(100);
        }
        throw new TimeoutException($"Condition not met within {Timeout.TotalSeconds:0}s.");
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Timeout)
        {
            try { if (condition()) return; } catch { /* retry */ }
            Thread.Sleep(100);
        }
        throw new TimeoutException($"Condition not met within {Timeout.TotalSeconds:0}s.");
    }

    /// <summary>Reads the single <c>config_*.json</c> under <c>{baseDir}\{configName}\</c> and returns its root.</summary>
    private static JsonElement ReadConfig(string baseDir, string configName)
    {
        var dir = Path.Combine(baseDir, configName);
        Assert.IsTrue(Directory.Exists(dir), $"Expected config directory '{dir}' was not created.");
        var file = Directory.GetFiles(dir, "config_*.json").SingleOrDefault();
        Assert.IsNotNull(file, $"No config_*.json written under '{dir}'.");
        using var doc = JsonDocument.Parse(File.ReadAllText(file!));
        return doc.RootElement.Clone();
    }

    /// <summary>Resolves Nexaflow.exe via the shared harness locator (layout-independent walk-up —
    /// this class had its own fixed-depth copy that broke when the x64 pin added a path level).</summary>
    private static string FindAppExe() => Infrastructure.UITestBase.FindAppExe();
}
