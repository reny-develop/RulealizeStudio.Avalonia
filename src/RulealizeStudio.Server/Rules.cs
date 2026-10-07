// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RulealizeStudio.Server;

/// <summary>The rules a rule set has, each under its <see cref="Rule.Name"/>.</summary>
/// <remarks>
/// <para>
/// A rule is what an element of a specification is bound to: a state field and what its schema allows;
/// an input, and within it each parameter, its guard and its actor, each <c>validate</c> clause and
/// its effects; a definition; a projection; the ending. A refusal is named by its code, under
/// <c>validate</c>, wherever it is written — a parameter's <c>invalid</c> as much as a clause, since
/// the runtime makes them one set of codes and a host reads both off the same refusal. An input is a rule and so is each of those
/// within it, so an element saying that a booking can be made is bound to <c>/inputs/book</c>, and one
/// saying when to <c>/inputs/book/when</c>.
/// </para>
/// <para>
/// These are the rule set format's own keys, and no operation is named: what a guard or an effect
/// is made of is not a rule of its own, and a vocabulary nobody has written yet is read the same way.
/// </para>
/// </remarks>
public static class Rules
{
    /// <summary>The parts of an input that are each a rule, besides its parameters and clauses.</summary>
    private static readonly string[] InputParts = ["when", "actor", "effects"];

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads the rules a rule set has.</summary>
    /// <param name="text">The rule set, comments and all.</param>
    /// <returns>Its rules, in the order written; an input before what is within it.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static ImmutableArray<Rule> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        DocumentMap map = DocumentMap.Read(text);
        if (JsonNode.Parse(text, documentOptions: Options) is not JsonObject document)
        {
            return [];
        }

        List<(string Name, string Pointer)> named = [];
        foreach (string field in Keys(document["state"]?["schema"]))
        {
            named.Add(($"/state/schema/{Escape(field)}", $"/state/schema/{Escape(field)}"));
        }

        foreach (string definition in Keys(document["definitions"]))
        {
            named.Add(($"/definitions/{Escape(definition)}", $"/definitions/{Escape(definition)}"));
        }

        foreach (string key in Keys(document["inputs"]))
        {
            string input = $"/inputs/{Escape(key)}";
            JsonNode? node = document["inputs"]![key];
            named.Add((input, input));

            foreach (string parameter in Keys(node?["params"]))
            {
                named.Add(($"{input}/params/{Escape(parameter)}", $"{input}/params/{Escape(parameter)}"));

                // A refusal is named by its code wherever it is written: a value the schema does not
                // admit, under the parameter's invalid, is one beside the validate clauses.
                if (node!["params"]![parameter]?["invalid"] is JsonValue invalid && invalid.TryGetValue(out string? code))
                {
                    named.Add(($"{input}/validate/{Escape(code)}", $"{input}/params/{Escape(parameter)}/invalid"));
                }
            }

            foreach (string part in InputParts.Where(p => node?[p] is not null))
            {
                named.Add(($"{input}/{part}", $"{input}/{part}"));
            }

            if (node?["validate"] is JsonArray clauses)
            {
                for (int i = 0; i < clauses.Count; i++)
                {
                    // A clause without a code yet is one being written, and has no name to bind by.
                    if (clauses[i]?["code"] is JsonValue code && code.TryGetValue(out string? name))
                    {
                        named.Add(($"{input}/validate/{Escape(name)}", $"{input}/validate/{i}"));
                    }
                }
            }
        }

        foreach (string projection in Keys(document["projections"]))
        {
            named.Add(($"/projections/{Escape(projection)}", $"/projections/{Escape(projection)}"));
        }

        if (document.ContainsKey("terminal"))
        {
            named.Add(("/terminal", "/terminal"));
        }

        return [.. named
            .Select(n => (n.Name, Span: map.Span(n.Pointer)))
            .Where(n => n.Span is not null)
            .Select(n => new Rule(n.Name, n.Span!.Value.Start, n.Span.Value.Length))
            .OrderBy(r => r.Start)
            .ThenByDescending(r => r.Length)];
    }

    private static IEnumerable<string> Keys(JsonNode? node) =>
        node is JsonObject members ? members.Select(m => m.Key) : [];

    /// <summary>A name as one segment of a pointer: <c>~</c> and <c>/</c> written the way a pointer writes them.</summary>
    internal static string Escape(string name) =>
        name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
