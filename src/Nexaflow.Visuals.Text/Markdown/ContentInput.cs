using System;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What the reader did to content, said in the content's own units — what whatever shows content hands its engine
/// (<see cref="ContentEngine.Input"/>), and all it hands it. A real key and a test's key are the same thing by the time they
/// get here, so what one does is what the other does.
/// </summary>
public abstract record ContentInput;

/// <summary>A key pressed, with the modifiers held.</summary>
public sealed record ContentKey(Key Key, ModifierKeys Modifiers = ModifierKeys.None) : ContentInput;

/// <summary>Characters typed — what a key, a composition or a dictation comes to.</summary>
public sealed record ContentText(string Text) : ContentInput;

/// <summary>A press at a point, the second of two in quick succession where <paramref name="Clicks"/> says so.</summary>
public sealed record ContentPress(LayoutPoint At, int Clicks = 1, ModifierKeys Modifiers = ModifierKeys.None) : ContentInput;

/// <summary>
/// The pointer moved while pressed, far enough to be a drag rather than a hand's tremor — how far that is being the
/// pointer's to say, since it is counted in the pixels of the screen.
/// </summary>
public sealed record ContentDrag(LayoutPoint At) : ContentInput;

/// <summary>The press was let go.</summary>
public sealed record ContentRelease : ContentInput;

/// <summary>The pointer is over the content at a point, or — null — has left it.</summary>
public sealed record ContentHover(LayoutPoint? At) : ContentInput;

/// <summary>
/// The wheel turned sideways at a point — tilted, swept on a touchpad, or turned with Shift held — <paramref name="By"/> along, in the
/// content's own units: a box narrower than its lines, slid that far to show more of them.
/// </summary>
public sealed record ContentSlide(LayoutPoint At, double By) : ContentInput;
