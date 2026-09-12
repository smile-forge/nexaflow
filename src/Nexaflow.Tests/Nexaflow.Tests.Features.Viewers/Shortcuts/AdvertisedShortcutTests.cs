using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Shortcuts;

/// <summary>
/// A tooltip that names a keyboard shortcut is a promise, and nothing else in the build checks it.
///
/// <para>
/// The JSON viewer's Save button said <c>Save (Ctrl+S)</c> for as long as it existed and the key did
/// nothing: the view registered no <c>InputBindings</c>, and there is no shell-level Ctrl+S to fall back
/// on. Every neighbouring editor wired it — Hex in its constructor, Markdown in a <c>KeyDown</c> handler,
/// Text in code-behind, the shared <c>FileTextEditorView</c> in XAML — which is exactly why the gap was
/// invisible: the behaviour is normal everywhere you would think to look.
/// </para>
/// <para>
/// So the promise is checked mechanically. For every view under <c>src/</c>, each shortcut a tooltip
/// advertises must be registered by that view's XAML or its code-behind. The guard asserts the gesture is
/// wired <em>at all</em>, not that it is wired to the right command — a code-behind is small enough that
/// "mentions this key with these modifiers" is a sound signal, and the alternative (matching command
/// identity across four different registration idioms) would fail for reasons that are not bugs.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("UI shortcut/tooltip consistency guard — spans every view, no single product node")]
public class AdvertisedShortcutTests
{
    private static string Repo => Nexaflow.Tests.Features.Architecture.RepoRoot.Locate();

    /// <summary>A gesture as both sides spell it: modifiers sorted, then the key — <c>CONTROL+S</c>.</summary>
    private static string Canonical(IEnumerable<string> modifiers, string key)
    {
        var mods = modifiers
            .Select(m => m.Trim().ToUpperInvariant())
            .Select(m => m switch { "CTRL" => "CONTROL", "WINDOWS" => "WIN", var other => other })
            .Where(m => m is "CONTROL" or "ALT" or "SHIFT" or "WIN")
            .Distinct()
            .OrderBy(m => m, StringComparer.Ordinal);
        return string.Concat(mods.Select(m => m + "+")) + key.Trim().ToUpperInvariant();
    }

    // ── What a tooltip advertises ────────────────────────────────────────────

    private static readonly Regex ToolTipText = new(@"ToolTip=""([^""]*)""", RegexOptions.Compiled);

    /// <summary>A modified gesture — <c>Ctrl+S</c>, <c>Shift+F3</c>, <c>Ctrl+Shift+P</c>.</summary>
    private static readonly Regex ModifiedGesture = new(
        @"\b(?<mods>(?:Ctrl|Alt|Shift)(?:\+(?:Ctrl|Alt|Shift))*)\+(?<key>[A-Za-z0-9±]+)",
        RegexOptions.Compiled);

    /// <summary>A bare function key — <c>F3</c>, <c>F12</c>.</summary>
    private static readonly Regex FunctionKey = new(@"\b(?<key>F(?:1[0-2]|[1-9]))\b", RegexOptions.Compiled);

    /// <summary>
    /// A key a view can actually bind: a letter, or a function key. Everything else a tooltip pairs with
    /// Ctrl is chrome rather than a command — <c>Ctrl+scroll</c>, <c>Ctrl+±</c>, and the <c>Ctrl+0</c> the
    /// shared ZoomChip names on behalf of whichever view is hosting it, which is not the file to look in.
    /// </summary>
    private static bool IsBindableKey(string key) =>
        (key.Length == 1 && char.IsAsciiLetter(key[0])) || FunctionKey.IsMatch(key);

    private static IEnumerable<string> AdvertisedGestures(string xaml)
    {
        foreach (Match tip in ToolTipText.Matches(xaml))
        {
            var text     = tip.Groups[1].Value;
            var consumed = new List<(int Start, int End)>();

            foreach (Match g in ModifiedGesture.Matches(text))
            {
                consumed.Add((g.Index, g.Index + g.Length));
                var key = g.Groups["key"].Value;
                if (IsBindableKey(key))
                    yield return Canonical(g.Groups["mods"].Value.Split('+'), key);
            }

            foreach (Match f in FunctionKey.Matches(text))
                if (!consumed.Any(span => f.Index >= span.Start && f.Index < span.End))
                    yield return Canonical([], f.Groups["key"].Value);
        }
    }

    // ── What a view registers ────────────────────────────────────────────────

    private static readonly Regex XamlKeyBinding = new(@"<KeyBinding\b[^>]*?/?>", RegexOptions.Compiled);
    private static readonly Regex Attribute      = new(@"(?<name>\w+)=""(?<value>[^""]*)""", RegexOptions.Compiled);

    private static readonly Regex CodeKeyBinding = new(
        @"new\s+KeyBinding\s*\((?<args>[^;]*?)\)\s*[,;)]", RegexOptions.Compiled);
    private static readonly Regex CodeKey        = new(@"\bKey\.(?<key>\w+)\b", RegexOptions.Compiled);
    private static readonly Regex CodeModifier   = new(@"\bModifierKeys\.(?<mod>\w+)\b", RegexOptions.Compiled);

    /// <summary>
    /// WPF registers these gestures itself, so a view that routes a button at one (and gives it a
    /// CommandTarget) has a working shortcut without declaring a KeyBinding of its own.
    /// </summary>
    private static readonly Dictionary<string, string> ApplicationCommandGestures = new()
    {
        ["Cut"]       = "CONTROL+X",
        ["Copy"]      = "CONTROL+C",
        ["Paste"]     = "CONTROL+V",
        ["Undo"]      = "CONTROL+Z",
        ["Redo"]      = "CONTROL+Y",
        ["SelectAll"] = "CONTROL+A",
        ["Find"]      = "CONTROL+F",
        ["Replace"]   = "CONTROL+H",
    };

    private static HashSet<string> RegisteredGestures(string xaml, string codeBehind)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match binding in XamlKeyBinding.Matches(xaml))
        {
            var attrs = Attribute.Matches(binding.Value)
                                 .ToDictionary(a => a.Groups["name"].Value, a => a.Groups["value"].Value);
            if (!attrs.TryGetValue("Key", out var key)) continue;
            var mods = attrs.GetValueOrDefault("Modifiers", string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries);
            found.Add(Canonical(mods, key));
        }

        // `new KeyBinding(cmd, Key.S, ModifierKeys.Control)` — the Hex idiom.
        foreach (Match binding in CodeKeyBinding.Matches(codeBehind))
        {
            var args = binding.Groups["args"].Value;
            var key  = CodeKey.Match(args);
            if (!key.Success) continue;
            found.Add(Canonical(CodeModifier.Matches(args).Select(m => m.Groups["mod"].Value), key.Groups["key"].Value));
        }

        // A hand-rolled KeyDown/PreviewKeyDown handler — the Markdown and Text idiom. Read as a bag: the
        // handler is a few lines, and pairing each `Key.X` with the modifier test guarding it would be
        // parsing C# with a regex for no extra signal.
        var handlerModifiers = CodeModifier.Matches(codeBehind).Select(m => m.Groups["mod"].Value).ToList();
        if (handlerModifiers.Count > 0)
            foreach (Match key in CodeKey.Matches(codeBehind))
                foreach (var mod in handlerModifiers)
                    found.Add(Canonical([mod], key.Groups["key"].Value));

        foreach (var (command, gesture) in ApplicationCommandGestures)
            if (xaml.Contains("ApplicationCommands." + command, StringComparison.Ordinal) ||
                codeBehind.Contains("ApplicationCommands." + command, StringComparison.Ordinal))
                found.Add(gesture);

        return found;
    }

    // ── The guards ───────────────────────────────────────────────────────────

    private static IEnumerable<string> ViewFiles() =>
        Directory.EnumerateFiles(Path.Combine(Repo, "src"), "*.xaml", SearchOption.AllDirectories)
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                             !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [TestMethod]
    public void Every_shortcut_a_tooltip_advertises_is_registered_by_that_view()
    {
        var unbacked = new List<string>();

        foreach (var view in ViewFiles())
        {
            var xaml       = File.ReadAllText(view);
            var advertised = AdvertisedGestures(xaml).Distinct().ToList();
            if (advertised.Count == 0) continue;

            var codeBehindPath = view + ".cs";
            var codeBehind     = File.Exists(codeBehindPath) ? File.ReadAllText(codeBehindPath) : string.Empty;
            var registered     = RegisteredGestures(xaml, codeBehind);

            unbacked.AddRange(
                advertised.Where(g => !registered.Contains(g))
                          .Select(g => $"{Path.GetRelativePath(Repo, view)} advertises {g} but registers no binding for it"));
        }

        Assert.AreEqual(
            0, unbacked.Count,
            "A tooltip names a shortcut the view never wires up, so the key does nothing:" +
            Environment.NewLine + string.Join(Environment.NewLine, unbacked));
    }

    [TestMethod]
    public void Json_view_binds_Ctrl_S_to_save()
    {
        var view = Path.Combine(Repo, "src", "Nexaflow.Features", "Nexaflow.Features.Json", "Views", "JsonView.xaml");
        var xaml = File.ReadAllText(view);

        var save = XamlKeyBinding.Matches(xaml)
            .Select(b => Attribute.Matches(b.Value)
                                  .ToDictionary(a => a.Groups["name"].Value, a => a.Groups["value"].Value))
            .SingleOrDefault(a => a.GetValueOrDefault("Key") == "S");

        Assert.IsNotNull(save, "JsonView.xaml declares no Ctrl+S KeyBinding, but its Save button advertises one.");
        Assert.AreEqual("Control", save.GetValueOrDefault("Modifiers"));
        Assert.AreEqual("{Binding SaveCommand}", save.GetValueOrDefault("Command"));
    }
}
