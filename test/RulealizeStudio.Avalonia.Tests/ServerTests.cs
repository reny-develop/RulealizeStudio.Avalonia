// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using Rulealize;
using RulealizeStudio.Server;

namespace RulealizeStudio.Tests;

/// <summary>What the extension is told about a rule set, by the runtime rather than by TypeScript.</summary>
/// <remarks>
/// A choice is written into a test design by putting an entry into a list or taking one out, so
/// what is worth holding here is that a list is the document's own, under the document's own names,
/// and that changing one is replacing characters — which leaves everything else written, comments
/// included, where it was. And that what is wrong with a rule set is said where the runtime found it.
/// </remarks>
public class ServerTests
{
    [Fact]
    public void AListIsAnyArrayTheDocumentWritesAndItsEntriesAreNamedByPlace()
    {
        DocumentMap map = DocumentMap.Read(Text("signup.json"));

        Listing validate = Assert.Single(map.Lists, l => l.Pointer == "/inputs/setParty/validate");
        Entry clause = Assert.Single(validate.Entries);
        Assert.Equal("inputs › setParty › validate 1", clause.Name);
        Assert.Contains("party.unchanged", clause.Text, StringComparison.Ordinal);

        Listing seats = Assert.Single(map.Lists, l => l.Pointer == "/inputs/chooseSeat/params/seat/domain/of");
        Assert.Equal(["\"window\"", "\"aisle\""], seats.Entries.Select(e => e.Text));

        Assert.DoesNotContain(map.Lists, l => l.Pointer.StartsWith("/requires", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("countdown.json")]
    [InlineData("signup.json")]
    public void AnEntryTakenOutAndPutBackLeavesTheDocumentAsItWas(string document)
    {
        string text = Text(document);

        foreach (Listing list in DocumentMap.Read(text).Lists)
        {
            for (int i = 0; i < list.Entries.Length; i++)
            {
                Entry entry = list.Entries[i];
                string without = Apply(text, DocumentMap.Read(text).Remove(entry.Pointer));
                Assert.DoesNotContain(DocumentMap.Read(without).Lists.Single(l => l.Pointer == list.Pointer).Entries, e => e.Text == entry.Text && e.Start == entry.Start);

                string back = Apply(without, [DocumentMap.Read(without).Insert(list.Pointer, i, entry.Text)]);
                Assert.Equal(text, back);
            }
        }
    }

    [Fact]
    public void TheLastClauseOfAListTakesItsLineWithIt()
    {
        string text = Text("signup.json");

        string without = Apply(text, DocumentMap.Read(text).Remove("/inputs/setParty/validate/0"));

        Assert.DoesNotContain("party.unchanged", without, StringComparison.Ordinal);
        Assert.Contains("\"validate\": [\r\n      ],", without.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
        Assert.Empty(Check.Run(without, Runtime()));
    }

    [Fact]
    public void AnEntryAddedIsWrittenTheWayTheListSeparatesThem()
    {
        string text = Text("signup.json");
        DocumentMap map = DocumentMap.Read(text);

        string added = Apply(text, [map.Insert("/inputs/chooseSeat/params/seat/domain/of", 2, "\"aisle\"")]);

        Assert.Contains("\"of\": [\"window\", \"aisle\", \"aisle\"]", added, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentThatDoesNotCompileIsMarkedWhereTheRuntimeSaidTheFaultIs()
    {
        string text = Text("countdown.json").Replace("\"cmp.lte\"", "\"cmp.lt3\"", StringComparison.Ordinal);

        Finding finding = Assert.Single(Check.Run(text, Runtime()));

        Assert.Contains("cmp.lt3", finding.Message, StringComparison.Ordinal);
        int when = text.IndexOf("\"when\": {", StringComparison.Ordinal);
        Assert.Equal(text.IndexOf('{', when), finding.Start);
    }

    [Fact]
    public void TextThatIsNotJsonIsMarkedWhereItStoppedBeingJson()
    {
        string text = "{\n  \"id\": \"x\",\n  \"version\" \"1\"\n}";

        Finding finding = Assert.Single(Check.Run(text, Runtime()));

        Assert.Equal(2, text[..finding.Start].Count(c => c == '\n'));
    }

    [Fact]
    public async Task TheExtensionIsAnsweredOverTheLanguageServerProtocol()
    {
        string uri = new Uri(Path.Combine(AppContext.BaseDirectory, "ruleset", "countdown.json")).AbsoluteUri;
        string broken = Text("countdown.json").Replace("\"cmp.lte\"", "\"cmp.lt3\"", StringComparison.Ordinal);

        List<JsonObject> answers = await Converse(
            Message(1, "initialize", new JsonObject { ["initializationOptions"] = new JsonObject { ["plugins"] = AppContext.BaseDirectory } }),
            Message(null, "textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["text"] = broken } }),
            Message(null, "textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = Text("countdown.json") }),
            }),
            Message(2, "rulealize/rules", new JsonObject { ["uri"] = uri }),
            Message(3, "shutdown", null),
            Message(null, "exit", null));

        JsonNode[] diagnostics = [.. answers
            .Where(a => (string?)a["method"] == "textDocument/publishDiagnostics")
            .Select(a => a["params"]!["diagnostics"]!)];
        Assert.Equal(2, diagnostics.Length);
        Assert.Contains("cmp.lt3", (string)diagnostics[0].AsArray().Single()!["message"]!, StringComparison.Ordinal);
        Assert.Empty(diagnostics[1].AsArray());

        // Answered from the text as it was changed to, not as it was opened.
        JsonArray rules = answers.Single(a => (int?)a["id"] == 2)["result"]!.AsArray();
        Assert.Contains(rules, r => (string?)r!["name"] == "/inputs/add");
    }

    [Fact]
    public async Task AValueToTryIsWrittenIntoATestDesignInTheFormRuledgerReads()
    {
        string design = Text("signup.test-design.json");
        IReadOnlyList<Choice> before = Choice.Read(design);

        List<JsonObject> answers = await Converse(
            Message(1, "initialize", null),
            Message(2, "rulealize/choose", new JsonObject { ["text"] = design, ["state"] = "#1", ["input"] = "setName", ["args"] = new JsonObject { ["to"] = "bob" } }),
            Message(3, "rulealize/choose", new JsonObject { ["text"] = design, ["at"] = 0, ["args"] = new JsonObject { ["to"] = "carol" } }),
            Message(4, "rulealize/choose", new JsonObject { ["text"] = design, ["at"] = 1 }),
            Message(null, "exit", null));
        string Answer(int id) => (string)answers.Single(a => (int?)a["id"] == id)["result"]!;

        // Added last, with nothing said about whether it was carried: that is Ruledger's to write.
        string added = Answer(2);
        Choice bob = Choice.Read(added)[^1];
        Assert.Equal(before.Count + 1, Choice.Read(added).Count);
        Assert.Equal(("#1", "setName", "setName(to: bob)", "yes"), (bob.State, bob.Input, bob.Step, bob.Carried));
        Assert.Contains("{ \"state\": \"#1\", \"input\": \"setName\", \"args\": { \"to\": \"bob\" } }", added, StringComparison.Ordinal);
        Assert.EndsWith(design[design.IndexOf("\"states\": [", StringComparison.Ordinal)..], added, StringComparison.Ordinal);

        // Called with another value, from where it was made and for the input it was made for.
        Choice carol = Choice.Read(Answer(3))[0];
        Assert.Equal((before[0].State, "setName(to: carol)"), (carol.State, carol.Step));
        Assert.Equal(before.Count, Choice.Read(Answer(3)).Count);

        // Taken out, and the design is still JSON with every other choice in it.
        Assert.Equal([.. before.Where(c => c.At != 1).Select(c => c.Step)], Choice.Read(Answer(4)).Select(c => c.Step));
    }

    [Fact]
    public async Task ADocumentIsFoundByTheUriVsCodeWritesForIt()
    {
        // VS Code escapes a Windows drive's colon, file:///c%3A/…, where .NET would not.
        string path = Path.Combine(AppContext.BaseDirectory, "ruleset", "countdown.json");
        string uri = new Uri(path).AbsoluteUri;
        if (OperatingSystem.IsWindows())
        {
            uri = $"file:///{char.ToLowerInvariant(path[0])}%3A{uri["file:///C:".Length..]}";
        }

        string broken = Text("countdown.json").Replace("\"cmp.lte\"", "\"cmp.lt3\"", StringComparison.Ordinal);

        List<JsonObject> answers = await Converse(
            Message(1, "initialize", new JsonObject { ["initializationOptions"] = new JsonObject { ["plugins"] = ".." } }),
            Message(null, "textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["text"] = broken } }),
            Message(null, "exit", null));

        // The folder is relative to the document, so it was found only if the document was.
        JsonObject published = answers.Single(a => (string?)a["method"] == "textDocument/publishDiagnostics");
        Assert.Equal(uri, (string?)published["params"]!["uri"]);
        Assert.Contains("cmp.lt3", (string)published["params"]!["diagnostics"]!.AsArray().Single()!["message"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMessageTheServerFailsOverIsAnsweredAsAnErrorAndTheNextIsRead()
    {
        StringWriter log = new();

        List<JsonObject> answers = await Converse(
            log,
            Message(1, "initialize", null),
            Message(2, "rulealize/rules", new JsonObject()),
            Message(3, "shutdown", null),
            Message(null, "exit", null));

        Assert.NotNull(answers.Single(a => (int?)a["id"] == 2)["error"]);
        Assert.Contains(answers, a => (int?)a["id"] == 3 && a.ContainsKey("result"));
        Assert.Contains("rulealize/rules", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonThatIsNotARuleSetIsLeftAlone()
    {
        string uri = new Uri(Path.Combine(AppContext.BaseDirectory, "ruleset", "countdown.test-design.json")).AbsoluteUri;

        List<JsonObject> answers = await Converse(
            Message(1, "initialize", new JsonObject { ["initializationOptions"] = new JsonObject { ["plugins"] = AppContext.BaseDirectory } }),
            Message(null, "textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["text"] = Text("countdown.test-design.json") } }),
            Message(2, "rulealize/rules", new JsonObject { ["uri"] = uri }),
            Message(null, "exit", null));

        Assert.Empty(answers.Single(a => (string?)a["method"] == "textDocument/publishDiagnostics")["params"]!["diagnostics"]!.AsArray());
        Assert.Empty(answers.Single(a => (int?)a["id"] == 2)["result"]!.AsArray());
    }

    [Fact]
    public async Task ADocumentIsCheckedAgainOnceItsVocabulariesAreThere()
    {
        string folder = Directory.CreateTempSubdirectory("rulealize-").FullName;
        try
        {
            string path = Path.Combine(folder, "countdown.json");
            File.WriteAllText(path, Text("countdown.json"));
            string uri = new Uri(path).AbsoluteUri;

            using Conversation server = new();
            await server.SendAsync(Message(1, "initialize", null));
            await server.SendAsync(Message(null, "textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["text"] = Text("countdown.json") } }));
            Assert.NotEmpty((await server.DiagnosticsAsync()).AsArray());

            // What 'rulealize restore' does, and what a client watching the folder then says.
            string plugin = Directory.CreateDirectory(Path.Combine(folder, "plugin")).FullName;
            foreach (string dll in Directory.GetFiles(AppContext.BaseDirectory, "Rulealize.Plugin.*.dll"))
            {
                File.Copy(dll, Path.Combine(plugin, Path.GetFileName(dll)));
            }

            await server.SendAsync(Message(null, "workspace/didChangeWatchedFiles", new JsonObject
            {
                ["changes"] = new JsonArray(new JsonObject { ["uri"] = new Uri(Path.Combine(plugin, "Rulealize.Plugin.State.dll")).AbsoluteUri, ["type"] = 1 }),
            }));

            Assert.Empty((await server.DiagnosticsAsync()).AsArray());
        }
        finally
        {
            // The vocabularies loaded from it are held open by this process on Windows.
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception held) when (held is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task WhatAScreenAsksOfItsRulesIsAnsweredOverTheProtocol()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "design", "RulealizeStudio.Sample.Signup", "MainWindow.axaml");
        string uri = new Uri(path).AbsoluteUri;
        string text = File.ReadAllText(path);

        List<JsonObject> answers = await Converse(
            Message(1, "initialize", new JsonObject()),
            Message(null, "textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["text"] = text } }),
            Message(2, "rulealize/asks", new JsonObject { ["uri"] = uri }),
            Message(null, "textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text[..^20] }),
            }),
            Message(3, "rulealize/asks", new JsonObject { ["uri"] = uri }),
            Message(null, "exit", null));

        JsonNode to = answers.Single(a => (int?)a["id"] == 2)["result"]!.AsArray()
            .First(a => (string?)a!["name"] == "SetName.To")!;
        Assert.Equal("parameter", (string?)to["kind"]);
        string line = text.Split('\n')[(int)to["range"]!["start"]!["line"]!];
        Assert.Equal("{Binding SetName.To}", line[(int)to["range"]!["start"]!["character"]!..(int)to["range"]!["end"]!["character"]!]);

        // A design cut off mid-edit is not XML, and asks nothing until it is again.
        Assert.Empty(answers.Single(a => (int?)a["id"] == 3)["result"]!.AsArray());
        Assert.All(
            answers.Where(a => (string?)a["method"] == "textDocument/publishDiagnostics"),
            a => Assert.Empty(a["params"]!["diagnostics"]!.AsArray()));
    }

    [Fact]
    public async Task ARuleSetsRulesAndWhatItsSpecificationHasAreAnsweredOverTheProtocol()
    {
        string rules = Path.Combine(AppContext.BaseDirectory, "ruleset", "countdown.json");
        string specification = SpecificationOf("RulealizeStudio.Sample.Countdown");
        string text = File.ReadAllText(rules);
        string unasked = text.Replace("\"terminal\": {", "\"projections\": { \"left\": \"$total\" },\n  \"terminal\": {", StringComparison.Ordinal);

        // The specification is not open: a rule set opened alone still has the one beside it.
        List<JsonObject> answers = await Converse(
            Message(1, "initialize", new JsonObject()),
            Message(null, "textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = new Uri(rules).AbsoluteUri, ["text"] = unasked } }),
            Message(2, "rulealize/rules", new JsonObject { ["uri"] = new Uri(rules).AbsoluteUri }),
            Message(3, "rulealize/specification", new JsonObject { ["uri"] = new Uri(specification).AbsoluteUri, ["rules"] = new Uri(rules).AbsoluteUri }),
            Message(4, "rulealize/specification", new JsonObject { ["uri"] = new Uri(rules).AbsoluteUri }),
            Message(null, "exit", null));

        JsonNode terminal = answers.Single(a => (int?)a["id"] == 2)["result"]!.AsArray().Single(r => (string?)r!["name"] == "/terminal")!;
        Assert.Equal("\"terminal\": {", unasked.Split('\n')[(int)terminal["range"]!["start"]!["line"]!].Trim());

        JsonNode machine = answers.Single(a => (int?)a["id"] == 3)["result"]!;
        Assert.Equal(StateMachine.Schema, (string?)machine["schema"]);
        Assert.Equal("counting", (string?)machine["initial"]);
        JsonNode step = machine["elements"]!.AsArray().Single(e => (string?)e!["id"] == "step")!;
        Assert.Equal("transition", (string?)step["kind"]);
        Assert.Equal("counting", (string?)step["to"]);
        Assert.Equal(["/inputs/add", "/inputs/add/params/n", "/inputs/add/effects"], step["rules"]!.AsArray().Select(r => (string?)r!["name"]));
        Assert.All(step["rules"]!.AsArray(), r => Assert.True((bool)r!["known"]!));
        Assert.Equal(["/projections/left"], machine["unasked"]!.AsArray().Select(r => (string?)r));
        Assert.Contains("/projections/left", machine["rules"]!.AsArray().Select(r => (string?)r));

        // A rule set is not a specification.
        Assert.Null(answers.Single(a => (int?)a["id"] == 4)["result"]);
    }

    [Fact]
    public async Task EverySpecificationAndRuleSetOfAnApplicationIsMarkedHeldToAll()
    {
        string countdown = Path.Combine(AppContext.BaseDirectory, "ruleset", "countdown.json");
        string signup = Path.Combine(AppContext.BaseDirectory, "ruleset", "signup.json");
        string specification = SpecificationOf("RulealizeStudio.Sample.Countdown");

        List<JsonObject> answers = await Converse(
            Message(1, "initialize", new JsonObject()),
            Message(2, "rulealize/disagreements", new JsonObject
            {
                ["specifications"] = new JsonArray(new Uri(specification).AbsoluteUri),
                ["ruleSets"] = new JsonArray(new Uri(countdown).AbsoluteUri, new Uri(signup).AbsoluteUri),
            }),
            Message(3, "rulealize/specification", new JsonObject
            {
                ["uri"] = new Uri(specification).AbsoluteUri,
                ["ruleSets"] = new JsonArray(new Uri(countdown).AbsoluteUri, new Uri(signup).AbsoluteUri),
            }),
            Message(null, "exit", null));

        JsonArray documents = answers.Single(a => (int?)a["id"] == 2)["result"]!["documents"]!.AsArray();
        Assert.Equal([new Uri(specification).AbsoluteUri, new Uri(countdown).AbsoluteUri, new Uri(signup).AbsoluteUri], documents.Select(d => (string?)d!["uri"]));
        Assert.Contains(
            documents[0]!["marks"]!.AsArray(),
            m => (string?)m!["message"] == "'/terminal' could be a rule of any of countdown, signup: say which, as 'countdown#/terminal'.");

        // With two rule sets every rule is offered by the rule set it is in, and a binding naming none is known by neither.
        JsonNode machine = answers.Single(a => (int?)a["id"] == 3)["result"]!;
        Assert.Contains("countdown#/terminal", machine["rules"]!.AsArray().Select(r => (string?)r));
        Assert.Contains("signup#/inputs/book", machine["unasked"]!.AsArray().Select(r => (string?)r));
        Assert.All(machine["elements"]!.AsArray().SelectMany(e => e!["rules"]!.AsArray()), r => Assert.False((bool)r!["known"]!));
    }

    [Fact]
    public async Task AnEditToASpecificationIsAnsweredAsTheWholeTextAfterIt()
    {
        List<JsonObject> answers = await Converse(
            Message(1, "initialize", new JsonObject()),
            Message(2, "rulealize/edit", new JsonObject
            {
                ["text"] = File.ReadAllText(SpecificationOf("RulealizeStudio.Sample.Countdown")),
                ["edit"] = new JsonObject { ["op"] = "add", ["kind"] = "state" },
            }),
            Message(null, "exit", null));

        JsonNode answer = answers.Single(a => (int?)a["id"] == 2)["result"]!;
        Assert.Equal("state-1", (string?)answer["added"]);
        Assert.Contains("state-1", Specification.Read((string)answer["text"]!).Select(e => e.Id));
    }

    private static string SpecificationOf(string application) =>
        Path.Combine(AppContext.BaseDirectory, "specification", application, Specification.File);

    private static string Text(string document) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ruleset", document));

    /// <summary>Changes placed in the text before any of them, applied the way a client applies one document's edits.</summary>
    private static string Apply(string text, IEnumerable<Change> changes) =>
        changes.OrderByDescending(c => c.Start).Aggregate(text, (t, c) => t[..c.Start] + c.Text + t[(c.Start + c.Length)..]);

    private static RuleRuntime Runtime() => new RuleRuntime().LoadPluginsFrom(AppContext.BaseDirectory);

    private static JsonObject Message(int? id, string method, JsonObject? parameters)
    {
        JsonObject message = new() { ["jsonrpc"] = "2.0", ["method"] = method };
        if (id is not null)
        {
            message["id"] = id;
        }

        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        return message;
    }

    private static Task<List<JsonObject>> Converse(params JsonObject[] messages) => Converse(null, messages);

    private static async Task<List<JsonObject>> Converse(TextWriter? log, params JsonObject[] messages)
    {
        MemoryStream input = new();
        foreach (JsonObject message in messages)
        {
            byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
            input.Write(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
            input.Write(body);
        }

        input.Position = 0;
        MemoryStream output = new();
        await LanguageServer.RunAsync(input, output, log);

        // Content-Length counts bytes, and a name has '›' in it.
        List<JsonObject> answers = [];
        byte[] bytes = output.ToArray();
        int at = 0;
        while (at < bytes.Length)
        {
            int blank = bytes.AsSpan(at).IndexOf("\r\n\r\n"u8) + at;
            int length = int.Parse(Encoding.ASCII.GetString(bytes, at + "Content-Length: ".Length, blank - at - "Content-Length: ".Length), System.Globalization.CultureInfo.InvariantCulture);
            answers.Add(JsonNode.Parse(bytes.AsSpan(blank + 4, length))!.AsObject());
            at = blank + 4 + length;
        }

        return answers;
    }

    /// <summary>A server spoken to one message at a time, for what has to happen between two of them.</summary>
    private sealed class Conversation : IDisposable
    {
        private readonly AnonymousPipeServerStream _toServer = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _fromServer = new(PipeDirection.In);
        private readonly Task _running;

        public Conversation()
        {
            AnonymousPipeClientStream input = new(PipeDirection.In, _toServer.ClientSafePipeHandle);
            AnonymousPipeClientStream output = new(PipeDirection.Out, _fromServer.ClientSafePipeHandle);
            _running = Task.Run(async () =>
            {
                await LanguageServer.RunAsync(input, output);
                output.Dispose();
            });
        }

        public async Task SendAsync(JsonObject message)
        {
            byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
            await _toServer.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
            await _toServer.WriteAsync(body);
            await _toServer.FlushAsync();
        }

        /// <summary>The diagnostics the server publishes next, skipping anything else it says.</summary>
        public async Task<JsonNode> DiagnosticsAsync()
        {
            while (true)
            {
                JsonObject said = await ReadAsync();
                if ((string?)said["method"] == "textDocument/publishDiagnostics")
                {
                    return said["params"]!["diagnostics"]!;
                }
            }
        }

        private async Task<JsonObject> ReadAsync()
        {
            StringBuilder header = new();
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                int b = _fromServer.ReadByte();
                Assert.NotEqual(-1, b);
                header.Append((char)b);
            }

            int length = int.Parse(header.ToString()["Content-Length: ".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            byte[] body = new byte[length];
            await _fromServer.ReadExactlyAsync(body);
            return JsonNode.Parse(body)!.AsObject();
        }

        public void Dispose()
        {
            _toServer.Dispose();
            _running.Wait(TimeSpan.FromSeconds(10));
            _fromServer.Dispose();
        }
    }
}
