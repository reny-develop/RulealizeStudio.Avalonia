// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using Rulealize;

namespace RulealizeStudio.Server;

/// <summary>The language server protocol, as much of it as the extension speaks.</summary>
/// <remarks>
/// <para>
/// Whole documents, sent on every change, and what is wrong with each rule set, published as
/// diagnostics where the runtime said the fault is.
/// </para>
/// <para>
/// <c>rulealize/choose</c> answers a test design's text with a value somebody chose to try added to
/// its <c>edits</c>, one already there called with another, or one taken out, in the form Ruledger
/// reads (<see cref="Choice"/>) — so what that list's separators and layout are is read here, and
/// not in TypeScript.
/// </para>
/// <para>
/// <c>rulealize/asks</c> answers for a screen rather than a rule set: the names its XAML binds,
/// which are what the rules behind it have to give, read by <see cref="Design"/> from the XAML
/// alone — so it answers for a design whose rules are not written yet.
/// </para>
/// <para>
/// <c>rulealize/rules</c> answers the rules a rule set has, each under the name a specification's
/// elements are bound to it by (<see cref="Rules"/>), and <c>rulealize/specification</c> what a
/// specification has — each element, what it says and which of those names it is bound to
/// (<see cref="StateMachine"/>) — with the rules of the rule set in its folder and which of them no
/// element asks for, which is everything the Studio's editor draws. A specification is read as it is
/// open in the editor, and from its file when it is not, since a rule set opened alone still has
/// one beside it. <c>rulealize/edit</c> answers an edit to one, as the whole text after it
/// (<see cref="Specification.Edit"/>), so that how the format is written is written here once.
/// </para>
/// <para>
/// <c>rulealize/disagreements</c> answers where an application's specifications and its rule sets
/// disagree, every one held to every other, as <see cref="Agreement"/> compares them. The answer is
/// what to mark on each; it is the extension that marks them, since it is the extension that knows
/// when any of them changed.
/// </para>
/// <para>
/// <c>rulealize/beyond</c> answers what changed of the specification or a screen, as last committed and as written now,
/// beyond binding it to the rules (<see cref="Beyond"/>) — what the person reads before committing it.
/// </para>
/// <para>
/// The folder of vocabularies is <c>initializationOptions.plugins</c>, a path taken relative to
/// each document's own folder. When it is not given it is <see cref="Command.BuildOutput"/> beside
/// a project — an application's, whose build puts there the vocabularies the project lists — and
/// otherwise <c>plugin</c>, where <c>rulealize restore</c> puts one. A folder's vocabularies are
/// loaded once, and again after <c>workspace/didChangeWatchedFiles</c> names a file in it — so a
/// document that did not compile before its vocabularies were fetched does once they are.
/// </para>
/// </remarks>
public sealed class LanguageServer
{
    private const int FullSync = 1;

    private readonly Dictionary<string, string> _documents = [];
    private readonly Dictionary<string, (string Stamp, RuleRuntime Runtime)> _runtimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stream _output;
    private string? _plugins;

    private LanguageServer(Stream output) => _output = output;

    /// <summary>Answers messages until the client says to exit or stops sending.</summary>
    /// <param name="input">Where messages arrive.</param>
    /// <param name="output">Where answers go.</param>
    /// <param name="log">Where a message that could not be answered is reported; standard error, which a client shows as the server's output.</param>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the conversation is over.</returns>
    /// <remarks>
    /// A message this server fails over is reported and answered as an error, and the next one is
    /// read: one document it could not make sense of is no reason for every other to go unchecked,
    /// and a client whose server has gone away can only say that it is not running.
    /// </remarks>
    public static async Task RunAsync(Stream input, Stream output, TextWriter? log = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        LanguageServer server = new(output);
        while (await ReadAsync(input, cancellationToken) is { } message)
        {
            try
            {
                if (!await server.AnswerAsync(message, cancellationToken))
                {
                    return;
                }
            }
            catch (Exception wrong) when (wrong is not OperationCanceledException)
            {
                log?.WriteLine($"'{(string?)message["method"]}' failed: {wrong}");
                if (message["id"] is { } id)
                {
                    await server.WriteAsync(new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = id.DeepClone(),
                        ["error"] = new JsonObject { ["code"] = -32603, ["message"] = wrong.Message },
                    }, cancellationToken);
                }
            }
        }
    }

    private async Task<bool> AnswerAsync(JsonObject message, CancellationToken cancellationToken)
    {
        string? method = (string?)message["method"];
        JsonNode? id = message["id"]?.DeepClone();
        JsonObject? parameters = message["params"] as JsonObject;

        switch (method)
        {
            case "initialize":
                if (parameters?["initializationOptions"]?["plugins"] is JsonValue plugins
                    && (string?)plugins is { Length: > 0 } folder)
                {
                    _plugins = folder;
                }

                await RespondAsync(id, new JsonObject
                {
                    ["capabilities"] = new JsonObject { ["textDocumentSync"] = FullSync },
                    ["serverInfo"] = new JsonObject { ["name"] = "RulealizeStudio.Server" },
                }, cancellationToken);
                return true;

            case "textDocument/didOpen":
                await ChangedAsync(
                    (string)parameters!["textDocument"]!["uri"]!,
                    (string)parameters["textDocument"]!["text"]!,
                    cancellationToken);
                return true;

            case "textDocument/didChange":
                await ChangedAsync(
                    (string)parameters!["textDocument"]!["uri"]!,
                    (string)parameters["contentChanges"]!.AsArray()[^1]!["text"]!,
                    cancellationToken);
                return true;

            case "textDocument/didClose":
                string closed = (string)parameters!["textDocument"]!["uri"]!;
                _documents.Remove(closed);
                await PublishAsync(closed, "", [], cancellationToken);
                return true;

            case "workspace/didChangeWatchedFiles":
                await VocabulariesChangedAsync(parameters!["changes"]!.AsArray().Select(c => (string)c!["uri"]!), cancellationToken);
                return true;



            case "rulealize/asks":
                await RespondAsync(id, Asks((string)parameters!["uri"]!), cancellationToken);
                return true;

            case "rulealize/rules":
                await RespondAsync(id, RulesOf((string)parameters!["uri"]!), cancellationToken);
                return true;

            case "rulealize/specification":
                await RespondAsync(id, SpecificationOf(parameters!), cancellationToken);
                return true;

            case "rulealize/edit":
                (string edited, string? added) = Specification.Edit((string)parameters!["text"]!, parameters["edit"]!.AsObject());
                await RespondAsync(id, new JsonObject { ["text"] = edited, ["added"] = added }, cancellationToken);
                return true;

            case "rulealize/disagreements":
                await RespondAsync(id, DisagreementsOf(parameters!), cancellationToken);
                return true;



            case "rulealize/choose":
                await RespondAsync(id, Chosen((string)parameters!["text"]!, parameters), cancellationToken);
                return true;

            case "rulealize/beyond":
                await RespondAsync(id, BeyondBinding(parameters!), cancellationToken);
                return true;

            case "shutdown":
                await RespondAsync(id, null, cancellationToken);
                return true;

            case "exit":
                return false;

            default:
                if (id is not null)
                {
                    await WriteAsync(new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = id,
                        ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"'{method}' is not answered here." },
                    }, cancellationToken);
                }

                return true;
        }
    }

    private async Task ChangedAsync(string uri, string text, CancellationToken cancellationToken)
    {
        _documents[uri] = text;

        // Every JSON file in a workspace reaches a server that asks for them, and only a rule
        // set is this one's business. Text that is not JSON yet may be a rule set mid-edit.
        if (TryRead(text) is { IsRuleSet: false } || !text.Contains("\"rulealize/ruleset/", StringComparison.Ordinal))
        {
            await PublishAsync(uri, text, [], cancellationToken);
            return;
        }

        string beside = Path.GetDirectoryName(FilePath(uri))!;
        string folder = Path.GetFullPath(
            _plugins ?? (Directory.EnumerateFiles(beside, "*.csproj").Any() ? Command.BuildOutput : "plugin"),
            beside);
        IEnumerable<Finding> findings;
        try
        {
            findings = Check.Run(text, Runtime(folder));
        }
        catch (Exception wrong) when (wrong is PluginLoadException or IOException)
        {
            findings = [new Finding(0, 0, $"The vocabularies in '{folder}' could not be loaded: {wrong.Message}")];
        }

        await PublishAsync(uri, text, findings, cancellationToken);
    }

    /// <summary>
    /// A folder of vocabularies changed — <c>rulealize restore</c> filled it, or a build wrote it —
    /// so its runtime is loaded again and every open document is checked again against it.
    /// </summary>
    /// <remarks>
    /// The client says which files to report; this server takes any file it is told about as a
    /// change to the folder that holds it, and forgets only the runtimes loaded from such a folder.
    /// </remarks>
    private async Task VocabulariesChangedAsync(IEnumerable<string> uris, CancellationToken cancellationToken)
    {
        foreach (string uri in uris)
        {
            _runtimes.Remove(Path.GetDirectoryName(FilePath(uri))!);
        }

        foreach ((string uri, string text) in _documents.ToArray())
        {
            await ChangedAsync(uri, text, cancellationToken);
        }
    }

    /// <summary>The file a <c>file:</c> URI names.</summary>
    /// <remarks>
    /// Not the local path .NET reads out of one: VS Code writes a Windows drive as <c>file:///c%3A/…</c>,
    /// and that leaves the colon escaped, which makes a path that is not rooted anywhere.
    /// </remarks>
    private static string FilePath(string uri)
    {
        Uri parsed = new(uri);
        string path = Uri.UnescapeDataString(parsed.AbsolutePath);

        if (path.Length >= 3 && path[0] == '/' && path[2] == ':' && char.IsAsciiLetter(path[1]))
        {
            path = path[1..];
        }
        else if (parsed.Host.Length > 0)
        {
            path = $"//{parsed.Host}{path}";
        }

        return Path.GetFullPath(path);
    }

    /// <summary>The map, or nothing where the text is not JSON — which <see cref="Check"/> reports, at the place the reader stopped.</summary>
    private static DocumentMap? TryRead(string text)
    {
        try
        {
            return DocumentMap.Read(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// One runtime per folder, loaded again only when the folder is built again — a sweep is the slow
    /// part, and an edit does not change the folder — and loaded without holding its files, so that
    /// it can be built again while this runs (<see cref="Vocabularies"/>).
    /// </summary>
    private RuleRuntime Runtime(string folder)
    {
        string stamp = Vocabularies.Stamp(folder);
        if (!_runtimes.TryGetValue(folder, out (string Stamp, RuleRuntime Runtime) loaded) || loaded.Stamp != stamp)
        {
            loaded = (stamp, Vocabularies.Load(folder));
            _runtimes[folder] = loaded;
        }

        return loaded.Runtime;
    }

    /// <summary>
    /// A test design's text with a choice added to it, one of its choices called with other values,
    /// or one taken out: <c>state</c> and <c>input</c> add one, <c>at</c> says which otherwise, and
    /// <c>at</c> with no <c>args</c> takes it out.
    /// </summary>
    /// <remarks>
    /// The text is handed in and the text answered, rather than edits to a document held open: a
    /// design is written by <c>ruledger derive</c> straight after, and a copy held here would be one
    /// that write had not reached yet.
    /// </remarks>
    private static string Chosen(string design, JsonObject parameters)
    {
        Dictionary<string, string>? args = (parameters["args"] as JsonObject)?.ToDictionary(
            arg => arg.Key,
            arg => (string?)arg.Value ?? throw new ArgumentException($"The value for '{arg.Key}' is written as text."),
            StringComparer.Ordinal);

        IEnumerable<Change> changes = (parameters["at"], args) switch
        {
            ({ } at, null) => Choice.Remove(design, (int)at),
            ({ } at, _) => [Choice.Change(design, (int)at, args)],
            _ => [Choice.Add(design, (string)parameters["state"]!, (string)parameters["input"]!, args ?? [])],
        };

        // Placed in the text as it was before any of them, so the last is made first.
        foreach (Change change in changes.OrderByDescending(change => change.Start))
        {
            design = string.Concat(design.AsSpan(0, change.Start), change.Text, design.AsSpan(change.Start + change.Length));
        }

        return design;
    }

    /// <summary>
    /// What changed of a file of the blueprint beyond binding it (<see cref="Beyond"/>): a
    /// specification, by what it says it is, or a screen, by its name. Nothing for any other file, and nothing for one that is not
    /// read as it is meant to be yet — what is wrong with it is said where it is open.
    /// </summary>
    private static JsonArray BeyondBinding(JsonObject parameters)
    {
        string name = (string)parameters["name"]!;
        string? before = (string?)parameters["before"];
        string after = (string)parameters["after"]!;
        try
        {
            ImmutableArray<string> said = Specification.Is(after) || (before is not null && Specification.Is(before)) ? Beyond.Specification(before, after)
                : name.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ? Beyond.Screen(before, after)
                : [];
            return [.. said.Select(s => (JsonNode)s)];
        }
        catch (Exception wrong) when (wrong is JsonException or FormatException)
        {
            return [];
        }
    }

    private JsonArray Asks(string uri)
    {
        if (!_documents.TryGetValue(uri, out string? text))
        {
            return [];
        }

        ImmutableArray<Ask> read;
        try
        {
            read = Design.Read(text);
        }
        catch (XmlException)
        {
            // Mid-edit: what it binds is said again once it is XML.
            return [];
        }

        JsonArray asks = [];
        foreach (Ask ask in read)
        {
            asks.Add(new JsonObject
            {
                ["kind"] = ask.Kind,
                ["name"] = ask.Name,
                ["range"] = Range(text, ask.Start, ask.Length),
            });
        }

        return asks;
    }

    private JsonArray RulesOf(string uri)
    {
        if (!_documents.TryGetValue(uri, out string? text) || TryRead(text) is not { IsRuleSet: true })
        {
            return [];
        }

        JsonArray rules = [];
        foreach (Rule rule in Rules.Read(text))
        {
            rules.Add(new JsonObject
            {
                ["name"] = rule.Name,
                ["range"] = Range(text, rule.Start, rule.Length),
            });
        }

        return rules;
    }

    /// <summary>
    /// What a specification has, as the Studio's editor draws it: each element, how it stands to the
    /// others and which rules it is bound to, whether the rules have each; every rule of the rule sets
    /// beside it, to bind, each by the rule set it is in where there is more than one; and which of
    /// them nothing asks for. Nothing where it is not a specification in a format the Studio knows, or
    /// not JSON just now.
    /// </summary>
    /// <remarks>
    /// Which of them nothing asks for is said of this specification alone; that nothing in any of the
    /// application's asks for it is <see cref="DisagreementsOf"/>'.
    /// </remarks>
    private JsonObject? SpecificationOf(JsonObject parameters)
    {
        if (TextOf((string)parameters["uri"]!) is not { } text || TryRead(text) is null || !Specification.Is(text))
        {
            return null;
        }

        (string Name, string Text)[] ruleSets = [.. Uris(parameters, "ruleSets", "rules")
            .Select(uri => (Name: Path.GetFileNameWithoutExtension(FilePath(uri)), Text: TextOf(uri)))
            .Where(each => each.Text is not null && TryRead(each.Text) is { IsRuleSet: true })
            .Select(each => (each.Name, each.Text!))];
        bool one = ruleSets.Length == 1;
        string? rules = ruleSets.Length == 0 && Uris(parameters, "ruleSets", "rules").Length == 0 ? null : string.Empty;

        // What a binding may say for each rule: by the rule set it is in, and without it where there is one.
        List<(string Shown, string[] Said)> named = [];
        foreach ((string ruleSet, string written) in ruleSets)
        {
            foreach (Rule rule in Rules.Read(written))
            {
                string qualified = $"{ruleSet}{Agreement.Separator}{rule.Name}";
                named.Add(one ? (rule.Name, [rule.Name, qualified]) : (qualified, [qualified]));
            }
        }

        HashSet<string> sayable = [.. named.SelectMany(n => n.Said)];
        string[] names = [.. named.Select(n => n.Shown)];
        Machine machine = StateMachine.Read(text);

        JsonArray elements = [];
        foreach (Element element in machine.Elements)
        {
            Shape shape = machine.Shapes[element.Id];
            elements.Add(new JsonObject
            {
                ["id"] = element.Id,
                ["kind"] = element.Kind,
                ["name"] = element.Title,
                ["says"] = element.Says,
                ["guard"] = shape.Guard,
                ["from"] = shape.From,
                ["to"] = shape.To,
                ["on"] = shape.On,
                ["final"] = shape.Final,
                ["rules"] = new JsonArray([.. element.Rules.Select(r => new JsonObject { ["name"] = r.Rule, ["known"] = rules is null || sayable.Contains(r.Rule) })]),
            });
        }

        HashSet<string> asked = [.. machine.Elements.SelectMany(e => e.Rules.Select(r => r.Rule))];
        return new JsonObject
        {
            ["schema"] = StateMachine.Schema,
            ["initial"] = machine.Initial,
            ["elements"] = elements,
            ["rules"] = new JsonArray([.. names.Select(n => JsonValue.Create(n))]),
            ["unasked"] = new JsonArray([.. named.Where(n => !n.Said.Any(asked.Contains)).Select(n => JsonValue.Create(n.Shown))]),
            ["faults"] = new JsonArray([.. machine.Faults.Select(f => JsonValue.Create(f.Message))]),
        };
    }

    /// <summary>
    /// What to mark on each of an application's specifications and rule sets, all held to all, by
    /// document; or, asked with one specification and the rule set beside it, on those two. Nothing
    /// where one of them is not JSON yet, or a specification is not there.
    /// </summary>
    private JsonObject? DisagreementsOf(JsonObject parameters)
    {
        if (parameters["specifications"] is JsonArray)
        {
            List<(string Uri, Written File)> specifications = [];
            List<(string Uri, Written File)> ruleSets = [];
            foreach ((string key, List<(string, Written)> into) in new[] { ("specifications", specifications), ("ruleSets", ruleSets) })
            {
                foreach (string document in Uris(parameters, key, key))
                {
                    if (TextOf(document) is not { } written || TryRead(written) is not { } read)
                    {
                        return null;
                    }

                    if (key == "ruleSets" ? read.IsRuleSet : Specification.Is(written))
                    {
                        into.Add((document, new Written(Path.GetFileName(FilePath(document)), written)));
                    }
                }
            }

            IReadOnlyDictionary<string, ImmutableArray<Finding>> marked =
                Agreement.Compare([.. specifications.Select(each => each.File)], [.. ruleSets.Select(each => each.File)]);
            return new JsonObject
            {
                ["documents"] = new JsonArray([.. specifications.Concat(ruleSets).Select(each => new JsonObject
                {
                    ["uri"] = each.Uri,
                    ["marks"] = Marks(each.File.Text, marked[each.File.Name]),
                })]),
            };
        }

        if (TextOf((string)parameters["specification"]!) is not { } specification || TryRead(specification) is null || !Specification.Is(specification))
        {
            return null;
        }

        string? rules = parameters["rules"] is JsonValue uri ? TextOf((string)uri!) : null;
        if (rules is not null && TryRead(rules) is null)
        {
            return null;
        }

        Disagreements found = Agreement.Compare(specification, rules);
        return new JsonObject
        {
            ["specification"] = Marks(specification, found.Specification),
            ["rules"] = rules is null ? new JsonArray() : Marks(rules, found.Rules),
        };
    }

    /// <summary>The documents a request names under one key, as a list, or as the one an older form of it names under another.</summary>
    private static string[] Uris(JsonObject parameters, string many, string one) => parameters[many] switch
    {
        JsonArray uris => [.. uris.Select(uri => (string)uri!)],
        _ => parameters[one] is JsonValue uri ? [(string)uri!] : [],
    };

    private static JsonArray Marks(string text, IEnumerable<Finding> findings) =>
        [.. findings.Select(f => new JsonObject { ["range"] = Range(text, f.Start, f.Length), ["message"] = f.Message })];

    /// <summary>A document as it is open in the editor, or as its file says where it is not open; nothing where there is neither.</summary>
    private string? TextOf(string uri)
    {
        if (_documents.TryGetValue(uri, out string? text))
        {
            return text;
        }

        string file = FilePath(uri);
        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    private Task PublishAsync(string uri, string text, IEnumerable<Finding> findings, CancellationToken cancellationToken)
    {
        JsonArray diagnostics = [];
        foreach (Finding finding in findings)
        {
            diagnostics.Add(new JsonObject
            {
                ["range"] = Range(text, finding.Start, finding.Length),
                ["severity"] = 1,
                ["source"] = "rulealize",
                ["message"] = finding.Message,
            });
        }

        return WriteAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject { ["uri"] = uri, ["diagnostics"] = diagnostics },
        }, cancellationToken);
    }

    /// <summary>A place in the text as the protocol names one: lines, and UTF-16 code units into them, both from zero.</summary>
    private static JsonObject Range(string text, int start, int length) => new()
    {
        ["start"] = Position(text, start),
        ["end"] = Position(text, start + length),
    };

    private static JsonObject Position(string text, int offset)
    {
        int line = 0;
        int from = 0;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                from = i + 1;
            }
        }

        return new JsonObject { ["line"] = line, ["character"] = offset - from };
    }

    private Task RespondAsync(JsonNode? id, JsonNode? result, CancellationToken cancellationToken) =>
        WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }, cancellationToken);

    private async Task WriteAsync(JsonObject message, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        await _output.WriteAsync(header, cancellationToken);
        await _output.WriteAsync(body, cancellationToken);
        await _output.FlushAsync(cancellationToken);
    }

    /// <summary>Reads one framed message, or nothing when the stream has ended.</summary>
    private static async Task<JsonObject?> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        int length = -1;
        StringBuilder line = new();
        byte[] one = new byte[1];

        while (true)
        {
            if (await input.ReadAsync(one, cancellationToken) == 0)
            {
                return null;
            }

            if (one[0] != '\n')
            {
                if (one[0] != '\r')
                {
                    line.Append((char)one[0]);
                }

                continue;
            }

            if (line.Length == 0)
            {
                break;
            }

            string header = line.ToString();
            line.Clear();
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(header["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        byte[] body = new byte[length];
        await input.ReadExactlyAsync(body, cancellationToken);
        return JsonNode.Parse(body)!.AsObject();
    }
}
