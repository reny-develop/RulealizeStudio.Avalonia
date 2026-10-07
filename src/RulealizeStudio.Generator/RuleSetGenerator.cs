// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RulealizeStudio.Generator;

/// <summary>Writes a model per rule set among a project's additional files.</summary>
/// <remarks>
/// <para>
/// A rule set's names are known only when the document is read, and compiled bindings want them
/// at build time. So the document is read at build time: a property per state field, a property
/// per projection with a type per record in it, and an input type per input with a property per
/// parameter. XAML binds those names with <c>x:DataType</c> and compiled bindings unchanged, the
/// Previewer knows them, and renaming a field in the rule set breaks the build instead of the
/// screen.
/// </para>
/// <para>
/// The model carries the document it was generated from, so the rules it opens and the types a
/// screen was compiled against are one document and cannot drift apart.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class RuleSetGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Unreadable = new(
        "RSTUDIO001",
        "A rule set could not be read",
        "'{0}' could not be read as a rule set, so no model was written for it: {1}",
        "RulealizeStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Taken = new(
        "RSTUDIO002",
        "A name in a rule set is one the model already uses",
        "'{0}' in '{1}' would be called {2}, which {3} already has",
        "RulealizeStudio",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Unlabelled = new(
        "RSTUDIO003",
        "A label document could not be read",
        "'{0}' could not be read as a label document, so the application says codes where it would say its sentences: {1}",
        "RulealizeStudio",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>What a label document says it is, as <c>RulealizeStudio.Binding.Labels.Schema</c> does.</summary>
    private const string LabelSchema = "rulealize-studio/labels/v1";

    /// <summary>What sits between a rule set's name and a language in a label document's file name.</summary>
    private const string LabelInfix = ".labels.";

    /// <summary>What every model already has, so a rule set cannot name something the same.</summary>
    private static readonly string[] ModelMembers =
    [
        "Session", "RuleSet", "Document", "IsTerminal", "Ending", "CanGoBack", "CanGoForward", "Back", "Forward",
        "IsWaitingForOutcome", "Outcomes", "Trouble", "Inputs", "Moves", "State", "Position", "PropertyChanged", "Source",
        "Read", "Moved", "Field", "Answer", "Register", "Labels", "LabelDocuments",
    ];

    /// <summary>What every input already has.</summary>
    private static readonly string[] InputMembers =
    [
        "Input", "Apply", "IsOffered", "Refusal", "HasErrors", "Model", "PropertyChanged", "ErrorsChanged",
        "GetErrors", "Get", "Set", "Settled", "Open", "Options", "Limits", "Hold", "StandsFor",
    ];

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<string> space = context.AnalyzerConfigOptionsProvider
            .Select((options, _) => options.GlobalOptions.TryGetValue("build_property.RootNamespace", out string? root) ? root : string.Empty)
            .Combine(context.CompilationProvider.Select((compilation, _) => compilation.AssemblyName ?? "RuleSets"))
            .Select((pair, _) => pair.Left.Length > 0 ? pair.Left : pair.Right);

        IncrementalValuesProvider<(string File, string Text)> files = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select((file, token) => (Path.GetFileName(file.Path), file.GetText(token)?.ToString() ?? string.Empty));

        // A label document is named after its rule set, so each rule set is paired with every
        // file there is; what is not a rule set is passed over by the reader.
        context.RegisterSourceOutput(
            files.Combine(files.Collect()).Combine(space),
            (output, pair) => Emit(output, pair.Left.Left.File, pair.Left.Left.Text, pair.Left.Right, pair.Right));

        // Which model each window binds, which compiling its XAML drops: read from its root here,
        // and written into the assembly beside the file's name.
        IncrementalValueProvider<string> project = context.AnalyzerConfigOptionsProvider
            .Select((options, _) => options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out string? folder) ? folder : string.Empty);
        IncrementalValuesProvider<(string File, string? Bound)> windows = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Select((file, token) => (file.Path, Window.Bound(file.GetText(token)?.ToString() ?? string.Empty)));
        context.RegisterSourceOutput(windows.Collect().Combine(project), (output, pair) => EmitWindows(output, pair.Left, pair.Right));
    }

    private static void EmitWindows(SourceProductionContext output, IEnumerable<(string File, string? Bound)> windows, string project)
    {
        // Written whether or not any window binds, so that an application built before windows were
        // recorded is told apart from one whose windows bind nothing.
        StringBuilder source = new StringBuilder()
            .Append("[assembly: global::System.Reflection.AssemblyMetadata(")
            .Append(Literal(Window.Recorded))
            .AppendLine(", \"1\")]");
        foreach ((string file, string? bound) in windows.Where(each => each.Bound is not null).OrderBy(each => each.File, StringComparer.Ordinal))
        {
            source.Append("[assembly: global::System.Reflection.AssemblyMetadata(")
                .Append(Literal(Window.Prefix + Window.Relative(file, project)))
                .Append(", ")
                .Append(Literal(bound!))
                .AppendLine(")]");
        }

        output.AddSource("RuleWindows.g.cs", SourceText.From("// <auto-generated/>\n" + source, Encoding.UTF8));
    }

    private static void Emit(SourceProductionContext output, string file, string text, IEnumerable<(string File, string Text)> all, string space)
    {
        Document? document;

        try
        {
            document = Document.Read(file, text);
        }
        catch (FormatException wrong)
        {
            output.ReportDiagnostic(Diagnostic.Create(Unreadable, Location.None, file, wrong.Message));
            return;
        }

        if (document is null || !Check(output, document))
        {
            return;
        }

        List<(string Language, string Text)> labels = [];
        string prefix = Path.GetFileNameWithoutExtension(file) + LabelInfix;
        foreach ((string other, string written) in all.OrderBy(each => each.File, StringComparer.OrdinalIgnoreCase))
        {
            if (other.Length <= prefix.Length + ".json".Length || !other.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Unlabelling(written) is string wrong)
            {
                output.ReportDiagnostic(Diagnostic.Create(Unlabelled, Location.None, other, wrong));
                continue;
            }

            labels.Add((other.Substring(prefix.Length, other.Length - prefix.Length - ".json".Length), written));
        }

        output.AddSource($"{document.ClassName}.g.cs", SourceText.From(Write(document, labels, space), Encoding.UTF8));
    }

    /// <summary>Why a label document cannot be read, or null where it can.</summary>
    /// <remarks>What <c>Labels.Read</c> refuses, said at build time so that an application never starts with labels it cannot read.</remarks>
    private static string? Unlabelling(string text)
    {
        Json root;
        try
        {
            root = Json.Parse(text);
        }
        catch (FormatException wrong)
        {
            return wrong.Message;
        }

        if (root is not Json.Record record || record["$schema"] is not Json.Text { Value: LabelSchema })
        {
            return $"it does not say it is '{LabelSchema}'";
        }

        foreach (KeyValuePair<string, Json> label in record["labels"] is Json.Record labels ? labels.Fields : [])
        {
            string[] parts = label.Key.Split('/');
            if (parts.Length != 5 || parts[0].Length != 0 || parts[1] != "inputs" || parts[2].Length == 0 || parts[3] != "validate" || parts[4].Length == 0)
            {
                return $"'{label.Key}' is labelled, and only a refusal is: '/inputs/<input>/validate/<code>'";
            }

            if (label.Value is not Json.Text)
            {
                return $"the label for '{label.Key}' is a sentence";
            }
        }

        return null;
    }

    /// <summary>Refuses a document that names two things the same, or something the base already is.</summary>
    private static bool Check(SourceProductionContext output, Document document)
    {
        bool fine = true;
        HashSet<string> model = new(ModelMembers, StringComparer.Ordinal) { document.ClassName };

        IEnumerable<(string Name, string Property)> members = document.Inputs
            .SelectMany(input => new[] { (input.Name, input.Property), (input.Name, input.TypeName) })
            .Concat(document.Projections.SelectMany(projection => new[] { (projection.Name, projection.Property), (projection.Name, projection.Shape.Type) }));

        foreach ((string name, string property) in members)
        {
            if (!model.Add(property))
            {
                output.ReportDiagnostic(Diagnostic.Create(Taken, Location.None, name, document.File, property, document.ClassName));
                fine = false;
            }
        }

        foreach (Document.Input input in document.Inputs)
        {
            HashSet<string> taken = new(InputMembers, StringComparer.Ordinal) { input.TypeName };

            foreach (Document.Parameter parameter in input.Parameters)
            {
                string extra = parameter.Property + (parameter.IsSettled ? "Options" : "Limits");

                foreach (string property in new[] { parameter.Property, extra })
                {
                    if (!taken.Add(property))
                    {
                        output.ReportDiagnostic(Diagnostic.Create(Taken, Location.None, parameter.Name, document.File, property, input.TypeName));
                        fine = false;
                    }
                }
            }
        }

        return fine;
    }

    private static string Write(Document document, IReadOnlyList<(string Language, string Text)> labels, string space)
    {
        Writer w = new();
        string model = document.ClassName;

        w.Line("// <auto-generated/>");
        w.Line("#nullable enable");
        w.Line();
        w.Open($"namespace {space}");

        w.Line($"/// <summary>The rule set <c>{Xml(document.Id)}</c>, as something a screen binds to.</summary>");
        w.Line($"/// <remarks>Generated from <c>{Xml(document.File)}</c>. Every name here is one the document gave; editing the document regenerates this.</remarks>");
        w.Line($"[global::System.CodeDom.Compiler.GeneratedCode(\"RulealizeStudio.Generator\", \"1.0\")]");
        w.Open($"public sealed partial class {model} : global::RulealizeStudio.Binding.RuleModel");

        w.Line("/// <summary>The document this type was generated from, which is the one it opens.</summary>");
        w.Line($"public const string Source = @\"{document.Text.Replace("\"", "\"\"")}\";");
        w.Line();

        w.Line("/// <summary>The label documents beside the rule set, by the language each is in, which the model says refusals in.</summary>");
        w.Line("public static readonly global::System.Collections.Generic.IReadOnlyList<global::System.Collections.Generic.KeyValuePair<string, string>> LabelDocuments =");
        w.Line("[");
        foreach ((string language, string text) in labels)
        {
            w.Line($"    new({Literal(language)}, @\"{text.Replace("\"", "\"\"")}\"),");
        }

        w.Line("];");
        w.Line();

        w.Line("/// <summary>Opens the rule set against the vocabularies beside the application.</summary>");
        w.Line("/// <remarks>Beside this assembly rather than beside whatever process loaded it, which for the Previewer is a host of its own.</remarks>");
        w.Line($"public {model}()");
        w.Line($"    : this(global::System.IO.Path.GetDirectoryName(typeof({model}).Assembly.Location) is {{ Length: > 0 }} folder ? folder : global::System.AppContext.BaseDirectory)");
        w.Line("{");
        w.Line("}");
        w.Line();

        w.Line("/// <summary>Opens the rule set against a folder of vocabularies.</summary>");
        w.Line("/// <param name=\"pluginFolder\">The folder to sweep.</param>");
        w.Line($"public {model}(string pluginFolder)");
        w.Line($"    : base(global::RulealizeStudio.Binding.Session.Read(Source, {Literal(document.File)}, pluginFolder), global::System.Linq.Enumerable.Select(LabelDocuments, each => global::RulealizeStudio.Binding.Labels.Read(each.Key, each.Value)))");
        w.Open(null);
        foreach (Document.Input input in document.Inputs)
        {
            w.Line($"{input.Property} = Register(new {input.TypeName}(this));");
        }

        w.Line("Moved();");
        w.Close();
        w.Line();

        w.Line("/// <summary>Gets the position, field by field.</summary>");
        w.Line("public Position State { get; private set; } = null!;");
        w.Line();

        foreach (Document.Projection projection in document.Projections)
        {
            w.Line($"/// <summary>Gets what the rule set says as <c>{Xml(projection.Name)}</c>, or null where the rules fault over the position.</summary>");
            w.Line($"public {Nullable(projection.Shape.Type)} {projection.Property} {{ get; private set; }}");
            w.Line();
        }

        foreach (Document.Input input in document.Inputs)
        {
            w.Line($"/// <summary>Gets the input <c>{Xml(input.Name)}</c>.</summary>");
            w.Line($"public {input.TypeName} {input.Property} {{ get; }}");
            w.Line();
        }

        w.Line("/// <inheritdoc />");
        w.Open("protected override void Read()");
        w.Line("State = new Position(this);");
        foreach (Document.Projection projection in document.Projections)
        {
            w.Line($"{projection.Property} = Answer<{Nullable(projection.Shape.Type)}>({Literal(projection.Name)}, node => {Reading(projection.Shape, "node", nullable: true)});");
        }

        w.Close();
        w.Line();

        w.Line("/// <summary>The position, a property per state field.</summary>");
        w.Open("public sealed class Position");
        w.Line($"internal Position({model} model)");
        w.Open(null);
        foreach (Document.Field field in document.Fields)
        {
            w.Line($"{field.Property} = model.Field<{field.Type}>({Literal(field.Name)});");
        }

        w.Close();
        foreach (Document.Field field in document.Fields)
        {
            w.Line();
            w.Line($"/// <summary>Gets the field <c>{Xml(field.Name)}</c>.</summary>");
            w.Line($"public {field.Type} {field.Property} {{ get; }}");
        }

        w.Close();

        foreach (Document.Projection projection in document.Projections)
        {
            if (projection.Shape.IsRecord)
            {
                w.Line();
                WriteRecord(w, projection.Shape, $"What the rule set says as <c>{Xml(projection.Name)}</c>.");
            }
        }

        foreach (Document.Input input in document.Inputs)
        {
            w.Line();
            WriteInput(w, model, input);
        }

        w.Close();
        w.Close();
        return w.ToString();
    }

    private static void WriteRecord(Writer w, Document.Shape shape, string summary)
    {
        w.Line($"/// <summary>{summary}</summary>");
        w.Open($"public sealed class {shape.Type}");
        w.Line($"internal {shape.Type}(global::System.Text.Json.Nodes.JsonNode? node)");
        w.Open(null);
        foreach (Document.ShapeField field in shape.Fields)
        {
            w.Line($"{field.Property} = {Reading(field.Shape, $"global::RulealizeStudio.Binding.Values.At(node, {Literal(field.Key)})")};");
        }

        w.Close();

        foreach (Document.ShapeField field in shape.Fields)
        {
            w.Line();
            w.Line($"/// <summary>Gets <c>{Xml(field.Key)}</c>.</summary>");
            w.Line($"public {field.Shape.Type} {field.Property} {{ get; }}");
        }

        foreach (Document.ShapeField field in shape.Fields.Where(field => field.Shape.IsRecord))
        {
            w.Line();
            WriteRecord(w, field.Shape, $"The part <c>{Xml(field.Key)}</c>.");
        }

        w.Close();
    }

    private static void WriteInput(Writer w, string model, Document.Input input)
    {
        w.Line($"/// <summary>The input <c>{Xml(input.Name)}</c>: a property per parameter, and the command that applies it.</summary>");
        w.Open($"public sealed class {input.TypeName} : global::RulealizeStudio.Binding.RuleInput");
        w.Line($"internal {input.TypeName}({model} model)");
        w.Line($"    : base(model, {Literal(input.Name)})");
        w.Open(null);
        foreach (Document.Parameter parameter in input.Parameters)
        {
            w.Line($"{(parameter.IsSettled ? "Settled" : "Open")}<{parameter.Type}>({Literal(parameter.Name)}, nameof({parameter.Property}));");
        }

        w.Close();

        foreach (Document.Parameter parameter in input.Parameters)
        {
            w.Line();
            w.Line($"/// <summary>Gets or sets the value for <c>{Xml(parameter.Name)}</c>.</summary>");
            w.Line($"public {parameter.Type} {parameter.Property}");
            w.Open(null);
            w.Line($"get => Get<{parameter.Type}>({Literal(parameter.Name)});");
            w.Line($"set => Set({Literal(parameter.Name)}, value);");
            w.Close();
            w.Line();

            if (parameter.IsSettled)
            {
                w.Line($"/// <summary>Gets what <c>{Xml(parameter.Name)}</c> may be from the position.</summary>");
                w.Line($"public global::System.Collections.Generic.IReadOnlyList<string> {parameter.Property}Options => Options({Literal(parameter.Name)});");
            }
            else
            {
                w.Line($"/// <summary>Gets what <c>{Xml(parameter.Name)}</c> may hold, as its schema declares.</summary>");
                w.Line($"public global::RulealizeStudio.Binding.Limits {parameter.Property}Limits => Limits({Literal(parameter.Name)});");
            }
        }

        w.Close();
    }

    private static string Reading(Document.Shape shape, string node, bool nullable = false) =>
        shape.IsRecord
            ? $"new {shape.Type}({node})"
            : $"global::RulealizeStudio.Binding.Values.As<{(nullable ? Nullable(shape.Type) : shape.Type)}>({node})";

    private static string Nullable(string type) => type.EndsWith("?", StringComparison.Ordinal) ? type : type + "?";

    private static string Literal(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";

    private static string Xml(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Indented lines, and nothing cleverer.</summary>
    private sealed class Writer
    {
        private readonly StringBuilder _text = new();
        private int _depth;

        public void Line(string line = "")
        {
            if (line.Length > 0)
            {
                _text.Append(' ', _depth * 4);
            }

            _text.Append(line).Append('\n');
        }

        public void Open(string? header)
        {
            if (header is not null)
            {
                Line(header);
            }

            Line("{");
            _depth++;
        }

        public void Close()
        {
            _depth--;
            Line("}");
        }

        public override string ToString() => _text.ToString();
    }
}
