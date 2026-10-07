// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using RulealizeStudio.Server;

namespace RulealizeStudio.Tests;

/// <summary>What a screen calls the box a value is entered in, read from its XAML by one rule.</summary>
public sealed class CaptionsTests
{
    private const string Window = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:m="using:Sample"
                x:DataType="m:SampleModel">
          <StackPanel>
            {0}
          </StackPanel>
        </Window>
        """;

    [Fact]
    public void SignupsBoxesAreCalledByTheWordsWrittenAboveThem()
    {
        string signup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "RulealizeStudio.Sample.Signup", "MainWindow.axaml"));

        Assert.Equal(
            [("SetName.To", "Your name"), ("SetParty.Size", "How many of you?"), ("Note.What", "Anything we should know?")],
            Captions.Read(signup).Where(c => c.Box.Name != "ChooseSeat.Seat").Select(c => (c.Box.Name, c.Caption)));
    }

    [Fact]
    public void ABoxsOwnAutomationNameIsItsCaption()
    {
        string xaml = Read("""
            <TextBlock Text="Above" />
            <TextBox AutomationProperties.Name="Your name" Text="{Binding SetName.To}" />
            <Button Content="Save" Command="{Binding SetName.Apply}" />
            """);

        Assert.Equal(("SetName.To", "Your name"), Single(xaml));
    }

    [Fact]
    public void WhatIsPressedOrBoundIsPassedOverAndTheSearchGoesOutward()
    {
        string xaml = Read("""
            <TextBlock Text="Your name" />
            <DockPanel>
              <Button Content="Save" Command="{Binding SetName.Apply}" />
              <TextBlock Text="{Binding State.Name}" />
              <TextBox Text="{Binding SetName.To}" />
            </DockPanel>
            """);

        Assert.Equal(("SetName.To", "Your name"), Single(xaml));
    }

    [Fact]
    public void TheWordsOfAnotherBoxAreNeverTakenAndABoxWithNoneHasNoCaption()
    {
        string xaml = Read("""
            <StackPanel>
              <TextBlock Text="Your name" />
              <TextBox Text="{Binding SetName.To}" />
            </StackPanel>
            <TextBox Text="{Binding SetParty.Size}" />
            <Button Content="Save" Command="{Binding SetName.Apply}" />
            <Button Content="Set" Command="{Binding SetParty.Apply}" />
            """);

        Assert.Equal([("SetName.To", "Your name")], Captions.Read(xaml).Select(c => (c.Box.Name, c.Caption)));
    }

    [Fact]
    public void AnInputsNamesAreTheRulesNamesAsTheGeneratorSpellsThem()
    {
        (Ask box, string caption) = Assert.Single(Captions.Read(Read("""
            <TextBlock Text="Your name" />
            <TextBox Text="{Binding SetName.To}" />
            <Button Content="Save" Command="{Binding SetName.Apply}" />
            """)));

        Assert.True(box.IsNamedBy("setName", "to"));
        Assert.True(box.IsNamedBy("set-name", "to"));
        Assert.Equal("Your name", caption);
    }

    private static string Read(string inside) => Window.Replace("{0}", inside, StringComparison.Ordinal);

    private static (string, string) Single(string xaml)
    {
        (Ask box, string caption) = Assert.Single(Captions.Read(xaml));
        return (box.Name, caption);
    }
}
