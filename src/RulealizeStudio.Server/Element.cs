// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;

namespace RulealizeStudio.Server;

/// <summary>One element of a specification: what it says, and the rules it is bound to.</summary>
/// <param name="Id">The id it is bound and referred to by, which stays put while what it says is rewritten.</param>
/// <param name="Kind">What it is in its format — in a state machine, <c>state</c>, <c>transition</c> or <c>note</c>.</param>
/// <param name="Title">What it is called where it is drawn — a state's name, what a transition is triggered by; empty where it has none.</param>
/// <param name="Says">What it says the application does, as a sentence.</param>
/// <param name="Rules">The rules that carry it out, each by <see cref="Rule.Name"/> and where the specification writes it; none for a requirement nothing implements yet.</param>
/// <param name="Start">Where it is written in the specification, in UTF-16 code units.</param>
/// <param name="Length">How long it is.</param>
/// <remarks>
/// Nothing reads meaning out of an element: what it says is the person's and the agent's to read,
/// and whether its rules do it is the test design's to show. It is words and names.
/// </remarks>
public sealed record Element(string Id, string Kind, string Title, string Says, ImmutableArray<Reference> Rules, int Start, int Length);
