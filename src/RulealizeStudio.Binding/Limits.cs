// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize;
using Rulealize.Abstraction.Value;

namespace RulealizeStudio.Binding;

/// <summary>What an open parameter may hold, as the schema it is open to declares.</summary>
/// <param name="Minimum">The least number it may be.</param>
/// <param name="Maximum">The greatest number it may be.</param>
/// <param name="MaxLength">The longest text it may be, or zero where the schema sets no limit.</param>
/// <param name="Choices">The values it may be, where the schema enumerates them.</param>
/// <remarks>
/// <para>
/// Read from <see cref="OpenParameter.Description"/>, so a bound is stated once, in the rule
/// set, and a screen binds <c>MaxLength</c> or <c>Maximum</c> to it rather than writing the number
/// again. The defaults are the ones a control has when nothing is set, so binding an absent
/// bound changes nothing.
/// </para>
/// <para>
/// This is the half of a refusal that can be settled before asking. Whether a value is one the
/// rules admit is still decided when the input is applied.
/// </para>
/// </remarks>
public sealed record Limits(decimal Minimum, decimal Maximum, int MaxLength, IReadOnlyList<string> Choices)
{
    /// <summary>Gets the bounds of a parameter nobody is asking for: none.</summary>
    public static Limits None { get; } = new(decimal.MinValue, decimal.MaxValue, 0, []);

    /// <summary>Reads the bounds an open parameter's schema declares.</summary>
    /// <param name="parameter">The parameter, as the runtime offered it.</param>
    /// <returns>The bounds.</returns>
    public static Limits Of(OpenParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        RecordValue bounds = parameter.Description;

        return new Limits(
            Number(bounds["min"]) ?? decimal.MinValue,
            Number(bounds["max"]) ?? decimal.MaxValue,
            Number(bounds["maxLength"]) is decimal length ? (int)length : 0,
            bounds["values"] is SequenceValue values
                ? [.. values.Select(value => value.GetCanonicalText() ?? string.Empty)]
                : []);
    }

    private static decimal? Number(RuleValue value) => value is NumberValue number ? number.Value : null;
}
