// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RulealizeStudio.Server;

/// <summary>What changed of an application's blueprint beyond binding it to the rules.</summary>
/// <remarks>
/// <para>
/// The blueprint is the person's: when the rules are written from it, what binds the two is the
/// agent's to write, and nothing else of it is. Binding is an element's <c>rules</c> in the
/// specification, and on the screen the model the window is given, the bindings on its controls
/// and what a command is given. Whatever else changed — an element reworded, added or taken out, a
/// control placed, moved or given a layout — is said here, a sentence each, so that it is part of
/// what the person reads before they say yes, apart from the bindings.
/// </para>
/// <para>
/// Each is compared as written last committed and as written now; nothing here reads what a rule,
/// an element or a control means. A control is known by its type and where it is among the others,
/// and called by its words where it has some.
/// </para>
/// </remarks>
public static partial class Beyond
{
    /// <summary>What changed of a specification beyond its elements' bindings.</summary>
    /// <param name="before">The specification as last committed; <see langword="null"/> where it was not there.</param>
    /// <param name="after">The specification as written now.</param>
    /// <returns>A sentence for each change, in the order the elements are written; nothing where it is only bound.</returns>
    /// <exception cref="JsonException">One of the two is not JSON.</exception>
    public static ImmutableArray<string> Specification(string? before, string after)
    {
        ArgumentNullException.ThrowIfNull(after);

        Machine was = before is null ? new Machine(null, [], ImmutableDictionary<string, Shape>.Empty, []) : StateMachine.Read(before);
        Machine now = StateMachine.Read(after);
        ImmutableArray<string>.Builder said = ImmutableArray.CreateBuilder<string>();
        if (was.Initial != now.Initial)
        {
            said.Add($"starts in {Quoted(now.Initial)}, where it started in {Quoted(was.Initial)}");
        }

        foreach (Element element in now.Elements)
        {
            if (was.Elements.FirstOrDefault(e => e.Id == element.Id && e.Kind == element.Kind) is not { } old)
            {
                said.Add(element.Says.Length == 0 ? $"{Agreement.Called(element)} added" : $"{Agreement.Called(element)} added: {element.Says}");
                continue;
            }

            Shape oldShape = was.Shapes[old.Id];
            Shape shape = now.Shapes[element.Id];
            foreach ((string field, string? from, string? to) in new (string, string?, string?)[]
            {
                ("name", old.Title, element.Title),
                ("from", oldShape.From, shape.From),
                ("to", oldShape.To, shape.To),
                ("on", oldShape.On, shape.On),
                ("guard", oldShape.Guard, shape.Guard),
                ("final", oldShape.Final ? "true" : null, shape.Final ? "true" : null),
                ("says", old.Says, element.Says),
            })
            {
                if ((from ?? "") != (to ?? ""))
                {
                    said.Add($"{Agreement.Called(element)}: {field} {Quoted(to)}, was {Quoted(from)}");
                }
            }
        }

        foreach (Element old in was.Elements.Where(o => !now.Elements.Any(e => e.Id == o.Id && e.Kind == o.Kind)))
        {
            said.Add($"{Agreement.Called(old)} taken out");
        }

        return said.ToImmutable();
    }

    /// <summary>What changed of a screen beyond binding its controls.</summary>
    /// <param name="before">The XAML as last committed; <see langword="null"/> where it was not there.</param>
    /// <param name="after">The XAML as written now.</param>
    /// <returns>A sentence for each change, in the order the controls are written; nothing where it is only bound.</returns>
    /// <exception cref="FormatException">One of the two is not well-formed XML.</exception>
    /// <remarks>
    /// What binds a screen: a value that is a binding, <c>{Binding …}</c>; the model the window
    /// binds, its <c>x:DataType</c> and the namespace it is declared in; what a command is given,
    /// <c>CommandParameter</c>; and the model a designer draws it with, <c>Design.DataContext</c>.
    /// </remarks>
    public static ImmutableArray<string> Screen(string? before, string after)
    {
        ArgumentNullException.ThrowIfNull(after);

        Control[] was = before is null ? [] : Controls(Markup.Read(before));
        Control[] now = Controls(Markup.Read(after));

        // The longest run of controls of the same type in the same order is what stayed; the rest
        // was placed or taken out.
        int[,] common = new int[was.Length + 1, now.Length + 1];
        for (int i = was.Length - 1; i >= 0; i--)
        {
            for (int j = now.Length - 1; j >= 0; j--)
            {
                common[i, j] = was[i].Type == now[j].Type ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
        }

        ImmutableArray<string>.Builder said = ImmutableArray.CreateBuilder<string>();
        int a = 0, b = 0;
        while (a < was.Length || b < now.Length)
        {
            if (a < was.Length && b < now.Length && was[a].Type == now[b].Type && common[a, b] == common[a + 1, b + 1] + 1)
            {
                Compare(was[a], now[b], said);
                a++;
                b++;
            }
            else if (b < now.Length && (a == was.Length || common[a, b + 1] > common[a + 1, b]))
            {
                said.Add(now[b].In is { } parent ? $"{now[b].Called} placed in {parent}" : $"{now[b].Called} placed");
                b++;
            }
            else
            {
                said.Add($"{was[a].Called} taken out");
                a++;
            }
        }

        return said.ToImmutable();
    }

    private static void Compare(Control was, Control now, ImmutableArray<string>.Builder said)
    {
        if (was.Path != now.Path)
        {
            said.Add(now.In is { } parent ? $"{now.Called} moved into {parent}" : $"{now.Called} moved");
        }

        if (was.Words != now.Words)
        {
            said.Add($"{now.Called}: words {Quoted(now.Words)}, was {Quoted(was.Words)}");
        }

        foreach ((string name, string value) in now.Set)
        {
            if (!was.Set.TryGetValue(name, out string? old))
            {
                said.Add($"{now.Called}: {name}=\"{value}\" given");
            }
            else if (old != value)
            {
                said.Add($"{now.Called}: {name}=\"{value}\", was \"{old}\"");
            }
        }

        foreach (string name in was.Set.Keys.Where(n => !now.Set.ContainsKey(n)))
        {
            said.Add($"{now.Called}: {name} taken off");
        }
    }

    /// <summary>Every element of a screen but what binds it, in the order written.</summary>
    private static Control[] Controls(Markup markup)
    {
        List<Control> controls = [];
        void Walk(MarkupElement element, string path, string? @in)
        {
            if (element.LocalName == "Design.DataContext")
            {
                return;
            }

            SortedDictionary<string, string> set = new(StringComparer.Ordinal);
            foreach (MarkupAttribute attribute in element.Attributes.Where(a => !Binds(a)))
            {
                set[attribute.Name] = attribute.Value;
            }

            string? words = element.Words is { } inside && !string.IsNullOrWhiteSpace(inside) ? inside.Trim() : null;
            string? shown = WordsOf(set) ?? words;
            string called = shown is null ? element.LocalName : $"{element.LocalName} \"{shown}\"";
            string here = $"{path}/{element.LocalName}";
            controls.Add(new Control(element.LocalName, here, @in, called, words, set));
            foreach (MarkupElement child in element.Children)
            {
                Walk(child, here, called);
            }
        }

        Walk(markup.Root, "", null);
        return [.. controls];
    }

    /// <summary>The words a control is called by: those it shows of its own, as the designer gives them.</summary>
    private static string? WordsOf(IReadOnlyDictionary<string, string> set) =>
        new[] { "Title", "Text", "Header", "Content" }.Select(set.GetValueOrDefault).FirstOrDefault(w => !string.IsNullOrEmpty(w));

    private static bool Binds(MarkupAttribute attribute) =>
        attribute.Name == "xmlns"
        || attribute.Name.StartsWith("xmlns:", StringComparison.Ordinal)
        || attribute.Name.EndsWith(":DataType", StringComparison.Ordinal)
        || attribute.Name.EndsWith(":CompileBindings", StringComparison.Ordinal)
        || attribute.Name == "CommandParameter"
        || Binding().IsMatch(attribute.Value);

    private static string Quoted(string? value) => value is null ? "nothing" : $"\"{value}\"";

    [GeneratedRegex(@"^\{\s*(Binding|CompiledBinding|ReflectionBinding)(\s|\})")]
    private static partial Regex Binding();

    /// <param name="Type">The element's name without its prefix.</param>
    /// <param name="Path">Where it is: the types of the elements it is in, and its own.</param>
    /// <param name="In">What the element it is in is called.</param>
    /// <param name="Called">What it is called in a sentence.</param>
    /// <param name="Words">The text inside it, where it holds no element.</param>
    /// <param name="Set">Its attributes but those that bind it.</param>
    private sealed record Control(string Type, string Path, string? In, string Called, string? Words, SortedDictionary<string, string> Set);
}
