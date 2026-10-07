// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;

namespace RulealizeStudio.Server;

/// <summary>A list a rule set writes, and what is in it.</summary>
/// <param name="Pointer">Where it is, as the runtime names a place in a document — <c>/inputs/setParty/validate</c>.</param>
/// <param name="Name">The same place in the names the document gave it, with the operation each part is.</param>
/// <param name="Start">Where its opening bracket is, in UTF-16 code units.</param>
/// <param name="Length">How long it is, closing bracket included.</param>
/// <param name="Entries">What is in it, in the order written.</param>
/// <remarks>
/// An array is a list, whatever it is a list of — a domain's values, an enumeration's, the
/// clauses of a <c>validate</c>, the operands of an operation. Which entries a list may lose or
/// gain is the runtime's to say when the document is compiled, and not something decided here.
/// </remarks>
public sealed record Listing(string Pointer, string Name, int Start, int Length, ImmutableArray<Entry> Entries);

/// <summary>One entry of a list, as written.</summary>
/// <param name="Pointer">Where it is — <c>/inputs/setParty/validate/0</c>.</param>
/// <param name="Name">The same place in the document's names — <c>inputs › setParty › validate 1</c>.</param>
/// <param name="Text">The entry as written, comments and layout inside it included.</param>
/// <param name="Start">Where it starts in the text, in UTF-16 code units.</param>
/// <param name="Length">How long it is, in UTF-16 code units.</param>
public sealed record Entry(string Pointer, string Name, string Text, int Start, int Length);
