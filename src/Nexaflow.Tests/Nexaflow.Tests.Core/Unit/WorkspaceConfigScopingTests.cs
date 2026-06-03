using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Reflection;
using Nexaflow.Core;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Features.Console;
using Nexaflow.Features.Projects;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
public class WorkspaceConfigScopingTests
{
    // A provider config with a [Required] property, used to exercise the configured-ness checks.
    private sealed class FakeProviderConfig : IProviderConfig
    {
        public string ConfigName   => "fake";
        public string FriendlyName => "FakeProv";
        [Required] public string ApiKey { get; set; } = "";
    }

    private sealed class NoRequiredConfig
    {
        public string Anything { get; set; } = "";
    }

    // ── A1: the scoping attribute marks Projects/Console, not global configs ──

    [TestMethod]
    public void ScopedConfigs_AreAnnotated_GlobalConfigs_AreNot()
    {
        Assert.IsNotNull(typeof(ProjectsConfig).GetCustomAttribute<WorkspaceScopedConfigAttribute>());
        Assert.IsNotNull(typeof(ConsoleConfig).GetCustomAttribute<WorkspaceScopedConfigAttribute>());
        Assert.IsNull(typeof(ShellConfig).GetCustomAttribute<WorkspaceScopedConfigAttribute>());
    }

    // The wizard only shows config-class steps marked [MandatorySetup]. Shell + Projects opt in;
    // Console does not.
    [TestMethod]
    public void MandatorySetup_GatesWizardSteps()
    {
        Assert.IsNotNull(typeof(ShellConfig).GetCustomAttribute<Nexaflow.Features.Common.MandatorySetupAttribute>());
        Assert.IsNotNull(typeof(ProjectsConfig).GetCustomAttribute<Nexaflow.Features.Common.MandatorySetupAttribute>());
        Assert.IsNull(typeof(ConsoleConfig).GetCustomAttribute<Nexaflow.Features.Common.MandatorySetupAttribute>());
    }

    // ── A3: per-profile storage round-trips via SaveTo/LoadFrom under an explicit folder ──

    [TestMethod]
    public void SaveTo_ThenLoadFrom_RoundTripsScopedConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var saved = new ProjectsConfig { ProjectDirectory = @"X:\Custom\Projects" };
            ConfigManager.Instance.SaveTo(dir, saved, saved.ConfigName);

            // The file lands under <dir>\<ConfigName>\, mirroring Contexts\<profile>\projects\.
            Assert.IsTrue(Directory.Exists(Path.Combine(dir, saved.ConfigName)));

            var loaded = new ProjectsConfig();
            ConfigManager.Instance.LoadFrom(dir, loaded, loaded.ConfigName);
            Assert.AreEqual(@"X:\Custom\Projects", loaded.ProjectDirectory);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void LoadFrom_MissingFile_KeepsDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-test-" + Guid.NewGuid().ToString("N"));
        var cfg = new ProjectsConfig { ProjectDirectory = @"C:\Default" };
        ConfigManager.Instance.LoadFrom(dir, cfg, cfg.ConfigName);   // no file — no-op
        Assert.AreEqual(@"C:\Default", cfg.ProjectDirectory);
    }

    // ── Required-property gate (used by the wizard + ability grid) ──

    [TestMethod]
    public void AreRequiredPropertiesSatisfied_Cases()
    {
        Assert.IsTrue(ConfigEditViewModel.AreRequiredPropertiesSatisfied(new NoRequiredConfig()));
        Assert.IsFalse(ConfigEditViewModel.AreRequiredPropertiesSatisfied(new FakeProviderConfig { ApiKey = "" }));
        Assert.IsTrue(ConfigEditViewModel.AreRequiredPropertiesSatisfied(new FakeProviderConfig { ApiKey = "sk-123" }));
    }

    // ── Wizard predicate: is the startup profile usable as-is? ──

    [TestMethod]
    public void IsWorkspaceConfigured_NoAssignments_False()
    {
        var p = new Profile();
        Assert.IsFalse(SetupWizardViewModel.IsWorkspaceConfigured(p));
    }

    [TestMethod]
    public void IsWorkspaceConfigured_AssignedButNoProviderConfig_False()
    {
        var p = new Profile();
        var col = new ProviderModelPair { Id = "c1", ProviderName = "FakeProv", Model = "m" };
        p.AiConfig.Columns = new List<ProviderModelPair> { col };
        p.AiConfig.Assignments["Disambiguation"] = "c1";

        Assert.IsFalse(SetupWizardViewModel.IsWorkspaceConfigured(p));
    }

    [TestMethod]
    public void IsWorkspaceConfigured_AssignedWithSatisfiedProvider_True()
    {
        var p = new Profile();
        var col = new ProviderModelPair { Id = "c1", ProviderName = "FakeProv", Model = "m" };
        p.AiConfig.Columns = new List<ProviderModelPair> { col };
        p.AiConfig.Assignments["Disambiguation"] = "c1";
        SetProviderConfigs(p, new FakeProviderConfig { ApiKey = "sk-123" });

        Assert.IsTrue(SetupWizardViewModel.IsWorkspaceConfigured(p));
    }

    [TestMethod]
    public void IsWorkspaceConfigured_AssignedButProviderMissingKey_False()
    {
        var p = new Profile();
        var col = new ProviderModelPair { Id = "c1", ProviderName = "FakeProv", Model = "m" };
        p.AiConfig.Columns = new List<ProviderModelPair> { col };
        p.AiConfig.Assignments["Disambiguation"] = "c1";
        SetProviderConfigs(p, new FakeProviderConfig { ApiKey = "" });

        Assert.IsFalse(SetupWizardViewModel.IsWorkspaceConfigured(p));
    }

    // Profile.ProviderConfigs is loaded from disk in production; set it directly here.
    private static void SetProviderConfigs(Profile p, params IProviderConfig[] configs)
        => typeof(Profile).GetProperty(nameof(Profile.ProviderConfigs))!
            .SetValue(p, (IReadOnlyList<IProviderConfig>)configs);
}
