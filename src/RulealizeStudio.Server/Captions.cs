// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Xml;

namespace RulealizeStudio.Server;

/// <summary>What a screen calls the box it takes a value in: for a parameter its XAML binds, the words written for that box.</summary>
/// <remarks>
/// <para>
/// A move is said as the window says it — the words of the control pressed and the values entered
/// beside it (<see cref="Hosting.Reach.Say(System.Collections.Generic.IEnumerable{Avalonia.Visual}, Binding.RuleModel, string)"/>) — but a value is entered into a box, and the box is
/// called by what is written for it. That is read here from the XAML as it is written, by one rule
/// and nothing guessed beyond it:
/// </para>
/// <list type="number">
/// <item>The box is the element that binds the parameter — its value, or its <c>Limits</c> or
/// <c>Options</c> — as <see cref="Design"/> reads what a screen binds.</item>
/// <item>Its caption is its own <c>AutomationProperties.Name</c>, where that is written as words:
/// the name a screen reader says it by.</item>
/// <item>Otherwise it is the words of the nearest element written before it, in what holds it or in
/// what holds that, up to three levels out: an element whose <c>Text</c>, <c>Content</c> or
/// <c>Header</c> is written as words, and that is not pressed — it has no <c>Command</c> — and binds
/// nothing. The search stops at an element that holds another box, whose words are that box's.</item>
/// </list>
/// <para>
/// A box the rule finds nothing for has no caption, and is said by its parameter's name in the
/// rules, as that name.
/// </para>
/// </remarks>
public static class Captions
{
    private const int Levels = 3;

    private static readonly string[] WordsAttributes = ["Text", "Content", "Header"];

    /// <summary>Reads the caption of every box a screen binds a parameter to.</summary>
    /// <param name="xaml">The screen, as written.</param>
    /// <returns>Each caption found, by the name the XAML binds — <c>SetName.To</c> — in the order the boxes are written.</returns>
    /// <exception cref="XmlException">The text is not XML.</exception>
    /// <exception cref="FormatException">The text is not XML <see cref="Markup"/> reads.</exception>
    public static IReadOnlyList<(Ask Box, string Caption)> Read(string xaml)
    {
        ArgumentNullException.ThrowIfNull(xaml);

        Ask[] parameters = [.. Design.Read(xaml).Where(ask => ask.Kind == "parameter")];
        Markup markup = Markup.Read(xaml);
        List<MarkupElement> all = [];
        Collect(markup.Root, all);

        Dictionary<MarkupElement, Ask> boxes = [];
        foreach (Ask ask in parameters)
        {
            MarkupElement? box = all.LastOrDefault(element => element.Attributes.Any(a => a.Start <= ask.Start && ask.Start < a.End));
            if (box is not null && !boxes.ContainsKey(box))
            {
                boxes[box] = ask;
            }
        }

        List<(Ask, string)> found = [];
        HashSet<string> said = new(StringComparer.Ordinal);
        foreach ((MarkupElement box, Ask ask) in boxes.OrderBy(pair => pair.Key.Start))
        {
            if (!said.Contains(ask.Name) && CaptionOf(box, boxes) is { } caption)
            {
                said.Add(ask.Name);
                found.Add((ask, caption));
            }
        }

        return found;
    }

    private static void Collect(MarkupElement element, List<MarkupElement> all)
    {
        all.Add(element);
        foreach (MarkupElement child in element.Children)
        {
            Collect(child, all);
        }
    }

    private static string? CaptionOf(MarkupElement box, Dictionary<MarkupElement, Ask> boxes)
    {
        if (Literal(box.Attribute("AutomationProperties.Name")?.Value) is { } named)
        {
            return named;
        }

        MarkupElement current = box;
        for (int level = 0; level < Levels && current.Parent is { } parent; level++, current = parent)
        {
            List<MarkupElement> siblings = [.. parent.Children.Where(child => !child.IsProperty)];
            for (int i = siblings.IndexOf(current) - 1; i >= 0; i--)
            {
                MarkupElement before = siblings[i];
                if (Holds(before, boxes))
                {
                    return null;
                }

                if (WordsOf(before) is { } words)
                {
                    return words;
                }
            }
        }

        return null;
    }

    /// <summary>Whether an element is a box, or holds one.</summary>
    private static bool Holds(MarkupElement element, Dictionary<MarkupElement, Ask> boxes) =>
        boxes.ContainsKey(element) || element.Children.Any(child => Holds(child, boxes));

    /// <summary>The words an element is written with, where it is one that only says them: not pressed, and bound to nothing.</summary>
    private static string? WordsOf(MarkupElement element)
    {
        if (element.Attribute("Command") is not null || element.Attributes.Any(a => Literal(a.Value) is null && a.Value.StartsWith('{')))
        {
            return null;
        }

        foreach (string name in WordsAttributes)
        {
            if (Literal(element.Attribute(name)?.Value) is { } words)
            {
                return words;
            }
        }

        return element.Children.Count == 0 && !string.IsNullOrWhiteSpace(element.Words) ? element.Words.Trim() : null;
    }

    /// <summary>A value written as words: not a markup extension, an escaped one read as what follows the escape.</summary>
    private static string? Literal(string? value) =>
        value is null || string.IsNullOrWhiteSpace(value) ? null
        : value.StartsWith("{}", StringComparison.Ordinal) ? value[2..].Trim()
        : value.StartsWith('{') ? null
        : value.Trim();
}
