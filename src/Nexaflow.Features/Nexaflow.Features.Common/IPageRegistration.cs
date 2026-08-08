namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by each feature assembly to advertise one page kind that it can create.
/// Instances are registered with <c>FeatureManager</c> at application startup so
/// the shell never needs a direct reference to a feature's view or view-model types.
/// </summary>
public interface IPageRegistration
{
    /// <summary>
    /// The stable string identifier for this page kind (e.g. <c>"Console"</c>).
    /// Used as the key in <c>FeatureManager</c> and persisted in ribbon.json.
    /// Each implementation must also expose a
    /// <c>public static string StaticPageKind { get; }</c> so that
    /// <c>FeatureManager</c> can discover the page kind via reflection
    /// without instantiation.
    /// </summary>
    string PageKind { get; }

    /// <summary>
    /// Returns a lightweight <see cref="Page"/> <b>definition</b> for this page kind: its
    /// <see cref="Page.Title"/>, <see cref="Page.Icon"/>, breadcrumbs, and an <b>un-invoked</b>
    /// <see cref="Page.ContentFactory"/>. The real view/view-model is built lazily only when the page
    /// is shown (<see cref="Page.GetOrCreateContent"/>).
    /// <para>
    /// Keep this method cheap and side-effect-free — do NOT construct view-models, open files, hit the
    /// network, or load data here. Callers create definitions speculatively (e.g. to read a page's
    /// Title/Icon for a menu) without ever realizing them. Put all such work inside the
    /// <see cref="Page.ContentFactory"/> closure so it runs only when the content is actually needed.
    /// </para>
    /// </summary>
    /// <param name="pageParams">Optional parameters (e.g. <c>{"folder":"MyProject"}</c>).</param>
    Page CreatePageDefinition(Dictionary<string, string>? pageParams = null);

    /// <summary>
    /// The parameters this page kind accepts in its <c>pageParams</c> dictionary. Default: none.
    /// Override to advertise required/optional params so the shell can describe openable pages to
    /// the AI and other callers.
    /// <para>
    /// This is also what tells the shell which params make a tab a <em>different</em> tab. Mark any
    /// param that merely locates a view inside the document — a byte offset, a selected node — as
    /// <see cref="PageParameter.Identity"/> <c>false</c>, and re-opening that document at a new
    /// location re-points the open tab via <see cref="IPageView.Reinitialize"/> instead of adding one.
    /// </para>
    /// </summary>
    IReadOnlyList<PageParameter> Parameters => [];

    /// <summary>
    /// True when this page can be created without any specific context (no required params) and is
    /// meaningful standalone — so it may be offered in the AI conversation's "add context" menu.
    /// Default: false. May be dynamic (e.g. only when the owning feature is enabled for the workspace).
    /// <para>
    /// When this is (or can become) true, <see cref="CreatePageDefinition"/> MUST be cheap: the menu
    /// builds a definition just to read its Title/Icon and discards it if not chosen, so any heavy work
    /// must live in the <see cref="Page.ContentFactory"/>, not in the definition.
    /// </para>
    /// </summary>
    bool CanBeContextItem => false;

    /// <summary>
    /// The plural form of <see cref="CreatePageDefinition"/>: the set of page definitions this
    /// registration contributes. Most registrations expose exactly one page (the default), but some
    /// expand into several variants of the same page kind — e.g. the file-system registration offering
    /// "This PC" plus each named Windows folder (Documents, Downloads, …). For
    /// <see cref="CanBeContextItem"/> registrations these populate the ribbon editor's "add page" dropdown.
    /// <para>
    /// Each returned <see cref="Page"/> is a cheap definition like <see cref="CreatePageDefinition"/> —
    /// keep it side-effect-free. Default: the single <see cref="CreatePageDefinition"/> result.
    /// </para>
    /// </summary>
    IReadOnlyList<Page> CreatePageDefinitions(Dictionary<string, string>? pageParams = null)
        => [CreatePageDefinition(pageParams)];
}
