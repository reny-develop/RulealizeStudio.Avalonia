// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Rulealize;
using RulealizeStudio.Binding;
using RulealizeStudio.Hosting;

namespace RulealizeStudio.Server;

/// <summary>What the editor shows, asked from the command line of an application's folder.</summary>
/// <remarks>
/// <para>
/// Three of the checks an application is finished by, each the code the editor runs for the same
/// question rather than a second account of it: whether the rule set compiles (<see cref="Check"/>),
/// whether the specification and the rules agree (<see cref="Agreement"/>), and whether the screen does
/// what the test design says (<see cref="Replay"/>, on the application as it was last built). The
/// other two are <c>dotnet build</c> and <c>ruledger diff</c>, which are already one command each.
/// </para>
/// <para>
/// And one that is not a check: <c>show</c>, which the editor runs to draw the test design as the
/// application's own window — the situations listed by the moves that reach them, each move said
/// as well as the window says it where the application is built (<see cref="Reach.Say(IEnumerable{Visual}, RuleModel, string)"/>), with all the
/// design records at each — the values somebody chose to try there among it — or one of them stood in by <see cref="Hosting.Replay"/>, with a picture
/// of the window before each press, one of where it stands, and one of each refusal there as the
/// window drew it. It runs in a process of its own for the reason <c>replay</c> does, which also
/// makes the window the one last built every time it is asked for. With <c>--design</c> it stands
/// the window in another design than the one beside the rules: the one the rules give after a change.
/// </para>
/// <para>
/// And <c>changes</c>, what the rules as saved decide otherwise than the design beside them —
/// Ruledger's diff, by <see cref="Changes"/> — which the editor runs to show what a change did, as
/// the window before it and after it. The editor gives it, with <c>--was</c>, the rule set as last
/// committed, and with <c>--design</c> the design committed with it: the design before the change
/// is derived from those rules, carrying that design's choices, so that what a change did is read
/// against what was last committed — whether or not the design beside the rules has been derived
/// again since, and whether or not it was derived again before that commit.
/// </para>
/// <para>
/// And <c>design</c>, which the editor runs for as long as the screen is open in it, before there
/// are any rules or after: a window drawn from its XAML as it is written now, the
/// controls the build loads, and each change made to it as the smallest edit to the text, by
/// <see cref="Designer"/> — asked a line of JSON at a time on standard input, and answered a line at a
/// time on standard output.
/// </para>
/// <para>
/// A refusal is said in the label document beside the rule set — <c>signup.labels.en.json</c> —
/// read from the folder, never out of the application, so <c>replay</c> catches an application
/// built before a sentence changed, and <c>show</c> says what the design expects in the words the
/// application is held to.
/// </para>
/// <para>
/// Each is run in the folder the application is in, or is given it, and finds the rest there: the
/// rule set is the one JSON file in it that says it is one, or the one <c>--rules</c> names where
/// there are more, its test design is named after it, a specification is any JSON file there whose
/// <c>$schema</c> says it is one, <see cref="Specification.File"/> the first, every window is opened as
/// the application opens them (<see cref="RuleWindows"/>), and the vocabularies and the built application are in <c>--plugins</c>, the build's
/// output. What is found is written the way a compiler writes it — a file, a place in it, and what
/// is wrong there — so the editor's own tasks show it as problems too. The exit code is the one
/// <c>ruledger diff</c> gives: 0 nothing to report, 1 it could not be done, 2 the command line was not
/// understood, 3 something was found.
/// </para>
/// </remarks>
public static class Command
{
    /// <summary>Nothing to report.</summary>
    public const int Agreed = 0;

    /// <summary>It could not be done: nothing to check yet, or not what was expected where it was.</summary>
    public const int Failed = 1;

    /// <summary>The command line was not understood.</summary>
    public const int NotUnderstood = 2;

    /// <summary>Something was found, and written.</summary>
    public const int Found = 3;

    /// <summary>Where an application's vocabularies and its built assembly are, from its folder, unless <c>--plugins</c> says.</summary>
    public const string BuildOutput = "bin/Debug/net10.0";

    private const string Usage = """
        usage:
          rulealize-studio check  [folder]   does the rule set compile
          rulealize-studio agree  [folder]   do the specifications and the rules agree
          rulealize-studio replay [folder]   does the screen do what the test design says
          rulealize-studio show   [folder]   the test design's situations, by the moves that reach them
              --state <name> --out <folder>  one of them, as pictures of the window written there
              --design <file>                of that design rather than the one beside the rules
          rulealize-studio changes [folder]  what the rules as saved decide otherwise than the design,
                                             by the moves that reach each situation
              --out <file>                   the design the rules give now, written there
              --design <file>                of that design rather than the one beside the rules
              --was <file>                   of the design derived from that rule set, as it was,
                                             carrying the choices of --design
              --was-out <file>               that design, written there
          rulealize-studio design [folder]   the screen drawn from its XAML as written, and changes to it
                                             as edits to the text, asked a line of JSON at a time

          folder               the application's folder         (default the current one)
          --rules <file>       the rule set, where the folder has more than one; its test
                               design is the one named after it
          --plugins <folder>   the vocabularies, and the built application, from that folder
                               (default 'bin/Debug/net10.0', where 'dotnet build' writes them)

        exit: 0 nothing to report, 1 could not be done, 2 command line not understood,
              3 something was found

        With nothing on the command line, it is a language server on standard input and output.
        """;

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Runs one check.</summary>
    /// <param name="args">The command line: a check, then the folder and <c>--plugins</c>, either of which may be left out.</param>
    /// <param name="output">Where what was found is written.</param>
    /// <param name="error">Where why it could not be done is written.</param>
    /// <returns>The exit code.</returns>
    /// <remarks>
    /// <c>replay</c> starts Avalonia, with no window server, in this process; it can be run once in
    /// a process, which is what a command line is.
    /// </remarks>
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error) => Run(args, TextReader.Null, output, error);

    /// <summary>Runs one check, or the designer, which is asked what it is asked on <paramref name="input"/>.</summary>
    /// <param name="args">The command line: a check, then the folder and <c>--plugins</c>, either of which may be left out.</param>
    /// <param name="input">What <c>design</c> is asked, a line of JSON at a time, until it ends.</param>
    /// <param name="output">Where what was found is written.</param>
    /// <param name="error">Where why it could not be done is written.</param>
    /// <returns>The exit code.</returns>
    /// <remarks>
    /// <c>replay</c>, <c>show</c> and <c>design</c> start Avalonia, with no window server, in this
    /// process; each can be run once in a process, which is what a command line is.
    /// </remarks>
    public static int Run(IReadOnlyList<string> args, TextReader input, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string? verb = null;
        string? folder = null;
        string? plugins = null;
        string? state = null;
        string? pictures = null;
        string? design = null;
        string? was = null;
        string? wasOut = null;
        string? ruleSet = null;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == "--plugins" && i + 1 < args.Count)
            {
                plugins = args[++i];
            }
            else if (args[i] == "--rules" && i + 1 < args.Count)
            {
                ruleSet = args[++i];
            }
            else if (args[i] == "--state" && i + 1 < args.Count)
            {
                state = args[++i];
            }
            else if (args[i] == "--out" && i + 1 < args.Count)
            {
                pictures = args[++i];
            }
            else if (args[i] == "--design" && i + 1 < args.Count)
            {
                design = args[++i];
            }
            else if (args[i] == "--was" && i + 1 < args.Count)
            {
                was = args[++i];
            }
            else if (args[i] == "--was-out" && i + 1 < args.Count)
            {
                wasOut = args[++i];
            }
            else if (args[i].StartsWith('-') || (verb is not null && folder is not null))
            {
                error.WriteLine(Usage);
                return NotUnderstood;
            }
            else if (verb is null)
            {
                verb = args[i];
            }
            else
            {
                folder = args[i];
            }
        }

        if (verb is not ("check" or "agree" or "replay" or "show" or "changes" or "design")
            || (verb is not ("show" or "changes") && (state ?? pictures ?? design) is not null)
            || (verb == "show" && (state is null) != (pictures is null))
            || (verb == "changes" && state is not null)
            || (verb != "changes" && (was ?? wasOut) is not null)
            || (was is null && wasOut is not null))
        {
            error.WriteLine(Usage);
            return NotUnderstood;
        }

        Folder application = new(Path.GetFullPath(folder ?? "."), plugins ?? BuildOutput, design, ruleSet);
        if (!Directory.Exists(application.At))
        {
            error.WriteLine($"'{application.At}' is not a folder.");
            return Failed;
        }

        try
        {
            return verb switch
            {
                "check" => Compiles(application, output, error),
                "agree" => Agrees(application, output, error),
                "replay" => Replays(application, output, error),
                "design" => Designs(application, input, output, error),
                "changes" => Changed(application, pictures is null ? null : Path.GetFullPath(pictures), Before(was, design, wasOut), output, error),
                _ when state is null => Lists(application, output, error),
                _ => Shows(application, state, Path.GetFullPath(pictures!), output, error),
            };
        }
        catch (JsonException wrong)
        {
            error.WriteLine($"Not JSON: {wrong.Message}");
            return Failed;
        }
    }

    /// <summary>Whether the rule set compiles against the vocabularies the application loads.</summary>
    private static int Compiles(Folder application, TextWriter output, TextWriter error)
    {
        if (application.RuleSet(error) is not { } rules)
        {
            return Failed;
        }

        RuleRuntime runtime;
        try
        {
            runtime = Vocabularies.Load(application.Plugins);
        }
        catch (Exception wrong) when (wrong is PluginLoadException or IOException)
        {
            error.WriteLine($"The vocabularies in '{application.Plugins}' could not be loaded: {wrong.Message} Build the application first, so that they are there.");
            return Failed;
        }

        string text = File.ReadAllText(rules);
        return Write(output, "error", [(rules, text, Check.Run(text, runtime))]);
    }

    /// <summary>Whether the specifications and the rules agree: every specification in the folder, against every rule set in it.</summary>
    private static int Agrees(Folder application, TextWriter output, TextWriter error)
    {
        string named = Path.Combine(application.At, Specification.File);
        if (File.Exists(named) && !Specification.Is(File.ReadAllText(named)))
        {
            error.WriteLine($"'{named}' does not say it is a specification: its $schema is not '{StateMachine.Schema}'.");
            return Failed;
        }

        string[] specifications = application.Specifications();
        if (specifications.Length == 0)
        {
            error.WriteLine($"'{application.At}' has no specification yet, '{Specification.File}'. The blueprint comes first.");
            return Failed;
        }

        Dictionary<string, string> files = [];
        foreach (string file in specifications.Concat(application.RuleSets()))
        {
            files[Path.GetFileName(file)] = file;
        }

        Written[] said = [.. specifications.Select(file => new Written(Path.GetFileName(file), File.ReadAllText(file)))];
        Written[] rules = [.. application.RuleSets().Select(file => new Written(Path.GetFileName(file), File.ReadAllText(file)))];
        IReadOnlyDictionary<string, ImmutableArray<Finding>> found = Agreement.Compare(said, rules);
        List<(string, string, ImmutableArray<Finding>)> marked =
            [.. said.Concat(rules).Select(file => (files[file.Name], file.Text, found[file.Name]))];

        return Write(output, "warning", marked);
    }

    /// <summary>Whether the application as last built does on its screen what the test design says.</summary>
    private static int Replays(Folder application, TextWriter output, TextWriter error)
    {
        if (application.Design(error) is not { } design || application.Built(error, "replayed") is not { } built)
        {
            return Failed;
        }

        string text = File.ReadAllText(design);
        IReadOnlyList<Divergence> found;
        try
        {
            found = Replayed(Load(built), application, text, application.Spoken());
        }
        catch (Exception wrong) when (wrong is FormatException or InvalidOperationException
            or PluginLoadException or Rulealize.Abstraction.RuleSetBuildException or RuleDocumentException)
        {
            error.WriteLine(wrong.Message);
            return Failed;
        }

        DocumentMap map = DocumentMap.Read(text);
        JsonArray states = JsonNode.Parse(text, documentOptions: Options)?["states"] as JsonArray ?? [];
        ImmutableArray<Finding> findings = [.. found.Select(divergence =>
        {
            int index = states.Select((state, i) => (string?)state?["name"] == divergence.State ? i : -1).FirstOrDefault(i => i >= 0, -1);
            (int start, int length) = index < 0 ? (0, 0) : map.Span($"/states/{index}/name") ?? (0, 0);
            return new Finding(start, length, divergence.ToString());
        })];

        return Write(output, "error", [(design, text, findings)]);
    }

    /// <summary>
    /// What the rules as saved decide otherwise than the test design beside them — Ruledger's diff,
    /// asked for as values — with each situation it names said by the moves that reach it, as JSON;
    /// and the design the rules give now, which carries the choices of the one beside them, written
    /// where it is asked for, so that the window can be stood in what it names.
    /// </summary>
    private static int Changed(Folder application, string? after, Was? before, TextWriter output, TextWriter error)
    {
        string? beside = before is null ? application.Design(error) : null;
        if ((before is null && beside is null) || application.RuleSet(error) is not { } rules)
        {
            return Failed;
        }

        foreach (string named in before is null ? [] : new[] { before.Rules, before.Design }.OfType<string>())
        {
            if (!File.Exists(Path.GetFullPath(named, application.At)))
            {
                error.WriteLine($"'{Path.GetFullPath(named, application.At)}' does not exist.");
                return Failed;
            }
        }

        JsonObject changed;
        string derived;
        try
        {
            RuleRuntime runtime = Vocabularies.Load(application.Plugins);
            string design;
            if (before is null)
            {
                design = File.ReadAllText(beside!);
            }
            else
            {
                string? committed = before.Design is null ? null : File.ReadAllText(Path.GetFullPath(before.Design, application.At));
                design = Changes.Before(runtime, File.ReadAllText(Path.GetFullPath(before.Rules, application.At)), committed);
                if (before.Out is not null)
                {
                    string written = Path.GetFullPath(before.Out, application.At);
                    Directory.CreateDirectory(Path.GetDirectoryName(written)!);
                    File.WriteAllText(written, design);
                }
            }

            (changed, derived) = Changes.Of(runtime, design, File.ReadAllText(rules), application.Spoken());
        }
        catch (Exception wrong) when (wrong is FormatException or InvalidOperationException or IOException
            or PluginLoadException or Rulealize.Abstraction.RuleSetBuildException or RuleDocumentException)
        {
            error.WriteLine(wrong.Message);
            return Failed;
        }

        if (after is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(after)!);
            File.WriteAllText(after, derived);
        }

        output.WriteLine(changed.ToJsonString());
        return changed["changed"]!.AsArray().Count + changed["gone"]!.AsArray().Count + changed["appeared"]!.AsArray().Count
            + changed["admits"]!.AsArray().Count + changed["choices"]!.AsArray().Count == 0 && (bool)changed["comparedTheSameWay"]!
            ? Agreed
            : Found;
    }

    /// <summary>
    /// The situations the test design names, each by the moves that first reached it — and each move
    /// as the application's window says it, where it is built — with whether
    /// it is an ending, every move and where it leads, what a move still waiting admits, every
    /// refusal with what it is said as, and every value somebody chose to try there with what became
    /// of it, as JSON; with a choice made from a state that is none of them listed apart, since a
    /// choice is never dropped.
    /// </summary>
    private static int Lists(Folder application, TextWriter output, TextWriter error)
    {
        if (application.Design(error) is not { } design)
        {
            return Failed;
        }

        IReadOnlyList<Situation> situations;
        IReadOnlyList<Choice> choices;
        Labels? labels;
        try
        {
            string text = File.ReadAllText(design);
            situations = Hosting.Replay.Situations(text);
            choices = Choice.Read(text);
            labels = application.Spoken();
        }
        catch (FormatException wrong)
        {
            error.WriteLine(wrong.Message);
            return Failed;
        }

        ILookup<string?, Choice> made = Choice.Placed(situations, choices);
        IReadOnlyDictionary<string, string> words = Words(application, [
            .. situations.SelectMany(s => s.Route.Concat(s.Moves.Select(m => m.Step)).Concat(s.Refused.Select(r => r.Step))),
            .. choices.Select(c => c.Step),
        ]);
        JsonValue? Said(string step) => words.TryGetValue(step, out string? said) ? JsonValue.Create(said) : null;
        JsonObject Chosen(Choice choice) => new()
        {
            ["at"] = choice.At,
            ["state"] = choice.State,
            ["step"] = choice.Step,
            ["words"] = Said(choice.Step),
            ["input"] = choice.Input,
            ["args"] = new JsonObject(choice.Args.Select(arg => KeyValuePair.Create<string, JsonNode?>(arg.Key, arg.Value))),
            ["carried"] = choice.Carried,
        };

        output.WriteLine(new JsonObject
        {
            ["language"] = labels?.Language,
            ["captions"] = CaptionsOf(application, [
                .. situations.SelectMany(s => s.Moves.SelectMany(m => m.Admits.Keys.Select(parameter => (m.Input, parameter)))),
                .. choices.SelectMany(c => c.Args.Keys.Select(parameter => (c.Input, parameter))),
            ]),
            ["unplaced"] = new JsonArray([.. made[null].Select(Chosen)]),
            ["situations"] = new JsonArray([.. situations.Select(situation => new JsonObject
            {
                ["state"] = situation.State,
                ["route"] = new JsonArray([.. situation.Route.Select(step => JsonValue.Create(step))]),
                ["words"] = new JsonArray([.. situation.Route.Select(Said)]),
                ["ending"] = situation.IsEnding ? new JsonObject { ["result"] = situation.Result } : null,
                ["moves"] = new JsonArray([.. situation.Moves.Select(move => new JsonObject
                {
                    ["step"] = move.Step,
                    ["words"] = Said(move.Step),
                    ["input"] = move.Input,
                    ["to"] = move.To,
                    ["followed"] = move.Followed,
                    ["admits"] = new JsonObject(move.Admits.Select(parameter => KeyValuePair.Create<string, JsonNode?>(
                        parameter.Key,
                        new JsonArray([.. parameter.Value.Select(bound => new JsonObject { ["bound"] = bound.Key, ["value"] = bound.Value })])))),
                })]),
                ["chosen"] = new JsonArray([.. made[situation.State].Select(Chosen)]),
                ["refused"] = new JsonArray([.. situation.Refused.Select(refusal => new JsonObject
                {
                    ["step"] = refusal.Step,
                    ["words"] = Said(refusal.Step),
                    ["input"] = refusal.Input,
                    ["codes"] = new JsonArray([.. refusal.Codes.Select(code => JsonValue.Create(code))]),
                    ["said"] = new JsonArray([.. refusal.Codes.Select(code => JsonValue.Create(Labels.Say(labels, refusal.Input, code)))]),
                })]),
            })]),
        }.ToJsonString());
        return Agreed;
    }

    /// <summary>
    /// Each move as the application's screen says it — the words of the control that stands for it,
    /// and the values entered beside it (<see cref="Reach.Say(IEnumerable{Visual}, RuleModel, string)"/>) — where the application is built
    /// and a control says it in words; a move it cannot be said for is left out, to be said by its name.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Words(Folder application, IEnumerable<string> steps)
    {
        Dictionary<string, string> words = new(StringComparer.Ordinal);
        if (application.Built(TextWriter.Null, "said") is not { } built)
        {
            return words;
        }

        try
        {
            Assembly assembly = Load(built);
            AppBuilder.Configure(() => new RuleApplication(assembly))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            if (RuleApp.Models(assembly).Count == 0)
            {
                return words;
            }

            (RuleWindows screens, RuleModel model) = Opened(assembly, application);
            try
            {
                foreach (string step in steps.Distinct(StringComparer.Ordinal))
                {
                    if (Reach.Say(screens.Of(model), model, step) is { } said)
                    {
                        words[step] = said;
                    }
                }
            }
            finally
            {
                screens.End();
            }
        }
        catch (Exception wrong) when (wrong is IOException or BadImageFormatException or InvalidOperationException
            or PluginLoadException or Rulealize.Abstraction.RuleSetBuildException or RuleDocumentException)
        {
            // The words are what the screen adds to the list; without them it is still the list.
            words.Clear();
        }

        return words;
    }

    /// <summary>
    /// What the screen calls the box each parameter is entered in, by input and parameter as the rules
    /// name them (<see cref="Captions"/>); nothing for one it has no words for, or where the screen
    /// is not there or not XML.
    /// </summary>
    private static JsonObject CaptionsOf(Folder application, IEnumerable<(string Input, string Parameter)> parameters)
    {
        JsonObject said = [];
        List<(Ask Box, string Caption)> captions = [];
        foreach (string screen in Directory.GetFiles(application.At, "*.axaml").Order(StringComparer.Ordinal))
        {
            try
            {
                captions.AddRange(Captions.Read(File.ReadAllText(screen)));
            }
            catch (Exception wrong) when (wrong is System.Xml.XmlException or FormatException or IOException)
            {
                // A window that is not XML yet has no words for a box; the others still do.
            }
        }

        foreach ((string input, string parameter) in parameters.Distinct())
        {
            if (captions.FirstOrDefault(caption => caption.Box.IsNamedBy(input, parameter)) is ({ } _, { } caption))
            {
                (said[input] ??= new JsonObject()).AsObject()[parameter] = caption;
            }
        }

        return said;
    }

    /// <summary>
    /// Stands the application as last built in one situation of its test design, and writes a
    /// picture of the window pressed on before each press that reached it, one of each window shown
    /// where it stands, and one of the window each refusal there was pressed on as it drew it, with
    /// where on its window each control pressed or standing for a legal move is, as JSON.
    /// </summary>
    /// <remarks>
    /// Each window is named by the file it is written in. <c>picture</c> and <c>size</c> are the first
    /// window shown where it stands, which is the whole of it for an application of one window.
    /// </remarks>
    private static int Shows(Folder application, string state, string pictures, TextWriter output, TextWriter error)
    {
        if (application.Design(error) is not { } design || application.Built(error, "shown") is not { } built)
        {
            return Failed;
        }

        Directory.CreateDirectory(pictures);
        int taken = 0;
        JsonArray presses = [];
        JsonArray moves = [];
        JsonArray refused = [];
        JsonArray standing = [];
        IReadOnlyList<Divergence> found;
        JsonObject shown;
        try
        {
            Labels? labels = application.Spoken();
            Assembly assembly = Load(built);
            AppBuilder.Configure(() => new RuleApplication(assembly))
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            if (RuleApp.Models(assembly).Count == 0)
            {
                throw new InvalidOperationException($"'{assembly.GetName().Name}' has no model: it was built with no rule set.");
            }

            (RuleWindows screens, RuleModel model) = Opened(assembly, application);
            try
            {
                string Taken(Window window) => Picture(window, Path.Combine(pictures, $"{taken++}.png"));
                Window On(Control control) => TopLevel.GetTopLevel(control) as Window ?? screens.Shown[0];
                JsonObject Pictured(Window window) => new()
                {
                    ["window"] = screens.File(window),
                    ["title"] = window.Title,
                    ["size"] = Size(window),
                    ["picture"] = Taken(window),
                };
                void Stood()
                {
                    foreach (Window window in screens.Shown)
                    {
                        standing.Add(Pictured(window));
                    }
                }

                found = Hosting.Replay.Stand(screens, model, File.ReadAllText(design), state, labels, Press, new Witness(
                    Pressing: (step, control) => presses.Add(new JsonObject
                    {
                        ["step"] = step,
                        ["window"] = screens.File(On(control)),
                        ["size"] = Size(On(control)),
                        ["at"] = Where(On(control), control),
                        ["picture"] = Taken(On(control)),
                    }),
                    Arrived: Stood,
                    Offered: (step, control) => moves.Add(new JsonObject
                    {
                        ["step"] = step,
                        ["window"] = control is null ? null : screens.File(On(control)),
                        ["at"] = control is null ? null : Where(On(control), control),
                    }),
                    Refused: (step, control) => refused.Add(new JsonObject
                    {
                        ["step"] = step,
                        ["window"] = screens.File(On(control)),
                        ["size"] = Size(On(control)),
                        ["at"] = Where(On(control), control),
                        ["picture"] = Taken(On(control)),
                    })));

                if (standing.Count == 0)
                {
                    Stood();
                }

                JsonObject first = standing.Count > 0 ? standing[0]!.AsObject() : [];
                shown = new JsonObject
                {
                    ["state"] = state,
                    ["size"] = first["size"]?.DeepClone(),
                    ["presses"] = presses,
                    ["picture"] = first["picture"]?.DeepClone(),
                    ["windows"] = standing,
                    ["moves"] = moves,
                    ["refused"] = refused,
                };
            }
            finally
            {
                screens.End();
            }
        }
        catch (Exception wrong) when (wrong is FormatException or ArgumentException or InvalidOperationException
            or PluginLoadException or Rulealize.Abstraction.RuleSetBuildException or RuleDocumentException)
        {
            error.WriteLine(wrong.Message);
            return Failed;
        }

        shown["divergences"] = new JsonArray([.. found.Select(divergence => JsonValue.Create(divergence.ToString()))]);
        output.WriteLine(shown.ToJsonString());
        return found.Count == 0 ? Agreed : Found;
    }

    /// <summary>
    /// Draws the application's screen from its XAML as it is written, for as long as it is asked to:
    /// each line of input a request of <see cref="Designer"/>, each answered by a line of output.
    /// </summary>
    /// <remarks>
    /// The window is loaded with what the application was last built with beside it — its own
    /// assembly, and every one its build wrote — since the controls to place are the ones those hold.
    /// Each is read into memory rather than loaded from its file, so that the application can be
    /// built again while its screen is open: Windows holds a file loaded from its path until the
    /// process ends.
    /// </remarks>
    private static int Designs(Folder application, TextReader input, TextWriter output, TextWriter error)
    {
        if (application.Built(error, "designed") is not { } built)
        {
            return Failed;
        }

        string folder = Path.GetDirectoryName(built)!;
        static Assembly Read(string file) => Vocabularies.Read(AssemblyLoadContext.Default, file);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
            Path.Combine(folder, name.Name + ".dll") is var file && File.Exists(file) ? Read(file) : null;

        Assembly assembly = Read(built);
        List<Assembly> loaded = [assembly];
        foreach (string file in Directory.GetFiles(application.Plugins, "*.dll").Where(file => !string.Equals(file, built, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                AssemblyName name = AssemblyName.GetAssemblyName(file);
                loaded.Add(AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name))
                    ?? Read(file));
            }
            catch (Exception wrong) when (wrong is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                // Not an assembly, or not one this runtime loads: it holds no control to place.
            }
        }

        AppBuilder.Configure(() => new RuleApplication(assembly))
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        Designer designer = new(assembly, [.. loaded.Distinct()]);
        while (input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonObject answer;
            try
            {
                answer = JsonNode.Parse(line) is JsonObject asked ? designer.Answer(asked) : new JsonObject { ["trouble"] = "A request is a JSON object." };
            }
            catch (Exception wrong) when (wrong is JsonException or InvalidOperationException or FormatException)
            {
                answer = new JsonObject { ["trouble"] = wrong.Message };
            }

            output.WriteLine(answer.ToJsonString());
            output.Flush();
        }

        return Agreed;
    }

    /// <summary>Writes what the window draws now, as the application draws it, and answers where it was written.</summary>
    private static string Picture(Window window, string file)
    {
        // What was entered last is laid out and drawn before the frame is taken, not a tick later.
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The window drew nothing to take a picture of.");
        frame.Save(file);
        return file;
    }

    /// <summary>How large a window is drawn, in its own units.</summary>
    private static JsonObject Size(Window window) => new() { ["width"] = window.ClientSize.Width, ["height"] = window.ClientSize.Height };

    /// <summary>Where a control is on its window, in the window's own units.</summary>
    private static JsonObject Where(Window window, Control control)
    {
        Point at = control.TranslatePoint(default, window) ?? default;
        return new JsonObject { ["x"] = at.X, ["y"] = at.Y, ["width"] = control.Bounds.Width, ["height"] = control.Bounds.Height };
    }

    /// <summary>Loads a built application, and anything of its own it needs, from its build's output.</summary>
    /// <remarks>
    /// Into this process's own context, so that the model it holds is a <see cref="RuleModel"/> of
    /// the same binding layer this runs, and its screen is drawn by the same Avalonia. From its file,
    /// since an application finds the vocabularies beside it by where its assembly is; the file is
    /// held only while this one command runs.
    /// </remarks>
    private static Assembly Load(string built)
    {
        string folder = Path.GetDirectoryName(built)!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
            Path.Combine(folder, name.Name + ".dll") is var file && File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(built);
    }

    /// <summary>Opens the application's windows with no window server and replays the design on them, pressing as a pointer would.</summary>
    private static IReadOnlyList<Divergence> Replayed(Assembly built, Folder application, string design, Labels? labels)
    {
        AppBuilder.Configure(() => new RuleApplication(built))
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();

        if (RuleApp.Models(built).Count == 0)
        {
            throw new InvalidOperationException($"'{built.GetName().Name}' has no model: it was built with no rule set.");
        }

        (RuleWindows screens, RuleModel model) = Opened(built, application);
        try
        {
            return Hosting.Replay.Run(screens, model, design, labels, Press);
        }
        finally
        {
            screens.End();
        }
    }

    /// <summary>
    /// Opens every window of a built application, shown as the rules show them where they start, with
    /// the model of the folder's rule set — the one there is, or the one <c>--rules</c> chose.
    /// </summary>
    /// <exception cref="InvalidOperationException">The application was built from no rule set of that id.</exception>
    private static (RuleWindows RuleWindows, RuleModel Model) Opened(Assembly built, Folder application)
    {
        RuleWindows screens = RuleWindows.Open(built);
        RuleModel model = screens.Models.Count == 1
            ? screens.Models[0]
            : (application.Id() is { } id ? screens.Model(id) : null)
                ?? throw new InvalidOperationException($"'{built.GetName().Name}' was built from {string.Join(", ", screens.Models.Select(each => each.RuleSet))}, and the rule set asked about is none of them: build it again, or say which with --rules.");
        screens.Show();
        return (screens, model);
    }

    /// <summary>Presses a control the way somebody would: the pointer, down and up at its centre on the window it is on.</summary>
    private static void Press(Control control)
    {
        Dispatcher.UIThread.RunJobs();
        if (TopLevel.GetTopLevel(control) is not Window window
            || control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) is not { } centre)
        {
            return;
        }

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Writes what was found the way a compiler does, and says by the exit code whether anything was.</summary>
    private static int Write(TextWriter output, string severity, IEnumerable<(string File, string Text, ImmutableArray<Finding> Findings)> found)
    {
        int written = 0;
        foreach ((string file, string text, ImmutableArray<Finding> findings) in found)
        {
            foreach (Finding finding in findings)
            {
                (int line, int column) = Position(text, finding.Start);
                (int toLine, int toColumn) = Position(text, finding.Start + finding.Length);
                output.WriteLine($"{file}({line},{column},{toLine},{toColumn}): {severity} : {finding.Message.ReplaceLineEndings(" ")}");
                written++;
            }
        }

        return written == 0 ? Agreed : Found;
    }

    /// <summary>A place in a text as a compiler writes one: a line and a column, both from one.</summary>
    private static (int Line, int Column) Position(string text, int offset)
    {
        int line = 1;
        int from = 0;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                from = i + 1;
            }
        }

        return (line, offset - from + 1);
    }

    /// <summary>The label document beside a rule set a refusal is said in, chosen as the application chooses; null where there is none.</summary>
    /// <exception cref="FormatException">A label document there cannot be read.</exception>
    internal static Labels? SpokenBeside(string rules)
    {
        List<Labels> written = [];
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(rules)!, "*.json"))
        {
            if (Binding.Labels.LanguageOf(rules, file) is { } language)
            {
                try
                {
                    written.Add(Binding.Labels.Read(language, File.ReadAllText(file)));
                }
                catch (FormatException wrong)
                {
                    throw new FormatException($"'{file}': {wrong.Message}", wrong);
                }
            }
        }

        return Binding.Labels.Choose(written, System.Globalization.CultureInfo.CurrentUICulture);
    }

    /// <summary>The rules before a change, where <c>changes</c> is given them: the design before it is derived from them, carrying the choices of the design committed with them.</summary>
    /// <param name="Rules">The rule set as it was.</param>
    /// <param name="Design">The test design committed with it, or null where none was.</param>
    /// <param name="Out">Where the design before the change is written, or null.</param>
    private sealed record Was(string Rules, string? Design, string? Out);

    /// <summary>The rules before a change from the command line, or null where it gave none.</summary>
    private static Was? Before(string? rules, string? design, string? written) =>
        rules is null ? null : new Was(rules, design, written);

    /// <summary>What a test design is called beside the rule set it is named after.</summary>
    private static class Beside
    {
        public const string TestDesign = ".test-design.json";
    }

    /// <summary>An application's folder, and the build's output in it.</summary>
    private sealed class Folder(string folder, string plugins, string? chosen = null, string? rules = null)
    {
        public string At { get; } = folder;

        public string Plugins { get; } = Path.GetFullPath(plugins, folder);

        /// <summary>The test design beside the rule set; nothing, said, where either is not there.</summary>
        public string? Design(TextWriter error)
        {
            if (chosen is not null)
            {
                string named = Path.GetFullPath(chosen, At);
                if (!File.Exists(named))
                {
                    error.WriteLine($"'{named}' does not exist.");
                    return null;
                }

                return named;
            }

            if (RuleSet(error) is not { } rules)
            {
                return null;
            }

            string design = Path.ChangeExtension(rules, null) + Beside.TestDesign;
            if (!File.Exists(design))
            {
                error.WriteLine($"'{rules}' has no test design beside it, '{Path.GetFileName(design)}': 'ruledger derive' writes it.");
                return null;
            }

            return design;
        }

        /// <summary>The application as last built; nothing, said, where the folder is not one project or it was not built.</summary>
        public string? Built(TextWriter error, string what)
        {
            string[] projects = Directory.GetFiles(At, "*.csproj");
            if (projects.Length != 1)
            {
                error.WriteLine($"'{At}' holds {projects.Length} projects, and an application is one.");
                return null;
            }

            string built = Path.Combine(Plugins, Path.GetFileNameWithoutExtension(projects[0]) + ".dll");
            if (!File.Exists(built))
            {
                error.WriteLine($"'{built}' is not there. Build the application first: what is {what} is the screen as it was last built.");
                return null;
            }

            return built;
        }

        /// <summary>The label document beside the rule set a refusal is said in, chosen as the application chooses; null where there is none.</summary>
        /// <exception cref="FormatException">A label document there cannot be read.</exception>
        public Labels? Spoken() => RuleSet(error: null) is { } rules ? SpokenBeside(rules) : null;

        /// <summary>
        /// The rule set asked about: the one <c>--rules</c> named, or the one in the folder; nothing,
        /// said, where that is not a rule set, or the folder has none or more than one and none was named.
        /// </summary>
        public string? RuleSet(TextWriter? error)
        {
            if (rules is not null)
            {
                string named = Path.GetFullPath(rules, At);
                if (File.Exists(named) && IsRuleSet(named))
                {
                    return named;
                }

                error?.WriteLine(File.Exists(named) ? $"'{named}' does not say it is a rule set." : $"'{named}' does not exist.");
                return null;
            }

            string[] found = RuleSets();
            if (found.Length == 1)
            {
                return found[0];
            }

            error?.WriteLine(found.Length == 0
                ? $"'{At}' has no rule set yet: no JSON file in it says it is one."
                : $"'{At}' has {found.Length} rule sets, {string.Join(", ", found.Select(Path.GetFileName))}: say which with --rules.");
            return null;
        }

        /// <summary>Every rule set in the folder, in the order of their names.</summary>
        public string[] RuleSets() => [.. Directory.GetFiles(At, "*.json").Where(IsRuleSet).Order(StringComparer.Ordinal)];

        /// <summary>Every specification in the folder — any JSON whose <c>$schema</c> says it is one — in the order of their names.</summary>
        public string[] Specifications() =>
            [.. Directory.GetFiles(At, "*.json").Where(file => Specification.Is(File.ReadAllText(file))).Order(StringComparer.Ordinal)];

        /// <summary>The id of the rule set asked about, which its model is found by; null where there is none or it has no id.</summary>
        public string? Id()
        {
            if (RuleSet(error: null) is not { } named)
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(File.ReadAllText(named), documentOptions: Options)?["id"]?.GetValue<string>();
            }
            catch (Exception wrong) when (wrong is JsonException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }

        private static bool IsRuleSet(string file)
        {
            try
            {
                return DocumentMap.Read(File.ReadAllText(file)).IsRuleSet;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
