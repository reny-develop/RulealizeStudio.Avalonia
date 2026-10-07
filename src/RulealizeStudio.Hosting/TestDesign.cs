// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace RulealizeStudio.Hosting;

/// <summary>A <c>ruledger/test-design/v2</c> document, or a v1 one, read for the part a replay needs.</summary>
/// <remarks>
/// The form is Ruledger's, and its <c>doc/test-design.md</c> is where it is described. This reads
/// the three observables — legal inputs, the ending, where each input leads — and the marks the
/// walk's limits leave, and nothing else: <c>edits</c> say which input a person took first, and
/// a replay follows every one there is.
/// </remarks>
internal sealed class TestDesign
{
    public const string Schema = "ruledger/test-design/v2";

    /// <summary>What the form was before a move could wait for a value; every v1 document is a v2 one.</summary>
    public const string Earlier = "ruledger/test-design/v1";

    private TestDesign(
        string ruleSet,
        IReadOnlyList<string> observed,
        IReadOnlyDictionary<(string Input, string Parameter), JsonNode?> admits,
        IReadOnlyList<DesignState> states)
    {
        RuleSet = ruleSet;
        Admits = admits;
        Observed = observed;
        States = states;
        Named = states.ToDictionary(state => state.Name, StringComparer.Ordinal);
    }

    public string RuleSet { get; }

    /// <summary>What each parameter left open admitted when the design was derived: the op of its schema and the bounds beside it.</summary>
    /// <remarks>Empty for a design written before Ruledger recorded it.</remarks>
    public IReadOnlyDictionary<(string Input, string Parameter), JsonNode?> Admits { get; }

    /// <summary>The state fields two positions have to agree on to be the same position.</summary>
    public IReadOnlyList<string> Observed { get; }

    /// <summary>The states, in the order the walk first arrived at them; the first is where the rule set starts.</summary>
    public IReadOnlyList<DesignState> States { get; }

    public IReadOnlyDictionary<string, DesignState> Named { get; }

    /// <summary>Spells out how the walk first arrived at a state.</summary>
    public string Route(DesignState state)
    {
        List<string> steps = [];

        for (DesignState? at = state; at?.From is not null && at.By is not null; at = Named.GetValueOrDefault(at.From))
        {
            steps.Add(at.By);
        }

        steps.Add(States[0].Name);
        steps.Reverse();
        return string.Join(" → ", steps);
    }

    /// <summary>
    /// The moves that first reached a state, from where the rule set starts: each with the state it
    /// was taken in, the branch it landed on where the rules drew, and the step as a replay says it.
    /// </summary>
    /// <remarks>
    /// Found by the move whose arrival is the next state, rather than read out of <c>by</c>, which
    /// does not say what was drawn.
    /// </remarks>
    /// <exception cref="FormatException">The design has a state reached from one where no move leads to it.</exception>
    public IReadOnlyList<(DesignState From, DesignMove Move, DesignLanding? Landing, string Step)> Hops(DesignState state)
    {
        List<(DesignState, DesignMove, DesignLanding?, string)> hops = [];

        for (DesignState at = state; at.From is not null && Named.TryGetValue(at.From, out DesignState? from); at = from)
        {
            (DesignMove Move, DesignLanding? Landing) hop = from.Moves
                .SelectMany(move => move.Lands is null
                    ? [(Move: move, Landing: (DesignLanding?)null, To: move.To)]
                    : move.Lands.Select(landing => (Move: move, Landing: (DesignLanding?)landing, To: landing.To)))
                .Where(each => each.To == at.Name)
                .Select(each => (each.Move, each.Landing))
                .FirstOrDefault();

            if (hop.Move is null)
            {
                throw new FormatException($"The design has {at.Name} reached from {from.Name}, and no move there leads to it.");
            }

            hops.Add((from, hop.Move, hop.Landing, hop.Landing is null ? hop.Move.ToString() : $"{hop.Move} drawing {string.Join(", ", hop.Landing.Draws)}"));
        }

        hops.Reverse();
        return hops;
    }

    public static TestDesign Read(string document)
    {
        JsonObject root = JsonNode.Parse(document, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }) as JsonObject
            ?? throw new FormatException("A test design is a JSON object.");

        if (Text(root["$schema"]) is not (Schema or Earlier))
        {
            throw new FormatException($"A test design says it is '{Schema}' or '{Earlier}', and this one does not.");
        }

        List<DesignState> states = [.. Array(root, "states").Select(node => DesignState.Read(Object(node, "a state")))];
        if (states.Count == 0)
        {
            throw new FormatException("A test design has at least the state the rule set starts in.");
        }

        return new TestDesign(
            Text(root["ruleSet"]) ?? throw new FormatException("A test design names its rule set."),
            [.. Array(root, "observed").Select(node => Text(node) ?? string.Empty)],
            Admitted(root["admits"] as JsonObject),
            states);
    }

    private static Dictionary<(string Input, string Parameter), JsonNode?> Admitted(JsonObject? admits)
    {
        Dictionary<(string Input, string Parameter), JsonNode?> read = [];

        foreach ((string input, JsonNode? parameters) in admits ?? [])
        {
            foreach ((string parameter, JsonNode? schema) in parameters as JsonObject ?? [])
            {
                read[(input, parameter)] = schema?.DeepClone();
            }
        }

        return read;
    }

    internal static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        _ => node.ToJsonString(),
    };

    internal static JsonArray Array(JsonObject owner, string key) => owner[key] switch
    {
        null => [],
        JsonArray list => list,
        _ => throw new FormatException($"'{key}' in a test design is a list."),
    };

    internal static JsonObject Object(JsonNode? node, string what) =>
        node as JsonObject ?? throw new FormatException($"{what} in a test design is a JSON object.");
}

/// <summary>One state a design names.</summary>
internal sealed record DesignState(
    string Name,
    string? From,
    string? By,
    JsonObject Data,
    bool Terminal,
    string? Result,
    bool Truncated,
    IReadOnlyList<DesignMove> Moves,
    IReadOnlyList<DesignRefusal> Refused)
{
    public static DesignState Read(JsonObject state)
    {
        JsonObject? terminal = state["terminal"] as JsonObject;

        return new DesignState(
            TestDesign.Text(state["name"]) ?? throw new FormatException("A state in a test design has a name."),
            TestDesign.Text(state["from"]),
            TestDesign.Text(state["by"]),
            TestDesign.Object(TestDesign.Object(state["state"], "'state'")["data"], "a state's 'data'"),
            terminal is not null,
            terminal is null ? null : TestDesign.Text(terminal["result"]),
            state["truncated"]?.GetValueKind() == JsonValueKind.True,
            [.. TestDesign.Array(state, "moves").Select(node => DesignMove.Read(TestDesign.Object(node, "a move")))],
            [.. TestDesign.Array(state, "refused").Select(node => DesignRefusal.Read(TestDesign.Object(node, "a refusal")))]);
    }
}

/// <summary>A value for a parameter left open that the walk tried in a state, and the rules refused.</summary>
/// <param name="Input">The input's name.</param>
/// <param name="Args">What it was tried with, by parameter name, in the text form the runtime writes.</param>
/// <param name="Codes">What refused it, in the order the clauses are written; none where no clause did.</param>
internal sealed record DesignRefusal(string Input, IReadOnlyDictionary<string, string> Args, IReadOnlyList<string> Codes)
{
    public static DesignRefusal Read(JsonObject refusal)
    {
        Dictionary<string, string> args = new(StringComparer.Ordinal);
        if (refusal["args"] is JsonObject given)
        {
            foreach ((string parameter, JsonNode? value) in given)
            {
                args[parameter] = TestDesign.Text(value) ?? string.Empty;
            }
        }

        return new DesignRefusal(
            TestDesign.Text(refusal["input"]) ?? throw new FormatException("A refusal in a test design names its input."),
            args,
            [.. TestDesign.Array(refusal, "codes").Select(node => TestDesign.Text(node) ?? string.Empty)]);
    }

    /// <inheritdoc />
    public override string ToString() => DesignMove.Written(Input, Args);
}

/// <summary>One input a design says is legal in a state, and where it leads.</summary>
/// <param name="Input">The input's name.</param>
/// <param name="Args">What it was called with, by parameter name, in the text form the runtime writes.</param>
/// <param name="Actor">Whose move it is, where the rule set says.</param>
/// <param name="Open">The parameters it is still waiting for, where nobody could enumerate their values and nobody wrote one.</param>
/// <param name="Followed">Whether the walk followed it: false for a move in a state the rules call final, and for one still waiting.</param>
/// <param name="To">The state it settles, or null where the walk had no states left for it.</param>
/// <param name="Lands">Where it arrives, one entry per branch, where the rules draw.</param>
internal sealed record DesignMove(
    string Input,
    IReadOnlyDictionary<string, string> Args,
    string? Actor,
    IReadOnlyList<string> Open,
    bool Followed,
    string? To,
    IReadOnlyList<DesignLanding>? Lands)
{
    public static DesignMove Read(JsonObject move)
    {
        Dictionary<string, string> args = new(StringComparer.Ordinal);
        if (move["args"] is JsonObject given)
        {
            foreach ((string parameter, JsonNode? value) in given)
            {
                args[parameter] = TestDesign.Text(value) ?? string.Empty;
            }
        }

        IReadOnlyList<DesignLanding>? lands = move["lands"] is JsonArray branches
            ? [.. branches.Select(node => DesignLanding.Read(TestDesign.Object(node, "a landing")))]
            : null;

        return new DesignMove(
            TestDesign.Text(move["input"]) ?? throw new FormatException("A move in a test design names its input."),
            args,
            TestDesign.Text(move["actor"]),
            [.. TestDesign.Array(move, "open").Select(node => TestDesign.Text(node) ?? string.Empty)],
            move.ContainsKey("to") || lands is not null,
            TestDesign.Text(move["to"]),
            lands);
    }

    /// <summary>The move as a design writes it in <c>by</c>: <c>place(at: e6)</c>, and <c>setName(to: ?)</c> for one still waiting.</summary>
    public override string ToString() => Written(Input, Args, Open);

    internal static string Written(string input, IEnumerable<KeyValuePair<string, string>> args, IEnumerable<string>? open = null)
    {
        string list = string.Join(", ", args.Select(arg => $"{arg.Key}: {arg.Value}").Concat((open ?? []).Select(name => $"{name}: ?")));
        return list.Length == 0 ? input : $"{input}({list})";
    }
}

/// <summary>One branch of a draw.</summary>
/// <param name="Draws">What was drawn, in order, each rendered as the runtime renders it.</param>
/// <param name="To">The state it settles, or null where the walk had no states left for it.</param>
internal sealed record DesignLanding(IReadOnlyList<string> Draws, string? To)
{
    public static DesignLanding Read(JsonObject landing) => new(
        [.. TestDesign.Array(TestDesign.Object(landing["drew"], "'drew'"), "draws").Select(node => TestDesign.Text(node) ?? "null")],
        TestDesign.Text(landing["to"]));
}
