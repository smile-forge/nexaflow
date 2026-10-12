using System.ComponentModel;
using Nexaflow.Core.AI;
using Nexaflow.Core.ViewModels;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Who runs a new workspace's AI: the grid each choice starts it with, and the wizard asking only the steps that
/// choice leaves.
/// </summary>
[TestClass]
[CoversNode("ws-harness-choice")]
public class WorkspaceHarnessTests
{
    private static readonly ProviderModelPair Claude = new() { ProviderName = "Claude", Model = "claude-sonnet-4-6", AssemblyFileName = "Nexaflow.Providers.Claude.dll" };
    private static readonly ProviderModelPair Gemma  = new() { ProviderName = "Nexaflow", Model = "gemma-4-e4b-it", AssemblyFileName = "Nexaflow.Providers.AiHost.dll" };
    private static readonly ProviderModelPair Ears   = new() { ProviderName = "Nexaflow", Model = "nemotron-whisper-turbo", Kind = ModelKind.SpeechToText, AssemblyFileName = "Nexaflow.Providers.AiHost.dll" };

    private static string[] Chain(AiConfig config, AiAbility ability) => [.. config.Assignments[ability.ToString()].ColumnIds];

    [TestMethod]
    public void NexaflowAlone_AnswersEveryLanguageRole_AndListensWithItsOwnSpeechModel_WhisperBehindIt()
    {
        var config = new AiConfig();

        WorkspaceAiDefaults.Assign(config, bridged: null, owned: Gemma, ownedSpeech: Ears, whisperModel: "base.en");

        foreach (var ability in Enum.GetValues<AiAbility>().Where(a => a != AiAbility.SpeechIn))
            CollectionAssert.AreEqual(new[] { Gemma.Id }, Chain(config, ability), ability.ToString());
        var speechIn = Chain(config, AiAbility.SpeechIn);
        Assert.AreEqual(Ears.Id, speechIn[0]);
        Assert.AreEqual("Whisper", config.Columns.Single(c => c.Id == speechIn[1]).ProviderName, "Whisper listens while Nexaflow's own is still downloading");
    }

    [TestMethod]
    public void AMixedWorkspace_GivesTheFrequentRolesToNexaflowFirst_AndTheConversationToTheProvider()
    {
        var config = new AiConfig();

        WorkspaceAiDefaults.Assign(config, Claude, Gemma, Ears, "base.en");

        foreach (var ability in new[] { AiAbility.Disambiguation, AiAbility.Analysis, AiAbility.ImageRecognition })
            CollectionAssert.AreEqual(new[] { Gemma.Id, Claude.Id }, Chain(config, ability), ability.ToString());
        foreach (var ability in new[] { AiAbility.Conversation, AiAbility.ProblemSolving })
            CollectionAssert.AreEqual(new[] { Claude.Id, Gemma.Id }, Chain(config, ability), $"{ability}: the stronger model first, the owned one standing in");
    }

    [TestMethod]
    public void AProviderAlone_AnswersEveryLanguageRole_AndListensWithWhisper()
    {
        var config = new AiConfig();

        WorkspaceAiDefaults.Assign(config, Claude, owned: null, ownedSpeech: null, "base.en");

        Assert.AreEqual(1, config.Columns.Count(c => c.Kind == ModelKind.Language));
        CollectionAssert.AreEqual(new[] { Claude.Id }, Chain(config, AiAbility.Conversation));
        Assert.AreEqual("Whisper", config.Columns.Single(c => c.Id == Chain(config, AiAbility.SpeechIn).Single()).ProviderName);
    }

    /// <summary>A step that applies or not as the test says.</summary>
    private sealed class Step(string title, Func<bool> applies) : IWizardStep
    {
        public string Title => title;
        public object Content => title;
        public bool IsComplete => true;
        public bool Applies => applies();
        public int Commits { get; private set; }
        public void OnEnter() { }
        public void Commit() => Commits++;
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    [TestMethod]
    public void TheWizard_PassesOverStepsThatDoNotApply_BothWays_AndCountsOnlyThoseThatDo()
    {
        bool owned = false;
        var harness  = new Step("Who runs the AI", () => true);
        var provider = new Step("Choose a provider", () => !owned);
        var model    = new Step("Choose a model", () => !owned);
        var persona  = new Step("Persona", () => true);
        var wizard   = new SetupWizardViewModel([harness, provider, model, persona], ctx: null);
        Assert.AreEqual("Step 1 of 4", wizard.StepIndicator);

        owned = true;   // what the harness step's commit decides
        wizard.NextCommand.Execute(null);

        Assert.AreSame(persona, wizard.CurrentStep);
        Assert.AreEqual("Step 2 of 2", wizard.StepIndicator);
        Assert.IsTrue(wizard.IsLastStep);

        wizard.BackCommand.Execute(null);
        Assert.AreSame(harness, wizard.CurrentStep, "Back passes over them too");
    }

    [TestMethod]
    public void AWizardWhoseFirstStepsDoNotApply_StartsAtTheFirstThatDoes()
    {
        var hidden = new Step("Who runs the AI", () => false);
        var first  = new Step("Choose a provider", () => true);

        var wizard = new SetupWizardViewModel([hidden, first], ctx: null);

        Assert.AreSame(first, wizard.CurrentStep);
        Assert.IsFalse(wizard.ShowBack);
    }
}
