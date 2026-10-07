// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Reflection;
using RulealizeStudio.Binding;
using RulealizeStudio.Sample.Countdown;
using RulealizeStudio.Sample.Signup;

namespace RulealizeStudio.Tests;

/// <summary>The layer an application binds to, with no window in the way.</summary>
/// <remarks>
/// The models here are generated from the rule sets at build time, so every name a test uses is
/// one the document gave, checked by the compiler. What is asserted is where each answer came
/// from: a command enabled because <c>GetValidInputs</c> offers the move, a bound because the
/// schema declares it, an error on a property because the clause knew its parameter.
/// </remarks>
public class ModelTests
{
    [Fact]
    public void ACommandIsEnabledExactlyWhereTheRulesOfferTheMove()
    {
        SignupModel signup = Signup();

        Assert.True(signup.SetName.IsOffered);
        Assert.True(signup.SetName.Apply.CanExecute(null));

        // `book` waits for #ready, and nothing here says what that is.
        Assert.False(signup.Book.IsOffered);
        Assert.False(signup.Book.Apply.CanExecute(null));
    }

    [Fact]
    public void ASettledParameterIsAChoiceAmongWhatTheRuntimeOffered()
    {
        SignupModel signup = Named("alice");

        Assert.Equal(["window", "aisle"], signup.ChooseSeat.SeatOptions);

        // Two legal moves and no seat chosen: the command cannot say which it would apply.
        Assert.False(signup.ChooseSeat.Apply.CanExecute(null));

        signup.ChooseSeat.Seat = "aisle";
        Assert.True(signup.ChooseSeat.Apply.CanExecute(null));

        signup.ChooseSeat.Apply.Execute(null);
        Assert.Equal("aisle", signup.State.Seat);
    }

    [Fact]
    public void ACommandParameterPicksTheMoveAButtonStandsFor()
    {
        CountdownModel countdown = new(AppContext.BaseDirectory);

        countdown.Add.Apply.Execute("3");
        countdown.Add.Apply.Execute(3);
        countdown.Add.Apply.Execute("2");
        Assert.Equal(8, countdown.State.Total);

        // At eight the guard removes add(n: 3) — the domain still says three values, the rules
        // say two of them are allowed now.
        Assert.False(countdown.Add.Apply.CanExecute("3"));
        Assert.True(countdown.Add.Apply.CanExecute("2"));
        Assert.Equal(["1", "2"], countdown.Add.NOptions);
    }

    [Fact]
    public void AnOpenParameterIsBoundedByTheSchemaItIsOpenTo()
    {
        SignupModel signup = Signup();
        Assert.Equal(12, signup.SetName.ToLimits.MaxLength);

        signup = Named("alice");
        Assert.Equal(1m, signup.SetParty.SizeLimits.Minimum);
        Assert.Equal(6m, signup.SetParty.SizeLimits.Maximum);
        Assert.Equal(["quiet", "near the door"], signup.Note.WhatLimits.Choices);
    }

    [Fact]
    public void ARefusalIsAnErrorOnThePropertyItsParameterIsHeldIn()
    {
        SignupModel signup = Signup();
        List<string?> changed = [];
        signup.SetName.ErrorsChanged += (_, e) => changed.Add(e.PropertyName);

        signup.SetName.To = "admin";
        signup.SetName.Apply.Execute(null);

        // Said in the sentence signup's label document gives the clause; the code is the rules'.
        Assert.True(signup.SetName.HasErrors);
        Assert.Equal(["Nobody books under that name."], signup.SetName.GetErrors(nameof(signup.SetName.To)).Cast<string>());
        Assert.Equal(["name.reserved"], signup.SetName.RefusedWith);
        Assert.Contains(nameof(signup.SetName.To), changed);
        Assert.Equal(string.Empty, signup.State.Name);

        // Typing again is a new value, and what was said about the old one goes.
        signup.SetName.To = "alice";
        Assert.False(signup.SetName.HasErrors);
    }

    [Fact]
    public void AProjectionIsBoundByTheNamesTheRuleSetGaveIt()
    {
        SignupModel signup = Named("alice");

        Assert.Equal("alice", signup.Booking!.Who);
        Assert.Equal(1L, signup.Booking.Party.Size);
        Assert.Equal(false, signup.Booking.Ready);

        signup.ChooseSeat.Seat = "window";
        signup.ChooseSeat.Apply.Execute(null);

        // `ready` and the guard on `book` are one definition, so the answer and the command
        // cannot disagree.
        Assert.Equal(true, signup.Booking!.Ready);
        Assert.True(signup.Book.Apply.CanExecute(null));
    }

    [Fact]
    public void AMoveTellsEverybodyBoundToTheModel()
    {
        SignupModel signup = Signup();
        List<string?> said = [];
        List<string> commands = [];

        ((INotifyPropertyChanged)signup).PropertyChanged += (_, e) => said.Add(e.PropertyName);
        signup.Book.Apply.CanExecuteChanged += (_, _) => commands.Add("book");

        signup.SetName.To = "alice";
        signup.SetName.Apply.Execute(null);

        Assert.Contains(string.Empty, said);
        Assert.Contains("book", commands);

        // What was typed was for the position that is gone.
        Assert.Null(signup.SetName.To);
    }

    [Fact]
    public void AValueHeldByTheRuleSetsNameLandsInThePropertyTheScreenBinds()
    {
        SignupModel signup = Named("alice");

        // What a runner knows is the rule set: `setParty`, `size`, a number.
        signup.SetParty.Hold("size", 3);
        signup.ChooseSeat.Hold("seat", "aisle");

        Assert.Equal(3L, signup.SetParty.Size);
        Assert.Equal("aisle", signup.ChooseSeat.Seat);
        Assert.Throws<ArgumentException>(() => signup.SetParty.Hold("count", 3));
    }

    [Fact]
    public void GoingBackIsACommandAndSoIsGoingForward()
    {
        SignupModel signup = Named("alice");
        Assert.True(signup.Back.CanExecute(null));

        signup.Back.Execute(null);
        Assert.Equal(string.Empty, signup.State.Name);
        Assert.True(signup.Forward.CanExecute(null));

        signup.Forward.Execute(null);
        Assert.Equal("alice", signup.State.Name);
    }

    [Fact]
    public void AnInputThatResolvesSomethingNobodyChoseWaitsToBeToldWhichHappened()
    {
        DrawModel draw = new(AppContext.BaseDirectory);

        draw.Roll.Apply.Execute(null);

        Assert.True(draw.IsWaitingForOutcome);
        Assert.Equal(6, draw.Outcomes.Count);
        Assert.Equal(0, draw.State.Total);

        // The position is not settled, so nothing is legal from it yet.
        Assert.False(draw.Roll.Apply.CanExecute(null));
        Assert.False(draw.Back.CanExecute(null));

        OutcomeChoice chosen = draw.Outcomes[2];
        chosen.Choose.Execute(null);

        Assert.False(draw.IsWaitingForOutcome);
        Assert.Equal(long.Parse(chosen.Draws), draw.State.Total);
    }

    [Fact]
    public void AFinalPositionSaysSoAndOffersNothing()
    {
        CountdownModel countdown = new(AppContext.BaseDirectory);
        foreach (string n in new[] { "3", "3", "3", "1" })
        {
            countdown.Add.Apply.Execute(n);
        }

        Assert.True(countdown.IsTerminal);
        Assert.Equal("done", countdown.Ending);
        Assert.False(countdown.Add.IsOffered);
    }

    [Theory]
    [InlineData(typeof(SignupModel))]
    [InlineData(typeof(CountdownModel))]
    public void AnApplicationsOnlyCSharpIsItsEntryPoint(Type model)
    {
        // Everything else in the application's namespace was generated from its rule set.
        Assembly application = model.Assembly;
        string[] written = [.. application.GetTypes()
            .Where(type => type.Namespace == model.Namespace && !type.IsNested)
            .Where(type => type.GetCustomAttribute<System.CodeDom.Compiler.GeneratedCodeAttribute>() is null)
            .Select(type => type.Name)];

        Assert.Equal(["Program"], written);
    }

    [Fact]
    public void AnApplicationSpeaksInTheLabelDocumentsBesideItsRuleSet()
    {
        // Built in, from signup.labels.en.json, as the rule set is; countdown has none and says codes.
        Assert.Equal(["en"], SignupModel.LabelDocuments.Select(each => each.Key));
        Assert.Equal("en", Signup().Labels?.Language);
        Assert.Empty(CountdownModel.LabelDocuments);
        Assert.Null(new CountdownModel(AppContext.BaseDirectory).Labels);
    }

    private static SignupModel Signup() => new(AppContext.BaseDirectory);

    private static SignupModel Named(string name)
    {
        SignupModel signup = Signup();
        signup.SetName.To = name;
        signup.SetName.Apply.Execute(null);
        return signup;
    }
}
