using System;
using System.Collections.Generic;
using System.Windows.Media;

using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// One control of the mini ribbon a right-click opens, as the language owning what was right-clicked describes it — what it is
/// called, how it is drawn, and what the language does when it is used.
///
/// <para>
/// <strong>The language says what is offered and does it; the ribbon only draws.</strong> The engine finds the language whose part
/// the right-click landed on and asks it (<see cref="Markdown.IContentLanguage.Ribbon"/>). The ribbon knows how to draw each kind of
/// control and which value each kind hands back, and nothing about what any of them means. Using one hands the language's own
/// callback the edit as the content stands at that moment, since the ribbon stays open while the content changes under it, and the
/// change it answers is applied by the engine as every edit is.
/// </para>
/// </summary>
/// <param name="Id">What tells it from every other control on the ribbon, and the handle a journey finds it by.</param>
/// <param name="Name">What it is called where a reader reads it — hovering over it, or hearing the screen read.</param>
public abstract record MiniRibbonControl(string Id, string Name);

/// <summary>Something done at once, drawn as its picture where it has one and as its name where it has none.</summary>
/// <param name="Pressed">The change pressing it makes, given the content as it stands — or null for none.</param>
public sealed record MiniRibbonButton(string Id, string Name, Func<ContentEdit, ContentChange?> Pressed) : MiniRibbonControl(Id, Name)
{
    /// <summary>Its picture in a sixteen-by-sixteen box, filled in the ink of whatever shows it — or null, to be named.</summary>
    public Geometry? Shape { get; init; }
}

/// <summary>
/// One of a set of options, the one the content says now picked out — drawn side by side where there are a few, as their colours
/// where they are colours, and behind a button opening them where there are more than a row holds.
/// </summary>
/// <param name="Options">Every option, in the order they are drawn.</param>
/// <param name="Chosen">The change choosing an option makes, given the content as it stands — or null for none.</param>
public sealed record MiniRibbonChoice(string Id, string Name, IReadOnlyList<MiniRibbonOption> Options,
                                      Func<ContentEdit, MiniRibbonOption, ContentChange?> Chosen) : MiniRibbonControl(Id, Name)
{
    /// <summary>The <see cref="MiniRibbonOption.Id"/> of the option the content says now, or null where it says none of them.</summary>
    public string? Current { get; init; }
}

/// <summary>An option of a <see cref="MiniRibbonChoice"/>.</summary>
/// <param name="Id">What tells it from the choice's other options.</param>
/// <param name="Name">What it is called where a reader reads it.</param>
public sealed record MiniRibbonOption(string Id, string Name)
{
    /// <summary>What it is drawn as where a picture says it better than a word, in the box a button gives a picture — or null.</summary>
    public Geometry? Shape { get; init; }

    /// <summary>The colour it is, where the option is itself a colour — drawn as a swatch of it, and as nothing where it is clear.</summary>
    public Color? Shade { get; init; }

    /// <summary>The few letters it is drawn as, where neither a shape nor a colour says it.</summary>
    public string? Letters { get; init; }
}

/// <summary>
/// A value typed in or picked from a list of the usual ones — a box holding the value the content has now, which a value the
/// language accepts replaces, and beside it the list.
/// </summary>
/// <param name="Listed">The values offered in its list, in the order they are listed.</param>
/// <param name="Current">The value the content has now, as the box shows it.</param>
/// <param name="Entered">The change a value entered makes, given the content as it stands — or null for none.</param>
public sealed record MiniRibbonEntry(string Id, string Name, IReadOnlyList<string> Listed, string Current,
                                     Func<ContentEdit, string, ContentChange?> Entered) : MiniRibbonControl(Id, Name)
{
    /// <summary>
    /// Whether what is in the box is a value the language takes — asked before it is entered, so nothing it refuses ever reaches
    /// <see cref="Entered"/>. Anything, where it says nothing.
    /// </summary>
    public Func<string, bool>? Accepts { get; init; }
}
