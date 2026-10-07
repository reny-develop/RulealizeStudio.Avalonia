// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace RulealizeStudio.Server;

/// <summary>A name a screen binds, which the rule set behind it has to give.</summary>
/// <param name="Kind"><c>input</c>, <c>parameter</c>, <c>field</c> or <c>projection</c>: what the rule set has to give the name to.</param>
/// <param name="Name">
/// The name as the XAML spells it, with a dot between the parts it reaches through — <c>SetName.To</c>
/// is the parameter <c>To</c> of the input <c>SetName</c>, <c>Booking.Party.Size</c> a member of the
/// projection <c>Booking</c>, <c>Total</c> the field.
/// </param>
/// <param name="Start">Where the binding's text starts in the XAML, in UTF-16 code units.</param>
/// <param name="Length">How long the binding's text is.</param>
/// <remarks>
/// The XAML spells a name the way the generated model does, so <c>SetName</c> is what a rule set
/// calls <c>setName</c> or <c>set-name</c> alike. Which of those it is the XAML cannot say, and
/// <see cref="IsNamedBy"/> answers it the way the generator would.
/// </remarks>
public sealed record Ask(string Kind, string Name, int Start, int Length)
{
    /// <summary>Whether these names, as a rule set writes them, are the ones this binding reaches.</summary>
    /// <param name="names">The input and parameter, the field, or the projection and its members down to the one bound.</param>
    /// <returns>Whether the model generated from them has this name.</returns>
    public bool IsNamedBy(params IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        string[] parts = Name.Split('.');
        return parts.Length == names.Count && parts.Zip(names).All(pair => pair.First == Generator.Name.Pascal(pair.Second));
    }
}
