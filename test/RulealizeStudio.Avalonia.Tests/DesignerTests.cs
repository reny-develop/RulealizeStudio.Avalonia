// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RulealizeStudio.Sample.Signup;
using RulealizeStudio.Server;

namespace RulealizeStudio.Tests;

/// <summary>A screen designed without its XAML being read, from the window the template writes to signup's.</summary>
/// <remarks>
/// The designer is asked what the screen's editor asks it, a request at a time, and every change it
/// answers is a text: the screen is never anything else. Its window is loaded here with the controls
/// this suite loads — Avalonia's, signup's model, and <see cref="Dial"/>, which no list names.
/// </remarks>
public sealed class DesignerTests
{
    /// <summary>The window the <c>rulealize-avalonia-app</c> template writes, as it writes it.</summary>
    private const string EmptyWindow = """
        <!--
          The application's screen, with nothing on it yet. Once there is a rule set, the window binds
          the model generated from it: x:DataType set to that model, and bindings to the names the rule
          set gives.
        -->
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Title="Rulealize.Avalonia.Example"
                Width="480" Height="360">
        </Window>

        """;

    [AvaloniaFact]
    public void TheStepsMakeEveryControlSignupsWindowHasInWhatHoldsItThere()
    {
        JsonObject steps = Steps();
        JsonArray signup = Placed(Ask(Designer(), "draw", Signup()));

        Assert.Equal(signup.Count - 1, steps["controls"]!.AsArray().Count);
        foreach ((JsonNode? step, int i) in steps["controls"]!.AsArray().Select((step, i) => (step, i)))
        {
            JsonNode control = signup[i + 1]!;
            Assert.Equal((string?)control["type"], (string?)step!["control"]);
            Assert.Equal((int?)control["parent"], (int?)step["in"]);
            // Words where signup's window has words of its own, and none where they are bound.
            Assert.Equal(control["words"] is not null, step["words"] is not null);
        }
    }

    [AvaloniaFact]
    public void SignupsScreenIsMadeFromTheTemplatesEmptyWindowWithoutWritingXaml()
    {
        Designer designer = Designer();
        JsonObject steps = Steps();
        string text = EmptyWindow;

        text = Made(Ask(designer, "words", text, new() { ["at"] = 0, ["words"] = (string?)steps["title"] }), 0);
        foreach ((JsonNode? step, int i) in steps["controls"]!.AsArray().Select((step, i) => (step, i)))
        {
            JsonObject placed = Ask(designer, "place", text, new() { ["type"] = (string?)step!["control"], ["into"] = (int)step["in"]! });
            text = Made(placed, i + 1);
            if ((string?)step["words"] is { } words)
            {
                text = Made(Ask(designer, "words", text, new() { ["at"] = i + 1, ["words"] = words }), i + 1);
            }
        }

        JsonObject drawn = Ask(designer, "draw", text);
        Assert.Null(drawn["trouble"]);
        JsonArray made = Placed(drawn);
        JsonArray signup = Placed(Ask(Designer(), "draw", Signup()));
        Assert.Equal(signup.Select(c => ((string?)c!["type"], (int?)c["parent"])), made.Select(c => ((string?)c!["type"], (int?)c["parent"])));
        Assert.Equal(
            [(string?)steps["title"], .. steps["controls"]!.AsArray().Select(step => (string?)step!["words"])],
            made.Select(c => (string?)c!["words"]));
        Assert.DoesNotContain("Binding", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ACommentAndAHandFormattedElementSurviveTenEdits()
    {
        Designer designer = Designer();
        string text = Signup();
        string comment = text[..(text.IndexOf("-->", StringComparison.Ordinal) + 3)];
        int partyAt = text.IndexOf("<NumericUpDown", StringComparison.Ordinal);
        string party = text[partyAt..(text.IndexOf("/>", partyAt, StringComparison.Ordinal) + 2)];
        int scroll = Id(designer, text, "ScrollViewer");
        int panel = scroll + 1;

        text = Made(Ask(designer, "words", text, new() { ["at"] = 0, ["words"] = "席の予約" }), 0);
        text = Text(Ask(designer, "place", text, new() { ["type"] = typeof(TextBlock).FullName, ["into"] = panel, ["index"] = 0 }));
        text = Text(Ask(designer, "words", text, new() { ["at"] = panel + 1, ["words"] = "ようこそ" }));
        text = Text(Ask(designer, "move", text, new() { ["at"] = panel + 1, ["into"] = panel, ["index"] = 3 }));
        text = Text(Ask(designer, "words", text, new() { ["at"] = Id(designer, text, "Button"), ["words"] = "戻す" }));
        text = Text(Ask(designer, "remove", text, new() { ["at"] = Id(designer, text, "Button") + 1 }));
        text = Text(Ask(designer, "place", text, new() { ["type"] = typeof(Separator).FullName, ["into"] = panel }));
        text = Text(Ask(designer, "move", text, new() { ["at"] = Id(designer, text, "Separator"), ["into"] = panel, ["index"] = 0 }));
        text = Text(Ask(designer, "remove", text, new() { ["at"] = Id(designer, text, "Separator") }));
        text = Text(Ask(designer, "words", text, new() { ["at"] = 0, ["words"] = "Sign up" }));

        Assert.StartsWith(comment, text, StringComparison.Ordinal);
        Assert.Contains(party, text, StringComparison.Ordinal);
        Assert.Null(Ask(designer, "draw", text)["trouble"]);
    }

    [AvaloniaFact]
    public void AControlIsWrittenOnALineOfItsOwnInWhatHoldsItAndTakenOutWithItsLine()
    {
        Designer designer = Designer();

        string placed = Made(Ask(designer, "place", EmptyWindow, new() { ["type"] = typeof(StackPanel).FullName, ["into"] = 0 }), 1);
        Assert.Equal(EmptyWindow.Replace("Height=\"360\">\n", "Height=\"360\">\n  <StackPanel />\n", StringComparison.Ordinal), placed);

        string inside = Made(Ask(designer, "place", placed, new() { ["type"] = typeof(Button).FullName, ["into"] = 1 }), 2);
        Assert.Contains("  <StackPanel>\n    <Button />\n  </StackPanel>\n</Window>", inside, StringComparison.Ordinal);

        string removed = Made(Ask(designer, "remove", inside, new() { ["at"] = 2 }), 1);
        Assert.Contains("  <StackPanel>\n  </StackPanel>\n</Window>", removed, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void WhereAPointFallsIsAskedOfTheWindowsControls()
    {
        Designer designer = Designer();
        string text = EmptyWindow.Replace("</Window>", """
              <StackPanel>
                <Button Content="One" Height="40" />
                <Button Content="Two" Height="40" />
              </StackPanel>
            </Window>
            """, StringComparison.Ordinal);
        JsonArray placed = Placed(Ask(designer, "draw", text));
        JsonNode two = placed[3]!["box"]!;

        Assert.Equal(3, (int?)Ask(designer, "at", text, new() { ["x"] = 5, ["y"] = (double)two["y"]! + 5 })["at"]);
        Assert.Equal(1, (int?)Ask(designer, "at", text, new() { ["x"] = 5, ["y"] = 300 })["at"]);

        // Dropped on the panel below both, it goes after them; on the top half of the first, before it.
        string after = Text(Ask(designer, "place", text, new() { ["type"] = typeof(CheckBox).FullName, ["x"] = 5, ["y"] = 300 }));
        Assert.Contains("<Button Content=\"Two\" Height=\"40\" />\n    <CheckBox />", after, StringComparison.Ordinal);
        string before = Text(Ask(designer, "place", text, new() { ["type"] = typeof(CheckBox).FullName, ["x"] = 5, ["y"] = 2 }));
        Assert.Contains("<CheckBox />\n    <Button Content=\"One\"", before, StringComparison.Ordinal);

        // A control dragged onto another goes beside it.
        string moved = Text(Ask(designer, "move", text, new() { ["at"] = 2, ["x"] = 5, ["y"] = (double)two["y"]! + 35 }));
        Assert.Contains("<Button Content=\"Two\" Height=\"40\" />\n    <Button Content=\"One\"", moved, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void AControlTheBuildLoadsThatNoListNamesIsPlacedUnderAPrefixDeclaredForIt()
    {
        Designer designer = Designer();

        Assert.Contains(typeof(Dial), designer.Controls());
        Assert.DoesNotContain(typeof(Window), designer.Controls());
        string text = Made(Ask(designer, "place", EmptyWindow, new() { ["type"] = typeof(Dial).FullName, ["into"] = 0 }), 1);

        Assert.Contains("Height=\"360\" xmlns:tests=\"using:RulealizeStudio.Tests\">", text, StringComparison.Ordinal);
        Assert.Contains("  <tests:Dial />\n", text, StringComparison.Ordinal);
        Assert.Null(Ask(designer, "draw", text)["trouble"]);
    }

    [AvaloniaFact]
    public void WordsAreWrittenAsXamlReadsThem()
    {
        Designer designer = Designer();
        string text = Made(Ask(designer, "place", EmptyWindow, new() { ["type"] = typeof(TextBlock).FullName, ["into"] = 0 }), 1);

        text = Made(Ask(designer, "words", text, new() { ["at"] = 1, ["words"] = "{いらっしゃいませ} & \"<ようこそ>\"" }), 1);

        Assert.Contains("<TextBlock Text=\"{}{いらっしゃいませ} &amp; &quot;&lt;ようこそ&gt;&quot;\" />", text, StringComparison.Ordinal);
        Assert.Equal("{いらっしゃいませ} & \"<ようこそ>\"", (string?)Placed(Ask(designer, "draw", text))[1]!["words"]);

        string cleared = Made(Ask(designer, "words", text, new() { ["at"] = 1, ["words"] = string.Empty }), 1);
        Assert.Contains("  <TextBlock />\n", cleared, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void WhatCannotBeDoneIsRefusedAndSaysWhich()
    {
        Designer designer = Designer();
        string text = EmptyWindow.Replace("</Window>", """
              <StackPanel>
                <Button Content="One" />
                <TextBlock />
              </StackPanel>
            </Window>
            """, StringComparison.Ordinal);

        Assert.Equal("full", (string?)Ask(designer, "place", text, new() { ["type"] = typeof(Button).FullName, ["into"] = 2 })["refused"]);
        Assert.Equal("full", (string?)Ask(designer, "place", text, new() { ["type"] = typeof(Button).FullName, ["into"] = 0 })["refused"]);
        Assert.Equal("holdsNothing", (string?)Ask(designer, "place", text, new() { ["type"] = typeof(Button).FullName, ["into"] = 3 })["refused"]);
        Assert.Equal("inside", (string?)Ask(designer, "move", text, new() { ["at"] = 1, ["into"] = 1 })["refused"]);
        Assert.Equal("window", (string?)Ask(designer, "remove", text, new() { ["at"] = 0 })["refused"]);
        Assert.Equal("wordless", (string?)Ask(designer, "words", text, new() { ["at"] = 1, ["words"] = "何か" })["refused"]);
        Assert.Equal("unknown", (string?)Ask(designer, "place", text, new() { ["type"] = "Avalonia.Controls.NoSuchControl", ["into"] = 1 })["refused"]);
    }

    [AvaloniaFact]
    public void DroppedOnAGridAControlIsPutWhereItWasDroppedAsVisualStudiosWpfDesignerPutsIt()
    {
        Designer designer = Designer();
        string text = EmptyWindow.Replace("</Window>", "  <Grid />\n</Window>", StringComparison.Ordinal);

        text = Made(Ask(designer, "place", text, new() { ["type"] = typeof(Button).FullName, ["x"] = 40.4, ["y"] = 72 }), 2);

        Assert.Contains("<Button HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"40,72,0,0\" />", text, StringComparison.Ordinal);
        JsonNode button = Placed(Ask(designer, "draw", text))[2]!;
        Assert.Equal((40d, 72d), ((double)button["box"]!["x"]!, (double)button["box"]!["y"]!));
    }

    [AvaloniaFact]
    public void DroppedOnTheTemplatesEmptyWindowAControlIsPutInAGridWhereItWasDropped()
    {
        Designer designer = Designer();

        string text = Made(Ask(designer, "place", EmptyWindow, new() { ["type"] = typeof(Button).FullName, ["x"] = 40, ["y"] = 72 }), 2);

        Assert.Contains("""
              <Grid>
                <Button HorizontalAlignment="Left" VerticalAlignment="Top" Margin="40,72,0,0" />
              </Grid>
            </Window>
            """, text, StringComparison.Ordinal);
        text = Made(Ask(designer, "place", text, new() { ["type"] = typeof(TextBox).FullName, ["x"] = 40, ["y"] = 120 }), 3);
        Assert.Contains("<TextBox HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"40,120,0,0\" />", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void DraggedInAGridAControlIsMovedWhereItIsLetGoOfAndNothingElseOfItChanges()
    {
        Designer designer = Designer();
        string text = EmptyWindow.Replace("</Window>", """
              <Grid>
                <Button Content="One" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="40,72,0,0" />
              </Grid>
            </Window>
            """, StringComparison.Ordinal);
        Ask(designer, "draw", text);

        // Taken hold of 10 across and 5 down, and let go of at (210, 125).
        text = Made(Ask(designer, "move", text, new() { ["at"] = 2, ["x"] = 210, ["y"] = 125, ["dx"] = 10, ["dy"] = 5 }), 2);

        Assert.Contains("<Button Content=\"One\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"200,120,0,0\" />", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void InAGridOfColumnsAControlIsPutInTheColumnItWasDroppedIn()
    {
        Designer designer = Designer();
        string text = EmptyWindow.Replace("</Window>", "  <Grid ColumnDefinitions=\"200,*\" />\n</Window>", StringComparison.Ordinal);

        text = Made(Ask(designer, "place", text, new() { ["type"] = typeof(TextBlock).FullName, ["x"] = 230, ["y"] = 30 }), 2);

        Assert.Contains("<TextBlock Grid.Column=\"1\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"30,30,0,0\" />", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void MovedOutOfAGridIntoAPanelThatLaysItOutWhatSaidWhereItWasGoes()
    {
        Designer designer = Designer();
        string text = EmptyWindow.Replace("</Window>", """
              <Grid>
                <StackPanel Width="200" Height="100" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="0,200,0,0" />
                <Button Content="One" Width="80" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="40,72,0,0" />
              </Grid>
            </Window>
            """, StringComparison.Ordinal);
        Ask(designer, "draw", text);

        text = Made(Ask(designer, "move", text, new() { ["at"] = 3, ["x"] = 50, ["y"] = 250 }), 3);

        Assert.Contains("<StackPanel Width=\"200\" Height=\"100\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"0,200,0,0\">", text, StringComparison.Ordinal);
        Assert.Contains("<Button Content=\"One\" Width=\"80\" />", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void SizedByItsCornerAControlIsGivenAWidthAndHeightAndKeepsItsPlace()
    {
        Designer designer = Designer();
        string text = EmptyWindow.Replace("</Window>", """
              <Grid>
                <Button Content="One" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="40,72,0,0" />
                <TextBox />
              </Grid>
            </Window>
            """, StringComparison.Ordinal);
        Ask(designer, "draw", text);

        text = Made(Ask(designer, "size", text, new() { ["at"] = 2, ["width"] = 120.6, ["height"] = 40 }), 2);
        Assert.Contains("<Button Content=\"One\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"40,72,0,0\" Width=\"121\" Height=\"40\" />", text, StringComparison.Ordinal);

        // One that filled its cell is held where it was, at the top left, as it is sized.
        Ask(designer, "draw", text);
        text = Made(Ask(designer, "size", text, new() { ["at"] = 3, ["width"] = 160, ["height"] = 32 }), 3);
        Assert.Contains("<TextBox Width=\"160\" Height=\"32\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"0,0,0,0\" />", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void EveryPropertyXamlGivesAsTextIsListedWithTheKindOfValueItTakesAndWhatItIsNow()
    {
        Designer designer = Designer();
        string text = EmptyWindow.Replace("</Window>", """
              <Grid>
                <Button Content="One" FontSize="20" IsEnabled="{ReflectionBinding Ready}" />
              </Grid>
            </Window>
            """, StringComparison.Ordinal);
        Ask(designer, "draw", text);

        Dictionary<string, JsonNode> button = Ask(designer, "properties", text, new() { ["at"] = 2 })["properties"]!.AsArray()
            .ToDictionary(p => (string)p!["name"]!, p => p!);
        Assert.Equal(("color", "number", "choice", "text", "number"), (
            (string?)button["Background"]["kind"], (string?)button["FontSize"]["kind"], (string?)button["HorizontalAlignment"]["kind"],
            (string?)button["Margin"]["kind"], (string?)button["Grid.Row"]["kind"]));
        Assert.Contains("Center", button["HorizontalAlignment"]["choices"]!.AsArray().Select(c => (string?)c));
        Assert.Equal(("20", "20"), ((string?)button["FontSize"]["written"], (string?)button["FontSize"]["now"]));
        Assert.True((bool)button["IsEnabled"]["bound"]!);
        Assert.False(button.ContainsKey("Content"));
        Assert.False(button.ContainsKey("DataContext"));

        Dictionary<string, JsonNode> window = Ask(designer, "properties", text, new() { ["at"] = 0 })["properties"]!.AsArray()
            .ToDictionary(p => (string)p!["name"]!, p => p!);
        Assert.Equal(("480", "360"), ((string?)window["Width"]["written"], (string?)window["Height"]["written"]));
    }

    [AvaloniaFact]
    public void APropertyGivenAValueIsWrittenAsItsAttributeAndTakenOutGivenNone()
    {
        Designer designer = Designer();
        string text = EmptyWindow.Replace("</Window>", """
              <Grid>
                <Button Content="One" FontSize="20" IsEnabled="{ReflectionBinding Ready}" />
              </Grid>
            </Window>
            """, StringComparison.Ordinal);

        text = Made(Ask(designer, "set", text, new() { ["at"] = 2, ["name"] = "Background", ["value"] = "#FF3366" }), 2);
        text = Made(Ask(designer, "set", text, new() { ["at"] = 2, ["name"] = "FontSize", ["value"] = "28" }), 2);
        text = Made(Ask(designer, "set", text, new() { ["at"] = 0, ["name"] = "Width", ["value"] = "640" }), 0);
        Assert.Contains("<Button Content=\"One\" FontSize=\"28\" IsEnabled=\"{ReflectionBinding Ready}\" Background=\"#FF3366\" />", text, StringComparison.Ordinal);
        Assert.Contains("Width=\"640\" Height=\"360\"", text, StringComparison.Ordinal);
        Assert.Equal("#ffff3366", (string?)Ask(designer, "properties", text, new() { ["at"] = 2 })["properties"]!.AsArray()
            .Single(p => (string?)p!["name"] == "Background")!["now"]);

        text = Made(Ask(designer, "set", text, new() { ["at"] = 2, ["name"] = "FontSize", ["value"] = null }), 2);
        Assert.Contains("<Button Content=\"One\" IsEnabled=\"{ReflectionBinding Ready}\" Background=\"#FF3366\" />", text, StringComparison.Ordinal);

        Assert.Equal("value", (string?)Ask(designer, "set", text, new() { ["at"] = 2, ["name"] = "FontSize", ["value"] = "big" })["refused"]);
        Assert.Equal("value", (string?)Ask(designer, "set", text, new() { ["at"] = 2, ["name"] = "Margin", ["value"] = "wide" })["refused"]);
        Assert.Equal("bound", (string?)Ask(designer, "set", text, new() { ["at"] = 2, ["name"] = "IsEnabled", ["value"] = "False" })["refused"]);
        Assert.Equal("noProperty", (string?)Ask(designer, "set", text, new() { ["at"] = 2, ["name"] = "Nothing", ["value"] = "1" })["refused"]);
    }

    [AvaloniaFact]
    public void TabsArePutInATabControlAndWhatIsInOneIsShownWhenItIsChosen()
    {
        Designer designer = Designer();
        string text = Made(Ask(designer, "place", EmptyWindow, new() { ["type"] = typeof(TabControl).FullName, ["into"] = 0 }), 1);
        Assert.Equal("many", (string?)Placed(Ask(designer, "draw", text))[1]!["holds"]);

        // A tab is given its type's name as its header, so that it is drawn and can be pointed at.
        text = Made(Ask(designer, "place", text, new() { ["type"] = typeof(TabItem).FullName, ["into"] = 1 }), 2);
        Assert.Contains("<TabItem Header=\"TabItem\" />", text, StringComparison.Ordinal);
        Assert.True((double)Placed(Ask(designer, "draw", text))[2]!["box"]!["width"]! > 0);
        text = Made(Ask(designer, "words", text, new() { ["at"] = 2, ["words"] = "入力" }), 2);

        // A tab put in the tab chosen goes after it, in the tab control.
        text = Made(Ask(designer, "place", text, new() { ["type"] = typeof(TabItem).FullName, ["into"] = 2 }), 3);
        text = Made(Ask(designer, "words", text, new() { ["at"] = 3, ["words"] = "確認" }), 3);
        text = Made(Ask(designer, "place", text, new() { ["type"] = typeof(Button).FullName, ["into"] = 3 }), 4);

        Assert.Contains("""
              <TabControl>
                <TabItem Header="入力" />
                <TabItem Header="確認">
                  <Button />
                </TabItem>
              </TabControl>
            </Window>
            """, text, StringComparison.Ordinal);

        // What is in the second tab is drawn once it, or what is in it, is chosen, and the text stays as it is.
        Assert.Null(Placed(Ask(designer, "draw", text, new() { ["show"] = 2 }))[4]!["box"]);
        Assert.NotNull(Placed(Ask(designer, "draw", text, new() { ["show"] = 4 }))[4]!["box"]);
        Assert.False((bool)Ask(designer, "show", text, new() { ["at"] = 3 })["shown"]!);
        Assert.True((bool)Ask(designer, "show", text, new() { ["at"] = 2 })["shown"]!);

        // The tabs stand across, so one dropped to the right of the last goes after it.
        JsonArray placed = Placed(Ask(designer, "draw", text));
        JsonNode last = placed[3]!["box"]!;
        string after = Text(Ask(designer, "place", text, new() { ["type"] = typeof(TabItem).FullName, ["x"] = (double)last["x"]! + (double)last["width"]! + 4, ["y"] = (double)last["y"]! + 4 }));
        Assert.Contains("<TabItem Header=\"確認\">\n      <Button />\n    </TabItem>\n    <TabItem Header=\"TabItem\" />", after, StringComparison.Ordinal);

        Assert.Equal("listed", (string?)Ask(designer, "place", text.Replace("<TabControl>", "<TabControl ItemsSource=\"{Binding Tabs}\">", StringComparison.Ordinal), new() { ["type"] = typeof(TabItem).FullName, ["into"] = 1 })["refused"]);
    }

    [AvaloniaFact]
    public void ADrawersPaneIsAPartOfItsSplitViewThatAControlIsPutIn()
    {
        Designer designer = Designer();
        string text = Made(Ask(designer, "place", EmptyWindow, new() { ["type"] = typeof(SplitView).FullName, ["into"] = 0 }), 1);
        JsonNode pane = Placed(Ask(designer, "draw", text))[2]!;
        Assert.Equal(("Pane", true, false, "one", false), ((string?)pane["control"], (bool)pane["part"]!, (bool)pane["written"]!, (string?)pane["holds"], (bool)pane["full"]!));

        text = Made(Ask(designer, "place", text, new() { ["type"] = typeof(StackPanel).FullName, ["into"] = 2 }), 3);
        text = Made(Ask(designer, "place", text, new() { ["type"] = typeof(TextBlock).FullName, ["into"] = 1 }), 4);
        text = Made(Ask(designer, "set", text, new() { ["at"] = 1, ["name"] = "IsPaneOpen", ["value"] = "True" }), 1);

        Assert.Contains("""
              <SplitView IsPaneOpen="True">
                <SplitView.Pane>
                  <StackPanel />
                </SplitView.Pane>
                <TextBlock />
              </SplitView>
            </Window>
            """, text, StringComparison.Ordinal);
        Assert.Null(Ask(designer, "draw", text)["trouble"]);

        // The pane is drawn over the content, so what is under a point there is what is in the pane.
        Assert.Equal(3, (int?)Ask(designer, "at", text, new() { ["x"] = 5, ["y"] = 5 })["at"]);
        Assert.Equal(4, (int?)Ask(designer, "at", text, new() { ["x"] = 470, ["y"] = 5 })["at"]);
        string inPane = Text(Ask(designer, "place", text, new() { ["type"] = typeof(Button).FullName, ["x"] = 5, ["y"] = 5 }));
        Assert.Contains("<SplitView.Pane>\n      <StackPanel>\n        <Button />\n      </StackPanel>", inPane, StringComparison.Ordinal);

        Assert.Equal("part", (string?)Ask(designer, "move", text, new() { ["at"] = 2, ["into"] = 0 })["refused"]);
        Assert.Equal("full", (string?)Ask(designer, "place", text, new() { ["type"] = typeof(Button).FullName, ["into"] = 2 })["refused"]);
        string removed = Made(Ask(designer, "remove", text, new() { ["at"] = 2 }), 1);
        Assert.Contains("<SplitView IsPaneOpen=\"True\">\n    <TextBlock />\n  </SplitView>", removed, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TextThatIsNotAWindowYetIsSaidAndStillListsWhatIsOnIt()
    {
        Designer designer = Designer();

        Assert.NotNull(Ask(designer, "draw", EmptyWindow.Replace("</Window>", "  <StackPanel>\n</Window>", StringComparison.Ordinal))["trouble"]);
        JsonObject unknown = Ask(designer, "draw", EmptyWindow.Replace("</Window>", "  <StackPanel Orientation=\"Sideways\" />\n</Window>", StringComparison.Ordinal));
        Assert.NotNull(unknown["trouble"]);
        Assert.Equal(2, Placed(unknown).Count);
    }

    private static Designer Designer() =>
        new(typeof(SignupModel).Assembly, [typeof(Button).Assembly, typeof(SignupModel).Assembly, typeof(DesignerTests).Assembly]);

    private static JsonObject Steps() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "screen", "signup.ja.json")))!.AsObject();

    private static string Signup() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "RulealizeStudio.Sample.Signup", "MainWindow.axaml")).ReplaceLineEndings("\n");

    private static JsonObject Ask(Designer designer, string op, string text, JsonObject? more = null)
    {
        JsonObject asked = new() { ["op"] = op, ["text"] = text };
        foreach ((string key, JsonNode? value) in more ?? [])
        {
            asked[key] = value?.DeepClone();
        }

        return designer.Answer(asked);
    }

    private static JsonArray Placed(JsonObject drawn) => drawn["placed"]!.AsArray();

    private static string Text(JsonObject made)
    {
        Assert.Null(made["refused"]);
        Assert.Null(made["trouble"]);
        return (string)made["text"]!;
    }

    /// <summary>The text a change made, which chose what it placed, moved, gave words to or left.</summary>
    private static string Made(JsonObject made, int chosen)
    {
        string text = Text(made);
        Assert.Equal(chosen, (int?)made["select"]);
        return text;
    }

    private static int Id(Designer designer, string text, string control) =>
        (int)Placed(Ask(designer, "draw", text)).First(c => (string?)c!["control"] == control)!["id"]!;
}

/// <summary>A control no list names, which this suite's build loads.</summary>
public sealed class Dial : Control
{
}
