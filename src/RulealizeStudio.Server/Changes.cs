// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json.Nodes;
using Rulealize;
using RulealizeStudio.Binding;
using RulealizeStudio.Hosting;
using Ruledger;

namespace RulealizeStudio.Server;

/// <summary>What a change to the rules did to what the test design says, as Ruledger found it, said by the routes that reach each situation.</summary>
/// <remarks>
/// <para>
/// The account is Ruledger's diff: the design committed beside the rule set applied to the rules as
/// they are saved now, asked for as values. Nothing here compares two designs. What is added is how
/// each situation it names is said to a person — by the moves that reach it from where the
/// application starts, which mean the same situation in both designs, and never by a number, which
/// names a state inside one of them only — and what a refusal is said as, in the label document.
/// </para>
/// <para>
/// Each situation still carries the number each design gives it, which is what the window is stood
/// in by: the design before, on the application as it was before; the design the rules give now,
/// <see cref="TestDesignDiff.After"/>, which carries the choices of the one before, on the
/// application as built now.
/// </para>
/// </remarks>
public static class Changes
{
    /// <summary>The test design a rule set as it was gives: walked again, with the settings and the choices of the design written for it where there is one.</summary>
    /// <param name="runtime">A runtime with the vocabularies the rule set draws on loaded.</param>
    /// <param name="rules">The rule set as it was — as last committed — as text.</param>
    /// <param name="design">The test design committed beside it, as text, or null where none was.</param>
    /// <returns>The design, as text.</returns>
    /// <remarks>
    /// The design before a change is derived from the rules before it rather than taken as it was
    /// committed, so that rules committed without the design derived again are still read as what
    /// they were. What is taken from the committed design is what the rules do not hold: how far the
    /// walk went, and the values somebody chose to try, which Ruledger carries.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The text is not a test design, or the rule set holds others, which are not supplied here.</exception>
    /// <exception cref="System.Text.Json.JsonException">Either text is not JSON.</exception>
    /// <exception cref="Rulealize.Abstraction.RuleSetBuildException">The rules do not compile against the vocabularies.</exception>
    public static string Before(RuleRuntime runtime, string rules, string? design)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(rules);

        return design is null ? TestDesign.Derive(runtime, rules).ToJson() : TestDesignDiff.Of(runtime, design, rules).After.ToJson();
    }

    /// <summary>Applies a test design to the rules as they are now, and says what that changed as JSON.</summary>
    /// <param name="runtime">A runtime with the vocabularies the rule set draws on loaded.</param>
    /// <param name="design">The test design before the change, as text: the one committed beside the rule set, or the one <see cref="Before"/> gives.</param>
    /// <param name="rules">The rule set as it is now, as text.</param>
    /// <param name="labels">The label document a refusal is said in, or null where there is none.</param>
    /// <returns>What changed, and the design the rules give now.</returns>
    /// <exception cref="InvalidOperationException">The text is not a test design, or the rule set holds others, which are not supplied here.</exception>
    /// <exception cref="System.Text.Json.JsonException">Either text is not JSON.</exception>
    /// <exception cref="Rulealize.Abstraction.RuleSetBuildException">The rules do not compile against the vocabularies.</exception>
    public static (JsonObject Changed, string After) Of(RuleRuntime runtime, string design, string rules, Labels? labels)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(rules);

        TestDesignDiff diff = TestDesignDiff.Of(runtime, design, rules);
        string after = diff.After.ToJson();
        Routes was = new(Replay.Situations(design));
        Routes now = new(Replay.Situations(after));

        JsonObject Situation(Routes routes, string state) => new()
        {
            ["route"] = routes.Of(state),
            ["state"] = state,
        };

        JsonArray Landed(Routes routes, Move move) => [.. move.Landings.Select(landing => new JsonObject
        {
            ["drew"] = landing.Draw is null ? null : landing.Drew,
            ["to"] = landing.To is null ? null : routes.Of(landing.To),
        })];

        JsonArray Refusals(IEnumerable<Refusal> refusals) => [.. refusals.Select(refusal => new JsonObject
        {
            ["step"] = refusal.Text,
            ["codes"] = new JsonArray([.. refusal.Codes.Select(code => JsonValue.Create(code))]),
            ["said"] = new JsonArray([.. refusal.Codes.Select(code => JsonValue.Create(Labels.Say(labels, refusal.Input, code)))]),
        })];

        JsonObject Ending(TestDesignState state) => new()
        {
            ["ending"] = state.IsTerminal,
            ["result"] = state.Result,
        };

        JsonObject changed = new()
        {
            ["comparedTheSameWay"] = diff.ComparedTheSameWay,
            ["changed"] = new JsonArray([.. diff.Changed.Select(change => new JsonObject
            {
                ["route"] = was.Of(change.Before.Name),
                ["before"] = change.Before.Name,
                ["after"] = change.After.Name,
                ["gained"] = new JsonArray([.. change.Gained.Select(move => new JsonObject { ["step"] = move.Text, ["to"] = Landed(now, move) })]),
                ["lost"] = new JsonArray([.. change.Lost.Select(move => new JsonObject { ["step"] = move.Text, ["to"] = Landed(was, move) })]),
                ["moved"] = new JsonArray([.. change.Moved.Select(moved => new JsonObject
                {
                    ["step"] = moved.After.Text,
                    ["was"] = Landed(was, moved.Before),
                    ["now"] = Landed(now, moved.After),
                })]),
                ["ending"] = change.EndingMoved ? new JsonObject { ["was"] = Ending(change.Before), ["now"] = Ending(change.After) } : null,
                ["refusing"] = Refusals(change.Refusing),
                ["notRefusing"] = Refusals(change.NotRefusing),
                ["truncated"] = change.TruncationMoved ? change.After.Truncated : null,
            })]),
            ["gone"] = new JsonArray([.. diff.Gone.Select(state => Situation(was, state.Name))]),
            ["appeared"] = new JsonArray([.. diff.Appeared.Select(state => Situation(now, state.Name))]),
            ["admits"] = new JsonArray([.. diff.AdmitsMoved.Select(pair => new JsonObject
            {
                ["input"] = pair.After.Input,
                ["parameter"] = pair.After.Parameter,
                ["was"] = Bounds(pair.Before.Schema),
                ["now"] = Bounds(pair.After.Schema),
            })]),
            ["choices"] = new JsonArray([.. diff.After.Edits
                .Where(edit => edit.Outcome != EditOutcome.Carried)
                .Select(edit => new JsonObject
                {
                    ["step"] = edit.Edit.Arguments.Count == 0
                        ? edit.Edit.Input
                        : $"{edit.Edit.Input}({string.Join(", ", edit.Edit.Arguments.Select(argument => $"{argument.Key}: {argument.Value}"))})",
                    ["from"] = edit.Name ?? edit.Edit.State,
                    ["carried"] = edit.Outcome == EditOutcome.NotLegal ? "not legal here" : "not reached",
                })]),
        };

        return (changed, after);
    }

    // The schema of what a parameter admits, as Ruledger writes it, said as the listing says it: each
    // bound by its name, a value as itself.
    private static JsonArray Bounds(string schema) =>
        JsonNode.Parse(schema) is JsonObject bounds
            ? [.. bounds.Select(bound => new JsonObject
            {
                ["bound"] = bound.Key,
                ["value"] = bound.Value is JsonValue value && value.TryGetValue(out string? text) ? text : bound.Value?.ToJsonString(),
            })]
            : [];

    /// <summary>The moves that reach each state of one design, by its name in that design.</summary>
    private sealed class Routes(IReadOnlyList<Situation> situations)
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _routes =
            situations.ToDictionary(situation => situation.State, situation => situation.Route, StringComparer.Ordinal);

        public JsonArray Of(string state) =>
            [.. (_routes.TryGetValue(state, out IReadOnlyList<string>? route) ? route : []).Select(step => JsonValue.Create(step))];
    }
}
