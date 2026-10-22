using System;
using System.Collections.Generic;
using System.Text;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Features.Common
{
    /// <summary>
    /// The per-<c>Workspace</c> AI service injected into feature registrations (alongside
    /// <see cref="IShellServices"/>). It owns the client-side agent loop
    /// (<see cref="RunAgentAsync"/>), query-handler scoring/disambiguation for the AI input bar,
    /// conversation load/save plus its context pages and artifacts, and one-shot analysis/disambiguation
    /// completions. Resolves each ability through the active workspace's model assignments and the
    /// workspace's acquired providers, so a feature never talks to a provider directly.
    /// </summary>
    public interface IAIService
    {
        ConversationRecord? ActiveConversation { get; }

        Task<IEnumerable<ConversationRecord>> LoadConversationsAsync();
        Task SaveConversationAsync(ConversationRecord conversation);

        /// <summary>Permanently deletes a conversation (transcript + any artifacts) by id.</summary>
        Task DeleteConversationAsync(string conversationId);

        /// <summary>
        /// Recreates the (unrealized) context page definitions saved with <paramref name="conversation"/>
        /// so the conversation view can re-pin them; the caller owns realizing and closing them. Centralised
        /// here so feature view-models never construct page definitions themselves.
        /// </summary>
        IReadOnlyList<Page> RestoreContextPages(ConversationRecord conversation);

        /// <summary>
        /// Captures <paramref name="contextPages"/> into <paramref name="conversation"/> as durable
        /// references (page kind + params + the owning assembly version), replacing any existing context.
        /// </summary>
        void SetConversationContext(ConversationRecord conversation, IEnumerable<Page> contextPages);

        /// <summary>
        /// Fetches all query handlers for the owning context, applies symbol-prefix filtering,
        /// scores survivors against <paramref name="text"/>, and returns the positive-score
        /// candidates (sorted descending), the clear winner (top score ≥ 0.8 leading the next
        /// by &gt; 0.2), the effective text after any symbol prefix has been stripped, and whether
        /// a symbol prefix was present (pass it on to <see cref="IQueryHandler.ProcessAsync"/> /
        /// <see cref="IQueryHandler.CompleteAsync"/> so the handler interprets the text the same way).
        /// </summary>
        (IReadOnlyList<(IQueryHandler Handler, float Score)> Scored,
         IQueryHandler? ClearWinner,
         string EffectiveText,
         bool Prefixed)
            ScoreHandlers(string text, IPageViewModel? pageVm);

        /// <summary>
        /// Asks the LLM which of <paramref name="candidates"/> best fits the user's
        /// <paramref name="input"/> given the current page context.
        /// Each candidate carries the rule-based score (0–1) so the LLM can weight
        /// how confidently the handler's own rules matched the input.
        /// Returns null when the LLM selects "none of these apply".
        /// </summary>
        Task<IQueryHandler?> DisambiguateToolSelection(
            IPageViewModel? pageVm, string input,
            IReadOnlyList<(IQueryHandler Handler, float Score)> candidates);

        /// <summary>
        /// Runs the client-side agent loop: the LLM may call the active page's client tools (and
        /// built-in tools), see their results, and continue, until it produces a final message or
        /// a prefill. Tool batches and plans are approved through <paramref name="handler"/>.
        /// When <paramref name="includeContext"/> is false the active page's context and tools are
        /// omitted. Returns null when no Conversation provider is configured or on cancellation.
        /// </summary>
        Task<AiResponse?> RunAgentAsync(
            IPageViewModel? pageVm,
            string input,
            bool includeContext,
            IAIResponseHandler handler,
            CancellationToken ct = default);

        /// <summary>
        /// Context window (in tokens) for the model assigned to General Intelligence,
        /// or null if the provider doesn't know or no provider is configured.
        /// </summary>
        Task<int?> GetConversationContextWindowAsync(CancellationToken ct = default);

        /// <summary>Where the models Nexaflow runs itself run for this workspace; null when it uses none.</summary>
        Task<LocalAiSetup?> DescribeLocalAiAsync(CancellationToken ct = default);

        /// <summary>
        /// Checks this machine against the workspace's models of Nexaflow's own: each loaded where the setup places it
        /// and set its work once — a reply, a sentence spoken and heard back, a picture drawn — with each GPU's memory
        /// and each model's speed measured, and one report written.
        /// </summary>
        /// <param name="progress">What it is doing, as it goes.</param>
        /// <param name="ct">Stops the check where it is.</param>
        /// <exception cref="InvalidOperationException">The workspace uses no model Nexaflow runs itself, or the AI host
        /// could not be reached.</exception>
        Task<LocalAiCheck> CheckLocalAiAsync(IProgress<LocalAiCheckProgress> progress, CancellationToken ct = default);

        /// <summary>
        /// One-shot generic disambiguation: asks the Disambiguation-ability model to pick
        /// one of <paramref name="options"/> given <paramref name="contextDescription"/>
        /// and <paramref name="question"/>. Returns the chosen 0-based index, or null if
        /// the model picks "none of these" or no provider is configured.
        /// </summary>
        Task<int?> DisambiguateOptionAsync(
            string contextDescription,
            string question,
            IReadOnlyList<(string Label, string Detail)> options,
            CancellationToken ct = default);

        /// <summary>
        /// Runs a one-shot completion in the background on the General Intelligence model and returns its raw text. Returns
        /// null when no model serves it. Used by background conversation analysis.
        /// </summary>
        Task<string?> RunAnalysisAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

        /// <summary>
        /// A one-shot question about a picture, answered in the background by the workspace's image-recognition model — the
        /// first in its chain that sees pictures, on this machine or in the cloud as the workspace has it. Null when no model
        /// here sees pictures.
        /// </summary>
        Task<string?> AskAboutPictureAsync(string instruction, ContextImage picture, CancellationToken ct = default);

        /// <summary>
        /// Notes on a stretch of conversation, for the assistant to carry on from in place of the conversation itself: what
        /// was asked and decided, the facts, names and numbers that came up, what was acted on and how, and what is left open.
        /// Condensed by the General Intelligence model without reasoning, a part at a time where the stretch is longer than it reads at
        /// once. <paramref name="earlier"/>, notes condensed before it, are carried into them. Null when there is no model to
        /// condense with, or it could not.
        /// </summary>
        /// <param name="messages">The stretch, oldest first: who said each thing, and what.</param>
        /// <param name="earlier">Notes condensed before the stretch, carried into the new ones; null where there are none.</param>
        /// <param name="ct">Stops the condensing.</param>
        Task<string?> CondenseConversationAsync(IReadOnlyList<(string Speaker, string Text)> messages, string? earlier, CancellationToken ct = default);

        /// <summary>
        /// Runs a one-shot completion on the General Intelligence model, as one the user waits for, and returns its raw text;
        /// null when no model serves it. Separate from <see cref="RunAnalysisAsync"/>, which runs behind everything else.
        /// </summary>
        Task<string?> RunProblemSolvingAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

        /// <summary>
        /// Persists an arbitrary JSON artifact (e.g. a conversation analysis) beside the
        /// conversation's transcript as <c>{name}.json</c>. Never throws on IO failure.
        /// </summary>
        Task SaveConversationArtifactAsync(string conversationId, string name, string json);

        /// <summary>
        /// Raised after a conversation artifact is successfully saved (argument = conversation id).
        /// Lets the conversation browser refresh a row when its background analysis finishes.
        /// May fire on a background thread.
        /// </summary>
        event Action<string>? ConversationArtifactSaved;

        /// <summary>
        /// Loads a previously-saved conversation artifact's JSON, or null if absent/unreadable.
        /// </summary>
        Task<string?> LoadConversationArtifactAsync(string conversationId, string name);

        /// <summary>
        /// The lengths, in seconds and shortest first, the workspace makes a clip from a picture in now — by the model
        /// <c>generate_video</c> would use for one; empty where none of its video models carries a picture on.
        /// </summary>
        IReadOnlyList<int> PictureClipLengths { get; }

        /// <summary>
        /// Makes a clip from a picture as the user set it, the way the assistant's own slow calls run: one of the workspace's
        /// background jobs — listed with the others and stoppable there, the clip saved under Videos and opened when made,
        /// the user told when it ends. Returns once it has started; nothing is asked first, the user having asked.
        /// </summary>
        /// <exception cref="InvalidOperationException">The workspace has no model that makes a clip from a picture.</exception>
        void MakeClipFromPicture(ClipFromPicture clip);

        /// <summary>
        /// Where <paramref name="ability"/> stands in the workspace now: no model assigned, assigned but unreachable, waiting on a
        /// download of a model Nexaflow runs itself, or served. What needs it is enabled while
        /// <see cref="AiAbilityStatus.IsUsable"/> — <see cref="Gate"/> keeps a control so.
        /// </summary>
        AiAbilityStatus StatusOf(AiAbility ability);

        /// <summary>Raised, on whichever thread it happened, when an ability's status may have changed: the workspace's models were
        /// reassigned, or one became available or stopped being.</summary>
        event EventHandler? AbilitiesChanged;

        /// <summary>
        /// Gets <paramref name="ability"/> ready for a use the user asked for: true when a model serves it now. Where it waits on
        /// the download of a model Nexaflow runs itself, the user is asked whether to download it, and it downloads in the
        /// background if they agree — the use stops there, to be made again once it lands (false). False too while it downloads,
        /// and when nothing serves it.
        /// </summary>
        Task<bool> MakeReadyAsync(AiAbility ability, CancellationToken ct = default);

        /// <summary>A gate on <paramref name="abilities"/> for a control to bind, made on the UI thread and kept current on it.</summary>
        IAiAbilityGate Gate(params AiAbility[] abilities);

/// <summary>
/// Summarises a markdown document as the user set it going, the way the assistant's own slow calls run: one of the workspace's
/// background jobs — listed with the others and stoppable there, saying how far it has got, the user told when it ends. What
/// the document is is said first, then each section from the deepest up; the summary opens as a document of its own, a heading
/// per section leading back to it. Returns once it has started; nothing is asked first, the user having asked. What needs it
/// is enabled by a gate on <see cref="AiAbility.Documents"/>.
/// </summary>
/// <exception cref="InvalidOperationException">The workspace has no Documents model, or runs no background jobs.</exception>
void SummarizeDocument(DocumentAsked document);

/// <summary>
/// Translates a markdown document into <paramref name="language"/> as the user set it going, a background job as
/// <see cref="SummarizeDocument"/> is: its words in the other language, everything else as written. The translation opens as
/// a document of its own.
/// </summary>
/// <exception cref="InvalidOperationException">The workspace has no Documents model, or runs no background jobs.</exception>
void TranslateDocument(DocumentAsked document, string language);

/// <summary>
/// Follows the whole-document work under way on the document at <paramref name="path"/> — summarising it, translating it,
/// answering a question about it, whoever set it going — for a page to show on what sets such work going. Made on the UI
/// thread and kept current on it.
/// </summary>
IDocumentWorkWatch WatchDocumentWork(string path);
    }

    /// <summary>A clip the user asks for from a picture: the picture as they see it, what happens, how long, and which way up.</summary>
    /// <param name="Png">The picture, edits and all, as a PNG.</param>
    /// <param name="Description">What happens in the clip, in the user's words.</param>
    /// <param name="Seconds">One of <see cref="IAIService.PictureClipLengths"/>.</param>
    /// <param name="Portrait">Taller than wide; otherwise wider than tall.</param>
    public sealed record ClipFromPicture(byte[] Png, string Description, int Seconds, bool Portrait);

    /// <summary>
    /// A markdown document asked to be worked through as a whole — a Markdown file or a Markdown Package: where it is, which names
    /// what is made from it and what that leads back to; what it says now, edits not yet saved included; and where the files it
    /// names are read from as it stands — beside the file, or where its package is unpacked to be edited — or null for nowhere.
    /// </summary>
    public sealed record DocumentAsked(string Path, string Source, string? Folder);
}
