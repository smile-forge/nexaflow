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
    private static readonly ProviderModelPair Voice  = new() { ProviderName = "Nexaflow", Model = "kokoro-82m", Kind = ModelKind.TextToSpeech, AssemblyFileName = "Nexaflow.Providers.AiHost.dll" };
    private static readonly OwnedColumns Owned = new(Gemma, Ears, Voice);

    private static string[] Chain(AiConfig config, AiAbility ability) => [.. config.Assignments[ability.ToString()].ColumnIds];

    [TestMethod]
    public void NexaflowAlone_AnswersEveryLanguageRole_ListensWithItsOwnSpeechModel_AndSpeaksWithItsOwnVoice_AllOnTheLocalColumn()
    {
        var config = new AiConfig();

        WorkspaceAiDefaults.Assign(config, bridged: null, Owned);

        foreach (var ability in Enum.GetValues<AiAbility>().Where(a => AiAbilityNames.Kind(a) == ModelKind.Language))
            CollectionAssert.AreEqual(new[] { Local(LocalSlot.Language) }, Chain(config, ability), ability.ToString());
        CollectionAssert.AreEqual(new[] { Local(LocalSlot.SpeechToText) }, Chain(config, AiAbility.SpeechIn));
        CollectionAssert.AreEqual(new[] { Local(LocalSlot.TextToSpeech) }, Chain(config, AiAbility.SpeechOut));
        Assert.AreEqual(Gemma.Model, ModelOf(config, LocalSlot.Language));
        Assert.AreEqual(Ears.Model, ModelOf(config, LocalSlot.SpeechToText), "Nexaflow's own listens");
        Assert.AreEqual(Voice.Model, ModelOf(config, LocalSlot.TextToSpeech));
    }

    private static string Local(LocalSlot slot) => LocalColumns.IdOf(slot);

    private static string ModelOf(AiConfig config, LocalSlot slot) => config.Columns.Single(c => c.Id == Local(slot)).Model;

    [TestMethod]
    public void AMixedWorkspace_GivesTheFrequentRolesToNexaflowFirst_AndTheConversationToTheProvider()
    {
        var config = new AiConfig();

        WorkspaceAiDefaults.Assign(config, Claude, Owned);

        foreach (var ability in new[] { AiAbility.Disambiguation, AiAbility.Analysis, AiAbility.ImageRecognition })
            CollectionAssert.AreEqual(new[] { Local(LocalSlot.Language), Claude.Id }, Chain(config, ability), ability.ToString());
        foreach (var ability in new[] { AiAbility.Conversation, AiAbility.ProblemSolving })
            CollectionAssert.AreEqual(new[] { Claude.Id, Local(LocalSlot.Language) }, Chain(config, ability), $"{ability}: the stronger model first, the owned one standing in");
    }

    [TestMethod]
    public void AProviderAlone_AnswersEveryLanguageRole_AndListensOnTheLocalColumn()
    {
        var config = new AiConfig();

        WorkspaceAiDefaults.Assign(config, Claude, owned: null);

        Assert.AreEqual(1, config.Columns.Count(c => c.Kind == ModelKind.Language));
        CollectionAssert.AreEqual(new[] { Claude.Id }, Chain(config, AiAbility.Conversation));
        CollectionAssert.AreEqual(new[] { Local(LocalSlot.SpeechToText) }, Chain(config, AiAbility.SpeechIn),
                                  "the workspace settles on the speech model it offers here when it loads");
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
