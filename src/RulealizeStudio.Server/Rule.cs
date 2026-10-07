// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace RulealizeStudio.Server;

/// <summary>A rule of a rule set, under the name an element of a specification is bound to it by.</summary>
/// <param name="Name">
/// Where it is in the document, as a pointer into it — <c>/state/schema/party</c>,
/// <c>/inputs/setParty/params/size</c>, <c>/terminal</c> — except that a <c>validate</c> clause is
/// named by its code, <c>/inputs/setParty/validate/party.unchanged</c>, and not by how many clauses
/// stand before it.
/// </param>
/// <param name="Start">Where it is written, in UTF-16 code units: the start of its value.</param>
/// <param name="Length">How long its value is, brackets included.</param>
/// <remarks>
/// Every part of the name is one the document gave and the runtime hands a host — a field, an
/// input and its parameter, a refusal's code, a definition, a projection — or a key of the rule set
/// format itself. None is a place in the text, which moves whenever the text is edited: a name
/// stays what it is while the rules around it are rewritten.
/// </remarks>
public sealed record Rule(string Name, int Start, int Length);
