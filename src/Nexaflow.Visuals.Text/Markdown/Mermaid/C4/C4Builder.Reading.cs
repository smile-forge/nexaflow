using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.C4;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.C4;

/// <summary>What a structural C4 diagram draws, read down its tree in the order it is written.</summary>
internal sealed partial class C4Builder
{
    /// <summary>One element: a card saying what it is, what it is built with and what it does.</summary>
    private sealed record Element(ContentPart Part, string Id, int Order)
    {
        /// <summary>What is written across the top of it — its label, or its own name where it was given none.</summary>
        public ContentPart? Said { get; init; }

        /// <summary>The line in brackets under that, which its stages worked out rather than it being written as one run.</summary>
        public string Stereotype { get; init; } = string.Empty;

        /// <summary>What it is built with, which the stereotype says as well — kept so a press on it means what was written.</summary>
        public ContentPart? Technology { get; init; }

        /// <summary>The sentence under it.</summary>
        public ContentPart? Describes { get; init; }

        public C4Shape Shape { get; init; }

        /// <summary>Which band of C4's grading it takes — see <see cref="C4Elements.Banded"/>.</summary>
        public int Tone { get; init; }

        /// <summary>What it is filled, written in and outlined with, where anything says — otherwise the theme's own.</summary>
        public string? Fill { get; init; }
        public string? Ink { get; init; }
        public string? Border { get; init; }

        /// <summary>The boundary it was written inside, or null for one written outside them all.</summary>
        public string? Box { get; init; }

        /// <summary>Where a <c>$link</c> leads, where one was written.</summary>
        public string? Href { get; init; }
    }

    /// <summary>
    /// A boundary or a deployment node — one type, because the two are the same thing: a named box holding elements and other
    /// boundaries. They differ in how they draw, which is what <see cref="Physical"/> says.
    /// </summary>
    /// <param name="Key">Where it stands among the boundaries written, which is what an element says it is inside.</param>
    private sealed record Boundary(ContentPart Part, string Key, int Order)
    {
        public ContentPart? Said { get; init; }

        /// <summary>The line in brackets under its name — its <c>$type</c>, or what a deployment node runs on.</summary>
        public string? Says { get; init; }

        /// <summary>The boundary this one was written inside, or null for one written outside them all.</summary>
        public string? Parent { get; init; }

        /// <summary>Whether it is a deployment node, which is a real box and so is drawn solid rather than dashed.</summary>
        public bool Physical { get; init; }

        public string? Fill { get; init; }
        public string? Ink { get; init; }
        public string? Border { get; init; }

        /// <summary>Where a <c>$link</c> leads, where one was written.</summary>
        public string? Href { get; init; }

        /// <summary>Everything from the line that opened it through the line that closed it, which is what a press on it means.</summary>
        public SourceSpan Whole { get; init; }
    }

    /// <summary>A relationship: a line from one element to another, carrying what it is done with and what it is for.</summary>
    private sealed record Relation(ContentPart Part, string From, string To, int Order)
    {
        public ContentPart? Said { get; init; }

        /// <summary>What is written under the label in smaller type — what it is done with, and what it is for.</summary>
        public IReadOnlyList<ContentPart> Under { get; init; } = [];

        /// <summary>A <c>BiRel</c>, which draws a head at each end.</summary>
        public bool Both { get; init; }

        public bool Dotted { get; init; }

        public string? Ink { get; init; }
        public string? SaidInk { get; init; }

        /// <summary>Its number where the diagram counts its relationships, and null where it does not.</summary>
        public string? Number { get; init; }
    }

    /// <summary>
    /// What the block writes, read down the tree in the order it is written: the elements, the boundaries holding them — each
    /// gathered by its stages with the lines written in it — and the relationships between them. This is the logical shape, not
    /// the drawn one: where each lands on the page is the layout's.
    /// </summary>
    private sealed class Diagram
    {
        private Diagram(C4Metrics config) => Config = config;

        public C4Metrics Config { get; private init; }

        /// <summary>Which way the diagram runs: down, unless the last <c>LAYOUT_*</c> line turns it.</summary>
        public DiagramWay Way { get; private set; } = DiagramWay.Down;

        public List<Element> Nodes { get; private init; } = [];

        public List<Boundary> Boxes { get; private init; } = [];

        public List<Relation> Links { get; private init; } = [];

        /// <summary>The rows of the key, where <c>SHOW_LEGEND()</c> asked for one.</summary>
        public IReadOnlyList<C4Key> Legend { get; private set; } = [];

        public static Diagram Of(ContentPart root, C4Metrics config)
        {
            var diagram = new Diagram(config);
            diagram.Read(root, null);

            // A relationship may name something nobody declared, which still has to stand somewhere or the line would vanish.
            foreach (var link in diagram.Links)
                foreach (var end in new[] { link.From, link.To })
                    if (diagram.Find(end) is null)
                        diagram.Nodes.Add(new Element(link.Part, end, diagram.Nodes.Count) { Tone = C4Elements.Banded(C4Level.System, false) });

            return diagram;
        }

        /// <summary>
        /// The same diagram to different measures — what one too wide for the room it is given is laid out again as. Nothing is
        /// read again: it is the same elements, boundaries and relationships, drawn smaller.
        /// </summary>
        public Diagram Sized(C4Metrics config) =>
            new(config) { Way = Way, Nodes = Nodes, Boxes = Boxes, Links = Links, Legend = Legend };

        /// <summary>The boundaries written directly inside one, in the order they were written.</summary>
        public IEnumerable<Boundary> Within(string? key) =>
            Boxes.Where(box => string.Equals(box.Parent, key, StringComparison.Ordinal));

        /// <summary>And the elements written directly inside one.</summary>
        public IEnumerable<Element> Inside(string? key) =>
            Nodes.Where(node => string.Equals(node.Box, key, StringComparison.Ordinal));

        /// <summary>The element that name was given to, or null.</summary>
        public Element? Find(string id) => Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

        /// <summary>Whether the block declared nothing worth drawing.</summary>
        public bool Empty => Nodes.Count == 0 && Boxes.Count == 0;

        /// <summary>Everything written in one part of the block — the whole of it, or one boundary — inside the boundary given.</summary>
        private void Read(ContentPart holder, string? inside)
        {
            foreach (var part in holder.Children)
            {
                if (part.Kind == MermaidKinds.Group)
                {
                    Read(part, Opened(part, inside) ?? inside);
                    continue;
                }

                if (part.Stated() is not { } stated) continue;

                switch (stated.Node)
                {
                    case C4ElementNode element:
                        Nodes.Add(new Element(stated, element.Id, Nodes.Count)
                        {
                            Said = stated.Argument(C4Means.Label) ?? stated.Argument(C4Means.Alias),
                            Stereotype = element.Stereotype,
                            Technology = stated.Argument(C4Means.Technology),
                            Describes = stated.Argument(C4Means.Description),
                            Shape = element.Shape,
                            Tone = element.Tone,
                            Fill = element.Paint.Fill,
                            Ink = element.Paint.Ink,
                            Border = element.Paint.Border,
                            Box = inside,
                            Href = element.Href,
                        });
                        break;

                    case C4RelationNode relation:
                        Links.Add(new Relation(stated, relation.From, relation.To, Links.Count)
                        {
                            Said = stated.Argument(C4Means.Label),
                            Under = [.. new[] { stated.Argument(C4Means.Technology), stated.Argument(C4Means.Description) }.OfType<ContentPart>()],
                            Both = relation.Both,
                            Dotted = relation.Stroke.Dotted,
                            Ink = relation.Stroke.Ink,
                            SaidInk = relation.Stroke.Said,
                            Number = relation.Number,
                        });
                        break;

                    case C4LegendNode legend when Legend.Count == 0:
                        Legend = legend.Keys;
                        break;

                    case { Kind: C4Kinds.Macro }:
                        Way = stated.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == MermaidKinds.Key && inner.Role == C4Roles.Macro)?.Text.ToLowerInvariant() switch
                        {
                            "layout_top_down" => DiagramWay.Down,
                            "layout_left_right" => DiagramWay.Right,
                            _ => Way,
                        };
                        break;
                }
            }
        }

        /// <summary>
        /// A boundary, and the box it makes: known by where it stands among the boundaries, and reaching from the line opening it
        /// through the line closing it — the opening line alone, where nothing closes it. Null where it is no boundary its stages
        /// could read, and what is written in it is read as though written outside it.
        /// </summary>
        private string? Opened(ContentPart group, string? inside)
        {
            if (group.Children.FirstOrDefault()?.Stated() is not { Node: C4BoundaryNode boundary } opening) return null;

            var closing = group.Children.Count > 1 && group.Children[^1].Stated() is { Kind: C4Kinds.Ends } ends ? ends : opening;
            var key = Boxes.Count.ToString(CultureInfo.InvariantCulture);

            Boxes.Add(new Boundary(opening, key, Boxes.Count)
            {
                Said = opening.Argument(C4Means.Label) ?? opening.Argument(C4Means.Alias),
                Says = boundary.Says,
                Parent = inside,
                Physical = boundary.Physical,
                Fill = boundary.Paint.Fill,
                Ink = boundary.Paint.Ink,
                Border = boundary.Paint.Border,
                Href = boundary.Href,
                Whole = new SourceSpan(opening.Start, Math.Max(0, closing.End - opening.Start)),
            });

            return key;
        }
    }
}
