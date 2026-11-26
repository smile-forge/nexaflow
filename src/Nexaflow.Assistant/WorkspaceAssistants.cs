using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Session.Localization;

namespace Nexaflow.Assistant;

/// <summary>
/// Makes each workspace runtime's assistant from what the session shares with it: the feature system, the provider pool, the
/// shell's and security settings, the tools that read the app's help, and the session's background tasks and inbox, which the
/// assistant's slow work runs on and reports to. The session holds only the factory, as <see cref="IWorkspaceAssistant"/>s it makes.
/// </summary>
/// <param name="features">The feature system, whose pages the assistant can open.</param>
/// <param name="providers">The provider pool.</param>
/// <param name="shell">The shell's settings.</param>
/// <param name="security">What the person allows the assistant to do.</param>
/// <param name="language">The UI language, which a spoken conversation is held in.</param>
/// <param name="helpTools">The tools that read the help the app ships, offered on every page.</param>
/// <param name="framing">Fits a starting picture to the frame a video model begins its clip from.</param>
/// <param name="speech">The microphone and speakers a spoken conversation is held through, as the platform opens them.</param>
/// <param name="tasks">The session's background tasks, which a job of the assistant's runs as; null runs none.</param>
/// <param name="notify">The session's inbox, which a job reports to.</param>
public sealed class WorkspaceAssistants(FeatureManager features, ProviderManager providers, ShellConfig shell, SecurityConfig security,
                                        LanguageManager language, IReadOnlyList<IClientTool> helpTools, IPictureFitting framing, ISpeechDevices speech, BackgroundActivityManager? tasks,
                                        Action<NotificationItem> notify)
{
    /// <summary>The assistant for <paramref name="runtime"/>'s workspace, its slow work run on the session's background tasks.</summary>
    public IWorkspaceAssistant Create(WorkspaceRuntime runtime)
    {
        var jobs    = tasks is not null ? new BackgroundJobs(tasks, runtime, notify) : null;
        var service = new AIService(runtime, features, providers, shell, security, language, runtime.Workspace.ConversationsDir, helpTools, framing, speech, jobs);
        service.LoadAbilityConfig(runtime.Workspace.AiConfig);
        return service;
    }
}

/// <summary>A runtime's assistant, as the assistant's own code and the windows that drive it see it.</summary>
public static class WorkspaceRuntimeAssistant
{
    extension(WorkspaceRuntime runtime)
    {
        /// <summary>The runtime's assistant, which <see cref="WorkspaceAssistants"/> made for it.</summary>
        /// <exception cref="InvalidOperationException">The runtime's assistant was made by something else.</exception>
        public AIService AiService => runtime.Assistant as AIService
            ?? throw new InvalidOperationException($"The workspace '{runtime.Workspace.Name}' has an assistant {nameof(WorkspaceAssistants)} did not make.");
    }
}
