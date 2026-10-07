// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RulealizeStudio.Server;

namespace RulealizeStudio.Tests;

/// <summary>What changed of a blueprint beyond binding it, which the person reads apart from the bindings before saying yes to the rules.</summary>
/// <remarks>
/// Signup's blueprint as the person made it is signup's with nothing bound — the specification's
/// elements bound to no rule, the screen bound to no model — and binding it to signup's rules is
/// nothing beyond binding. Whatever else an agent changes is said, a sentence each.
/// </remarks>
public class BeyondTests
{
    [Fact]
    public void SignupsSpecificationBoundToItsRulesIsNothingBeyondBinding() =>
        Assert.Empty(Beyond.Specification(Unbound(Specified()), Specified()));

    [Fact]
    public void AnElementRewordedAddedOrTakenOutIsSaid()
    {
        string person = Unbound(Specified());
        string bound = Specified()
            .Replace("A party is one to six people, and one to start with.", "A party is one to eight people.", StringComparison.Ordinal);
        bound = Specification.Edit(bound, new JsonObject { ["op"] = "remove", ["id"] = "name-reserved" }).Text;
        (bound, string? added) = Specification.Edit(bound, new JsonObject { ["op"] = "add", ["kind"] = "note", ["on"] = "seat" });
        bound = Specification.Edit(bound, new JsonObject { ["op"] = "set", ["id"] = added, ["field"] = "says", ["value"] = "A seat is chosen once." }).Text;

        Assert.Equal(
            [
                "transition 'party-size' (Set the party): says \"A party is one to eight people.\", was \"A party is one to six people, and one to start with.\"",
                $"note '{added}' added: A seat is chosen once.",
                "note 'name-reserved' taken out",
            ],
            Beyond.Specification(person, bound).ToArray());
    }

    [Fact]
    public void WithNoSpecificationCommittedEveryElementIsAdded() =>
        Assert.Equal(15, Beyond.Specification(null, Specified()).Count(s => s.EndsWith(" added", StringComparison.Ordinal) || s.Contains(" added: ", StringComparison.Ordinal)));

    [Fact]
    public void SignupsScreenBoundToItsModelIsNothingBeyondBinding() =>
        Assert.Empty(Beyond.Screen(UnboundScreen(Signup()), Signup()));

    [Fact]
    public void ALayoutGivenAControlPlacedAndWordsChangedAreSaid()
    {
        string person = UnboundScreen(Signup());
        string bound = Signup()
            .Replace("<TextBlock Text=\"Your name\" FontWeight=\"SemiBold\" />", "<TextBlock Text=\"Name\" FontWeight=\"SemiBold\" Margin=\"0,4\" />", StringComparison.Ordinal)
            .Replace("<Button x:Name=\"Book\"", "<Separator />\n        <Button x:Name=\"Book\"", StringComparison.Ordinal);

        Assert.Equal(
            [
                "TextBlock \"Name\": Margin=\"0,4\" given",
                "TextBlock \"Name\": Text=\"Name\", was \"Your name\"",
                "Separator placed in StackPanel",
            ],
            Beyond.Screen(person, bound).ToArray());
    }

    [Fact]
    public void AControlTakenOutOrWrappedInAnotherIsSaid()
    {
        const string Person = """
            <Window xmlns="https://github.com/avaloniaui" Title="Count">
              <StackPanel>
                <TextBlock Text="Left" />
                <Button Content="Add" />
              </StackPanel>
            </Window>
            """;
        const string Bound = """
            <Window xmlns="https://github.com/avaloniaui" Title="Count">
              <StackPanel>
                <Border>
                  <Button Content="Add" Command="{Binding Add.Apply}" CommandParameter="1" />
                </Border>
              </StackPanel>
            </Window>
            """;

        Assert.Equal(
            ["TextBlock \"Left\" taken out", "Border placed in StackPanel", "Button \"Add\" moved into Border"],
            Beyond.Screen(Person, Bound).ToArray());
    }

    /// <summary>A specification as the person wrote it, before there were rules: every element bound to nothing.</summary>
    private static string Unbound(string specification)
    {
        JsonObject document = JsonNode.Parse(specification)!.AsObject();
        foreach (string member in new[] { "states", "transitions", "notes" })
        {
            foreach ((_, JsonNode? element) in document[member]!.AsObject())
            {
                element!.AsObject().Remove("rules");
            }
        }

        return document.ToJsonString();
    }

    /// <summary>
    /// A screen as the person made it, before there were rules: signup's with nothing that binds it —
    /// no bindings, no model, nothing a command is given.
    /// </summary>
    private static string UnboundScreen(string screen) =>
        Regex.Replace(
            Regex.Replace(screen, @"\s*<Design\.DataContext>.*?</Design\.DataContext>", "", RegexOptions.Singleline),
            @"\s+([\w.:]+)=""([^""]*)""",
            match => match.Groups[1].Value is "xmlns:m" or "x:DataType" or "CommandParameter" || match.Groups[2].Value.StartsWith("{Binding", StringComparison.Ordinal)
                ? ""
                : match.Value);

    private static string Specified() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "specification", "RulealizeStudio.Sample.Signup", Specification.File));

    private static string Signup() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "RulealizeStudio.Sample.Signup", "MainWindow.axaml")).ReplaceLineEndings("\n");
}
