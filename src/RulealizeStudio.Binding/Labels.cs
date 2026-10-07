// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RulealizeStudio.Binding;

/// <summary>A label document: the sentences an application says, in one language, for what its rule set gives only a code.</summary>
/// <remarks>
/// <para>
/// A rule set refuses with a code and never a sentence, because wording is the host's: a sentence
/// in the rule set would be in one language, and would travel with rules that do not need it.
/// This is where the host keeps it — beside the rule set, one file per language, named
/// <c>signup.labels.en.json</c> for <c>signup.json</c> in English — and the application, the review
/// of its test design and the replay of it all read the one sentence from here.
/// </para>
/// <code>
/// {
///   "$schema": "rulealize-studio/labels/v1",
///   "labels": {
///     "/inputs/setName/validate/name.reserved": "That name is kept for the staff."
///   }
/// }
/// </code>
/// <para>
/// A label is keyed by the name the rule it labels has in a specification: a refusal of an input, by
/// the input and its code, which is what a refusal carries and what a test design records — a
/// <c>validate</c> clause's code, or an open parameter's <c>invalid</c>, the code a value its schema
/// does not admit is refused with. The runtime makes the two one set of codes, so both are keyed
/// under <c>validate</c>. A refusal is the only thing labelled yet, and a key naming anything else is
/// refused rather than kept and never shown. A code with no label is shown as the code, which is
/// honest in a way an invented phrase is not.
/// </para>
/// </remarks>
public sealed class Labels
{
    /// <summary>What a label document says it is.</summary>
    public const string Schema = "rulealize-studio/labels/v1";

    /// <summary>What sits between a rule set's name and a language in a label document's file name.</summary>
    public const string Infix = ".labels.";

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, string> _sentences;

    private Labels(string language, Dictionary<string, string> sentences)
    {
        Language = language;
        _sentences = sentences;
        Sentences = new ReadOnlyDictionary<string, string>(sentences);
    }

    /// <summary>Gets the language the sentences are in, as the file name gives it: <c>en</c>, <c>ja</c>, <c>en-GB</c>.</summary>
    public string Language { get; }

    /// <summary>Gets every sentence, by the name of the rule it labels.</summary>
    public IReadOnlyDictionary<string, string> Sentences { get; }

    /// <summary>Reads a label document.</summary>
    /// <param name="language">The language it is in, as its file name gives it.</param>
    /// <param name="text">The document.</param>
    /// <returns>The sentences.</returns>
    /// <exception cref="FormatException">The text is not a label document, or labels something that is not a refusal.</exception>
    public static Labels Read(string language, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        ArgumentNullException.ThrowIfNull(text);

        JsonObject root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: Options) as JsonObject
                ?? throw new FormatException("A label document is a JSON object.");
        }
        catch (JsonException wrong)
        {
            throw new FormatException($"A label document is JSON: {wrong.Message}", wrong);
        }

        if (root["$schema"]?.GetValueKind() != JsonValueKind.String || (string?)root["$schema"] != Schema)
        {
            throw new FormatException($"A label document says it is '{Schema}', and this one does not.");
        }

        Dictionary<string, string> sentences = new(StringComparer.Ordinal);
        foreach ((string rule, JsonNode? sentence) in root["labels"] as JsonObject ?? [])
        {
            if (!IsRefusal(rule))
            {
                throw new FormatException($"'{rule}' is labelled, and only a refusal is: '/inputs/<input>/validate/<code>'.");
            }

            if (sentence?.GetValueKind() != JsonValueKind.String)
            {
                throw new FormatException($"The label for '{rule}' is a sentence.");
            }

            sentences[rule] = (string)sentence!;
        }

        return new Labels(language, sentences);
    }

    /// <summary>The language a label document's file name gives it.</summary>
    /// <param name="ruleSet">The rule set's file name: <c>signup.json</c>.</param>
    /// <param name="file">A file beside it: <c>signup.labels.en.json</c>.</param>
    /// <returns>The language, or null where the file is not a label document of that rule set.</returns>
    public static string? LanguageOf(string ruleSet, string file)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(file);

        string prefix = Path.GetFileNameWithoutExtension(ruleSet) + Infix;
        string name = Path.GetFileName(file);

        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && name.Length > prefix.Length + ".json".Length
                ? name[prefix.Length..^".json".Length]
                : null;
    }

    /// <summary>Chooses the label document to speak in.</summary>
    /// <param name="written">Every label document there is.</param>
    /// <param name="culture">The language the person reads, and the ones it falls back to.</param>
    /// <returns>
    /// The one in <paramref name="culture"/>, or else in the nearest language it falls back to,
    /// or else the first by language; null where there is none.
    /// </returns>
    /// <remarks>
    /// Falling back to some language rather than to codes is what makes a screen read in a language
    /// nobody wrote labels for still say sentences; ordering by language makes which one the same
    /// wherever it is asked, so the application and the review of it never choose differently.
    /// </remarks>
    public static Labels? Choose(IEnumerable<Labels> written, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(culture);

        Labels[] all = [.. written.OrderBy(labels => labels.Language, StringComparer.OrdinalIgnoreCase)];

        for (CultureInfo at = culture; !string.IsNullOrEmpty(at.Name); at = at.Parent)
        {
            if (all.FirstOrDefault(labels => string.Equals(labels.Language, at.Name, StringComparison.OrdinalIgnoreCase)) is Labels found)
            {
                return found;
            }
        }

        return all.FirstOrDefault();
    }

    /// <summary>The name a refusal has in a specification, and is labelled by.</summary>
    /// <param name="input">The input, as the rule set names it.</param>
    /// <param name="code">Its code: a clause's, or a parameter's <c>invalid</c>.</param>
    /// <returns><c>/inputs/setName/validate/name.reserved</c>.</returns>
    public static string Refusal(string input, string code)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(code);

        return $"/inputs/{Escape(input)}/validate/{Escape(code)}";
    }

    /// <summary>What to say for a refusal.</summary>
    /// <param name="input">The input, as the rule set names it.</param>
    /// <param name="code">What its clause refused with.</param>
    /// <returns>The sentence, or the code where there is no label for it.</returns>
    public string Say(string input, string code) =>
        _sentences.TryGetValue(Refusal(input, code), out string? sentence) ? sentence : code;

    /// <summary>What to say for a refusal, with no label document or one.</summary>
    /// <param name="labels">The label document, or null where there is none.</param>
    /// <param name="input">The input, as the rule set names it.</param>
    /// <param name="code">What its clause refused with.</param>
    /// <returns>The sentence, or the code where there is no label for it.</returns>
    public static string Say(Labels? labels, string input, string code) => labels?.Say(input, code) ?? code;

    private static bool IsRefusal(string rule)
    {
        string[] parts = rule.Split('/');
        return parts is ["", "inputs", { Length: > 0 }, "validate", { Length: > 0 }];
    }

    /// <summary>A name as one segment of a pointer: <c>~</c> and <c>/</c> written the way a pointer writes them.</summary>
    private static string Escape(string name) =>
        name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
