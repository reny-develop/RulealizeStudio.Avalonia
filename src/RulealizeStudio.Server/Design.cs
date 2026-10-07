// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using RulealizeStudio.Binding;

namespace RulealizeStudio.Server;

/// <summary>What a screen asks of a rule set: the names its XAML binds, read from the XAML alone.</summary>
/// <remarks>
/// <para>
/// A design is written before the rules it asks for, and until they exist its build fails — there
/// is no model to compile its bindings against. That is its expected state, and what it binds is
/// then the list of what the rules have to give. So the list is read here, out of the bindings
/// themselves, and kept nowhere else: a list written beside the XAML would be a second statement
/// of what the screen binds, and would fall behind it.
/// </para>
/// <para>
/// No rule set is read. Which part of a path is a rule set's name and which is the model's own is
/// told by the members the binding layer has — <see cref="RuleModel"/>'s and <see cref="RuleInput"/>'s,
/// as compiled, not a copy of them — and by the two the generator writes, <c>State</c> and a
/// parameter's <c>Limits</c> or <c>Options</c>. A name whose parts are bound with an input's
/// members anywhere in the XAML is an input; any other is a projection.
/// </para>
/// <para>
/// A binding is read where its data context is the model: from the root, and from an element whose
/// <c>DataContext</c> is bound to a part of it. Inside a template, under an <c>x:DataType</c> that is
/// not the root's, and on a binding with a source of its own, the context is something else, and
/// what is bound there is not asked of the rules.
/// </para>
/// </remarks>
public static class Design
{
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>What the generator writes on every model besides what <see cref="RuleModel"/> has.</summary>
    private const string State = "State";

    private static readonly ImmutableHashSet<string> ModelMembers = Members(typeof(RuleModel)).Add(State);

    private static readonly ImmutableHashSet<string> InputMembers = Members(typeof(RuleInput));

    /// <summary>What the generator writes beside a parameter, after the parameter's own name.</summary>
    private static readonly string[] ParameterMembers = ["Limits", "Options"];

    private static readonly string[] Sources = ["ElementName", "RelativeSource", "Source"];

    /// <summary>Reads what a design binds.</summary>
    /// <param name="xaml">The design, as written.</param>
    /// <returns>A name per binding the rules have to give, in the order written.</returns>
    /// <exception cref="XmlException">The text is not XML.</exception>
    public static ImmutableArray<Ask> Read(string xaml)
    {
        ArgumentNullException.ThrowIfNull(xaml);

        XDocument document = XDocument.Parse(xaml, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        int[] lines = LineStarts(xaml);
        List<(string[] Path, int Start, int Length)> bound = [];

        if (document.Root is { } root)
        {
            Walk(root, (string?)root.Attribute(XName.Get("DataType", Xaml)), [], xaml, lines, bound);
        }

        HashSet<string> inputs = bound
            .Where(each => each.Path.Length > 1 && InputMembers.Contains(each.Path[1]))
            .Select(each => each.Path[0])
            .ToHashSet(StringComparer.Ordinal);

        ImmutableArray<Ask>.Builder asks = ImmutableArray.CreateBuilder<Ask>();
        foreach ((string[] path, int start, int length) in bound)
        {
            if (Classify(path, inputs) is ({ } kind, { } name))
            {
                asks.Add(new Ask(kind, name, start, length));
            }
        }

        return asks.ToImmutable();
    }

    private static (string? Kind, string? Name) Classify(string[] path, HashSet<string> inputs)
    {
        string first = path[0];

        if (first == State)
        {
            return path.Length > 1 ? ("field", path[1]) : (null, null);
        }

        if (ModelMembers.Contains(first))
        {
            return (null, null);
        }

        if (!inputs.Contains(first))
        {
            return ("projection", string.Join('.', path));
        }

        if (path.Length == 1 || InputMembers.Contains(path[1]))
        {
            return ("input", first);
        }

        string parameter = path[1];
        foreach (string member in ParameterMembers)
        {
            if (parameter.Length > member.Length && parameter.EndsWith(member, StringComparison.Ordinal))
            {
                parameter = parameter[..^member.Length];
                break;
            }
        }

        return ("parameter", $"{first}.{parameter}");
    }

    /// <summary>
    /// Collects the bindings under an element whose context is the model, or the part of it the
    /// scope names — the path the context is at, from the model, and null where it is not the model at all.
    /// </summary>
    private static void Walk(XElement element, string? model, string[]? scope, string text, int[] lines, List<(string[], int, int)> bound)
    {
        string? type = (string?)element.Attribute(XName.Get("DataType", Xaml));
        if (type is not null && type != model)
        {
            scope = null;
        }
        else if (type is null && element.Name.LocalName.EndsWith("Template", StringComparison.Ordinal))
        {
            scope = null;
        }

        if (element.Name.LocalName is "Binding" or "CompiledBinding" && scope is not null)
        {
            Add(Path(element), scope, element.Attribute("Path"), text, lines, bound);
        }

        // The element's own bindings are against its own data context, so that one is read first.
        foreach (XAttribute attribute in element.Attributes().OrderBy(a => a.Name != "DataContext"))
        {
            if (scope is null || Markup(attribute.Value) is not { } arguments)
            {
                continue;
            }

            string[]? path = Path(arguments);
            Add(path, scope, attribute, text, lines, bound);

            if (attribute.Name == "DataContext")
            {
                scope = path is null ? null : [.. scope, .. path];
            }
        }

        foreach (XElement child in element.Elements())
        {
            Walk(child, model, scope, text, lines, bound);
        }
    }

    private static void Add(string[]? path, string[] scope, XAttribute? attribute, string text, int[] lines, List<(string[], int, int)> bound)
    {
        string[] full = [.. scope, .. path ?? []];
        if (path is null || full.Length == 0)
        {
            return;
        }

        (int start, int length) = attribute is null ? (0, 0) : Place(attribute, text, lines);
        bound.Add((full, start, length));
    }

    /// <summary>The arguments of a <c>{Binding}</c> or <c>{CompiledBinding}</c>, or null for any other value.</summary>
    private static Dictionary<string, string>? Markup(string value)
    {
        string trimmed = value.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}') || trimmed.StartsWith("{}", StringComparison.Ordinal))
        {
            return null;
        }

        string inner = trimmed[1..^1].Trim();
        int space = inner.IndexOfAny([' ', '\t', '\r', '\n']);
        string name = space < 0 ? inner : inner[..space];
        if (name is not ("Binding" or "CompiledBinding"))
        {
            return null;
        }

        Dictionary<string, string> arguments = new(StringComparer.Ordinal);
        int position = 0;
        foreach (string argument in Split(space < 0 ? "" : inner[(space + 1)..]))
        {
            int equals = argument.IndexOf('=');
            if (equals < 0)
            {
                arguments[position++ == 0 ? "Path" : $"#{position}"] = argument;
            }
            else
            {
                arguments[argument[..equals].Trim()] = argument[(equals + 1)..].Trim();
            }
        }

        return arguments;
    }

    /// <summary>A markup extension's arguments, split at the commas that are not inside quotes or braces.</summary>
    private static IEnumerable<string> Split(string arguments)
    {
        int depth = 0;
        char quote = '\0';
        int from = 0;

        for (int i = 0; i < arguments.Length; i++)
        {
            char c = arguments[i];
            if (quote != '\0')
            {
                quote = c == quote ? '\0' : quote;
            }
            else if (c is '\'' or '"')
            {
                quote = c;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                yield return arguments[from..i].Trim();
                from = i + 1;
            }
        }

        if (arguments[from..].Trim() is { Length: > 0 } last)
        {
            yield return last;
        }
    }

    private static string[]? Path(XElement element)
    {
        Dictionary<string, string> arguments = element.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.Ordinal);
        return Path(arguments);
    }

    /// <summary>The names a binding's path reaches through, or null where its source is not the data context or its path is not plain names.</summary>
    private static string[]? Path(Dictionary<string, string> arguments)
    {
        if (Sources.Any(arguments.ContainsKey) || !arguments.TryGetValue("Path", out string? path))
        {
            return null;
        }

        path = path.Trim().TrimStart('!');
        if (path.Length == 0 || path == "." || path[0] is '$' or '#')
        {
            return null;
        }

        List<string> parts = [];
        foreach (string part in path.Split('.'))
        {
            string name = part.Split('[')[0].TrimEnd('^').Trim();
            if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                return null;
            }

            parts.Add(name);
        }

        return [.. parts];
    }

    /// <summary>Where an attribute's value is in the text, between its quotes.</summary>
    private static (int Start, int Length) Place(XAttribute attribute, string text, int[] lines)
    {
        IXmlLineInfo info = attribute;
        if (!info.HasLineInfo() || info.LineNumber > lines.Length)
        {
            return (0, 0);
        }

        int at = lines[info.LineNumber - 1] + info.LinePosition - 1;
        int open = text.IndexOfAny(['"', '\''], at);
        int close = open < 0 ? -1 : text.IndexOf(text[open], open + 1);
        return close < 0 ? (at, 0) : (open + 1, close - open - 1);
    }

    private static int[] LineStarts(string text)
    {
        List<int> starts = [0];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    private static ImmutableHashSet<string> Members(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToImmutableHashSet(StringComparer.Ordinal);
}
