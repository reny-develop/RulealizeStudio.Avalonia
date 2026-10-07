// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace RulealizeStudio.Server;

/// <summary>A rule as a specification names it, and where the name is written in the specification.</summary>
/// <param name="Rule">The rule, by <see cref="Server.Rule.Name"/>.</param>
/// <param name="Start">Where the name is written, quotes included, in UTF-16 code units.</param>
/// <param name="Length">How long it is.</param>
public sealed record Reference(string Rule, int Start, int Length);
