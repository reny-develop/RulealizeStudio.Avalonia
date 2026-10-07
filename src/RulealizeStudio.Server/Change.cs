// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace RulealizeStudio.Server;

/// <summary>Characters of a document replaced by others.</summary>
/// <param name="Start">Where the replaced characters start, in UTF-16 code units of the text before any change.</param>
/// <param name="Length">How many are replaced. Zero for an insertion.</param>
/// <param name="Text">What replaces them. Empty for a deletion.</param>
/// <remarks>
/// Several changes to one document are all placed in the text as it was before any of them, and
/// do not overlap — which is how the language server protocol applies the edits of one document.
/// </remarks>
public sealed record Change(int Start, int Length, string Text);
