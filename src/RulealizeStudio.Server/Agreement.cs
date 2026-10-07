// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;

namespace RulealizeStudio.Server;

/// <summary>Where an application's specifications and its rules disagree, both ways.</summary>
/// <remarks>
/// <para>
/// An element bound to a rule the rules do not have is a requirement whose binding points at
/// nothing — marked on the specification where the name is written, and on the rules where it
/// would be. A rule no element of any specification is bound to is a rule nobody asked for — what
/// an agent writing rules is likeliest to add, and what the blueprint coming first forbids — marked
/// on the rule, and on every specification, which is where it has to be asked for first. An element
/// bound to nothing is a requirement nothing implements yet, and is not marked.
/// </para>
/// <para>
/// An application is as many specifications and rule sets as it needs, none counted against the
/// other, so a binding names the rule set it is to — <c>signup#/inputs/book</c>, by the rule set's
/// file without <c>.json</c> — and may leave it out where the application has one rule set. One
/// that leaves it out where there are more is marked, since it could be to any of them.
/// </para>
/// <para>
/// What in a specification refers to something it does not have is marked on it too
/// (<see cref="Specification.Faults"/>), since it is read here first by whoever is about to write rules from it.
/// </para>
/// </remarks>
public static class Agreement
{
    /// <summary>What separates a rule set's name from the rule in a binding: <c>signup#/inputs/book</c>.</summary>
    public const char Separator = '#';

    /// <summary>Compares a specification with the one rule set beside it.</summary>
    /// <param name="specification">The specification, as written.</param>
    /// <param name="rules">The rule set, as written; <see langword="null"/> where there is none yet.</param>
    /// <returns>What is marked on each of the two.</returns>
    /// <exception cref="JsonException">One of the two is not JSON.</exception>
    public static Disagreements Compare(string specification, string? rules)
    {
        ArgumentNullException.ThrowIfNull(specification);

        const string Specified = "specification.json";
        const string Ruled = "rules.json";
        IReadOnlyDictionary<string, ImmutableArray<Finding>> found =
            Compare([new Written(Specified, specification)], rules is null ? [] : [new Written(Ruled, rules)]);

        return new Disagreements(found[Specified], rules is null ? [] : found[Ruled]);
    }

    /// <summary>Compares every specification of an application with every rule set of it.</summary>
    /// <param name="specifications">The specifications, each by its file's name.</param>
    /// <param name="ruleSets">The rule sets, each by its file's name: <c>signup.json</c>, which a binding names as <c>signup</c>.</param>
    /// <returns>What is marked on each file, by its name; every file given has an entry.</returns>
    /// <exception cref="JsonException">One of them is not JSON.</exception>
    public static IReadOnlyDictionary<string, ImmutableArray<Finding>> Compare(IReadOnlyList<Written> specifications, IReadOnlyList<Written> ruleSets)
    {
        ArgumentNullException.ThrowIfNull(specifications);
        ArgumentNullException.ThrowIfNull(ruleSets);

        Dictionary<string, ImmutableArray<Finding>.Builder> marked = [];
        foreach (Written file in specifications.Concat(ruleSets))
        {
            marked[file.Name] = ImmutableArray.CreateBuilder<Finding>();
        }

        (Written File, ImmutableArray<Rule> Rules, DocumentMap Map)[] read =
            [.. ruleSets.Select(file => (file, Rules.Read(file.Text), DocumentMap.Read(file.Text)))];
        bool one = read.Length == 1;
        HashSet<(string RuleSet, string Rule)> asked = [];

        foreach (Written specification in specifications)
        {
            ImmutableArray<Element> elements = Specification.Read(specification.Text);
            marked[specification.Name].AddRange(Specification.Faults(specification.Text));

            foreach (Element element in elements)
            {
                foreach (Reference reference in element.Rules)
                {
                    (string? named, string rule) = Split(reference.Rule);
                    if (named is null && read.Length > 1)
                    {
                        marked[specification.Name].Add(new Finding(reference.Start, reference.Length,
                            $"'{reference.Rule}' could be a rule of any of {string.Join(", ", read.Select(each => Name(each.File)))}: say which, as '{Name(read[0].File)}{Separator}{rule}'."));
                        continue;
                    }

                    int index = named is null ? (one ? 0 : -1) : Array.FindIndex(read, each => Name(each.File) == named);
                    if (index < 0)
                    {
                        marked[specification.Name].Add(new Finding(reference.Start, reference.Length, read.Length == 0 || named is null
                            ? $"'{reference.Rule}' is not a rule the rules have."
                            : $"'{reference.Rule}' names a rule set the application does not have, '{named}'."));
                        continue;
                    }

                    (Written file, ImmutableArray<Rule> rules, DocumentMap map) = read[index];
                    if (rules.Any(each => each.Name == rule))
                    {
                        asked.Add((file.Name, rule));
                        continue;
                    }

                    marked[specification.Name].Add(new Finding(reference.Start, reference.Length, $"'{reference.Rule}' is not a rule the rules have."));
                    (int start, int length) = map.Locate(rule);
                    marked[file.Name].Add(new Finding(start, length, $"{Called(element)} is bound to '{reference.Rule}', which these rules do not have."));
                }
            }
        }

        foreach ((Written file, ImmutableArray<Rule> rules, _) in read)
        {
            foreach (Rule rule in rules.Where(each => !asked.Contains((file.Name, each.Name))))
            {
                string named = one ? rule.Name : $"{Name(file)}{Separator}{rule.Name}";
                string message = specifications.Count == 1
                    ? $"Nothing in the specification asks for '{named}'."
                    : $"Nothing in any specification asks for '{named}'.";
                bool bracket = file.Text[rule.Start] is '{' or '[';
                marked[file.Name].Add(new Finding(rule.Start, bracket ? 1 : rule.Length, message));
                foreach (Written specification in specifications)
                {
                    marked[specification.Name].Add(new Finding(0, Math.Min(1, specification.Text.Length), message));
                }
            }
        }

        return marked.ToDictionary(each => each.Key, each => each.Value.ToImmutable());
    }

    /// <summary>The rule set a binding names, if it names one, and the rule.</summary>
    /// <param name="binding">The binding, as a specification writes it: <c>signup#/inputs/book</c> or <c>/inputs/book</c>.</param>
    /// <returns>The rule set's name, or null where it names none, and the rule's <see cref="Rule.Name"/>.</returns>
    public static (string? RuleSet, string Rule) Split(string binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        int at = binding.IndexOf(Separator, StringComparison.Ordinal);
        return at <= 0 ? (null, binding) : (binding[..at], binding[(at + 1)..]);
    }

    /// <summary>An element as a message calls it: its kind and id, and its name where it has one.</summary>
    internal static string Called(Element element) =>
        element.Title.Length == 0 ? $"{element.Kind} '{element.Id}'" : $"{element.Kind} '{element.Id}' ({element.Title})";

    /// <summary>What a binding calls a rule set: its file's name without <c>.json</c>.</summary>
    private static string Name(Written file) => Path.GetFileNameWithoutExtension(file.Name);
}

/// <summary>A file of an application as it is written now.</summary>
/// <param name="Name">Its name in the application's folder.</param>
/// <param name="Text">What is written in it.</param>
public sealed record Written(string Name, string Text);
