// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RulealizeStudio.Server;

/// <summary>An application's behaviour specification, in a format the Studio owns.</summary>
/// <remarks>
/// <para>
/// An application has as many as it needs, each a JSON file in its folder whose <c>$schema</c> says
/// it is one: the first <c>specification.json</c>, written before there are any rules and so named
/// after none. Each is made of elements, and each element holds what it says and the rules it is
/// bound to, so nothing else binds it: what is shown of it — a diagram, sentences, a table — is drawn
/// from this one file, and an edit made in any of them is an edit to it.
/// </para>
/// <para>
/// Which format it is in is its <c>$schema</c>'s to say. There is one, <see cref="StateMachine"/>; a
/// second is added here the same way, holding its own bindings in its own elements.
/// </para>
/// </remarks>
public static class Specification
{
    /// <summary>What an application's first specification is called in its folder.</summary>
    public const string File = "specification.json";

    /// <summary>Whether a text says it is a specification in a format the Studio knows.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> where its <c>$schema</c> names a format the Studio knows; <see langword="false"/> otherwise, or where it is not JSON.</returns>
    public static bool Is(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            return DocumentMap.Read(text).Schema == StateMachine.Schema;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads a specification's elements.</summary>
    /// <param name="text">The specification.</param>
    /// <returns>Its elements, in the order written; nothing where the text is not in a format the Studio knows.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static ImmutableArray<Element> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return DocumentMap.Read(text).Schema == StateMachine.Schema ? StateMachine.Read(text).Elements : [];
    }

    /// <summary>What in a specification refers to something it does not have, said where it is written.</summary>
    /// <param name="text">The specification.</param>
    /// <returns>Each such place; nothing where the text is not in a format the Studio knows.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static ImmutableArray<Finding> Faults(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return DocumentMap.Read(text).Schema == StateMachine.Schema ? StateMachine.Read(text).Faults : [];
    }

    /// <summary>Makes an edit to a specification, in its format.</summary>
    /// <param name="text">The specification as it is.</param>
    /// <param name="edit">The edit, as the format says one; <see cref="StateMachine.Edit"/> for a state machine.</param>
    /// <returns>The whole of the specification after it, and the id of an element it added.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    /// <exception cref="FormatException">The text is not in a format the Studio knows, or the edit is not one.</exception>
    public static (string Text, string? Added) Edit(string text, JsonObject edit)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edit);

        return DocumentMap.Read(text).Schema == StateMachine.Schema
            ? StateMachine.Edit(text, edit)
            : throw new FormatException("The text is not a specification in a format the Studio knows.");
    }
}
