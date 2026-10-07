// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RulealizeStudio.Binding;
using RulealizeStudio.Hosting;
using RulealizeStudio.Sample.Countdown;
using RulealizeStudio.Sample.Signup;

namespace RulealizeStudio.Tests;

/// <summary>A test design Ruledger derived from a rule set, replayed against the screen made of it.</summary>
/// <remarks>
/// The designs are committed beside their rule sets as <c>ruledger derive</c> wrote them, and
/// nobody wrote a case in them. What these tests assert is that the replay says nothing when the
/// screen and the design agree, and says where when they do not.
/// </remarks>
public class ReplayTests
{
    [AvaloniaFact]
    public void TheCountdownApplicationDoesWhatItsDesignSays()
    {
        CountdownModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        int pressed = 0;
        IReadOnlyList<Divergence> found = Replay.Run(window, model, Design("countdown"), Presser(window, () => pressed++));

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
        Assert.Equal(Presses(Design("countdown")), pressed);
        Assert.Equal(0L, model.State.Total);
    }

    [AvaloniaFact]
    public void TheSignupApplicationDoesWhatItsDesignSays()
    {
        // Every value a bounded schema admits was tried when the design was derived, and the
        // name is the one a person wrote into it: the text box is the one editor whose values
        // nobody could enumerate.
        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        int pressed = 0;
        IReadOnlyList<Divergence> found = Replay.Run(window, model, Design("signup"), Presser(window, () => pressed++));

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
        Assert.Equal(Presses(Design("signup")), pressed);
    }

    [AvaloniaFact]
    public void AnEditorThatShowsAValueAndDoesNotPassItBackIsCaught()
    {
        // The list shows what the input holds and never tells it what was picked: bound one way,
        // which is an easy mistake to make in XAML and one no case written by hand would look for.
        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);
        ComboBox wants = window.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "WantsBox");
        wants[!SelectingItemsControl.SelectedItemProperty] = new Avalonia.Data.Binding("Note.What") { Mode = Avalonia.Data.BindingMode.OneWay };
        Dispatcher.UIThread.RunJobs();

        IReadOnlyList<Divergence> found = Replay.Run(window, model, Design("signup"), Presser(window));

        Assert.NotEmpty(found);
        Assert.All(found, each => Assert.EndsWith(": the what the screen shows does not reach the rules when it is entered there", each.What));
        Assert.Contains(found, each => each.ToString() == "#1 (#0 → setName(to: alice)): note(what: quiet): the what the screen shows does not reach the rules when it is entered there");
    }

    [AvaloniaFact]
    public void ALimitOnlyTheKeyboardMeetsIsCaught()
    {
        // A length written into the XAML rather than bound to what the rules admit: nothing
        // stops a value set from outside, and typing stops at three.
        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);
        window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "NameBox").MaxLength = 3;
        Dispatcher.UIThread.RunJobs();

        IReadOnlyList<Divergence> found = Replay.Run(window, model, Design("signup"), Presser(window));

        Assert.Equal("#0 (#0): setName(to: alice): typing the to into the screen gives 'ali'", Assert.Single(found).ToString());
    }

    [AvaloniaFact]
    public void ABoundThatMovedIsCaughtThoughNoMoveDid()
    {
        // What the design would say if it had been derived when the party went up to five: a
        // move waiting for a value is one move whatever its schema admits, so only what the
        // parameter admits shows the difference.
        JsonObject design = JsonNode.Parse(Design("signup"))!.AsObject();
        design["admits"]!["setParty"]!["size"]!["max"] = 5;

        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        Divergence found = Assert.Single(Replay.Run(window, model, design.ToJsonString(), Presser(window)));
        Assert.Equal(
            "#1 (#0 → setName(to: alice)): setParty(size) admits {\"op\":\"type.int\",\"min\":1,\"max\":6}, "
            + "and the design has it admit {\"op\":\"type.int\",\"min\":1,\"max\":5}",
            found.ToString());
    }

    [AvaloniaFact]
    public void AValueTheDesignHasRefusedAndTheRulesNowTakeIsCaught()
    {
        // What a validate clause relaxed since the design looks like from here: three was
        // refused then, and pressing it now lands.
        JsonObject design = JsonNode.Parse(Design("signup"))!.AsObject();
        State(design, "#1")["refused"]!.AsArray().Add(JsonNode.Parse("""{ "input": "setParty", "args": { "size": "3" }, "codes": ["party.unchanged"] }"""));

        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        Divergence found = Assert.Single(Replay.Run(window, model, design.ToJsonString(), Presser(window)));
        Assert.Equal("#1 (#0 → setName(to: alice)): the rules take setParty(size: 3), which the design has them refuse: party.unchanged", found.ToString());
    }

    [AvaloniaFact]
    public void ARefusalWithAnotherCodeIsCaught()
    {
        JsonObject design = JsonNode.Parse(Design("signup"))!.AsObject();
        State(design, "#1")["refused"]![0]!["codes"] = new JsonArray("party.same");

        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        Divergence found = Assert.Single(Replay.Run(window, model, design.ToJsonString(), Presser(window)));
        Assert.Equal("#1 (#0 → setName(to: alice)): setParty(size: 1) is refused with party.unchanged, and the design has it refused with party.same", found.ToString());
    }

    [AvaloniaFact]
    public void AButtonWithNoBoxBesideItForItsValueIsCaught()
    {
        // The button for setName is there and the rules enable it; what the name is typed into
        // is not, so nobody could apply the move the design names.
        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);
        window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "NameBox").IsVisible = false;
        Dispatcher.UIThread.RunJobs();

        IReadOnlyList<Divergence> found = Replay.Run(window, model, Design("signup"), Presser(window));

        // Nor is there anywhere left to say why 'admin' was refused.
        Assert.Equal(
            [
                "#0 (#0): setName(to: admin) is refused with name.reserved, and the screen does not say \"Nobody books under that name.\"",
                "#0 (#0): setName(to: alice): nothing on the screen shows the to it is to be applied with",
            ],
            found.Take(2).Select(each => each.ToString()));
        Assert.All(found, each => Assert.StartsWith("setName(to: ", each.What));
    }

    [AvaloniaFact]
    public void ARuleSetThatNoLongerDecidesWhatWasCommittedIsCaughtWhereItShows()
    {
        // What a commit of the rule set without the design would look like from here: the design
        // says add(n: 3) is not legal from the start, and the ending is called something else.
        JsonObject design = JsonNode.Parse(Design("countdown"))!.AsObject();
        JsonObject start = State(design, "#0");
        start["moves"]!.AsArray().RemoveAt(2);
        State(design, "#10")["terminal"]!["result"] = "finished";

        CountdownModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        IReadOnlyList<Divergence> found = Replay.Run(window, model, design.ToJsonString(), Presser(window));

        Assert.Contains(found, each => each.ToString() == "#0 (#0): the rules offer add(n: 3), which the design does not have");
        Assert.Contains(found, each => each.State == "#10" && each.What == "it ends done, and the design has it end finished");
    }

    [AvaloniaFact]
    public void AScreenThatFellBehindItsRuleSetIsCaughtAtEveryStateWhereItShows()
    {
        // Two buttons where the rules offer three.
        CountdownModel model = new(AppContext.BaseDirectory);
        StackPanel buttons = new();
        foreach (string n in new[] { "1", "2" })
        {
            buttons.Children.Add(new Button { Content = "+" + n, Command = model.Add.Apply, CommandParameter = n });
        }

        Window window = Show(new Window { Content = buttons, DataContext = model });

        IReadOnlyList<Divergence> found = Replay.Run(window, model, Design("countdown"), Presser(window));

        Assert.Equal("#0 (#0): add(n: 3) is legal, and nothing on the screen stands for it", found[0].ToString());
        Assert.All(found, each => Assert.EndsWith("add(n: 3) is legal, and nothing on the screen stands for it", each.What));
        Assert.Equal(8, found.Count);
    }

    [AvaloniaFact]
    public void ADrawIsReplayedByPickingWhatTheDesignRecordedWasDrawn()
    {
        DrawModel model = new(AppContext.BaseDirectory);
        Window window = Show(DrawScreen(model, outcomes: true));

        int pressed = 0;
        IReadOnlyList<Divergence> found = Replay.Run(window, model, Design("draw"), Presser(window, () => pressed++));

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
        Assert.Equal(Presses(Design("draw")), pressed);
    }

    [AvaloniaFact]
    public void AScreenWithNothingToPickAnOutcomeWithIsCaught()
    {
        DrawModel model = new(AppContext.BaseDirectory);
        Window window = Show(DrawScreen(model, outcomes: false));

        IReadOnlyList<Divergence> found = Replay.Run(window, model, Design("draw"), Presser(window));

        Assert.Equal("#0 (#0): roll drawing 1: nothing on the screen picks what was drawn", found[0].ToString());
        Assert.All(found, each => Assert.EndsWith("nothing on the screen picks what was drawn", each.What));
    }

    [AvaloniaFact]
    public void ADesignOfAnotherRuleSetIsOneDivergenceAndNoWalk()
    {
        CountdownModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        Divergence found = Assert.Single(Replay.Run(window, model, Design("draw"), Presser(window)));
        Assert.Equal("the design is of draw@1.0.0, and the screen is bound to countdown@1.0.0", found.What);
    }

    [AvaloniaFact]
    public void ADocumentThatIsNotATestDesignIsRefused()
    {
        CountdownModel model = new(AppContext.BaseDirectory);

        Assert.Throws<FormatException>(() => Replay.Run(new Window(), model, "{ \"$schema\": \"rulealize/state/v1\" }", _ => { }));
    }

    [AvaloniaFact]
    public void EveryStateOfSignupsDesignIsStoodInByPressingWhatReachedIt()
    {
        string design = Design("signup");
        IReadOnlyList<Situation> situations = Replay.Situations(design);

        Assert.Equal(JsonNode.Parse(design)!["states"]!.AsArray().Count, situations.Count);
        Assert.Empty(situations[0].Route);
        Assert.Equal(["setName(to: alice)"], situations[1].Route);

        foreach (Situation situation in situations)
        {
            SignupModel model = new(AppContext.BaseDirectory);
            Window window = ApplicationTests.Open(model);
            List<(string Step, Control Control)> pressed = [];

            IReadOnlyList<Divergence> found = Replay.Stand(window, model, design, situation.State, (step, control) =>
            {
                pressed.Add((step, control));
                ApplicationTests.Press(window, control);
            });

            Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
            Assert.Equal(situation.Route, pressed.Select(each => each.Step));
            Assert.All(pressed, each => Assert.IsType<Button>(each.Control));
            Assert.Equal(State(JsonNode.Parse(design)!.AsObject(), situation.State)["state"]!["data"]!.ToJsonString(), JsonNode.Parse(model.Document)!["data"]!.ToJsonString());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AStateADrawReachedIsStoodInByPickingWhatWasDrawn()
    {
        DrawModel model = new(AppContext.BaseDirectory);
        Window window = Show(DrawScreen(model, outcomes: true));
        // Three rolls of one were the first to reach a total of three.
        Situation third = Replay.Situations(Design("draw"))[3];

        List<string> pressed = [];
        IReadOnlyList<Divergence> found = Replay.Stand(window, model, Design("draw"), third.State, (step, control) =>
        {
            pressed.Add(step);
            ApplicationTests.Press(window, control);
        });

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
        Assert.Equal(["roll drawing 1", "roll drawing 1", "roll drawing 1"], third.Route);
        Assert.Equal(["roll", "roll drawing 1", "roll", "roll drawing 1", "roll", "roll drawing 1"], pressed);
        Assert.Equal(3L, model.State.Total);
    }

    [AvaloniaFact]
    public void AWindowThatCannotGetWhereTheDesignSaysStopsWhereItCouldNot()
    {
        // The design has choosing the window seat leave them by the aisle; the rules seat them by the window.
        JsonObject broken = JsonNode.Parse(Design("signup"))!.AsObject();
        State(broken, "#2")["state"]!["data"]!["seat"] = "aisle";
        string design = broken.ToJsonString();
        Situation beyond = Replay.Situations(design).First(situation => situation.Route.Count > 2 && situation.Route[1] == "chooseSeat(seat: window)");
        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        List<string> pressed = [];
        IReadOnlyList<Divergence> found = Replay.Stand(window, model, design, beyond.State, (step, control) =>
        {
            pressed.Add(step);
            ApplicationTests.Press(window, control);
        });

        Assert.Equal(
            "#2 (#0 → setName(to: alice) → chooseSeat(seat: window)): #1 + chooseSeat(seat: window) at seat \"window\", and the design has it at seat \"aisle\"",
            Assert.Single(found).ToString());
        Assert.Equal(["setName(to: alice)", "chooseSeat(seat: window)"], pressed);
        Assert.Throws<ArgumentException>(() => Replay.Stand(window, model, design, "#999", (_, _) => { }));
    }

    [AvaloniaFact]
    public void ARefusalIsDrawnAsTheSentenceItsLabelDocumentGivesIt()
    {
        // Signup's design tries 'admin' where it starts, a value somebody chose and the rules refuse.
        string design = Design("signup");
        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);
        string? drawn = null;

        IReadOnlyList<Divergence> found = Replay.Stand(window, model, design, "#0", Spoken("signup"), control => ApplicationTests.Press(window, control), new Witness(
            Pressing: (_, _) => { },
            Arrived: () => { },
            Offered: (_, _) => { },
            Refused: (step, _) => drawn = step + ": " + string.Join(" | ", DataValidationErrors.GetErrors(window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "NameBox"))!.Cast<object>())));

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
        Assert.Equal("setName(to: admin): Nobody books under that name.", drawn);
        Assert.True(Replay.Run(window, model, design, Spoken("signup"), Presser(window)).Count == 0);
    }

    [AvaloniaFact]
    public void AScreenThatSaysAnotherSentenceThanTheLabelDocumentIsCaught()
    {
        // The label document beside the rule set says something new, and the application was
        // built before it did: what the person reads is not what the design expects.
        Labels changed = Labels.Read("en", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ruleset", "signup.labels.en.json"))
            .Replace("The party is already that size.", "That is the party already.", StringComparison.Ordinal));
        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);

        IReadOnlyList<Divergence> found = Replay.Run(window, model, Design("signup"), changed, Presser(window));

        Assert.NotEmpty(found);
        Assert.All(found, divergence => Assert.EndsWith("is refused with party.unchanged, and the screen does not say \"That is the party already.\"", divergence.What, StringComparison.Ordinal));
        Assert.Contains(found, divergence => divergence.ToString() == "#1 (#0 → setName(to: alice)): setParty(size: 1) is refused with party.unchanged, and the screen does not say \"That is the party already.\"");
    }

    [AvaloniaFact]
    public void ASituationIsToldAsTheWindowStandsThereAndAsEachRefusalIsDrawn()
    {
        string design = Design("signup");
        Situation named = Replay.Situations(design)[1];
        SignupModel model = new(AppContext.BaseDirectory);
        Window window = ApplicationTests.Open(model);
        List<string> told = [];
        List<string?> offered = [];
        string? drawn = null;

        IReadOnlyList<Divergence> found = Replay.Stand(window, model, design, named.State, Spoken("signup"), control => ApplicationTests.Press(window, control), new Witness(
            Pressing: (step, _) => told.Add("pressing " + step),
            Arrived: () => told.Add("arrived"),
            Offered: (step, control) => offered.Add(control is null ? null : step),
            Refused: (step, control) =>
            {
                told.Add("refused " + step);
                drawn = string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>().Where(each => each.IsEffectivelyVisible).Select(each => each.Text));
            }));

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
        Assert.Equal(["pressing setName(to: alice)", "arrived", "refused setParty(size: 1)"], told);
        Assert.Equal(named.Moves.Select(move => move.Step), offered);
        Assert.Contains("The party is already that size.", drawn, StringComparison.Ordinal);
        Assert.Equal(1L, model.State.Party);
    }

    [Fact]
    public void ASituationCarriesEverythingTheDesignRecordsThere()
    {
        IReadOnlyList<Situation> situations = Replay.Situations(Design("signup"));
        Situation named = situations[1];

        Assert.False(named.IsEnding);
        SituationMove waiting = named.Moves[0];
        Assert.Equal(("setName(to: ?)", null, false), (waiting.Step, waiting.To, waiting.Followed));
        Assert.Equal([KeyValuePair.Create("op", "type.string"), KeyValuePair.Create("maxLength", "12")], waiting.Admits["to"]);
        SituationMove two = Assert.Single(named.Moves, move => move.Step == "setParty(size: 2)");
        Assert.True(two.Followed);
        Assert.Equal(["setName(to: alice)", "setParty(size: 2)"], situations.Single(each => each.State == two.To).Route);
        Assert.Equal([KeyValuePair.Create("op", "type.int"), KeyValuePair.Create("min", "1"), KeyValuePair.Create("max", "6")], two.Admits["size"]);
        Assert.Equal("quiet, near the door", named.Moves.First(move => move.Input == "note").Admits["what"].Single(bound => bound.Key == "values").Value);
        SituationRefusal one = Assert.Single(named.Refused);
        Assert.Equal(("setParty(size: 1)", "setParty"), (one.Step, one.Input));
        Assert.Equal(["party.unchanged"], one.Codes);

        Situation booked = situations.First(each => each.IsEnding);
        Assert.Equal("booked", booked.Result);
        Assert.All(booked.Moves, move => Assert.False(move.Followed));
    }

    private static Labels Spoken(string ruleSet) =>
        Labels.Read("en", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ruleset", ruleSet + ".labels.en.json")));

    private static string Design(string ruleSet) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ruleset", ruleSet + ".test-design.json"));

    private static JsonObject State(JsonObject design, string name) =>
        design["states"]!.AsArray().Select(each => each!.AsObject()).Single(each => (string?)each["name"] == name);

    /// <summary>Presses with the pointer, and counts the presses that moved the position.</summary>
    /// <remarks>
    /// Only those, because a replay also presses values the design leaves out, to find out
    /// whether the rules still refuse them, and a refusal moves nothing.
    /// </remarks>
    private static Action<Control> Presser(Window window, Action? counted = null) => control =>
    {
        bool moved = false;
        void Moved(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => moved |= string.IsNullOrEmpty(args.PropertyName);

        RuleModel model = (RuleModel)window.DataContext!;
        model.PropertyChanged += Moved;
        ApplicationTests.Press(window, control);
        model.PropertyChanged -= Moved;

        if (moved)
        {
            counted?.Invoke();
        }
    };

    /// <summary>How many presses following every move of a design takes: one per move settled, two per branch of a draw.</summary>
    private static int Presses(string design) =>
        JsonNode.Parse(design)!["states"]!.AsArray()
            .SelectMany(state => state!["moves"]!.AsArray())
            .Sum(move => move!["lands"] is JsonArray lands ? 2 * lands.Count : move!.AsObject().ContainsKey("to") ? 1 : 0);

    /// <summary>A screen for draw.json, which has no application: a button per input, and one per outcome where asked.</summary>
    private static Window DrawScreen(DrawModel model, bool outcomes)
    {
        StackPanel panel = new();
        panel.Children.Add(new Button { Content = "Roll", Command = model.Roll.Apply });
        panel.Children.Add(new Button { Content = "Stop", Command = model.Stop.Apply });

        if (outcomes)
        {
            panel.Children.Add(new ItemsControl
            {
                [!ItemsControl.ItemsSourceProperty] = new Avalonia.Data.Binding(nameof(RuleModel.Outcomes)),
                ItemTemplate = new FuncDataTemplate<OutcomeChoice>((choice, _) => new Button { Content = choice?.ToString(), Command = choice?.Choose }),
            });
        }

        return new Window { Content = panel, DataContext = model };
    }

    private static Window Show(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
