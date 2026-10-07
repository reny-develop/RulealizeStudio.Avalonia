// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json.Nodes;
using RulealizeStudio.Server;

namespace RulealizeStudio.Tests;

/// <summary>A specification whose every element is bound to its rules by names that hold while either is rewritten.</summary>
/// <remarks>
/// What is held here is that a rule has a name that is not a place in the text; that each sample's
/// specification binds every rule it has and nothing it does not; that where they disagree is said
/// on both; and that an edit to a specification is written in the one layout the format has.
/// </remarks>
public class SpecificationTests
{
    [Fact]
    public void EveryRuleOfARuleSetIsNamedByTheNamesTheDocumentGaveIt()
    {
        ImmutableArray<Rule> rules = Rules.Read(Text("signup.json"));

        Assert.Equal(
            [
                "/state/schema/stage", "/state/schema/name", "/state/schema/party", "/state/schema/seat", "/state/schema/wants",
                "/definitions/ready",
                "/inputs/setName", "/inputs/setName/params/to", "/inputs/setName/when",
                "/inputs/setName/validate/name.unchanged", "/inputs/setName/validate/name.reserved", "/inputs/setName/effects",
                "/inputs/chooseSeat", "/inputs/chooseSeat/params/seat", "/inputs/chooseSeat/when", "/inputs/chooseSeat/effects",
                "/inputs/setParty", "/inputs/setParty/params/size", "/inputs/setParty/when",
                "/inputs/setParty/validate/party.unchanged", "/inputs/setParty/effects",
                "/inputs/note", "/inputs/note/params/what", "/inputs/note/when",
                "/inputs/note/validate/wants.tooManyForQuiet", "/inputs/note/effects",
                "/inputs/book", "/inputs/book/when", "/inputs/book/effects",
                "/projections/booking",
                "/terminal",
            ],
            rules.Select(r => r.Name));
    }

    [Fact]
    public void ARuleIsWhereItsValueIsWritten()
    {
        string text = Text("signup.json");
        ImmutableArray<Rule> rules = Rules.Read(text);

        Rule clause = Assert.Single(rules, r => r.Name == "/inputs/setParty/validate/party.unchanged");
        Assert.StartsWith("{ \"require\"", text.Substring(clause.Start, clause.Length), StringComparison.Ordinal);
        Assert.EndsWith("\"party.unchanged\" }", text.Substring(clause.Start, clause.Length), StringComparison.Ordinal);

        Rule book = Assert.Single(rules, r => r.Name == "/inputs/book/when");
        Assert.Equal("\"#ready\"", text.Substring(book.Start, book.Length));
    }

    [Fact]
    public void AParametersInvalidIsARefusalNamedByItsCodeAndWrittenWhereItIs()
    {
        string text = Text("signup.json").Replace(
            "\"to\": { \"open\": { \"field\": \"name\" } }",
            "\"to\": { \"open\": { \"field\": \"name\" }, \"invalid\": \"name.malformed\" }",
            StringComparison.Ordinal);
        ImmutableArray<Rule> rules = Rules.Read(text);

        // One set of codes with the clauses, so it is named as they are, and found where it is written.
        Rule invalid = Assert.Single(rules, r => r.Name == "/inputs/setName/validate/name.malformed");
        Assert.Equal("\"name.malformed\"", text.Substring(invalid.Start, invalid.Length));
        Assert.Contains(rules, r => r.Name == "/inputs/setName/params/to");
    }

    [Fact]
    public void AClauseKeepsItsNameWhenAnotherIsWrittenBeforeIt()
    {
        string text = Text("signup.json");
        Change first = DocumentMap.Read(text).Insert(
            "/inputs/setParty/validate", 0, """{ "require": { "op": "cmp.lte", "left": "@size", "right": 6 }, "code": "party.tooMany" }""");
        string changed = text[..first.Start] + first.Text + text[first.Start..];

        string[] names = [.. Rules.Read(changed).Select(r => r.Name)];

        Assert.Contains("/inputs/setParty/validate/party.tooMany", names);
        Assert.Contains("/inputs/setParty/validate/party.unchanged", names);
    }

    [Theory]
    [InlineData("RulealizeStudio.Sample.Signup", "signup")]
    [InlineData("RulealizeStudio.Sample.Countdown", "countdown")]
    public void ASamplesSpecificationBindsEveryRuleItHasAndNoneItLacks(string application, string sample)
    {
        string[] rules = [.. Rules.Read(Text($"{sample}.json")).Select(r => r.Name)];
        ImmutableArray<Element> elements = Specification.Read(SpecificationOf(application));

        Assert.All(elements, e => Assert.NotEmpty(e.Rules));
        Assert.All(elements, e => Assert.NotEmpty(e.Says));
        Assert.Empty(rules.Except(elements.SelectMany(e => e.Rules.Select(r => r.Rule))));
        Assert.Empty(elements.SelectMany(e => e.Rules.Select(r => r.Rule)).Except(rules));
    }

    [Theory]
    [InlineData("RulealizeStudio.Sample.Signup", "signup")]
    [InlineData("RulealizeStudio.Sample.Countdown", "countdown")]
    public void ASamplesSpecificationAndRulesAgree(string application, string sample)
    {
        Disagreements found = Agreement.Compare(SpecificationOf(application), Text($"{sample}.json"));

        Assert.Empty(found.Specification);
        Assert.Empty(found.Rules);
    }

    [Fact]
    public void SignupIsAStateMachineOfThreeStates()
    {
        Machine machine = StateMachine.Read(SpecificationOf("RulealizeStudio.Sample.Signup"));

        Assert.Equal("unnamed", machine.Initial);
        Assert.Equal(["unnamed", "named", "booked"], machine.Elements.Where(e => e.Kind == "state").Select(e => e.Id));
        Assert.Equal(6, machine.Elements.Count(e => e.Kind == "transition"));
        Assert.Equal(6, machine.Elements.Count(e => e.Kind == "note"));
        Assert.True(machine.Shapes["booked"].Final);
        Assert.Equal(new Shape("named", "booked", null, "a name and a seat", false), machine.Shapes["ready"]);
        Assert.Equal("rename", machine.Shapes["name-unchanged"].On);
        Assert.Null(machine.Shapes["summary"].On);
        Assert.Empty(machine.Faults);
    }

    [Fact]
    public void ABindingIsFoundWhereItIsWrittenInTheSpecification()
    {
        string text = SpecificationOf("RulealizeStudio.Sample.Countdown");

        Element done = Assert.Single(Specification.Read(text), e => e.Id == "done");

        Reference terminal = Assert.Single(done.Rules);
        Assert.Equal("/terminal", terminal.Rule);
        Assert.Equal("\"/terminal\"", text.Substring(terminal.Start, terminal.Length));
        Assert.StartsWith("{", text.Substring(done.Start, done.Length), StringComparison.Ordinal);
        Assert.Contains("\"Ten\"", text.Substring(done.Start, done.Length), StringComparison.Ordinal);
        Assert.Equal("Ten", done.Title);
        Assert.Equal("state", done.Kind);
    }

    [Fact]
    public void JsonThatDoesNotSayItIsASpecificationHasNoElements()
    {
        Assert.Empty(Specification.Read(Text("signup.json")));
        Assert.False(Specification.Is(Text("signup.json")));
        Assert.Empty(Specification.Read("""{ "states": { "a": { "says": "b", "rules": ["/terminal"] } } }"""));
        Assert.False(Specification.Is("{ not json"));
    }

    [Fact]
    public void AnElementTakenOutMarksTheRulesOnlyItWasBoundTo()
    {
        string rules = Text("signup.json");
        string specification = Edited(SpecificationOf("RulealizeStudio.Sample.Signup"), new() { ["op"] = "remove", ["id"] = "party-unchanged" });
        specification = Edited(specification, new() { ["op"] = "remove", ["id"] = "name-length" });

        Disagreements found = Agreement.Compare(specification, rules);

        // name-length's one rule, the field, is asked for by name-first too.
        Finding clause = Assert.Single(found.Rules);
        Assert.StartsWith("{ \"require\": { \"op\": \"logic.not\", \"value\": { \"op\": \"cmp.eq\", \"left\": \"@size\"", rules[clause.Start..], StringComparison.Ordinal);
        Assert.Equal("Nothing in the specification asks for '/inputs/setParty/validate/party.unchanged'.", clause.Message);
        Assert.Equal(clause.Message, Assert.Single(found.Specification).Message);
    }

    [Fact]
    public void AClauseNoElementIsBoundToIsMarked()
    {
        string text = Text("signup.json");
        Change first = DocumentMap.Read(text).Insert(
            "/inputs/setParty/validate", 0, """{ "require": { "op": "cmp.lte", "left": "@size", "right": 4 }, "code": "party.tooMany" }""");
        string rules = text[..first.Start] + first.Text + text[first.Start..];

        Disagreements found = Agreement.Compare(SpecificationOf("RulealizeStudio.Sample.Signup"), rules);

        Finding clause = Assert.Single(found.Rules);
        Assert.Equal(first.Start, clause.Start);
        Assert.Equal("Nothing in the specification asks for '/inputs/setParty/validate/party.tooMany'.", clause.Message);
        Assert.Equal(clause.Message, Assert.Single(found.Specification).Message);
    }

    [Fact]
    public void ARuleABindingNamesAndTheRulesLackIsMarkedOnBoth()
    {
        string specification = SpecificationOf("RulealizeStudio.Sample.Countdown")
            .Replace("\"/terminal\"", "\"/inputs/add/validate/n.small\"", StringComparison.Ordinal);
        string rules = Text("countdown.json");

        Disagreements found = Agreement.Compare(specification, rules);

        Finding named = Assert.Single(found.Specification, f => f.Message.StartsWith("'/inputs/add/validate/n.small'", StringComparison.Ordinal));
        Assert.Equal("\"/inputs/add/validate/n.small\"", specification.Substring(named.Start, named.Length));

        // Where it would be: the input it names, since the input has no clauses to be among.
        Finding missing = Assert.Single(found.Rules, f => f.Message.Contains("n.small", StringComparison.Ordinal));
        Assert.Equal("state 'done' (Ten) is bound to '/inputs/add/validate/n.small', which these rules do not have.", missing.Message);
        Assert.Equal(DocumentMap.Read(rules).Locate("/inputs/add").Start, missing.Start);

        // And the ending it was bound to is now asked for by nothing.
        Assert.Contains(found.Rules, f => f.Message == "Nothing in the specification asks for '/terminal'.");
    }

    [Fact]
    public void SpecificationsAndRuleSetsOfOneApplicationAgreeWhereEachBindingNamesItsRuleSet()
    {
        Written[] specifications =
        [
            new("countdown.specification.json", Named(SpecificationOf("RulealizeStudio.Sample.Countdown"), "countdown")),
            new("specification.json", Named(SpecificationOf("RulealizeStudio.Sample.Signup"), "signup")),
        ];
        Written[] ruleSets = [new("countdown.json", Text("countdown.json")), new("signup.json", Text("signup.json"))];

        IReadOnlyDictionary<string, ImmutableArray<Finding>> found = Agreement.Compare(specifications, ruleSets);

        Assert.Equal(4, found.Count);
        Assert.All(found.Values, marked => Assert.Empty(marked));
    }

    [Fact]
    public void ABindingThatNamesNoRuleSetWhereThereAreTwoIsMarked()
    {
        string specification = SpecificationOf("RulealizeStudio.Sample.Countdown");
        Written[] ruleSets = [new("countdown.json", Text("countdown.json")), new("signup.json", Text("signup.json"))];

        IReadOnlyDictionary<string, ImmutableArray<Finding>> found = Agreement.Compare([new("specification.json", specification)], ruleSets);

        Finding terminal = Assert.Single(found["specification.json"], f => f.Message.StartsWith("'/terminal'", StringComparison.Ordinal));
        Assert.Equal("'/terminal' could be a rule of any of countdown, signup: say which, as 'countdown#/terminal'.", terminal.Message);

        // And so every rule of both is asked for by nothing, said by the rule set it is in.
        Assert.Contains(found["signup.json"], f => f.Message == "Nothing in the specification asks for 'signup#/inputs/book'.");
        Assert.Contains(found["countdown.json"], f => f.Message == "Nothing in the specification asks for 'countdown#/terminal'.");
    }

    [Fact]
    public void ARuleSetNoSpecificationAsksForIsMarkedOnEverySpecification()
    {
        Written[] specifications =
        [
            new("countdown.specification.json", Named(SpecificationOf("RulealizeStudio.Sample.Countdown"), "countdown")),
            new("more.specification.json", Named(SpecificationOf("RulealizeStudio.Sample.Countdown"), "countdown")),
        ];
        Written[] ruleSets = [new("countdown.json", Text("countdown.json")), new("signup.json", Text("signup.json"))];

        IReadOnlyDictionary<string, ImmutableArray<Finding>> found = Agreement.Compare(specifications, ruleSets);

        Assert.Empty(found["countdown.json"]);
        const string Book = "Nothing in any specification asks for 'signup#/inputs/book'.";
        Assert.Contains(found["signup.json"], f => f.Message == Book);
        Assert.Contains(found["countdown.specification.json"], f => f.Message == Book);
        Assert.Contains(found["more.specification.json"], f => f.Message == Book);
    }

    [Fact]
    public void ABindingNamingARuleSetTheApplicationLacksIsMarked()
    {
        string specification = Named(SpecificationOf("RulealizeStudio.Sample.Countdown"), "count");

        IReadOnlyDictionary<string, ImmutableArray<Finding>> found =
            Agreement.Compare([new("specification.json", specification)], [new("countdown.json", Text("countdown.json"))]);

        Assert.Contains(found["specification.json"], f => f.Message == "'count#/terminal' names a rule set the application does not have, 'count'.");
    }

    [Fact]
    public void ASpecificationWithNoRulesYetMarksEveryNameItBinds()
    {
        Disagreements found = Agreement.Compare(SpecificationOf("RulealizeStudio.Sample.Countdown"), null);

        Assert.Equal(7, found.Specification.Length);
        Assert.Empty(found.Rules);
    }

    [Fact]
    public void WhatAMachineRefersToAndDoesNotHaveIsMarkedOnIt()
    {
        string specification = SpecificationOf("RulealizeStudio.Sample.Countdown")
            .Replace("\"to\": \"done\"", "\"to\": \"finished\"", StringComparison.Ordinal)
            .Replace("\"on\": \"step\"", "\"on\": \"no-overshoot\"", StringComparison.Ordinal);

        Disagreements found = Agreement.Compare(specification, Text("countdown.json"));

        Assert.Equal(["'finished' is not a state of this machine.", "'no-overshoot' is not another element of this machine."], found.Specification.Select(f => f.Message));
        Assert.Equal("\"finished\"", specification.Substring(found.Specification[0].Start, found.Specification[0].Length));
    }

    [Fact]
    public void AnEditWritesTheWholeMachineInItsOneLayout()
    {
        string written = SpecificationOf("RulealizeStudio.Sample.Signup");

        // The samples are written as an edit writes them, so that an edit shows in a diff as only what changed.
        string bound = Edited(written, new() { ["op"] = "bind", ["id"] = "summary", ["rule"] = "/projections/booking" });
        Assert.Equal(written.ReplaceLineEndings("\n"), bound.ReplaceLineEndings("\n"));

        string said = Edited(written, new() { ["op"] = "set", ["id"] = "summary", ["field"] = "says", ["value"] = "画面には予約の中身が出る。" });
        Assert.Contains("\"says\": \"画面には予約の中身が出る。\"", said, StringComparison.Ordinal);
        Assert.Equal(written.Split('\n').Length, said.Split('\n').Length);
    }

    [Fact]
    public void AnElementIsAddedUnderAnIdOfItsOwnAndBoundAndUnbound()
    {
        string written = SpecificationOf("RulealizeStudio.Sample.Countdown");

        (string text, string? added) = Specification.Edit(written, new JsonObject { ["op"] = "add", ["kind"] = "note", ["on"] = "last-step" });
        Assert.Equal("note-1", added);
        text = Edited(text, new() { ["op"] = "set", ["id"] = "note-1", ["field"] = "says", ["value"] = "Eleven is never reached." });
        text = Edited(text, new() { ["op"] = "bind", ["id"] = "note-1", ["rule"] = "/state/schema/total" });

        Element note = Assert.Single(Specification.Read(text), e => e.Id == "note-1");
        Assert.Equal("Eleven is never reached.", note.Says);
        Assert.Equal(["/state/schema/total"], note.Rules.Select(r => r.Rule));
        Assert.Equal("last-step", StateMachine.Read(text).Shapes["note-1"].On);

        text = Edited(text, new() { ["op"] = "unbind", ["id"] = "note-1", ["rule"] = "/state/schema/total" });
        Assert.Empty(Assert.Single(Specification.Read(text), e => e.Id == "note-1").Rules);
    }

    [Fact]
    public void AStateTakenOutTakesItsTransitionsAndWhatIsNotedOnThem()
    {
        string text = Edited(SpecificationOf("RulealizeStudio.Sample.Signup"), new() { ["op"] = "remove", ["id"] = "named" });
        Machine machine = StateMachine.Read(text);

        Assert.Equal(["unnamed", "booked", "summary"], machine.Elements.Select(e => e.Id));
        Assert.Empty(machine.Faults);
    }

    [Fact]
    public void AStateIsAddedAndMadeWhereTheMachineStarts()
    {
        (string text, string? added) = Specification.Edit(StateMachine.Empty(), new JsonObject { ["op"] = "add", ["kind"] = "state" });
        Assert.Equal("state-1", added);
        Assert.Equal("state-1", StateMachine.Read(text).Initial);

        (text, string? second) = Specification.Edit(text, new JsonObject { ["op"] = "add", ["kind"] = "transition", ["from"] = "state-1" });
        Assert.Equal("transition-1", second);
        Assert.Equal(new Shape("state-1", "state-1", null, "", false), StateMachine.Read(text).Shapes["transition-1"]);

        text = Edited(text, new() { ["op"] = "set", ["id"] = "state-1", ["field"] = "final", ["value"] = true });
        Assert.True(StateMachine.Read(text).Shapes["state-1"].Final);
    }

    [Fact]
    public void AnEditNamingWhatTheMachineDoesNotHaveIsNotMade()
    {
        string written = SpecificationOf("RulealizeStudio.Sample.Countdown");

        Assert.Throws<FormatException>(() => Specification.Edit(written, new JsonObject { ["op"] = "set", ["id"] = "step", ["field"] = "to", ["value"] = "nowhere" }));
        Assert.Throws<FormatException>(() => Specification.Edit(written, new JsonObject { ["op"] = "remove", ["id"] = "nothing" }));
        Assert.Throws<FormatException>(() => Specification.Edit(written, new JsonObject { ["op"] = "set", ["id"] = "done", ["field"] = "guard", ["value"] = "x" }));
        Assert.Throws<FormatException>(() => Specification.Edit(Text("countdown.json"), new JsonObject { ["op"] = "remove", ["id"] = "step" }));
    }

    /// <summary>A specification with every binding naming the rule set it is to: <c>countdown#/terminal</c>.</summary>
    private static string Named(string specification, string ruleSet) =>
        System.Text.RegularExpressions.Regex.Replace(specification, "\"(/(?:state|inputs|definitions|projections|terminal)[^\"]*)\"", $"\"{ruleSet}#$1\"");

    private static string Edited(string text, JsonObject edit) => Specification.Edit(text, edit).Text;

    private static string SpecificationOf(string application) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "specification", application, Specification.File));

    private static string Text(string document) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ruleset", document));
}
