// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RulealizeStudio.Binding;
using RulealizeStudio.Hosting;
using RulealizeStudio.Sample.Countdown;
using RulealizeStudio.Sample.Signup;

namespace RulealizeStudio.Tests;

/// <summary>The applications made of a rule set and XAML, driven the way somebody would.</summary>
/// <remarks>
/// Each window is the application's own <c>MainWindow.axaml</c>, compiled into its assembly and
/// opened on the model generated from its rule set. A button is pressed with the pointer, so one
/// the rules disabled cannot be pressed here either.
/// </remarks>
public class ApplicationTests
{
    [AvaloniaFact]
    public void ATypedNameMovesThePositionAndTheAnswerBoundToIt()
    {
        Window window = Open(new SignupModel(AppContext.BaseDirectory));

        Assert.False(Find<Control>(window, "SeatBox").IsEffectivelyVisible);

        Find<TextBox>(window, "NameBox").Text = "alice";
        Press(window, "SaveName");

        Assert.Equal("alice", Find<TextBlock>(window, "Who").Text);
        Assert.True(Find<Control>(window, "SeatBox").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void TheBoxIsBoundedByTheSchemaThroughABinding()
    {
        Window window = Open(new SignupModel(AppContext.BaseDirectory));

        // Twelve is in signup.json and nowhere in this repository's XAML.
        Assert.Equal(12, Find<TextBox>(window, "NameBox").MaxLength);
    }

    [AvaloniaFact]
    public void ARefusalIsShownOnTheBoxItWasAbout()
    {
        Window window = Open(new SignupModel(AppContext.BaseDirectory));
        TextBox name = Find<TextBox>(window, "NameBox");

        name.Text = "admin";
        Press(window, "SaveName");

        // Avalonia's own per-field validation, fed by the clause's parameter, in the sentence
        // signup's label document gives the clause.
        Assert.Equal(["Nobody books under that name."], DataValidationErrors.GetErrors(name)!.Cast<object>().Select(e => e.ToString()));
    }

    [AvaloniaFact]
    public void BookIsEnabledWhenTheRulesSayTheBookingIsReady()
    {
        Window window = Open(new SignupModel(AppContext.BaseDirectory));
        Find<TextBox>(window, "NameBox").Text = "alice";
        Press(window, "SaveName");

        Assert.False(Find<Button>(window, "Book").IsEffectivelyEnabled);
        Assert.False(Find<CheckBox>(window, "Ready").IsChecked);

        Find<ComboBox>(window, "SeatBox").SelectedItem = "window";
        Press(window, "ChooseSeat");

        Assert.True(Find<CheckBox>(window, "Ready").IsChecked);
        Press(window, "Book");

        Assert.True(Find<TextBlock>(window, "Done").IsEffectivelyVisible);
        Assert.Equal("All done: booked.", Find<TextBlock>(window, "Done").Text);
    }

    [AvaloniaFact]
    public void ASecondRuleSetWithItsOwnXamlIsADifferentApplication()
    {
        Window window = Open(new CountdownModel(AppContext.BaseDirectory));

        Press(window, "AddThree");
        Press(window, "AddThree");
        Press(window, "AddTwo");

        Assert.Equal("8", Find<TextBlock>(window, "Total").Text);

        // The XAML wrote three buttons; the guard disabled one.
        Assert.False(Find<Button>(window, "AddThree").IsEffectivelyEnabled);
        Assert.True(Find<Button>(window, "AddTwo").IsEffectivelyEnabled);

        Press(window, "AddTwo");
        Assert.Equal("done", Find<TextBlock>(window, "Done").Text);
        Assert.False(Find<Button>(window, "AddOne").IsEffectivelyEnabled);

        Press(window, "Undo");
        Assert.Equal("8", Find<TextBlock>(window, "Total").Text);
    }

    [Fact]
    public void TheEntryPointFindsTheModelItsRuleSetWasGeneratedAs()
    {
        // Program.cs names no model, so it is the same in every application and in the template.
        Assert.Equal([typeof(SignupModel)], RuleApp.Models(typeof(SignupModel).Assembly));
        Assert.Equal([typeof(CountdownModel)], RuleApp.Models(typeof(CountdownModel).Assembly));
    }

    [Fact]
    public void AnApplicationWithNoRuleSetYetHasNoModelRatherThanFailing()
    {
        // What an application folder is when the template has just made it.
        Assert.Empty(RuleApp.Models(typeof(RuleApp).Assembly));
    }

    /// <summary>Opens an application's own window on a model made for the test.</summary>
    internal static Window Open(RuleModel model)
    {
        Window window = (Window)AvaloniaXamlLoader.Load(new Uri($"avares://{model.GetType().Assembly.GetName().Name}/{RuleApp.Screen}"));
        window.DataContext = model;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Presses a button with the pointer, and fails where the rules left it disabled.</summary>
    private static void Press(Window window, string name) => Press(window, Find<Button>(window, name));

    /// <summary>Presses a control with the pointer, and fails where the rules left it disabled.</summary>
    internal static void Press(Window window, Control button)
    {
        Dispatcher.UIThread.RunJobs();
        string name = button.Name ?? button.ToString() ?? "the control";
        Assert.True(button.IsEffectivelyEnabled, $"'{name}' is disabled");

        Point centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException($"'{name}' is not laid out");

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(Visual window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name)
        ?? throw new InvalidOperationException($"'{name}' is not on the screen.");
}
