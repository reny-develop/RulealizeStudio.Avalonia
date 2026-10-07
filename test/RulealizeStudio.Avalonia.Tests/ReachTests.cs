// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Rulealize;
using RulealizeStudio.Hosting;
using RulealizeStudio.Sample.Countdown;
using RulealizeStudio.Sample.Signup;

namespace RulealizeStudio.Tests;

/// <summary>Finding the control for a move on a screen nobody named for the purpose.</summary>
/// <remarks>
/// This is the part a runner replaying a test design needs: given a move in the
/// rule set's terms, the control to press, found by what its command is bound to. The walks here
/// know only the rule set's names — inputs, parameters, arguments — and never a control's.
/// </remarks>
public class ReachTests
{
    [AvaloniaFact]
    public void EveryLegalMoveOfTheSignupScreenHasAControlAllTheWayToTheEnd()
    {
        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        Walk(window, model, "setName", ("to", "alice"));
        Walk(window, model, "chooseSeat", ("seat", "window"));
        Walk(window, model, "setParty", ("size", 2));
        Walk(window, model, "note", ("what", "quiet"));
        Walk(window, model, "book");

        Assert.True(model.IsTerminal);
        Assert.Equal(2L, model.State.Party);
        Assert.Equal("quiet", model.State.Wants);
    }

    [AvaloniaFact]
    public void AButtonThatPassesTheArgumentStandsForThatMoveAlone()
    {
        CountdownModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        Assert.Empty(Reach.Unreachable(window, model));

        foreach (ValidInput move in model.Moves)
        {
            Assert.Single(Reach.For(window, model, move));
        }

        Walk(window, model, "add", ("n", "3"));
        Assert.Equal(3L, model.State.Total);
    }

    [AvaloniaFact]
    public void AMoveIsSaidInTheWordsOfTheControlThatStandsForIt()
    {
        SignupModel signup = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(signup);

        // The value typed or chosen beside the control is said after its words; a control shown
        // later is found all the same, since a move is said wherever it is listed.
        Assert.Equal("Save: alice", Reach.Say(window, signup, "setName(to: alice)"));
        Assert.Equal("Choose: window", Reach.Say(window, signup, "chooseSeat(seat: window)"));
        Assert.Equal("Book", Reach.Say(window, signup, "book"));
        Assert.Null(Reach.Say(window, signup, "nothing(at: all)"));

        CountdownModel countdown = new(AppContext.BaseDirectory);
        Window counting = ApplicationTests.Open(countdown);

        // A value the control passes itself is said by its words, and not again.
        Assert.Equal("+2", Reach.Say(counting, countdown, "add(n: 2)"));
        Assert.Null(Reach.Say(counting, countdown, "add(n: 4)"));
    }

    [AvaloniaFact]
    public void AScreenThatLeavesALegalMoveOutIsCaught()
    {
        // A screen written before the domain grew, say: two buttons where the rules offer three.
        CountdownModel model = new(AppContext.BaseDirectory);
        Window window = Screen(model, "1", "2");

        ValidInput missing = Assert.Single(Reach.Unreachable(window, model));
        Assert.Equal("3", missing.Arguments["n"]);
    }

    [AvaloniaFact]
    public void AHiddenControlDoesNotCount()
    {
        CountdownModel model = new(AppContext.BaseDirectory);
        Window window = Screen(model, "1", "2", "3");
        ((StackPanel)window.Content!).Children[2].IsVisible = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("3", Assert.Single(Reach.Unreachable(window, model)).Arguments["n"]);
    }

    [AvaloniaFact]
    public void WhileAnOutcomeIsOwedNothingIsReachableBecauseNothingIsLegal()
    {
        DrawModel model = new(AppContext.BaseDirectory);
        model.Roll.Apply.Execute(null);

        Assert.Empty(model.Moves);
        Assert.Empty(Reach.Unreachable(new Window(), model));
    }

    /// <summary>Takes one move the way a runner would: values held by parameter name, the control found by its command.</summary>
    private static void Walk(Window window, Binding.RuleModel model, string input, params (string Parameter, JsonNode? Value)[] values)
    {
        Assert.Empty(Reach.Unreachable(window, model));

        Binding.RuleInput held = model.Inputs.Single(each => each.Input == input);
        foreach ((string parameter, JsonNode? value) in values)
        {
            held.Hold(parameter, value);
        }

        // The move the design names: its settled arguments are the values given.
        ValidInput move = model.Moves.First(each => each.Input == input
            && values.All(value => each.Open.ContainsKey(value.Parameter)
                || each.Arguments[value.Parameter] == value.Value?.ToString()));

        Control control = Reach.For(window, model, move).First(each => each.IsEffectivelyEnabled);
        ApplicationTests.Press(window, control);

        Assert.False(held.HasErrors, $"{input} was refused");
        Assert.Null(held.Refusal);
    }

    /// <summary>A countdown screen with one button per argument given, built without XAML.</summary>
    private static Window Screen(CountdownModel model, params string[] arguments)
    {
        StackPanel buttons = new();
        foreach (string n in arguments)
        {
            buttons.Children.Add(new Button { Content = "+" + n, Command = model.Add.Apply, CommandParameter = n });
        }

        Window window = new() { Content = buttons, DataContext = model };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
