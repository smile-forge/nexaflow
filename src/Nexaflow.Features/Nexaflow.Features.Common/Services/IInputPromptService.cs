namespace Nexaflow.Features.Common
{
    /// <summary>
    /// Injectable service that a <see cref="IFileAction"/> can use to show a
    /// text-input overlay and retrieve the user's answer.
    /// The action receives this via constructor injection so it has no
    /// dependency on any WPF or view-model type directly.
    /// </summary>
    public interface IInputPromptService
    {
        /// <summary>
        /// Shows an input prompt overlay with <paramref name="title"/> and
        /// <paramref name="label"/>, pre-filled with <paramref name="initialValue"/>.
        /// Calls <paramref name="onConfirm"/> with the entered text when the user
        /// confirms, or <paramref name="onCancel"/> if they dismiss.
        /// </summary>
        void Show(string title, string label, string initialValue,
                  Action<string> onConfirm, Action onCancel);

        /// <summary>
        /// Shows a confirmation overlay with <paramref name="title"/> and
        /// <paramref name="message"/>. Calls <paramref name="onConfirm"/> when the
        /// user accepts or <paramref name="onCancel"/> when they dismiss.
        /// </summary>
        void ShowConfirmation(string title, string message, Action onConfirm, Action onCancel);

        /// <summary>
        /// Requests that the file list / tree be refreshed after a prompt-driven
        /// action has completed (e.g. after a rename).
        /// </summary>
        void RequestRefresh();
    }
}
