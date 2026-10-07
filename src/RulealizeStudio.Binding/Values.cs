// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RulealizeStudio.Binding;

/// <summary>Values between the JSON the runtime answers in and the types a screen binds to.</summary>
/// <remarks>
/// A generated property has the type the document states — a field's schema, a literal — and
/// <see cref="object"/> where only evaluating the rules would say. The second kind is read as
/// the plainest thing the JSON is, so a binding converts it the way it would any value.
/// </remarks>
public static class Values
{
    /// <summary>Reads a JSON value as a type.</summary>
    /// <typeparam name="T">The type the document says it is.</typeparam>
    /// <param name="node">The value.</param>
    /// <returns>The value as that type, or the default for a JSON null.</returns>
    /// <exception cref="InvalidCastException">The value is not one the type can hold.</exception>
    public static T As<T>(JsonNode? node)
    {
        if (node is null)
        {
            return default!;
        }

        Type type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        object? value = type == typeof(object) ? Plain(node) : Typed(node, type);

        return value is null ? default! : (T)value;
    }

    /// <summary>Reads one field of a record.</summary>
    /// <param name="node">The record.</param>
    /// <param name="key">The field.</param>
    /// <returns>The field's value, or null where the value is not a record or has no such field.</returns>
    public static JsonNode? At(JsonNode? node, string key) => node is JsonObject record ? record[key] : null;

    /// <summary>Reads a JSON value as the plainest thing it is.</summary>
    /// <param name="node">The value.</param>
    /// <returns>Text, a boolean, a whole number, a decimal, a list or a record, or null.</returns>
    public static object? Plain(JsonNode? node) => node switch
    {
        null => null,
        JsonArray list => list.Select(Plain).ToList(),
        JsonObject record => record.ToDictionary(field => field.Key, field => Plain(field.Value), StringComparer.Ordinal),
        _ => node.GetValueKind() switch
        {
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when Number(node) is decimal number =>
                decimal.Truncate(number) == number && number is >= long.MinValue and <= long.MaxValue ? (long)number : number,
            _ => null,
        },
    };

    /// <summary>Writes a value a screen held as JSON, for an input document.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Its JSON form.</returns>
    public static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        long number => JsonValue.Create(number),
        int number => JsonValue.Create(number),
        decimal number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        _ => JsonValue.Create(Text(value)),
    };

    /// <summary>Renders a value as text the way the runtime renders an argument.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A word as itself, a boolean in lower case, a number in invariant digits.</returns>
    public static string Text(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>A number read from its JSON text, which is the same whether the value was parsed or built from a CLR number.</summary>
    private static decimal Number(JsonNode node) =>
        decimal.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static object Typed(JsonNode node, Type type)
    {
        JsonValueKind kind = node.GetValueKind();

        if (type == typeof(string))
        {
            return kind == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();
        }

        if (type == typeof(bool) && kind is JsonValueKind.True or JsonValueKind.False)
        {
            return kind == JsonValueKind.True;
        }

        if (kind == JsonValueKind.Number)
        {
            decimal number = Number(node);

            if (type == typeof(long))
            {
                return (long)number;
            }

            if (type == typeof(decimal))
            {
                return number;
            }

            if (type == typeof(double))
            {
                return (double)number;
            }
        }

        throw new InvalidCastException($"{node.ToJsonString()} is not a {type.Name}.");
    }
}
