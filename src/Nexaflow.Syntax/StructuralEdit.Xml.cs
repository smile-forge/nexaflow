using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.Syntax;

/// <summary>
/// The XML half of <see cref="StructuralEdit"/>: the same operations, on elements, plus the one kind of edit XML
/// has that code does not — an attribute.
/// <para>
/// An element is found by what the outline calls it (a XAML <c>T:</c>/<c>N:</c>/<c>K:</c>/<c>A:</c> id) or by an
/// <see cref="XmlPath"/>, which reaches the elements nothing names — a project file's <c>PackageReference</c>s.
/// Everything else holds as it does for code: the edit is worked out against the parse of the text in hand, the
/// payload is indented for where it lands with the file's own line endings, and a result that no longer parses is
/// refused rather than returned.
/// </para>
/// </summary>
public static partial class StructuralEdit
{
    private const string XmlHasNoImports =
        "XML has no imports to add - a namespace is an attribute of the element that declares it, so set-attribute "
      + "xmlns:<prefix> on that element instead.";

    /// <summary>
    /// The edit at an element named by an XPath — <see cref="Options.At"/> — in an XML-family file.
    /// <para>
    /// Exactly one element must match, because an edit applied to whichever of several came first is a guess. The
    /// exception is <c>--all</c> with delete, set-attribute or remove-attribute: each of those does the same thing
    /// to every element whatever its neighbours look like, which is what "all" can safely mean. A replace or an
    /// insert across several places is a different edit in each, and is refused.
    /// </para>
    /// </summary>
    public static Result ApplyAt(string grammarId, string source, Op op, string? text, Options? options = null,
                                 string? renameTo = null)
    {
        var o = options ?? new Options();
        if (!TreeSitterLanguages.IsXml(grammarId))
            return Result.Fail("An element path (--at) addresses an element of an XML file, and this file parses as "
                             + (string.IsNullOrEmpty(grammarId) ? "no language at all." : $"{grammarId}."));
        if (op == Op.Import) return Result.Fail(XmlHasNoImports);
        if (!XmlPath.TryParse(o.At ?? "", out var path, out var syntax)) return Result.Fail(syntax!);

        var (elements, error) = XmlAnchors.ByPath(grammarId, source, path!);
        if (error is not null) return Result.Fail(error);

        var notes = new List<string>();
        var bulk  = op is Op.Delete or Op.SetAttribute or Op.RemoveAttribute;
        if (elements.Count > 1)
        {
            if (!(o.AllOccurrences && bulk)) return Result.Fail(XmlAnchors.Ambiguous(elements, path!.Text, bulk));
            notes.Add($"applied to {elements.Count} elements");
        }

        if (op is Op.Delete or Op.InsertBefore or Op.InsertAfter
            && path!.Steps.Any(s => s.Predicates.Any(p => p is XmlPath.Position)))
            notes.Add("the path picks by position ([n]), and this edit moves the positions after it - work out the "
                    + "path again before reusing it.");

        return EditElements(grammarId, source, elements, identity: null, idKind: null, op, text, o, renameTo,
                            notes, path!.Text);
    }

    /// <summary>The edit at a XAML id (or plain XML's root id), from <see cref="Apply(string,string,string,string,Op,string?,Options?,string?)"/>.</summary>
    private static Result ApplyXml(string grammarId, string source, string astPath, string expectedName, Op op,
                                   string? text, Options o, string? renameTo, List<string> notes)
    {
        if (op == Op.Import) return Result.Fail(XmlHasNoImports);

        var (element, identity, label, error) = XmlAnchors.ByAstPath(grammarId, source, astPath);
        if (error is not null) return Result.Fail(error);
        if (element is null || label != expectedName)
            return Result.Fail($"The parser found no element with the id '{astPath}' named '{expectedName}'. Refusing "
                             + "to edit on position alone.");

        if (o.Expect is { Length: > 0 } expect
            && !source[element.Start..element.End].Contains(expect, StringComparison.Ordinal))
            return Result.Fail($"The element at line {element.Line} does not contain the expected text {Quote(expect)}.");

        var result = EditElements(grammarId, source, [element], identity, astPath[..astPath.IndexOf(':')], op, text, o,
                                  renameTo, notes, expectedName);

        if (result.Ok && astPath.Contains('#') && op is Op.Delete or Op.InsertBefore or Op.InsertAfter or Op.Replace)
            return result with
            {
                Notes = [.. result.Notes, $"the #n in '{astPath}' counts elements with the same id before it - this edit "
                                        + "renumbers the later ones. List the declarations again before further edits."],
            };
        return result;
    }

    private static Result EditElements(string grammarId, string source, IReadOnlyList<XmlElement> elements,
                                       XmlAttribute? identity, string? idKind, Op op, string? text, Options o,
                                       string? renameTo, List<string> notes, string subject)
    {
        if (op is Op.Replace or Op.InsertBefore or Op.InsertAfter or Op.Append or Op.Body
            && text is { } payload && payload.TrimStart().StartsWith("<?xml", StringComparison.Ordinal))
            return Result.Fail("This payload opens with an XML declaration, so it looks like a whole file rather than an "
                             + "element - putting it here would nest one document inside another. Use `graph edit "
                             + "create <path>` for a new file, or drop the declaration and pass just the element. "
                             + "Nothing written.");

        // Nested matches collapse into their outermost for a delete: deleting the parent takes the child with it,
        // and deleting the child first would move the parent out from under its recorded offsets.
        var targets = op == Op.Delete
            ? elements.Where(e => !elements.Any(outer => outer != e && outer.Start <= e.Start && e.End <= outer.End)).ToList()
            : [.. elements];

        var newline = SourceText.Of(source).Newline;
        var updated = source;

        // Back to front, so each edit's offsets - taken from the one parse - are still true when it is made.
        foreach (var element in targets.OrderByDescending(e => e.Start))
        {
            var (edited, error) = EditElement(grammarId, updated, element, identity, idKind, op, text, o, renameTo,
                                              newline, notes, subject);
            if (error is not null) return Result.Fail(error);
            updated = edited!;
        }

        if (VerifyXml(grammarId, source, updated, op, targets, identity, idKind, renameTo, o, text) is { } problem)
            return Result.Fail(problem);

        return new Result(true, $"{Describe(op)} {subject}", updated, HunkOf(source, updated), notes,
                          ChangeOf(source, updated));
    }

    private static (string? Text, string? Error) EditElement(string grammarId, string src, XmlElement e,
                                                             XmlAttribute? identity, string? idKind, Op op,
                                                             string? text, Options o, string? renameTo,
                                                             string newline, List<string> notes, string subject)
    {
        var indent = IndentAt(src, e.Start);
        var owns   = OwnsItsLines(src, e);

        switch (op)
        {
            case Op.Replace:
                if (text is null) return (null, "Replacement text is required.");
                return owns
                    ? Replace(src, e.ToAnchor(), text, indent, newline, o.WithTrivia, grammarId, notes)
                    : (src[..e.Start] + Block(text, indent, newline, indentFirst: false) + src[e.End..], null);

            case Op.Delete:
                if (e.IsRoot) return (null, "That is the root element, and a document has to have one. Replace it instead.");
                return owns ? Delete(src, e.ToAnchor()) : (src[..e.TriviaStart] + src[e.End..], null);

            case Op.InsertBefore:
            case Op.InsertAfter:
                if (text is null) return (null, "Text to insert is required.");
                if (e.IsRoot) return (null, "That is the root element, and a document has only one - insert inside it with append.");
                return op == Op.InsertBefore ? XmlInsertBefore(src, e, text, indent, newline, owns)
                                             : XmlInsertAfter(src, e, text, indent, newline, owns);

            case Op.Append:
                if (text is null) return (null, "Text to append is required.");
                return (XmlAppend(grammarId, src, e, text, indent, newline), null);

            case Op.Body:
                if (text is null) return (null, "Replacement content is required (an empty string empties the element).");
                return (XmlBody(grammarId, src, e, text, indent, newline), null);

            case Op.Signature:
                if (text is null) return (null, "A replacement start tag is required.");
                return XmlSignature(src, e, text, indent, newline, notes);

            case Op.Rename:
                return XmlRename(grammarId, src, e, identity, idKind, renameTo, notes);

            case Op.Doc:
                if (text is null) return (null, "Comment text is required.");
                return XmlDoc(src, e, text, indent, newline, owns);

            case Op.Substitute:
                return Substitute(grammarId, src, e.ToAnchor(), text, o, newline, subject, notes);

            case Op.SetAttribute:
                return SetAttribute(src, e, o.Attribute, text, newline, notes);

            case Op.RemoveAttribute:
                return RemoveAttribute(src, e, o.Attribute);

            default:
                return (null, $"Unsupported operation {op}.");
        }
    }

    /// <summary>
    /// Whether the element has its lines to itself — nothing but indentation before its comment, nothing but
    /// whitespace after it. An element that shares a line (<c>&lt;Run/&gt;&lt;Run x:Name="b"/&gt;</c>) is edited at its
    /// exact extent instead: taking "its lines" would take its neighbours, and adding a line break inside mixed
    /// content changes what the document says.
    /// </summary>
    private static bool OwnsItsLines(string src, XmlElement e)
    {
        if (!AtLineStart(src, e.TriviaStart)) return false;
        var i = e.End;
        while (i < src.Length && src[i] is ' ' or '\t') i++;
        return i >= src.Length || src[i] is '\r' or '\n';
    }

    private static (string?, string?) XmlInsertBefore(string src, XmlElement e, string text, string indent,
                                                      string newline, bool owns)
    {
        if (!owns) return (src[..e.TriviaStart] + Block(text, indent, newline, indentFirst: false) + src[e.TriviaStart..], null);

        // A blank line between siblings only where the siblings already have one: XAML seldom does, and code
        // always does, which is why this is not the code insert.
        var at    = LineStart(src, e.TriviaStart);
        var blank = PrecedingBlankLine(src, at) is not null;
        return (src[..at] + Block(text, indent, newline) + newline + (blank ? newline : "") + src[at..], null);
    }

    private static (string?, string?) XmlInsertAfter(string src, XmlElement e, string text, string indent,
                                                     string newline, bool owns)
    {
        if (!owns) return (src[..e.End] + Block(text, indent, newline, indentFirst: false) + src[e.End..], null);

        var at    = Math.Min(LineEndInclusive(src, e.End) + 1, src.Length);
        var lead  = at > 0 && src[at - 1] != '\n' ? newline : "";
        var blank = BlankLine(src, at, out _);
        return (src[..at] + lead + (blank ? newline : "") + Block(text, indent, newline) + newline + src[at..], null);
    }

    /// <summary>The indentation a child of <paramref name="e"/> takes: its existing children's, or one level in.</summary>
    private static string ChildIndent(string grammarId, string src, XmlElement e, string indent) =>
        e.FirstContentStart is { } first && XmlAnchors.LeadingIndent(src, first) is { } existing
            ? existing
            : indent + XmlAnchors.IndentUnit(grammarId, src);

    private static string XmlAppend(string grammarId, string src, XmlElement e, string text, string indent, string newline)
    {
        var child = ChildIndent(grammarId, src, e, indent);
        var block = Block(text, child, newline);

        if (e.SelfClosing) return Expanded(src, e, newline + block + newline + indent);

        // Nothing in it yet but whitespace: the gap becomes the child.
        if (e.LastContentEnd is null)
            return src[..e.TagEnd] + newline + block + newline + indent + src[e.EndTagStart!.Value..];

        // Text, or an end tag sharing a line with what is before it: this is mixed content or a one-liner, and a
        // line break added into it would be a change to what the element says.
        if (e.HasText || XmlAnchors.LeadingIndent(src, e.EndTagStart!.Value) is null)
            return !text.Contains('\n')
                ? src[..e.EndTagStart!.Value] + text.Trim() + src[e.EndTagStart!.Value..]
                : src[..e.EndTagStart!.Value] + newline + block + newline + indent + src[e.EndTagStart!.Value..];

        var at = Math.Min(LineEndInclusive(src, e.LastContentEnd.Value) + 1, src.Length);
        return src[..at] + block + newline + src[at..];
    }

    private static string XmlBody(string grammarId, string src, XmlElement e, string text, string indent, string newline)
    {
        var empty = SourceText.BlockOf(text).All(l => l.Trim().Length == 0);
        var inline = !empty && !text.Trim().Contains('\n') && !text.TrimStart().StartsWith('<');
        var block  = empty ? "" : inline ? text.Trim() : newline + Block(text, ChildIndent(grammarId, src, e, indent), newline) + newline + indent;

        return e.SelfClosing
            ? Expanded(src, e, block)
            : src[..e.TagEnd] + block + src[e.EndTagStart!.Value..];
    }

    /// <summary>A self-closing element opened up around <paramref name="content"/>: <c>&lt;X a="1"/&gt;</c> becomes
    /// <c>&lt;X a="1"&gt;content&lt;/X&gt;</c>, the space some write before <c>/&gt;</c> going with the slash.</summary>
    private static string Expanded(string src, XmlElement e, string content)
    {
        var cut = e.CloserStart;
        while (cut > e.TagNameEnd && char.IsWhiteSpace(src[cut - 1])) cut--;
        return src[..cut] + ">" + content + "</" + e.QName + ">" + src[e.End..];
    }

    private static (string?, string?) XmlSignature(string src, XmlElement e, string text, string indent,
                                                   string newline, List<string> notes)
    {
        var tag = Block(text, indent, newline, indentFirst: false).Trim();
        if (!tag.StartsWith('<') || !tag.EndsWith('>') || tag.StartsWith("</", StringComparison.Ordinal))
            return (null, "A signature for an element is its start tag - <Name attr=\"…\"> - and nothing else.");

        var closes = tag.EndsWith("/>", StringComparison.Ordinal);
        if (e.SelfClosing && !closes)
            return (null, "That element closes itself, so its start tag has to as well (…/>). Use body or append to "
                        + "give it content.");
        if (!e.SelfClosing && closes)
            return (null, "That element has content, so its start tag has to end with > rather than />. Use replace to "
                        + "swap the whole element for an empty one.");

        var name = Regex.Match(tag, @"^<([^\s/>]+)").Groups[1].Value;
        var updated = src[..e.Start] + tag + src[e.TagEnd..];
        if (e.SelfClosing || name == e.QName) return (updated, null);

        // A new name has to reach the end tag too, or the element does not close.
        var shift = tag.Length - (e.TagEnd - e.Start);
        notes.Add($"the element was renamed from {e.QName} to {name}, so its end tag was renamed to match.");
        return (updated[..(e.EndTagNameStart!.Value + shift)] + name + updated[(e.EndTagNameEnd!.Value + shift)..], null);
    }

    private static readonly Regex XmlName = new(@"^[A-Za-z_][A-Za-z0-9_:.\-]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Renames an element — meaning its <i>id</i> when it was addressed by one, its tag otherwise.
    /// <para>
    /// A code rename changes the name a declaration is known by, and for a XAML element that is the id, not the tag:
    /// the generated field is named after <c>x:Name</c>, <c>{StaticResource}</c> looks up <c>x:Key</c>, a journey clicks
    /// the AutomationId. So <c>rename N:Save --to Store</c> changes <c>x:Name="Save"</c>, and the tag - which has to
    /// change when the element's type does - is <c>signature</c>'s job. An element with no id, found by path, has
    /// only its tag to be renamed.
    /// </para>
    /// </summary>
    private static (string?, string?) XmlRename(string grammarId, string src, XmlElement e, XmlAttribute? identity,
                                                string? idKind, string? renameTo, List<string> notes)
    {
        if (renameTo is not { Length: > 0 } name) return (null, "A new name is required.");

        if (identity is not null)
        {
            var value = name;
            if (identity.Name.EndsWith(":Class", StringComparison.Ordinal))
            {
                var dot = identity.Value.LastIndexOf('.');
                value = identity.Value[..(dot + 1)] + name;
                notes.Add($"x:Class is now {value} - the code-behind's partial class has to be renamed to match, or the "
                        + "view will not compile.");
            }
            else if (idKind == "N")
            {
                if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                    return (null, $"'{name}' is not an identifier, and x:Name becomes a field of the code-behind.");
                if (Declarations(grammarId, src).Any(d => d.AstPath == "N:" + name))
                    return (null, $"Something in this view is already named '{name}', and x:Name has to be unique.");
            }
            else if (idKind == "K" && Declarations(grammarId, src).Any(d => d.AstPath == "K:" + name))
                notes.Add($"another element is already keyed '{name}' - that is only right if they are in different dictionaries.");

            return (src[..(identity.ValueStart + 1)] + Escape(value, identity.Quote) + src[(identity.ValueEnd - 1)..], null);
        }

        if (!XmlName.IsMatch(name)) return (null, $"'{name}' is not an XML element name.");
        var colon = e.QName.LastIndexOf(':');
        var qname = name.Contains(':') || colon < 0 ? name : e.QName[..(colon + 1)] + name;

        var updated = src;
        if (!e.SelfClosing)
            updated = updated[..e.EndTagNameStart!.Value] + qname + updated[e.EndTagNameEnd!.Value..];
        return (updated[..e.TagNameStart] + qname + updated[e.TagNameEnd..], null);
    }

    private static (string?, string?) XmlDoc(string src, XmlElement e, string text, string indent, string newline, bool owns)
    {
        var lines   = SourceText.BlockOf(text);
        var comment = OpensWithComment("xml", text)
            ? text
            : lines.Count <= 1 ? $"<!-- {text.Trim()} -->" : "<!--\n" + string.Join("\n", lines) + "\n-->";

        var inner = comment.Trim();
        if (inner.StartsWith("<!--", StringComparison.Ordinal) && inner.EndsWith("-->", StringComparison.Ordinal))
        {
            inner = inner[4..^3];
            if (inner.Contains("--", StringComparison.Ordinal) || inner.EndsWith('-'))
                return (null, "An XML comment cannot contain -- or end with -, so that text cannot go in one.");
        }

        return owns
            ? Doc(src, e.ToAnchor(), comment, indent, newline)
            : (src[..e.TriviaStart] + comment + src[e.Start..], null);
    }

    private static (string?, string?) SetAttribute(string src, XmlElement e, string? name, string? value,
                                                   string newline, List<string> notes)
    {
        if (name is not { Length: > 0 } || !XmlName.IsMatch(name))
            return (null, name is { Length: > 0 } ? $"'{name}' is not an attribute name." : "The attribute's name is required (--name).");
        if (value is null) return (null, "The attribute's value is required (--text; an empty one is allowed).");

        if (name.EndsWith(":Name", StringComparison.Ordinal) || name.EndsWith(":Key", StringComparison.Ordinal)
            || name.EndsWith("AutomationProperties.AutomationId", StringComparison.Ordinal))
            notes.Add($"{name} is an id - the element's id in the graph and outline changes with it.");

        if (e.Attribute(name) is { } existing)
            return (src[..(existing.ValueStart + 1)] + Escape(value, existing.Quote) + src[(existing.ValueEnd - 1)..], null);

        var quote = e.Attributes.Count > 0 ? e.Attributes[0].Quote
                  : Regex.Match(src, "=\\s*(['\"])") is { Success: true } m ? m.Groups[1].Value[0]
                  : '"';
        var written = $"{name}={quote}{Escape(value, quote)}{quote}";

        if (e.Attributes.Count == 0) return (src[..e.TagNameEnd] + " " + written + src[e.TagNameEnd..], null);

        // One to a line when the last one already has its own line, aligned under it; beside it otherwise.
        var last = e.Attributes[^1];
        return XmlAnchors.LeadingIndent(src, last.Start) is { } aligned
            ? (src[..last.End] + newline + aligned + written + src[last.End..], null)
            : (src[..last.End] + " " + written + src[last.End..], null);
    }

    private static (string?, string?) RemoveAttribute(string src, XmlElement e, string? name)
    {
        if (name is not { Length: > 0 }) return (null, "The attribute's name is required (--name).");

        var index = -1;
        for (var i = e.Attributes.Count - 1; i >= 0 && index < 0; i--)
            if (e.Attributes[i].Name == name) index = i;

        if (index < 0)
            return (null, $"<{e.QName}> at line {e.Line} has no {name} attribute"
                        + (e.Attributes.Count == 0 ? " - it has none at all." : $" - it has {string.Join(", ", e.Attributes.Select(a => a.Name))}."));

        var attribute = e.Attributes[index];

        // The first on the tag's line, with the next on a line below: pull the next one up beside the name rather than
        // leaving the tag name alone on its line.
        if (index == 0 && index + 1 < e.Attributes.Count && XmlAnchors.LeadingIndent(src, e.Attributes[1].Start) is not null)
            return (src[..attribute.Start] + src[e.Attributes[1].Start..], null);

        var from = attribute.Start;
        while (from > e.TagNameEnd && char.IsWhiteSpace(src[from - 1])) from--;
        return (src[..from] + src[attribute.End..], null);
    }

    /// <summary>A literal value made safe for an attribute quoted with <paramref name="quote"/>. Always literal: a
    /// caller passing <c>&amp;amp;</c> gets that text, escaped, not an ampersand.</summary>
    private static string Escape(string value, char quote)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(c switch
            {
                '&'  => "&amp;",
                '<'  => "&lt;",
                '"'  when quote == '"'  => "&quot;",
                '\'' when quote == '\'' => "&apos;",
                '\n' => "&#10;",
                '\r' => "&#13;",
                '\t' => "&#9;",
                _    => c.ToString(),
            });
        return sb.ToString();
    }

    /// <summary>
    /// Whether the result is safe to hand back. The parse first, as for any language; then, for one element, the
    /// promise the op makes about the part it was not meant to touch — checked on the text, not trusted.
    /// </summary>
    private static string? VerifyXml(string grammarId, string original, string updated, Op op,
                                     IReadOnlyList<XmlElement> targets, XmlAttribute? identity, string? idKind,
                                     string? renameTo, Options o, string? text)
    {
        var anchors = new DeclarationAnchors();
        if (anchors.ParsesCleanly(grammarId, original) && !anchors.ParsesCleanly(grammarId, updated))
            return "The edit would leave the document unparseable, so it has not been applied. The replacement is "
                 + "probably unbalanced - an element left open, or a stray < or &.";

        if (op == Op.Delete)
        {
            var removed = targets.Sum(t => 1 + t.DescendantElements);
            if (XmlAnchors.CountElements(grammarId, updated) != XmlAnchors.CountElements(grammarId, original) - removed)
                return "The delete removed a different number of elements than it was meant to, so it has not been applied.";
            return null;
        }

        if (targets.Count != 1) return null;
        var before = targets[0];

        // Every op below leaves where the element starts where it was.
        if (op is not (Op.SetAttribute or Op.RemoveAttribute or Op.Rename or Op.Body or Op.Append or Op.Signature))
            return null;
        if (XmlAnchors.ElementAt(grammarId, updated, before.Start) is not { } after)
            return "The element could not be found where it started after the edit, so it has not been applied.";

        switch (op)
        {
            case Op.SetAttribute when after.Attribute(o.Attribute!)?.Value != text:
                return $"{o.Attribute} does not read back as the value written, so the edit has not been applied.";

            case Op.RemoveAttribute when after.Attribute(o.Attribute!) is not null
                                      && after.Attributes.Count(a => a.Name == o.Attribute) >= before.Attributes.Count(a => a.Name == o.Attribute):
                return $"{o.Attribute} is still there after removing it, so the edit has not been applied.";

            case Op.Body or Op.Append when !SameAttributes(before, after) || after.QName != before.QName:
                return "The start tag changed while the content was being edited, so the edit has not been applied.";

            case Op.Signature when !before.SelfClosing
                                && original[before.TagEnd..before.EndTagStart!.Value] != updated[after.TagEnd..after.EndTagStart!.Value]:
                return "The content changed while the start tag was being replaced, so the edit has not been applied. The "
                     + "replacement should be the start tag alone.";

            case Op.Rename when identity is null && after.QName.Split(':')[^1] != renameTo!.Split(':')[^1]:
                return $"The element is not called {renameTo} after the rename, so it has not been applied.";

            case Op.Rename when identity is not null && idKind is "N" or "K" or "A"
                             && !Declarations(grammarId, updated).Any(d => d.AstPath == $"{idKind}:{renameTo}" || d.AstPath.StartsWith($"{idKind}:{renameTo}#", StringComparison.Ordinal)):
                return $"No element answers to {idKind}:{renameTo} after the rename, so it has not been applied.";
        }
        return null;
    }

    private static bool SameAttributes(XmlElement a, XmlElement b) =>
        a.Attributes.Select(x => (x.Name, x.Value)).SequenceEqual(b.Attributes.Select(x => (x.Name, x.Value)));
}
