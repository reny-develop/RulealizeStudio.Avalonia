// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace RulealizeStudio.Hosting;

/// <summary>One place where a screen did not do what a test design says the rule set does.</summary>
/// <param name="State">The state the design names, as the design numbers it.</param>
/// <param name="Route">How the design first arrived there, from the state the rule set starts in.</param>
/// <param name="What">What differed, in the rule set's terms.</param>
/// <remarks>
/// A state's number is a name inside one design only; the route is the name that still means that
/// state after the rule set changes, so both are given.
/// </remarks>
public sealed record Divergence(string State, string Route, string What)
{
    /// <inheritdoc />
    public override string ToString() => $"{State} ({Route}): {What}";
}
