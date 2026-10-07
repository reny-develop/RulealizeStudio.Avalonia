// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RulealizeStudio.Server;

/// <summary>A behaviour specification as a UML state machine diagram: the format the Studio has first.</summary>
/// <remarks>
/// <para>
/// Where the application is — its states — and what moves it from one to another — its
/// transitions, each triggered by something the person does and maybe held by a guard — with notes
/// on either, or on the whole machine, for what is said about them that is not a move: a bound, a
/// refusal, what the screen shows. Each of the three is an element, holding what it says and the
/// rules it is bound to:
/// </para>
/// <code>
/// {
///   "$schema": "rulealize-studio/state-machine/v1",
///   "initial": "named",
///   "states": {
///     "named": { "name": "Named", "says": "There is a name.", "rules": ["/state/schema/stage"] }
///   },
///   "transitions": {
///     "party-size": {
///       "from": "named", "to": "named", "name": "Set the party",
///       "says": "A party is one to six people.", "rules": ["/inputs/setParty"]
///     }
///   },
///   "notes": {
///     "party-unchanged": {
///       "on": "party-size", "says": "Setting the party to the size it already is is refused.",
///       "rules": ["/inputs/setParty/validate/party.unchanged"]
///     }
///   }
/// }
/// </code>
/// <para>
/// An element's id is its key, and is unique across the three, since a note may be on either kind.
/// A state may be <c>final</c>; a transition's <c>guard</c> is what it waits for, said in words. A
/// rule is named by <see cref="Rule.Name"/>, never by a place in the rules' text.
/// </para>
/// <para>
/// It is written by the Studio's editor and by an agent, and never by hand: an edit here rewrites
/// the whole file in one layout, with each element's members in the order above, so that whoever
/// wrote it last, a change to it reads in a diff as what changed.
/// </para>
/// </remarks>
public static class StateMachine
{
    /// <summary>What a state machine says it is, in <c>$schema</c>.</summary>
    public const string Schema = "rulealize-studio/state-machine/v1";

    /// <summary>The kinds of element, each with the member its elements are kept under.</summary>
    private static readonly (string Kind, string Member)[] Kinds = [("state", "states"), ("transition", "transitions"), ("note", "notes")];

    /// <summary>The members each kind of element is written with, in order; any other is kept after them.</summary>
    private static readonly Dictionary<string, string[]> Members = new()
    {
        ["state"] = ["name", "final", "says", "rules"],
        ["transition"] = ["from", "to", "name", "guard", "says", "rules"],
        ["note"] = ["on", "says", "rules"],
    };

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads a state machine.</summary>
    /// <param name="text">The specification.</param>
    /// <returns>What it has; nothing where the text does not say it is a state machine.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static Machine Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        DocumentMap map = DocumentMap.Read(text);
        if (map.Schema != Schema || JsonNode.Parse(text, documentOptions: Options) is not JsonObject document)
        {
            return new Machine(null, [], ImmutableDictionary<string, Shape>.Empty, []);
        }

        ImmutableArray<Element>.Builder elements = ImmutableArray.CreateBuilder<Element>();
        ImmutableDictionary<string, Shape>.Builder shapes = ImmutableDictionary.CreateBuilder<string, Shape>(StringComparer.Ordinal);
        ImmutableArray<Finding>.Builder faults = ImmutableArray.CreateBuilder<Finding>();

        foreach ((string kind, string member) in Kinds)
        {
            foreach ((string id, JsonObject element) in Each(document, member))
            {
                string at = $"/{member}/{Rules.Escape(id)}";
                (int start, int length) = map.Span(at) ?? (0, 0);
                if (shapes.ContainsKey(id))
                {
                    faults.Add(new Finding(start, 1, $"'{id}' is the id of another element too."));
                    continue;
                }

                ImmutableArray<Reference>.Builder rules = ImmutableArray.CreateBuilder<Reference>();
                if (element["rules"] is JsonArray list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] is JsonValue value && value.TryGetValue(out string? name) && map.Span($"{at}/rules/{i}") is { } written)
                        {
                            rules.Add(new Reference(name, written.Start, written.Length));
                        }
                    }
                }

                elements.Add(new Element(id, kind, Word(element, "name"), Word(element, "says"), rules.ToImmutable(), start, length));
                shapes[id] = new Shape(
                    Named(element, "from"),
                    Named(element, "to"),
                    Named(element, "on"),
                    Word(element, "guard"),
                    element["final"] is JsonValue final && final.TryGetValue(out bool ends) && ends);
            }
        }

        string? initial = Named(document, "initial");
        bool IsState(string? id) => id is not null && elements.Any(e => e.Id == id && e.Kind == "state");
        void Fault(string pointer, string message)
        {
            (int start, int length) = map.Span(pointer) ?? (0, 0);
            faults.Add(new Finding(start, length, message));
        }

        if (initial is not null && !IsState(initial))
        {
            Fault("/initial", $"'{initial}' is not a state of this machine.");
        }

        foreach (Element element in elements)
        {
            Shape shape = shapes[element.Id];
            string at = $"/{Kinds.Single(k => k.Kind == element.Kind).Member}/{Rules.Escape(element.Id)}";
            if (element.Kind == "transition")
            {
                foreach ((string end, string? state) in new[] { ("from", shape.From), ("to", shape.To) })
                {
                    if (!IsState(state))
                    {
                        Fault(state is null ? at : $"{at}/{end}", state is null
                            ? $"Transition '{element.Id}' has no '{end}'."
                            : $"'{state}' is not a state of this machine.");
                    }
                }
            }

            if (element.Kind == "note" && shape.On is { } on && (on == element.Id || !shapes.ContainsKey(on)))
            {
                Fault($"{at}/on", $"'{on}' is not another element of this machine.");
            }
        }

        return new Machine(initial, elements.ToImmutable(), shapes.ToImmutable(), [.. faults.OrderBy(f => f.Start)]);
    }

    /// <summary>Makes an edit to a state machine, and writes it out whole.</summary>
    /// <param name="text">The specification as it is.</param>
    /// <param name="edit">
    /// <para>One of:</para>
    /// <list type="bullet">
    /// <item><c>{ "op": "add", "kind": "state" | "transition" | "note", "from", "to", "on" }</c> — a new element saying nothing yet, under an id made here; a transition from and to the states given, or the first state; a note on the element given, or the whole machine.</item>
    /// <item><c>{ "op": "set", "id", "field", "value" }</c> — <c>name</c>, <c>says</c>, <c>guard</c>, <c>from</c>, <c>to</c> or <c>on</c> to a string, a guard or a note's <c>on</c> taken out by an empty one; <c>final</c> to a boolean.</item>
    /// <item><c>{ "op": "initial", "id" }</c> — the state the machine starts in.</item>
    /// <item><c>{ "op": "remove", "id" }</c> — the element, with the transitions from and to a state and every note on what goes.</item>
    /// <item><c>{ "op": "bind", "id", "rule" }</c> and <c>{ "op": "unbind", "id", "rule" }</c> — a rule, by <see cref="Rule.Name"/>, to the element or from it.</item>
    /// </list>
    /// </param>
    /// <returns>The whole of the specification after it, and the id of the element an <c>add</c> made.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    /// <exception cref="FormatException">The text is not a state machine, or the edit is not one of the above or names what the machine does not have.</exception>
    public static (string Text, string? Added) Edit(string text, JsonObject edit)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edit);

        if (DocumentMap.Read(text).Schema != Schema || JsonNode.Parse(text, documentOptions: Options) is not JsonObject document)
        {
            throw new FormatException("The text is not a state machine.");
        }

        string op = Required(edit, "op");
        string? added = null;
        switch (op)
        {
            case "add":
                added = Add(document, Required(edit, "kind"), Named(edit, "from"), Named(edit, "to"), Named(edit, "on"));
                break;

            case "set":
                Set(document, Required(edit, "id"), Required(edit, "field"), edit["value"]);
                break;

            case "initial":
                string initial = Required(edit, "id");
                if (KindOf(document, initial) != "state")
                {
                    throw new FormatException($"'{initial}' is not a state of this machine.");
                }

                document["initial"] = initial;
                break;

            case "remove":
                Remove(document, Required(edit, "id"));
                break;

            case "bind" or "unbind":
                JsonObject element = Find(document, Required(edit, "id")).Element;
                string rule = Required(edit, "rule");
                if (element["rules"] is not JsonArray rules)
                {
                    element["rules"] = rules = [];
                }

                JsonNode? there = rules.FirstOrDefault(r => r is JsonValue v && v.TryGetValue(out string? name) && name == rule);
                if (op == "bind" && there is null)
                {
                    rules.Add(rule);
                }
                else if (op == "unbind" && there is not null)
                {
                    rules.Remove(there);
                }

                break;

            default:
                throw new FormatException($"'{op}' is not an edit of a state machine.");
        }

        return (Write(document, text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"), added);
    }

    /// <summary>A state machine with nothing in it yet, as an application's folder starts with one.</summary>
    /// <returns>Its text.</returns>
    public static string Empty() =>
        Write(new JsonObject { ["$schema"] = Schema, ["states"] = new JsonObject(), ["transitions"] = new JsonObject(), ["notes"] = new JsonObject() }, "\n");

    private static string Add(JsonObject document, string kind, string? from, string? to, string? on)
    {
        string member = Kinds.FirstOrDefault(k => k.Kind == kind).Member
            ?? throw new FormatException($"'{kind}' is not a kind of element of a state machine.");

        string id = Enumerable.Range(1, int.MaxValue).Select(n => $"{kind}-{n}").First(i => KindOf(document, i) is null);
        string? first = Each(document, "states").Select(s => s.Id).FirstOrDefault();
        JsonObject element = kind switch
        {
            "state" => new JsonObject { ["name"] = "" },
            "transition" => new JsonObject
            {
                ["from"] = State(document, from ?? first ?? throw new FormatException("A transition needs a state to start from.")),
                ["to"] = State(document, to ?? from ?? first!),
                ["name"] = "",
            },
            _ => on is null ? [] : new JsonObject { ["on"] = Other(document, on, id) },
        };

        element["says"] = "";
        element["rules"] = new JsonArray();
        if (document[member] is not JsonObject elements)
        {
            document[member] = elements = [];
        }

        elements[id] = element;
        if (kind == "state" && Named(document, "initial") is null)
        {
            document["initial"] = id;
        }

        return id;
    }

    private static void Set(JsonObject document, string id, string field, JsonNode? value)
    {
        (string kind, JsonObject element) = Find(document, id);
        if (!Members[kind].Contains(field) || field == "rules")
        {
            throw new FormatException($"A {kind} has no '{field}' to set.");
        }

        if (field == "final")
        {
            if (value is JsonValue final && final.TryGetValue(out bool ends) && ends)
            {
                element["final"] = true;
            }
            else
            {
                element.Remove("final");
            }

            return;
        }

        string written = value is JsonValue v && v.TryGetValue(out string? s) ? s : throw new FormatException($"A {kind}'s '{field}' is a string.");
        switch (field)
        {
            case "from" or "to":
                element[field] = State(document, written);
                break;

            case "on" or "guard" when written.Length == 0:
                element.Remove(field);
                break;

            case "on":
                element[field] = Other(document, written, id);
                break;

            default:
                element[field] = written;
                break;
        }
    }

    private static void Remove(JsonObject document, string id)
    {
        (string kind, _) = Find(document, id);
        List<string> gone = [id];
        if (kind == "state")
        {
            gone.AddRange(Each(document, "transitions").Where(t => Named(t.Element, "from") == id || Named(t.Element, "to") == id).Select(t => t.Id));
            if (Named(document, "initial") == id)
            {
                document.Remove("initial");
            }
        }

        // A note may be on a note, so what goes with the element is followed until nothing more does.
        for (int was = 0; was != gone.Count;)
        {
            was = gone.Count;
            gone.AddRange(Each(document, "notes").Where(n => !gone.Contains(n.Id) && Named(n.Element, "on") is { } on && gone.Contains(on)).Select(n => n.Id));
        }

        foreach ((_, string member) in Kinds)
        {
            if (document[member] is JsonObject elements)
            {
                foreach (string each in gone)
                {
                    elements.Remove(each);
                }
            }
        }
    }

    /// <summary>The kind of element an id is, or nothing where the machine has no element by it.</summary>
    private static string? KindOf(JsonObject document, string id) =>
        Kinds.Where(k => document[k.Member] is JsonObject elements && elements.ContainsKey(id)).Select(k => k.Kind).FirstOrDefault();

    private static (string Kind, JsonObject Element) Find(JsonObject document, string id)
    {
        foreach ((string kind, string member) in Kinds)
        {
            if (document[member]?[id] is JsonObject element)
            {
                return (kind, element);
            }
        }

        throw new FormatException($"This machine has no element '{id}'.");
    }

    private static string State(JsonObject document, string id) =>
        KindOf(document, id) == "state" ? id : throw new FormatException($"'{id}' is not a state of this machine.");

    private static string Other(JsonObject document, string id, string self) =>
        id != self && KindOf(document, id) is not null ? id : throw new FormatException($"'{id}' is not another element of this machine.");

    private static IEnumerable<(string Id, JsonObject Element)> Each(JsonObject document, string member) =>
        document[member] is JsonObject elements
            ? elements.Where(e => e.Value is JsonObject).Select(e => (e.Key, (JsonObject)e.Value!)).ToArray()
            : [];

    private static string? Named(JsonObject node, string member) =>
        node[member] is JsonValue value && value.TryGetValue(out string? name) && name.Length > 0 ? name : null;

    private static string Word(JsonObject node, string member) => Named(node, member) ?? "";

    private static string Required(JsonObject edit, string member) =>
        Named(edit, member) ?? throw new FormatException($"The edit has no '{member}'.");

    /// <summary>The whole document in the one layout the format is written in: the schema, where it starts, then each kind of element, each element's members in order.</summary>
    private static string Write(JsonObject document, string newLine)
    {
        JsonObject written = new() { ["$schema"] = Schema };
        if (Named(document, "initial") is { } initial)
        {
            written["initial"] = initial;
        }

        foreach ((string kind, string member) in Kinds)
        {
            JsonObject elements = [];
            foreach ((string id, JsonObject element) in Each(document, member))
            {
                JsonObject ordered = [];
                foreach (string key in Members[kind].Where(element.ContainsKey).Concat(element.Select(m => m.Key).Where(k => !Members[kind].Contains(k))))
                {
                    ordered[key] = element[key]?.DeepClone();
                }

                elements[id] = ordered;
            }

            written[member] = elements;
        }

        foreach ((string key, JsonNode? value) in document.Where(m => m.Key is not ("$schema" or "initial") && !Kinds.Any(k => k.Member == m.Key)))
        {
            written[key] = value?.DeepClone();
        }

        return written.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentSize = 2,
            NewLine = newLine,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + newLine;
    }
}

/// <summary>What a state machine has.</summary>
/// <param name="Initial">The state it starts in; <see langword="null"/> where it does not say.</param>
/// <param name="Elements">Its states, then its transitions, then its notes, each in the order written.</param>
/// <param name="Shapes">How each element stands to the others, by id.</param>
/// <param name="Faults">Where it refers to something it does not have.</param>
public sealed record Machine(string? Initial, ImmutableArray<Element> Elements, ImmutableDictionary<string, Shape> Shapes, ImmutableArray<Finding> Faults);

/// <summary>How an element of a state machine stands to the others.</summary>
/// <param name="From">The state a transition starts from.</param>
/// <param name="To">The state a transition goes to.</param>
/// <param name="On">The element a note is on; <see langword="null"/> for a note on the whole machine.</param>
/// <param name="Guard">What a transition waits for, in words; empty where it waits for nothing.</param>
/// <param name="Final">Whether a state is one the machine ends in.</param>
public sealed record Shape(string? From, string? To, string? On, string Guard, bool Final);
