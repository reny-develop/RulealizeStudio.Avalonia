// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;

namespace RulealizeStudio.Server;

/// <summary>Where a specification and its rules disagree, as marked on each of them.</summary>
/// <param name="Specification">What is marked on the specification, at the places in its text.</param>
/// <param name="Rules">What is marked on the rule set, at the places in its text.</param>
public sealed record Disagreements(ImmutableArray<Finding> Specification, ImmutableArray<Finding> Rules);
