// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace RulealizeStudio.Hosting;

/// <summary>A state a test design names, the moves that first reached it, and everything the design records there.</summary>
/// <param name="State">The state, as the design numbers it: a name inside that one design, to ask for it by.</param>
/// <param name="Route">
/// The moves that first reached it from where the rule set starts, each as the design writes it,
/// with what was drawn where the rules drew: <c>roll drawing 3</c>. Empty for where it starts.
/// </param>
/// <remarks>
/// <para>
/// The route is what says which situation this is to somebody reading it, and it still means the
/// same one after the rule set changes; the number is for asking <see cref="Replay"/> to stand the window in it.
/// </para>
/// <para>
/// The rest is the whole of what the design says about the state — whether it is an ending, what
/// is legal there and where each leads, what an open parameter admits, what was refused — so that
/// a review of the design leaves out nothing because it looked uninteresting.
/// </para>
/// </remarks>
public sealed record Situation(string State, IReadOnlyList<string> Route)
{
    /// <summary>Gets whether the rules call the state final.</summary>
    public bool IsEnding { get; init; }

    /// <summary>Gets what the rules call the ending, where the state is final and they say.</summary>
    public string? Result { get; init; }

    /// <summary>Gets every input legal in the state, in the order the runtime offered them, each with where it leads.</summary>
    public IReadOnlyList<SituationMove> Moves { get; init; } = [];

    /// <summary>Gets the values tried in the state and refused, each with what refused it.</summary>
    public IReadOnlyList<SituationRefusal> Refused { get; init; } = [];
}

/// <summary>Where one legal input leads from a situation: one entry per move, or per branch where the rules draw.</summary>
/// <param name="Step">The move as a route writes it: <c>setParty(size: 2)</c>, <c>roll drawing 3</c>, <c>setName(to: ?)</c> for one still waiting.</param>
/// <param name="Input">The input's name.</param>
/// <remarks>
/// <see cref="To"/> is null for three reasons, which <see cref="Followed"/> and <see cref="Admits"/>
/// tell apart: the walk had no states left for it, the state is final and the move was not
/// followed, or the move is waiting for a value nobody enumerated.
/// </remarks>
public sealed record SituationMove(string Step, string Input)
{
    /// <summary>Gets the state it leads to, by the design's name for it, or null where the walk did not go on.</summary>
    public string? To { get; init; }

    /// <summary>Gets whether the walk followed it: false in a final state, and for a move still waiting for a value.</summary>
    public bool Followed { get; init; }

    /// <summary>
    /// Gets what each parameter it waits for admitted when the design was derived, by parameter: the
    /// schema's op and each bound beside it, as text. Empty for a move that waits for nothing.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<KeyValuePair<string, string>>> Admits { get; init; } =
        new Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>>();
}

/// <summary>A value tried in a situation and refused.</summary>
/// <param name="Step">What was tried, as a route writes a move: <c>setName(to: admin)</c>.</param>
/// <param name="Input">The input's name.</param>
/// <param name="Codes">What refused it, in the order the clauses are written; none where no clause did.</param>
public sealed record SituationRefusal(string Step, string Input, IReadOnlyList<string> Codes);
