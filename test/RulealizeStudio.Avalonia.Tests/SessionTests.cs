// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json.Nodes;
using Rulealize;
using RulealizeStudio;
using RulealizeStudio.Binding;

namespace RulealizeStudio.Tests;

/// <summary>What the program knows about a rule set, with no screen in the way.</summary>
/// <remarks>
/// The core has no Avalonia in it on purpose: what is legal, what a move waits for, why one
/// was refused and where a draw could land are all answered here, and a test of them should
/// not have to open a window.
/// </remarks>
public class SessionTests
{
    [Fact]
    public void ARuleSetOpensAgainstTheFolderItsVocabulariesAreIn()
    {
        Session session = Signup();

        Assert.Equal("signup@1.0.0", session.RuleSet);
        Assert.Equal("signup.json", session.Source);

        // The plugins are ordinary package references, so the sweep is pointed at this
        // project's own output folder and finds them there.
        Assert.True(session.Plugins >= 6, $"only {session.Plugins} vocabularies were found");
        Assert.Empty(session.Skipped);
    }

    [Fact]
    public void AMoveWaitingForAValueIsOfferedWithTheHoleInIt()
    {
        ValidInput rename = Move(Signup(), "setName");

        Assert.False(rename.IsComplete);
        OpenParameter to = Assert.Single(rename.Open);

        // The field it is edited into, and the schema of that field — one declaration, read
        // by the editor and by the state check alike.
        Assert.Equal("name", to.Field);
        Assert.Equal("type.string", to.Op);
        Assert.Equal(12m, ((Rulealize.Abstraction.Value.NumberValue)to.Description["maxLength"]).Value);
    }

    [Fact]
    public void AValueTheRulesRefuseComesBackAsACodeAgainstItsField()
    {
        Session session = Signup();
        Applied said = session.Apply(Filled(session, "setName", "to", "admin"));

        Applied.Refused refused = Assert.IsType<Applied.Refused>(said);
        InputRejection rejection = Assert.Single(refused.Rejections);

        Assert.Equal("name.reserved", rejection.Code);
        Assert.Equal("to", rejection.Parameter);

        // And nothing moved: evaluation is pure until a transition commits.
        Assert.Equal(session.State, Signup().State);
        Assert.False(session.CanGoBack);
    }

    [Fact]
    public void AValueTheSchemaDoesNotAdmitIsRefusedUnderItsParametersInvalidOrInTheRuntimesWords()
    {
        // Thirteen letters where the field holds twelve. Named by invalid, it is a code against its
        // field like any clause's; not named, the runtime's sentence, beside the codes there are.
        Session named = Read(Text("signup.json").Replace(
            "\"to\": { \"open\": { \"field\": \"name\" } }",
            "\"to\": { \"open\": { \"field\": \"name\" }, \"invalid\": \"name.malformed\" }",
            StringComparison.Ordinal));
        Applied.Refused refused = Assert.IsType<Applied.Refused>(named.Apply(Filled(named, "setName", "to", "Christabellas")));

        InputRejection rejection = Assert.Single(refused.Rejections);
        Assert.Equal("name.malformed", rejection.Code);
        Assert.Equal("to", rejection.Parameter);
        Assert.Empty(refused.Unexplained);

        Session unnamed = Signup();
        Applied.Refused worded = Assert.IsType<Applied.Refused>(unnamed.Apply(Filled(unnamed, "setName", "to", "Christabellas")));

        Assert.Empty(worded.Rejections);
        Assert.Contains("12", worded.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueTheRulesTakeMovesThePosition()
    {
        Session session = Signup();

        Assert.IsType<Applied.Landed>(session.Apply(Filled(session, "setName", "to", "alice")));
        Assert.Contains("alice", session.State, StringComparison.Ordinal);

        // What is legal moved with it.
        Assert.Contains(session.Moves(), move => move.Input == "chooseSeat");
    }

    [Fact]
    public void GoingBackIsHoldingOnToThePositionBefore()
    {
        Session session = Signup();
        string opening = session.State;

        session.Apply(Filled(session, "setName", "to", "alice"));
        Assert.True(session.CanGoBack);

        session.Back();
        Assert.Equal(opening, session.State);
        Assert.True(session.CanGoForward);

        session.Forward();
        Assert.Contains("alice", session.State, StringComparison.Ordinal);
    }

    [Fact]
    public void MovingOnFromAPositionSteppedBackToDropsWhatDidNotHappen()
    {
        Session session = Signup();
        session.Apply(Filled(session, "setName", "to", "alice"));
        session.Back();

        session.Apply(Filled(session, "setName", "to", "bo"));

        Assert.Contains("bo", session.State, StringComparison.Ordinal);
        Assert.False(session.CanGoForward);
    }

    [Fact]
    public void AnInputThatResolvesSomethingNobodyChoseAsksWhichHappened()
    {
        Session session = Open("draw.json");
        ValidInput roll = Move(session, "roll");

        Applied said = session.Apply(roll.ToInputDocument(session.RuleSet));
        Applied.NeedsOutcome needs = Assert.IsType<Applied.NeedsOutcome>(said);

        // Six faces, and the position has not moved.
        Assert.Equal(6, needs.Outcomes.Count);
        Assert.Equal(1.0, needs.Outcomes.Coverage, 6);
        Assert.Contains("\"total\": 0", session.State, StringComparison.Ordinal);

        session.Choose(needs.Outcomes[0]);
        Assert.DoesNotContain("\"total\": 0", session.State, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuleSetThatLeavesNothingOpenIsAllButtons()
    {
        Session session = Open("countdown.json");
        ValidInputSet moves = session.Moves();

        Assert.False(moves.HasOpenParameters);
        Assert.All(moves, move => Assert.True(move.IsComplete));
    }

    [Fact]
    public void ARuleSetSaysWhatItMakesOfAPositionWhereItDeclaresOne()
    {
        Session session = Signup();

        Assert.Equal("booking", Assert.Single(session.Projections));

        JsonObject booking = Booking(session);
        Assert.Equal("empty", (string?)booking["stage"]);
        Assert.False((bool?)booking["ready"]);

        // A shape assembled out of computed parts comes back as that shape. Nothing was
        // reached for to build it: an object with no `op` in it is already a record.
        Assert.Equal(1, (int?)booking["party"]!["size"]);
        Assert.Null((string?)booking["party"]!["wants"]);
    }

    [Fact]
    public void WhatARuleSetSaysMovesWithThePosition()
    {
        Session session = Signup();
        session.Apply(Filled(session, "setName", "to", "alice"));
        session.Apply(Move(session, "chooseSeat").ToInputDocument(session.RuleSet));

        JsonObject booking = Booking(session);
        Assert.Equal("alice", (string?)booking["who"]);
        Assert.Equal("window", (string?)booking["seat"]);
        Assert.True((bool?)booking["ready"]);

        // `ready` and the guard on `book` are one definition, so a caller that believes the
        // answer and a caller that reads the moves cannot come to different conclusions.
        Assert.Contains(session.Moves(), move => move.Input == "book");
    }

    [Fact]
    public void ARuleSetWithNothingFurtherToSayDeclaresNoProjection()
    {
        Assert.Empty(Open("countdown.json").Projections);
    }

    internal static Session Signup() => Open("signup.json");

    private static string Text(string document) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ruleset", document));

    private static Session Read(string document) =>
        Session.Read(document, "signup.json", AppContext.BaseDirectory);

    internal static Session Open(string document) =>
        Session.Open(
            Path.Combine(AppContext.BaseDirectory, "ruleset", document),
            AppContext.BaseDirectory);

    internal static ValidInput Move(Session session, string input) =>
        session.Moves().First(move => move.Input == input);

    private static JsonObject Booking(Session session) =>
        (JsonObject)JsonNode.Parse(Assert.IsType<Said.Answer>(session.Project("booking")).Json)!;

    internal static string Filled(Session session, string input, string parameter, JsonNode? value) =>
        Move(session, input).ToInputDocument(
            session.RuleSet,
            new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { [parameter] = value });
}
