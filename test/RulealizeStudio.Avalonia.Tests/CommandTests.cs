// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Text.Json.Nodes;
using RulealizeStudio.Server;

namespace RulealizeStudio.Tests;

/// <summary>The checks an application's folder is finished by, from the command line, each failing on signup broken the way it is meant to catch.</summary>
/// <remarks>
/// Each folder here is signup's rule set, specification, test design and label document, copied, with
/// the vocabularies and the built application in this suite's own output folder. The replay runs
/// in a process of its own, as it does from a command line, because it starts Avalonia and this
/// suite has started it already.
/// </remarks>
public sealed class CommandTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rulealize-studio-" + Guid.NewGuid().ToString("N"));

    public CommandTests()
    {
        Directory.CreateDirectory(_folder);
        foreach (string file in new[] { "signup.json", "signup.test-design.json", "signup.labels.en.json" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ruleset", file), Path.Combine(_folder, file));
        }

        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "specification", "RulealizeStudio.Sample.Signup", Specification.File),
            Path.Combine(_folder, Specification.File));

        // The replay finds the built application by its project's name; nothing reads what is in it.
        File.WriteAllText(Path.Combine(_folder, "RulealizeStudio.Sample.Signup.csproj"), "<Project />");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void SignupAsCommittedHasNothingToReport()
    {
        Assert.Equal((Command.Agreed, string.Empty), Run("check"));
        Assert.Equal((Command.Agreed, string.Empty), Run("agree"));
        Assert.Equal((Command.Agreed, string.Empty), Replay());
    }

    [Fact]
    public void ARuleSetThatDoesNotCompileIsSaidWhereTheRuntimeSaidTheFaultIs()
    {
        Edit("signup.json", "\"logic.not\"", "\"logic.nope\"");

        (int code, string said) = Run("check");

        Assert.Equal(Command.Found, code);
        string line = Assert.Single(Lines(said));
        Assert.StartsWith(Path.Combine(_folder, "signup.json") + "(", line, StringComparison.Ordinal);
        Assert.Contains("): error : 'logic.nope' is not an operation any loaded plugin provides.", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AClauseNobodyAskedForIsSaidOnTheRulesAndOnTheSpecification()
    {
        Edit(
            "signup.json",
            "\"code\": \"party.unchanged\" }",
            "\"code\": \"party.unchanged\" },\n        { \"require\": { \"op\": \"cmp.lte\", \"left\": \"@size\", \"right\": 6 }, \"code\": \"party.tooMany\" }");

        (int code, string said) = Run("agree");

        Assert.Equal(Command.Found, code);
        Assert.Equal(
            [
                $"{Path.Combine(_folder, Specification.File)}: Nothing in the specification asks for '/inputs/setParty/validate/party.tooMany'.",
                $"{Path.Combine(_folder, "signup.json")}: Nothing in the specification asks for '/inputs/setParty/validate/party.tooMany'.",
            ],
            Lines(said).Select(Unplaced));
    }

    [Fact]
    public void AnElementTakenOutOfTheSpecificationLeavesTheRulesOnlyItAskedForAskedForByNothing()
    {
        string file = Path.Combine(_folder, Specification.File);
        File.WriteAllText(file, Specification.Edit(File.ReadAllText(file), new JsonObject { ["op"] = "remove", ["id"] = "party-unchanged" }).Text);

        (int code, string said) = Run("agree");

        Assert.Equal(Command.Found, code);
        Assert.Equal(
            [
                $"{file}: Nothing in the specification asks for '/inputs/setParty/validate/party.unchanged'.",
                $"{Path.Combine(_folder, "signup.json")}: Nothing in the specification asks for '/inputs/setParty/validate/party.unchanged'.",
            ],
            Lines(said).Select(Unplaced));
    }

    [Fact]
    public void AFolderWithASpecificationAndNoRulesYetHasEveryBindingSaid()
    {
        File.Delete(Path.Combine(_folder, "signup.json"));

        (int code, string said) = Run("agree");

        Assert.Equal(Command.Found, code);
        Assert.Contains($"{Path.Combine(_folder, Specification.File)}: '/terminal' is not a rule the rules have.", Lines(said).Select(Unplaced));
    }

    [Fact]
    public void AScreenThatDoesNotDoWhatTheDesignSaysIsSaidAtTheState()
    {
        // The design has choosing the window seat lead back to where it came from, and the
        // application as built does not.
        Edit("signup.test-design.json", "\"to\": \"#2\"", "\"to\": \"#1\"");

        (int code, string said) = Replay();

        Assert.Equal(Command.Found, code);
        Assert.Contains(Lines(said), line =>
            line.StartsWith(Path.Combine(_folder, "signup.test-design.json") + "(", StringComparison.Ordinal)
            && line.Contains("): error : #1 (#0 → setName(to: alice)): #1 + chooseSeat(seat: window) at seat \"window\", and the design has it at seat null", StringComparison.Ordinal));
    }

    [Fact]
    public void ASituationIsShownAsTheWindowBeforeEachPressAndWhereItStands()
    {
        (int listed, string situations) = Started("show", _folder, "--plugins", AppContext.BaseDirectory);
        string[] route = ["setName(to: alice)", "chooseSeat(seat: window)"];
        string state = JsonNode.Parse(situations)!["situations"]!.AsArray()
            .Single(each => each!["route"]!.AsArray().Select(step => (string?)step).SequenceEqual(route))!["state"]!.GetValue<string>();
        string pictures = Path.Combine(_folder, "pictures");

        (int code, string said) = Started("show", _folder, "--plugins", AppContext.BaseDirectory, "--state", state, "--out", pictures);

        Assert.Equal((Command.Agreed, Command.Agreed), (listed, code));
        JsonObject shown = JsonNode.Parse(said)!.AsObject();
        JsonArray presses = shown["presses"]!.AsArray();
        Assert.Equal(route, presses.Select(press => (string?)press!["step"]));
        Assert.Empty(shown["divergences"]!.AsArray());

        // Each picture is the whole of signup's window, as it opens: 560 by 620.
        foreach (string picture in presses.Select(press => (string)press!["picture"]!).Append((string)shown["picture"]!))
        {
            Assert.Equal(pictures, Path.GetDirectoryName(picture));
            Assert.Equal((560, 620), Size(picture));
        }

        // What was pressed for the seat is on the window, below what was pressed for the name.
        JsonNode name = presses[0]!["at"]!;
        JsonNode seat = presses[1]!["at"]!;
        Assert.True((double)seat["y"]! > (double)name["y"]! && (double)seat["x"]! + (double)seat["width"]! <= 560);
    }

    [Fact]
    public void TheScreenIsDrawnFromItsTextAndChangedByEditsToItForAsLongAsItIsAsked()
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (string arg in new[] { Path.Combine(AppContext.BaseDirectory, "RulealizeStudio.Server.dll"), "design", _folder, "--plugins", AppContext.BaseDirectory })
        {
            start.ArgumentList.Add(arg);
        }

        string signup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "RulealizeStudio.Sample.Signup", "MainWindow.axaml"));
        using Process process = Process.Start(start)!;
        JsonObject Ask(JsonObject asked)
        {
            process.StandardInput.WriteLine(asked.ToJsonString());
            process.StandardInput.Flush();
            return JsonNode.Parse(process.StandardOutput.ReadLine()!)!.AsObject();
        }

        JsonObject controls = Ask(new() { ["op"] = "controls" });
        JsonObject drawn = Ask(new() { ["op"] = "draw", ["text"] = signup });
        JsonObject retitled = Ask(new() { ["op"] = "words", ["text"] = signup, ["at"] = 0, ["words"] = "席の予約" });
        process.StandardInput.Close();
        string trouble = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(trouble.Length == 0, trouble);
        Assert.Equal(Command.Agreed, process.ExitCode);
        Assert.Contains(controls["controls"]!.AsArray(), control => (string?)control!["type"] == "Avalonia.Controls.NumericUpDown");

        // The whole of signup's window, as it opens: 560 by 620, drawn by Avalonia as a PNG.
        byte[] picture = Convert.FromBase64String((string)drawn["picture"]!);
        Assert.Equal((560, 620), (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(picture.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(picture.AsSpan(20))));
        Assert.Contains(drawn["placed"]!.AsArray(), control => (string?)control!["words"] == "Book");

        // Somebody's words reach it as they wrote them, and change the one attribute they are.
        Assert.Equal(signup.Replace("Title=\"Sign up\"", "Title=\"席の予約\"", StringComparison.Ordinal), (string?)retitled["text"]);
    }

    [Fact]
    public void EverySituationIsListedWithAllTheDesignRecordsThere()
    {
        (int code, string said) = Started("show", _folder, "--plugins", AppContext.BaseDirectory);

        Assert.Equal(Command.Agreed, code);
        JsonObject listed = JsonNode.Parse(said)!.AsObject();
        Assert.Equal("en", (string?)listed["language"]);
        JsonArray situations = listed["situations"]!.AsArray();
        Assert.Equal(181, situations.Count);

        JsonNode named = situations[1]!;
        Assert.Null(named["ending"]);
        JsonNode two = named["moves"]!.AsArray().Single(move => (string?)move!["step"] == "setParty(size: 2)")!;
        Assert.True((bool)two["followed"]!);
        Assert.Equal(
            ["op type.int", "min 1", "max 6"],
            two["admits"]!["size"]!.AsArray().Select(bound => $"{bound!["bound"]} {bound["value"]}"));
        JsonNode refusal = Assert.Single(named["refused"]!.AsArray())!;
        Assert.Equal("setParty(size: 1)", (string?)refusal["step"]);
        Assert.Equal("The party is already that size.", (string?)Assert.Single(refusal["said"]!.AsArray()));

        Assert.Equal(72, situations.Count(situation => (string?)situation!["ending"]?["result"] == "booked"));

        // Each move as signup's window says it: the words of the control pressed, and the value
        // entered beside it.
        Assert.Equal(["Save: alice"], named["words"]!.AsArray().Select(word => (string?)word));
        Assert.Equal("Set: 2", (string?)two["words"]);
        Assert.Equal("Set: 1", (string?)refusal["words"]);
        Assert.All(situations, situation => Assert.All(situation!["words"]!.AsArray(), word => Assert.NotNull(word)));

        // The values somebody chose to try where it starts, each with what became of it.
        Assert.Equal(
            ["setName(to: alice) yes", "setName(to: admin) not legal here", "setName(to: Christabella) yes"],
            situations[0]!["chosen"]!.AsArray().Select(choice => $"{choice!["step"]} {choice["carried"]}"));
        Assert.Empty(listed["unplaced"]!.AsArray());
    }

    [Fact]
    public void AChoiceIsListedWhereItWasMadeByEitherNameRuledgerReads()
    {
        // As Ruledger writes one back, by how the walk arrives there; and one it never arrived at.
        Edit(
            "signup.test-design.json",
            "\"edits\": [",
            "\"edits\": [ { \"state\": \"#0 + setName(to: alice) + chooseSeat(seat: window)\", \"input\": \"setName\", \"args\": { \"to\": \"bob\" } }, " +
            "{ \"state\": \"#0 + setName(to: zed)\", \"input\": \"setName\", \"args\": { \"to\": \"bob\" }, \"carried\": \"not reached\" },");

        (int code, string said) = Started("show", _folder, "--plugins", AppContext.BaseDirectory);

        Assert.Equal(Command.Agreed, code);
        JsonObject listed = JsonNode.Parse(said)!.AsObject();
        JsonNode window = listed["situations"]!.AsArray()
            .Single(situation => string.Join(" → ", situation!["route"]!.AsArray().Select(step => (string?)step)) == "setName(to: alice) → chooseSeat(seat: window)")!;
        JsonNode bob = Assert.Single(window["chosen"]!.AsArray())!;
        Assert.Equal((0, "setName(to: bob)"), ((int)bob["at"]!, (string?)bob["step"]));

        JsonNode nowhere = Assert.Single(listed["unplaced"]!.AsArray())!;
        Assert.Equal(("#0 + setName(to: zed)", "not reached"), ((string?)nowhere["state"], (string?)nowhere["carried"]));
    }

    [Fact]
    public void ARefusalIsShownAsTheWindowDrewIt()
    {
        string pictures = Path.Combine(_folder, "pictures");

        (int code, string said) = Started("show", _folder, "--plugins", AppContext.BaseDirectory, "--state", "#1", "--out", pictures);

        Assert.Equal(Command.Agreed, code);
        JsonObject shown = JsonNode.Parse(said)!.AsObject();
        Assert.Empty(shown["divergences"]!.AsArray());

        // Every move legal there, each with the control that stands for it.
        Assert.Equal(10, shown["moves"]!.AsArray().Count);
        Assert.All(shown["moves"]!.AsArray(), move => Assert.NotNull(move!["at"]));

        JsonNode refusal = Assert.Single(shown["refused"]!.AsArray())!;
        Assert.Equal("setParty(size: 1)", (string?)refusal["step"]);
        Assert.Equal((560, 620), Size((string)refusal["picture"]!));
        Assert.NotEqual((string)shown["picture"]!, (string)refusal["picture"]!);
    }

    [Fact]
    public void AnApplicationBuiltBeforeASentenceChangedIsCaught()
    {
        Edit("signup.labels.en.json", "The party is already that size.", "That is the party already.");

        (int code, string said) = Replay();

        Assert.Equal(Command.Found, code);
        Assert.Contains(Lines(said), line =>
            line.Contains("): error : #1 (#0 → setName(to: alice)): setParty(size: 1) is refused with party.unchanged, and the screen does not say \"That is the party already.\"", StringComparison.Ordinal));
    }

    [Fact]
    public void SignupAsCommittedMovesNoSituation()
    {
        (int code, string said) = Run("changes");

        // Ruledger still reports 'admin', the choice the rules refuse, as not carried; it is a report, not a move.
        Assert.Equal(Command.Found, code);
        JsonObject changed = JsonNode.Parse(said)!.AsObject();
        Assert.Empty(changed["changed"]!.AsArray());
        Assert.Empty(changed["gone"]!.AsArray());
        Assert.Empty(changed["appeared"]!.AsArray());
        Assert.Empty(changed["admits"]!.AsArray());
        JsonNode choice = Assert.Single(changed["choices"]!.AsArray())!;
        Assert.Equal(("setName(to: admin)", "not legal here"), ((string?)choice["step"], (string?)choice["carried"]));
    }

    [Fact]
    public void APartyOfUpToEightIsSaidByTheMovesThatReachEachSituationItMoved()
    {
        Edit("signup.json", "\"party\": { \"op\": \"type.int\", \"min\": 1, \"max\": 6 }", "\"party\": { \"op\": \"type.int\", \"min\": 1, \"max\": 8 }");
        string after = Path.Combine(_folder, "after", "signup.test-design.json");

        using StringWriter output = new();
        Assert.Equal(Command.Found, Command.Run(["changes", _folder, "--plugins", AppContext.BaseDirectory, "--out", after], output, TextWriter.Null));

        JsonObject changed = JsonNode.Parse(output.ToString())!.AsObject();
        JsonNode admits = Assert.Single(changed["admits"]!.AsArray())!;
        Assert.Equal("8", (string?)admits["now"]!.AsArray().Single(b => (string?)b!["bound"] == "max")!["value"]);
        Assert.Equal("6", (string?)admits["was"]!.AsArray().Single(b => (string?)b!["bound"] == "max")!["value"]);

        JsonNode alice = changed["changed"]!.AsArray().Single(c => Route(c!) == "setName(to: alice)")!;
        Assert.Equal(["setParty(size: 7)", "setParty(size: 8)"], alice["gained"]!.AsArray().Select(m => (string?)m!["step"]));
        Assert.Equal(
            "setName(to: alice) → setParty(size: 2) → setParty(size: 3) → setParty(size: 4) → setParty(size: 5) → setParty(size: 6) → setParty(size: 7)",
            string.Join(" → ", alice["gained"]![0]!["to"]![0]!["to"]!.AsArray().Select(step => (string?)step)));

        // The design the rules give now, where the window after the change is stood: the situation is
        // the one the route names, and a party of seven can be set there.
        Hosting.Situation now = Hosting.Replay.Situations(File.ReadAllText(after)).Single(s => s.State == (string?)alice["after"]);
        Assert.Equal(["setName(to: alice)"], now.Route);
        Assert.Contains(now.Moves, m => m.Step == "setParty(size: 7)");
    }

    [Fact]
    public void AChangeIsSaidAgainstTheRulesAsCommittedThoughTheirDesignWasNotDerivedAgainBeforeIt()
    {
        // Committed: the rules with a party of up to seven, and the design beside them not derived
        // again since it said six.
        const string Six = "\"party\": { \"op\": \"type.int\", \"min\": 1, \"max\": 6 }";
        string committed = Path.Combine(_folder, "committed");
        Directory.CreateDirectory(committed);
        File.Copy(Path.Combine(_folder, "signup.test-design.json"), Path.Combine(committed, "signup.test-design.json"));
        File.WriteAllText(Path.Combine(committed, "signup.json"), File.ReadAllText(Path.Combine(_folder, "signup.json")).Replace(Six, Six.Replace("6", "7"), StringComparison.Ordinal));

        // Now: eight, with the design beside the rules derived again, as an agent would leave it.
        Edit("signup.json", Six, Six.Replace("6", "8"));
        string beside = Path.Combine(_folder, "signup.test-design.json");
        Assert.Equal(Command.Found, Command.Run(["changes", _folder, "--plugins", AppContext.BaseDirectory, "--out", beside], TextWriter.Null, TextWriter.Null));

        string before = Path.Combine(_folder, "before", "signup.test-design.json");
        using StringWriter output = new();
        Assert.Equal(Command.Found, Command.Run(
            ["changes", _folder, "--plugins", AppContext.BaseDirectory, "--was", Path.Combine(committed, "signup.json"), "--design", Path.Combine(committed, "signup.test-design.json"), "--was-out", before],
            output, TextWriter.Null));

        // Seven, as the rules committed say, and not the six their stale design says.
        JsonObject changed = JsonNode.Parse(output.ToString())!.AsObject();
        JsonNode admits = Assert.Single(changed["admits"]!.AsArray())!;
        Assert.Equal(("7", "8"), (
            (string?)admits["was"]!.AsArray().Single(b => (string?)b!["bound"] == "max")!["value"],
            (string?)admits["now"]!.AsArray().Single(b => (string?)b!["bound"] == "max")!["value"]));
        JsonNode alice = changed["changed"]!.AsArray().Single(c => Route(c!) == "setName(to: alice)")!;
        Assert.Equal(["setParty(size: 8)"], alice["gained"]!.AsArray().Select(m => (string?)m!["step"]));

        // The design before, where the window before the change is stood, carrying the choices of the one committed.
        Hosting.Situation was = Hosting.Replay.Situations(File.ReadAllText(before)).Single(s => s.State == (string?)alice["before"]);
        Assert.Contains(was.Moves, m => m.Step == "setParty(size: 7)");
        Assert.Contains("Christabella", File.ReadAllText(before), StringComparison.Ordinal);

        // Against the design beside the rules, derived again, nothing moved.
        (_, string againstBeside) = Run("changes");
        Assert.Empty(JsonNode.Parse(againstBeside)!["admits"]!.AsArray());
    }

    [Fact]
    public void ARefusalThatComesOrGoesIsSaidInTheLabelDocument()
    {
        Edit("signup.json", "\"code\": \"party.unchanged\"", "\"code\": \"party.same\"");

        (int code, string said) = Run("changes");

        Assert.Equal(Command.Found, code);
        JsonNode alice = JsonNode.Parse(said)!["changed"]!.AsArray().Single(c => Route(c!) == "setName(to: alice)")!;
        Assert.Empty(alice["gained"]!.AsArray());
        JsonNode now = Assert.Single(alice["refusing"]!.AsArray())!;
        JsonNode was = Assert.Single(alice["notRefusing"]!.AsArray())!;
        Assert.Equal(("setParty(size: 1)", "party.same"), ((string?)now["step"], (string?)now["said"]![0]));
        Assert.Equal(("setParty(size: 1)", "The party is already that size."), ((string?)was["step"], (string?)was["said"]![0]));
    }

    [Fact]
    public void AFolderOfTwoRuleSetsIsAskedAboutOneByName()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "ruleset", "countdown.json"), Path.Combine(_folder, "countdown.json"));
        using StringWriter trouble = new();

        Assert.Equal(Command.Failed, Command.Run(["check", _folder, "--plugins", AppContext.BaseDirectory], TextWriter.Null, trouble));
        Assert.Contains("2 rule sets, countdown.json, signup.json: say which with --rules.", trouble.ToString(), StringComparison.Ordinal);

        Assert.Equal(Command.Agreed, Command.Run(["check", _folder, "--rules", "signup.json", "--plugins", AppContext.BaseDirectory], TextWriter.Null, TextWriter.Null));
        Assert.Equal((Command.Agreed, string.Empty), Started("replay", _folder, "--rules", "signup.json", "--plugins", AppContext.BaseDirectory));
    }

    [Fact]
    public void EverySpecificationInTheFolderIsHeldToEveryRuleSet()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "ruleset", "countdown.json"), Path.Combine(_folder, "countdown.json"));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "specification", "RulealizeStudio.Sample.Countdown", Specification.File),
            Path.Combine(_folder, "countdown.specification.json"));

        (int code, string said) = Run("agree");

        // Signup's specification and countdown's both bind without naming a rule set, and there are two.
        Assert.Equal(Command.Found, code);
        Assert.Contains(Lines(said), line => line.StartsWith(Path.Combine(_folder, "countdown.specification.json") + "(", StringComparison.Ordinal)
            && line.EndsWith("'/terminal' could be a rule of any of countdown, signup: say which, as 'countdown#/terminal'.", StringComparison.Ordinal));
        Assert.Contains(Lines(said), line => line.StartsWith(Path.Combine(_folder, Specification.File) + "(", StringComparison.Ordinal));
    }

    [Fact]
    public void AnApplicationOfSeveralWindowsIsReplayedAndShownOnEveryWindowOfTheRuleSet()
    {
        // This suite is itself such an application: window\*.axaml over order and draw.
        string application = Path.Combine(_folder, "windows");
        Directory.CreateDirectory(application);
        foreach (string file in new[] { "order.json", "order.test-design.json", "draw.json" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ruleset", file), Path.Combine(application, file));
        }

        File.WriteAllText(Path.Combine(application, "RulealizeStudio.Avalonia.Tests.csproj"), "<Project />");
        string[] asked = [application, "--rules", "order.json", "--plugins", AppContext.BaseDirectory];

        Assert.Equal((Command.Agreed, string.Empty), Started(["replay", .. asked]));

        // #3 is an order of two being confirmed: the confirming window is shown beside the other two, and its × is change.
        (int code, string said) = Started(["show", .. asked, "--state", "#3", "--out", Path.Combine(application, "pictures")]);

        Assert.Equal(Command.Agreed, code);
        JsonObject shown = JsonNode.Parse(said)!.AsObject();
        Assert.Equal(
            ["window/ConfirmWindow.axaml", "window/DrawWindow.axaml", "window/OrderWindow.axaml"],
            shown["windows"]!.AsArray().Select(window => (string?)window!["window"]));
        Assert.Equal(["add", "add", "review"], shown["presses"]!.AsArray().Select(press => (string?)press!["step"]));
        Assert.All(shown["presses"]!.AsArray(), press => Assert.Equal("window/OrderWindow.axaml", (string?)press!["window"]));
        Assert.Equal("window/ConfirmWindow.axaml", (string?)shown["moves"]!.AsArray().Single(move => (string?)move!["step"] == "change")!["window"]);
        Assert.Equal((320, 160), Size((string)shown["windows"]![0]!["picture"]!));

        // And the list says the × as the window says it.
        (_, string listed) = Started(["show", .. asked]);
        JsonNode confirmed = JsonNode.Parse(listed)!["situations"]!.AsArray().Single(each => (string?)each!["state"] == "#3")!;
        Assert.Equal("Confirm ×", (string?)confirmed["moves"]!.AsArray().Single(move => (string?)move!["step"] == "change")!["words"]);
    }

    [Fact]
    public void AFolderWithNothingToCheckYetIsNotAFinding()
    {
        string empty = Path.Combine(_folder, "empty");
        Directory.CreateDirectory(empty);

        Assert.Equal(Command.Failed, Run("check", empty).Code);
        Assert.Equal(Command.Failed, Run("agree", empty).Code);
        Assert.Equal(Command.NotUnderstood, Command.Run(["build"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(Command.NotUnderstood, Command.Run(["show", "--state", "#1"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(Command.NotUnderstood, Command.Run(["replay", "--state", "#1", "--out", "pictures"], TextWriter.Null, TextWriter.Null));
    }

    private (int Code, string Said) Run(string verb, string? folder = null)
    {
        using StringWriter output = new();
        int code = Command.Run([verb, folder ?? _folder, "--plugins", AppContext.BaseDirectory], output, TextWriter.Null);
        return (code, output.ToString());
    }

    /// <summary>The replay, as a command line runs it: the server in a process of its own.</summary>
    private (int Code, string Said) Replay() => Started("replay", _folder, "--plugins", AppContext.BaseDirectory);

    /// <summary>The server in a process of its own, for what starts Avalonia.</summary>
    private static (int Code, string Said) Started(params string[] args)
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (string arg in args.Prepend(Path.Combine(AppContext.BaseDirectory, "RulealizeStudio.Server.dll")))
        {
            start.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(start)!;
        string said = process.StandardOutput.ReadToEnd();
        string trouble = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(trouble.Length == 0, trouble);
        return (process.ExitCode, said);
    }

    private void Edit(string file, string from, string to)
    {
        string path = Path.Combine(_folder, file);
        string text = File.ReadAllText(path);
        Assert.Contains(from, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(from, to, StringComparison.Ordinal));
    }

    /// <summary>A PNG's width and height, from its header.</summary>
    private static (int Width, int Height) Size(string picture)
    {
        byte[] header = new byte[24];
        using (FileStream file = File.OpenRead(picture))
        {
            file.ReadExactly(header);
        }

        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)));
    }

    /// <summary>A situation the change moved, said as the moves that reach it.</summary>
    private static string Route(JsonNode situation) => string.Join(" → ", situation["route"]!.AsArray().Select(step => (string?)step));

    private static string[] Lines(string said) => said.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A line as a compiler writes it, without the place and the severity: the file and what was found.</summary>
    private static string Unplaced(string line)
    {
        int place = line.IndexOf("): ", StringComparison.Ordinal);
        int open = line.LastIndexOf('(', place);
        return line[..open] + ": " + line[(line.IndexOf(" : ", place, StringComparison.Ordinal) + 3)..];
    }
}
