// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace RulealizeStudio.Server;

/// <summary>Something wrong with a document, where it is.</summary>
/// <param name="Start">Where it starts in the text, in UTF-16 code units.</param>
/// <param name="Length">How long it is. Zero where only a point can be named.</param>
/// <param name="Message">What is wrong, as whoever found it said it.</param>
public sealed record Finding(int Start, int Length, string Message);
