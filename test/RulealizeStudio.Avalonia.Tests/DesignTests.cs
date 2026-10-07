// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using RulealizeStudio.Server;

namespace RulealizeStudio.Tests;

/// <summary>What a screen asks of a rule set, read before there is one.</summary>
/// <remarks>
/// A design is written before its rules, and what it binds is the list of what they have to give.
/// <see cref="Design"/> is handed the XAML and nothing else; the rule set is opened here only
/// afterwards, to hold the list against the names the document actually gives.
/// </remarks>
public class DesignTests
{
    [Fact]
    public void SignupsScreenAsksForExactlyTheNamesOfSignupThatItUses()
    {
        IEnumerable<Ask> asks = Design.Read(Xaml("RulealizeStudio.Sample.Signup"));

        string[] answered = Answered(asks, "signup.json");

        // What signup's XAML uses of signup.json. The fields are reached through the projection,
        // so none is bound directly, and `stage` is in the projection but not on the screen.
        Assert.Equal(
            [
                "input book",
                "input chooseSeat",
                "input note",
                "input setName",
                "input setParty",
                "parameter chooseSeat.seat",
                "parameter note.what",
                "parameter setName.to",
                "parameter setParty.size",
                "projection booking.party.size",
                "projection booking.party.wants",
                "projection booking.ready",
                "projection booking.seat",
                "projection booking.who",
            ],
            answered);
    }

    [Fact]
    public void CountdownsScreenAsksForAFieldAndAnInput()
    {
        IEnumerable<Ask> asks = Design.Read(Xaml("RulealizeStudio.Sample.Countdown"));

        Assert.Equal(["field total", "input add"], Answered(asks, "countdown.json"));
    }

    [Fact]
    public void WhatTheModelHasOfItsOwnIsNotAsked()
    {
        IEnumerable<Ask> asks = Design.Read(Window("""
            <Button Command="{Binding Back}" IsEnabled="{Binding !IsTerminal}" />
            <TextBlock Text="{Binding Ending, StringFormat='Over: {0}, done.'}" />
            <TextBlock Text="{Binding Trouble}" />
            """));

        Assert.Empty(asks);
    }

    [Fact]
    public void APartOfAnInputIsAParameterWhateverTheGeneratorHungOnIt()
    {
        IEnumerable<Ask> asks = Design.Read(Window("""
            <Button Command="{Binding Rename.Apply}" />
            <TextBox Text="{Binding Rename.To}" MaxLength="{Binding Rename.ToLimits.MaxLength}" />
            <ComboBox ItemsSource="{Binding Rename.StyleOptions}" />
            <TextBlock Text="{Binding Summary.Name}" />
            """));

        Assert.Equal(
            ["input Rename", "parameter Rename.To", "parameter Rename.To", "parameter Rename.Style", "projection Summary.Name"],
            asks.Select(a => $"{a.Kind} {a.Name}"));
    }

    [Fact]
    public void ABindingIsReadAgainstTheContextItIsIn()
    {
        IEnumerable<Ask> asks = Design.Read(Window("""
            <StackPanel IsVisible="{Binding IsOffered}" DataContext="{Binding Rename}">
              <Button Command="{Binding Apply}" />
              <TextBox Text="{Binding To}" />
            </StackPanel>
            <TextBlock Text="{Binding Text, ElementName=Other}" />
            <ItemsControl ItemsSource="{Binding Summary.Lines}">
              <ItemsControl.ItemTemplate>
                <DataTemplate><TextBlock Text="{Binding Length}" /></DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock>
              <TextBlock.Text>
                <MultiBinding StringFormat="{}{0} of {1}">
                  <Binding Path="State.Count" />
                  <Binding Path="State.Limit" />
                </MultiBinding>
              </TextBlock.Text>
            </TextBlock>
            """));

        Assert.Equal(
            ["input Rename", "input Rename", "input Rename", "parameter Rename.To", "projection Summary.Lines", "field Count", "field Limit"],
            asks.Select(a => $"{a.Kind} {a.Name}"));
    }

    [Fact]
    public void AnAskIsPlacedAtTheBindingItCameFrom()
    {
        string xaml = Xaml("RulealizeStudio.Sample.Signup");

        Ask ask = Design.Read(xaml).First(a => a.Name == "SetParty.Size");

        Assert.Equal("{Binding SetParty.Size}", xaml.Substring(ask.Start, ask.Length));
    }

    /// <summary>Each ask as the name the rule set gives it, which has to be exactly one.</summary>
    private static string[] Answered(IEnumerable<Ask> asks, string document)
    {
        List<(string Kind, string[] Names)> given = Names(document);

        return [.. asks
            .Select(ask => given.Where(g => g.Kind == ask.Kind && ask.IsNamedBy(g.Names)).ToArray() switch
            {
                [var one] => $"{one.Kind} {string.Join('.', one.Names)}",
                var none => throw new Xunit.Sdk.XunitException($"{ask.Kind} {ask.Name} is answered by {none.Length} names of {document}."),
            })
            .Distinct()
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Every name a rule set gives that a screen could bind, by what it is given to.</summary>
    private static List<(string Kind, string[] Names)> Names(string document)
    {
        JsonNode root = JsonNode.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ruleset", document)),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;
        List<(string, string[])> names = [];

        foreach ((string field, _) in root["state"]!["schema"]!.AsObject())
        {
            names.Add(("field", [field]));
        }

        foreach ((string input, JsonNode? body) in root["inputs"]!.AsObject())
        {
            names.Add(("input", [input]));
            foreach ((string parameter, _) in body?["params"]?.AsObject() ?? [])
            {
                names.Add(("parameter", [input, parameter]));
            }
        }

        foreach ((string projection, JsonNode? body) in root["projections"]?.AsObject() ?? [])
        {
            Members([projection], body);
        }

        return names;

        void Members(string[] path, JsonNode? body)
        {
            names.Add(("projection", path));
            if (body is JsonObject record && record["op"] is null)
            {
                foreach ((string key, JsonNode? part) in record)
                {
                    Members([.. path, key], part);
                }
            }
        }
    }

    private static string Xaml(string application) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", application, "MainWindow.axaml"));

    private static string Window(string body) => $"""
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:m="using:Somewhere"
                x:DataType="m:SomeModel">
          <StackPanel>
        {body}
          </StackPanel>
        </Window>
        """;
}
