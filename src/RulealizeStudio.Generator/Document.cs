// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;

namespace RulealizeStudio.Generator;

/// <summary>What a rule set says about its own shape, read before anything is compiled.</summary>
/// <remarks>
/// <para>
/// Only what the document states outright is read: the names of its fields, inputs, parameters
/// and projections, a field's schema, and a projection written as a record. A type comes from a
/// field's schema or from a literal; anything a vocabulary works out — an <c>op</c>, a
/// definition — is <see cref="object"/>, because saying more would take evaluating the rules,
/// and evaluating them here would be a second account of them.
/// </para>
/// <para>
/// A schema's <c>op</c> is read into a type here and nowhere else. It is the one place this
/// generator knows a vocabulary's names.
/// </para>
/// </remarks>
internal sealed class Document
{
    private const string Unknown = "object?";

    private Document(string id, string file, string text)
    {
        Id = id;
        File = file;
        Text = text;
    }

    public string Id { get; }

    public string File { get; }

    public string Text { get; }

    public string ClassName => Name.Pascal(Id) + "Model";

    public List<Field> Fields { get; } = [];

    public List<Input> Inputs { get; } = [];

    public List<Projection> Projections { get; } = [];

    /// <summary>Reads a rule set, or says it is not one.</summary>
    /// <returns>The document, or null for JSON that is not a rule set.</returns>
    /// <exception cref="FormatException">The text is not JSON, or a rule set is missing its id.</exception>
    public static Document? Read(string file, string text)
    {
        if (Json.Parse(text) is not Json.Record root
            || root["$schema"] is not Json.Text { Value: "rulealize/ruleset/v1" })
        {
            return null;
        }

        if (root["id"] is not Json.Text { Value.Length: > 0 } id)
        {
            throw new FormatException("A rule set has no id, and the model is named after it.");
        }

        Document document = new(id.Value, file, text);

        if (root["state"] is Json.Record state && state["schema"] is Json.Record schema)
        {
            foreach (KeyValuePair<string, Json> field in schema.Fields)
            {
                document.Fields.Add(new Field(field.Key, Name.Pascal(field.Key), Type(field.Value, nullableAlways: false)));
            }
        }

        if (root["inputs"] is Json.Record inputs)
        {
            foreach (KeyValuePair<string, Json> input in inputs.Fields)
            {
                document.Inputs.Add(document.ReadInput(input.Key, input.Value as Json.Record));
            }
        }

        if (root["projections"] is Json.Record projections)
        {
            foreach (KeyValuePair<string, Json> projection in projections.Fields)
            {
                string property = Name.Pascal(projection.Key);
                document.Projections.Add(new Projection(
                    projection.Key,
                    property,
                    document.Infer(projection.Value, property + "Projection")));
            }
        }

        return document;
    }

    /// <summary>What a field's schema says it holds.</summary>
    private static string Type(Json schema, bool nullableAlways)
    {
        if (schema is not Json.Record record)
        {
            return Unknown;
        }

        string type = record["op"] is Json.Text op
            ? op.Value switch
            {
                "type.int" => "long",
                "type.bool" => "bool",
                "type.string" or "type.enum" => "string",
                _ => "object",
            }
            : "object";

        bool nullable = nullableAlways || type == "object" || record["nullable"] is Json.Flag { Value: true };
        return nullable ? type + "?" : type;
    }

    private static string Nullable(string type) => type.EndsWith("?", StringComparison.Ordinal) ? type : type + "?";

    private Input ReadInput(string name, Json.Record? declared)
    {
        string property = Name.Pascal(name);
        Input input = new(name, property, property + "Input");

        if (declared?["params"] is Json.Record parameters)
        {
            foreach (KeyValuePair<string, Json> parameter in parameters.Fields)
            {
                Json.Record? body = parameter.Value as Json.Record;
                string parameterProperty = Name.Pascal(parameter.Key);

                if (body?["domain"] is not null)
                {
                    input.Parameters.Add(new Parameter(parameter.Key, parameterProperty, "string?", settled: true));
                    continue;
                }

                string type = body?["open"] switch
                {
                    Json.Record open when open["field"] is Json.Text field =>
                        Fields.FirstOrDefault(each => each.Name == field.Value) is Field known ? Nullable(known.Type) : Unknown,
                    Json.Record open => Type(open, nullableAlways: true),
                    _ => Unknown,
                };

                input.Parameters.Add(new Parameter(parameter.Key, parameterProperty, type, settled: false));
            }
        }

        return input;
    }

    /// <summary>The shape an expression evaluates to, as far as the document states it.</summary>
    private Shape Infer(Json expression, string typeName)
    {
        switch (expression)
        {
            case Json.Text text when text.Value.StartsWith("$", StringComparison.Ordinal):
                string name = text.Value.Substring(1);
                return new Shape(Fields.FirstOrDefault(field => field.Name == name)?.Type ?? Unknown);

            case Json.Text text when text.Value.StartsWith("#", StringComparison.Ordinal)
                || text.Value.StartsWith("@", StringComparison.Ordinal):
                return new Shape(Unknown);

            case Json.Text:
                return new Shape("string");

            case Json.Number number:
                return new Shape(number.IsWhole ? "long" : "decimal");

            case Json.Flag:
                return new Shape("bool");

            case Json.Record record when record["op"] is null:
                Shape shape = new(typeName);
                foreach (KeyValuePair<string, Json> field in record.Fields)
                {
                    string property = Name.Pascal(field.Key);
                    shape.Fields.Add(new ShapeField(field.Key, property, Infer(field.Value, property + "Record")));
                }

                return shape;

            default:
                return new Shape(Unknown);
        }
    }

    internal sealed class Field(string name, string property, string type)
    {
        public string Name { get; } = name;

        public string Property { get; } = property;

        public string Type { get; } = type;
    }

    internal sealed class Input(string name, string property, string typeName)
    {
        public string Name { get; } = name;

        public string Property { get; } = property;

        public string TypeName { get; } = typeName;

        public List<Parameter> Parameters { get; } = [];
    }

    internal sealed class Parameter(string name, string property, string type, bool settled)
    {
        public string Name { get; } = name;

        public string Property { get; } = property;

        public string Type { get; } = type;

        public bool IsSettled { get; } = settled;
    }

    internal sealed class Projection(string name, string property, Shape shape)
    {
        public string Name { get; } = name;

        public string Property { get; } = property;

        public Shape Shape { get; } = shape;
    }

    /// <summary>A value's type, or a record of named parts.</summary>
    internal sealed class Shape(string type)
    {
        /// <summary>Gets the type, which for a record is the nested class written for it.</summary>
        public string Type { get; } = type;

        public List<ShapeField> Fields { get; } = [];

        public bool IsRecord => Fields.Count > 0;
    }

    internal sealed class ShapeField(string key, string property, Shape shape)
    {
        public string Key { get; } = key;

        public string Property { get; } = property;

        public Shape Shape { get; } = shape;
    }
}
