// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Windows.Input;
using Rulealize;

namespace RulealizeStudio.Binding;

/// <summary>One thing that could have happened, for an input that resolves something nobody chose.</summary>
/// <remarks>
/// <c>GetOutcomes</c> lists them with their odds and leaves the pick to the caller. This layer
/// leaves it too: somebody says which happened, by the command here.
/// </remarks>
public sealed class OutcomeChoice
{
    internal OutcomeChoice(RuleModel model, Outcome outcome)
    {
        Probability = outcome.Probability;
        Draws = string.Join(", ", outcome.Draws);
        Choose = new RuleCommand(_ => true, _ => model.Choose(outcome));
    }

    /// <summary>Gets how likely this outcome is, among the ones the input can have.</summary>
    public double Probability { get; }

    /// <summary>Gets what was drawn, in order, rendered as text.</summary>
    public string Draws { get; }

    /// <summary>Gets the command that says this is what happened.</summary>
    public ICommand Choose { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Probability:P1}   {Draws}";
}
