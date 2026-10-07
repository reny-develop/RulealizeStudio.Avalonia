// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RulealizeStudio.Binding;
using RulealizeStudio.Hosting;

namespace RulealizeStudio.Tests;

/// <summary>An application of several windows over several rule sets: this assembly's own, window\*.axaml.</summary>
/// <remarks>
/// The order window is shown from the start, the confirming window while the rules offer
/// <c>change</c>, and its × is <c>change</c>; the draw window binds another rule set. Nothing of
/// that is written anywhere but in the three windows' XAML.
/// </remarks>
public class WindowsTests
{
    [AvaloniaFact]
    public void EveryWindowIsOpenedOnTheModelOfTheRuleSetItBinds()
    {
        RuleWindows windows = RuleWindows.Open(typeof(WindowsTests).Assembly);
        try
        {
            Window order = windows.Window("window/OrderWindow.axaml")!;
            Window confirm = windows.Window("window/ConfirmWindow.axaml")!;
            Window draw = windows.Window("window/DrawWindow.axaml")!;

            Assert.Equal(3, windows.Windows.Count);
            Assert.Equal(2, windows.Models.Count);
            Assert.IsType<OrderModel>(order.DataContext);
            Assert.Same(order.DataContext, confirm.DataContext);
            Assert.IsType<DrawModel>(draw.DataContext);
            Assert.Same(windows.Model("order"), order.DataContext);
            Assert.Equal([confirm, order], windows.Of((RuleModel)order.DataContext!));
        }
        finally
        {
            windows.End();
        }
    }

    [AvaloniaFact]
    public void AWindowIsShownWhileTheRulesSayAndItsCrossIsTheMoveItIsBoundTo()
    {
        RuleWindows windows = RuleWindows.Open(typeof(WindowsTests).Assembly);
        try
        {
            windows.Show();
            Window order = windows.Window("window/OrderWindow.axaml")!;
            Window confirm = windows.Window("window/ConfirmWindow.axaml")!;
            OrderModel model = (OrderModel)order.DataContext!;

            Assert.Equal(["window/DrawWindow.axaml", "window/OrderWindow.axaml"], windows.Shown.Select(windows.File).Order(StringComparer.Ordinal));

            ApplicationTests.Press(order, Find(order, "Add"));
            ApplicationTests.Press(order, Find(order, "Review"));
            Assert.True(confirm.IsVisible);

            // The × the rules offer: change, which takes the order back to the window it is made on.
            confirm.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(confirm.IsVisible);
            Assert.False(model.State.Confirming);

            // The order window's × makes no move, so it stays, and so does the order.
            order.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(order.IsVisible);
            Assert.Equal(1, model.State.Count);
        }
        finally
        {
            windows.End();
        }
    }

    [AvaloniaFact]
    public void ACrossTheRulesDoNotOfferMakesNoMove()
    {
        RuleWindows windows = RuleWindows.Open(typeof(WindowsTests).Assembly);
        try
        {
            windows.Show();
            Window order = windows.Window("window/OrderWindow.axaml")!;
            Window confirm = windows.Window("window/ConfirmWindow.axaml")!;
            OrderModel model = (OrderModel)order.DataContext!;

            ApplicationTests.Press(order, Find(order, "Add"));
            ApplicationTests.Press(order, Find(order, "Review"));
            ApplicationTests.Press(confirm, Find(confirm, "Place"));

            // Placed, the order is final: change is no longer offered, so the window it shows stays hidden.
            Assert.True(model.IsTerminal);
            Assert.False(confirm.IsVisible);
            Assert.False(RuleWindow.Close(confirm));
        }
        finally
        {
            windows.End();
        }
    }

    [AvaloniaFact]
    public void AMoveMadeByACrossIsSaidAsTheWindowsTitleAndCross()
    {
        RuleWindows windows = RuleWindows.Open(typeof(WindowsTests).Assembly);
        try
        {
            RuleModel model = windows.Model("order")!;

            Assert.Equal("Confirm ×", Reach.Say(windows.Of(model), model, "change"));
            Assert.Equal("Place", Reach.Say(windows.Of(model), model, "place"));
        }
        finally
        {
            windows.End();
        }
    }

    [AvaloniaFact]
    public void TheDesignIsWalkedOnEveryWindowOfItsRuleSet()
    {
        RuleWindows windows = RuleWindows.Open(typeof(WindowsTests).Assembly);
        try
        {
            windows.Show();
            RuleModel model = windows.Model("order")!;
            string design = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ruleset", "order.test-design.json"));

            Assert.Empty(Replay.Run(windows, model, design, labels: null, Press));
            Assert.Equal(0, ((OrderModel)model).State.Count);
        }
        finally
        {
            windows.End();
        }
    }

    [AvaloniaFact]
    public void AWindowShownByTheRulesIsStoodInAndItsCrossIsAmongTheMoves()
    {
        RuleWindows windows = RuleWindows.Open(typeof(WindowsTests).Assembly);
        try
        {
            windows.Show();
            RuleModel model = windows.Model("order")!;
            string design = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ruleset", "order.test-design.json"));
            string confirming = Replay.Situations(design).First(situation => situation.Moves.Any(move => move.Input == "change")).State;
            List<(string Step, Control? Control)> offered = [];

            IReadOnlyList<Divergence> found = Replay.Stand(windows, model, design, confirming, labels: null, Press, new Witness(
                Pressing: (_, _) => { },
                Arrived: () => { },
                Offered: (step, control) => offered.Add((step, control)),
                Refused: (_, _) => { }));

            Assert.Empty(found);
            Assert.True(windows.Window("window/ConfirmWindow.axaml")!.IsVisible);
            Assert.Same(windows.Window("window/ConfirmWindow.axaml"), offered.Single(each => each.Step == "change").Control);
        }
        finally
        {
            windows.End();
        }
    }

    private static void Press(Control control) =>
        ApplicationTests.Press((Window)TopLevel.GetTopLevel(control)!, control);

    private static Control Find(Window window, string name) =>
        window.GetVisualDescendants().OfType<Control>().First(control => control.Name == name);
}
