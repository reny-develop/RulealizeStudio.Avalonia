// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RulealizeStudio.Hosting;

namespace RulealizeStudio.Server;

/// <summary>A value somebody chose to try, as a test design's <c>edits</c> hold it, and what became of it.</summary>
/// <param name="At">Where it is among the design's edits, from zero.</param>
/// <param name="State">The state it is made from, as written: the design's name for one, or how the walk arrives there — <c>#0 + setName(to: alice)</c>.</param>
/// <param name="Input">The input taken first there.</param>
/// <param name="Args">What it is called with, by parameter, in the text form the runtime writes.</param>
/// <param name="Carried"><c>yes</c>, <c>not legal here</c> or <c>not reached</c>, as Ruledger wrote it when it last derived the design.</param>
/// <remarks>
/// <para>
/// The form is Ruledger's, described in its <c>doc/test-design.md</c> under <em>An edit</em>, and
/// this is the one place in the Studio that writes it. A choice is what a person writes into a test
/// design, and the only thing: the walk that follows it, and whether the rules take it, are
/// Ruledger's when it derives the design again, so a choice is written here and never anything it
/// led to.
/// </para>
/// <para>
/// A new choice names its state as the design being read names it; deriving again writes it out
/// the long way, which is the name that survives the rule set changing.
/// </para>
/// </remarks>
public sealed record Choice(int At, string State, string Input, IReadOnlyDictionary<string, string> Args, string Carried)
{
    /// <summary>Where a test design keeps the choices made in it.</summary>
    public const string Pointer = "/edits";

    private static readonly JsonSerializerOptions Written = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Gets the move it takes, as a route writes one: <c>setName(to: alice)</c>.</summary>
    public string Step => Args.Count == 0 ? Input : $"{Input}({string.Join(", ", Args.Select(arg => $"{arg.Key}: {arg.Value}"))})";

    /// <summary>Reads the choices a test design holds.</summary>
    /// <param name="design">A <c>ruledger/test-design</c> document.</param>
    /// <returns>Every choice, in the order written.</returns>
    /// <exception cref="FormatException">An edit is not one: it does not name a state and an input.</exception>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    public static IReadOnlyList<Choice> Read(string design)
    {
        ArgumentNullException.ThrowIfNull(design);

        JsonArray edits = JsonNode.Parse(design, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })?["edits"] as JsonArray ?? [];
        return [.. edits.Select((edit, at) => new Choice(
            at,
            Text(edit?["state"]) ?? throw new FormatException($"Edit {at + 1} of the test design names no state."),
            Text(edit?["input"]) ?? throw new FormatException($"Edit {at + 1} of the test design names no input."),
            edit?["args"] is JsonObject args
                ? args.ToDictionary(arg => arg.Key, arg => Text(arg.Value) ?? "null", StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal),
            Text(edit?["carried"]) ?? "yes"))];
    }

    /// <summary>Which situation of a design each choice is made from.</summary>
    /// <param name="situations">The design's situations, where it starts first.</param>
    /// <param name="choices">The design's choices.</param>
    /// <returns>The choices by the state they are made from, as the design names it; under <see langword="null"/>, those made from a state that is none of them.</returns>
    /// <remarks>
    /// A choice names its state either way Ruledger reads one: by the design's name for it, or
    /// spelled out as how the walk arrives there, which is how Ruledger writes it back.
    /// </remarks>
    public static ILookup<string?, Choice> Placed(IReadOnlyList<Situation> situations, IEnumerable<Choice> choices)
    {
        ArgumentNullException.ThrowIfNull(situations);
        ArgumentNullException.ThrowIfNull(choices);

        Dictionary<string, string> named = new(StringComparer.Ordinal);
        foreach (Situation situation in situations)
        {
            named[situation.State] = situation.State;
            named.TryAdd(string.Concat(situations[0].State, string.Concat(situation.Route.Select(step => " + " + step))), situation.State);
        }

        return choices.ToLookup(choice => named.GetValueOrDefault(choice.State));
    }

    /// <summary>Says how to add a choice to a test design: last among its edits.</summary>
    /// <param name="design">The design's text, as it is now.</param>
    /// <param name="state">The state to choose from, by the name the design gives it.</param>
    /// <param name="input">The input to take first there.</param>
    /// <param name="args">A value for each parameter it is to be called with, in the text form the runtime writes.</param>
    /// <returns>The change that writes it.</returns>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    public static Change Add(string design, string state, string input, IReadOnlyDictionary<string, string> args)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(args);

        DocumentMap map = DocumentMap.Read(design);
        Listing edits = map.Lists.FirstOrDefault(list => list.Pointer == Pointer)
            ?? throw new ArgumentException("The test design has no 'edits' to add a choice to.", nameof(design));
        return map.Insert(Pointer, edits.Entries.Length, Write(state, input, args));
    }

    /// <summary>Says how to change what a choice already written is called with, leaving where it is made and what it takes.</summary>
    /// <param name="design">The design's text, as it is now.</param>
    /// <param name="at">Which of its edits, from zero.</param>
    /// <param name="args">A value for each parameter, in the text form the runtime writes.</param>
    /// <returns>The change that writes it again, with those values and without what Ruledger last said became of it.</returns>
    /// <exception cref="FormatException">The edit there does not name a state and an input.</exception>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    public static Change Change(string design, int at, IReadOnlyDictionary<string, string> args)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(args);

        IReadOnlyList<Choice> choices = Read(design);
        ArgumentOutOfRangeException.ThrowIfNegative(at);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(at, choices.Count);

        (int start, int length) = DocumentMap.Read(design).Span($"{Pointer}/{at}")!.Value;
        return new Change(start, length, Write(choices[at].State, choices[at].Input, args));
    }

    /// <summary>Says how to take a choice out of a test design.</summary>
    /// <param name="design">The design's text, as it is now.</param>
    /// <param name="at">Which of its edits, from zero.</param>
    /// <returns>The changes that take it out, with the comma that went with it.</returns>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    public static IReadOnlyList<Change> Remove(string design, int at)
    {
        ArgumentNullException.ThrowIfNull(design);

        DocumentMap map = DocumentMap.Read(design);
        int written = map.Lists.FirstOrDefault(list => list.Pointer == Pointer)?.Entries.Length ?? 0;
        ArgumentOutOfRangeException.ThrowIfNegative(at);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(at, written);

        return map.Remove($"{Pointer}/{at}");
    }

    /// <summary>A choice as Ruledger's form writes one, on a line: what is carried is Ruledger's to add.</summary>
    private static string Write(string state, string input, IReadOnlyDictionary<string, string> args)
    {
        string written = $"{{ \"state\": {Quote(state)}, \"input\": {Quote(input)}";
        if (args.Count > 0)
        {
            written += $", \"args\": {{ {string.Join(", ", args.Select(arg => $"{Quote(arg.Key)}: {Quote(arg.Value)}"))} }}";
        }

        return written + " }";
    }

    private static string Quote(string text) => JsonSerializer.Serialize(text, Written);

    private static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        _ => node.ToJsonString(),
    };
}
