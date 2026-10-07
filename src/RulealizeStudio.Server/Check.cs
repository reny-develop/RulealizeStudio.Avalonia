// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using Rulealize;
using Rulealize.Abstraction;

namespace RulealizeStudio.Server;

/// <summary>Whether a document compiles, asked of the runtime.</summary>
/// <remarks>
/// The question <c>rulealize check</c> asks, asked after every edit and answered where the
/// runtime says the fault is. Nothing is decided here: an unknown operation, a missing key and a
/// reference to nothing are all the runtime's refusals, and this only turns the place it named
/// into a place in the text.
/// </remarks>
public static class Check
{
    /// <summary>Compiles a document against a runtime.</summary>
    /// <param name="text">The document.</param>
    /// <param name="runtime">The vocabularies to compile it against.</param>
    /// <returns>What is wrong; empty when it compiles.</returns>
    public static ImmutableArray<Finding> Run(string text, RuleRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(runtime);

        DocumentMap map;
        try
        {
            map = DocumentMap.Read(text);
        }
        catch (JsonException wrong)
        {
            return [At(text, wrong)];
        }

        try
        {
            runtime.CreateContext(text);
            return [];
        }
        catch (RuleSetBuildException wrong)
        {
            (int start, int length) = map.Locate(wrong.Path.ToString());
            return [new Finding(start, length, wrong.Detail)];
        }
    }

    /// <summary>Where the JSON stopped being JSON, which the reader says as a line and a byte.</summary>
    private static Finding At(string text, JsonException wrong)
    {
        int offset = 0;
        for (long line = 0; line < (wrong.LineNumber ?? 0) && offset < text.Length; line++)
        {
            int next = text.IndexOf('\n', offset);
            offset = next < 0 ? text.Length : next + 1;
        }

        offset = Math.Min(text.Length, offset + (int)(wrong.BytePositionInLine ?? 0));
        return new Finding(offset, 0, wrong.Message);
    }
}
