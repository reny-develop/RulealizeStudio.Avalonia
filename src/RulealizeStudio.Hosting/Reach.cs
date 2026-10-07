// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Rulealize;
using RulealizeStudio.Binding;

namespace RulealizeStudio.Hosting;

/// <summary>Which control on a screen stands for which move.</summary>
/// <remarks>
/// <para>
/// The contract a screen keeps without being asked: a control stands for a move when its
/// <c>Command</c> is that input's <see cref="RuleInput.Apply"/>. Its <c>CommandParameter</c>,
/// where it passes one, is the argument of the input's one settled parameter; where it passes
/// none it stands for every move of the input, and the values come from the editors bound beside
/// it — which a caller who knows only the rule set fills through <see cref="RuleInput.Hold"/>.
/// Nobody names a control for this, and no control is named here.
/// </para>
/// <para>
/// A window's × is one of those controls where its XAML makes it a move
/// (<see cref="RuleWindow.CloseWithProperty"/>): the window stands for that move as a button does, and
/// pressing it is <see cref="RuleWindow.Close"/>. An application of several windows is looked at
/// whole — every window given, each the root of what is looked in.
/// </para>
/// <para>
/// A screen cannot offer a move the rules do not: whether the command is enabled is
/// <c>GetValidInputs</c>'s answer. What a screen can do is offer less — leave a legal move with no
/// control that stands for it — and that is what <see cref="Unreachable(IEnumerable{Visual}, RuleModel)"/> reports.
/// </para>
/// </remarks>
public static class Reach
{
    /// <summary>Finds the visible controls that stand for a move.</summary>
    /// <param name="screen">The window, or any part of it.</param>
    /// <param name="model">The model the screen is bound to.</param>
    /// <param name="move">A move, as the runtime offered it.</param>
    /// <returns>The controls, in the order they are in the visual tree.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<Control> For(Visual screen, RuleModel model, ValidInput move)
    {
        ArgumentNullException.ThrowIfNull(screen);

        return For([screen], model, move);
    }

    /// <summary>Finds the visible controls that stand for a move, on any of several windows.</summary>
    /// <param name="screens">The windows.</param>
    /// <param name="model">The model the windows are bound to.</param>
    /// <param name="move">A move, as the runtime offered it.</param>
    /// <returns>The controls, window by window, in the order they are in the visual tree.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<Control> For(IEnumerable<Visual> screens, RuleModel model, ValidInput move)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(move);

        return [.. Sources(screens).Where(control => Stands(control, model, move))];
    }

    /// <summary>Lists the legal moves from the position that no visible control stands for.</summary>
    /// <param name="screen">The window.</param>
    /// <param name="model">The model the screen is bound to.</param>
    /// <returns>The moves the screen does not offer, in the order the runtime listed them.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<ValidInput> Unreachable(Visual screen, RuleModel model)
    {
        ArgumentNullException.ThrowIfNull(screen);

        return Unreachable([screen], model);
    }

    /// <summary>Lists the legal moves from the position that no visible control on any of several windows stands for.</summary>
    /// <param name="screens">The windows.</param>
    /// <param name="model">The model the windows are bound to.</param>
    /// <returns>The moves the windows do not offer, in the order the runtime listed them.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<ValidInput> Unreachable(IEnumerable<Visual> screens, RuleModel model)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(model);

        Control[] sources = [.. Sources(screens)];
        return [.. model.Moves.Where(move => !sources.Any(control => Stands(control, model, move)))];
    }

    /// <summary>Says a move as the screen says it: the words of the control that stands for it, and the values entered for it beside that control.</summary>
    /// <param name="screen">The window.</param>
    /// <param name="model">The model the screen is bound to.</param>
    /// <param name="step">The move as a route writes it: <c>setName(to: alice)</c>, <c>add(n: 2)</c>, <c>roll drawing 3</c>.</param>
    /// <returns>
    /// The words — <c>Save: alice</c>, <c>+2</c>, <c>Roll → 3</c> — or <see langword="null"/> where no
    /// control stands for the input, or the one that does says nothing in words.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A control is looked for whether it is shown now or not, since a move is said wherever it is
    /// listed, not only where it can be made. A value the control's own <c>CommandParameter</c>
    /// fixes is said by the control's words and not again.
    /// </remarks>
    public static string? Say(Visual screen, RuleModel model, string step)
    {
        ArgumentNullException.ThrowIfNull(screen);

        return Say([screen], model, step);
    }

    /// <summary>Says a move as the windows say it: the words of the control on any of them that stands for it, and the values entered for it beside that control.</summary>
    /// <param name="screens">The windows, shown or not.</param>
    /// <param name="model">The model the windows are bound to.</param>
    /// <param name="step">The move as a route writes it.</param>
    /// <returns>
    /// The words, as <see cref="Say(Visual, RuleModel, string)"/> gives them — a window's × said as its
    /// title and ×, <c>Confirm ×</c> — or <see langword="null"/> where nothing says it.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static string? Say(IEnumerable<Visual> screens, RuleModel model, string step)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(step);

        int drawing = step.IndexOf(" drawing ", StringComparison.Ordinal);
        string drew = drawing < 0 ? "" : $" → {step[(drawing + " drawing ".Length)..]}";
        string move = drawing < 0 ? step : step[..drawing];
        int open = move.IndexOf('(', StringComparison.Ordinal);
        string name = open < 0 ? move : move[..open];
        List<KeyValuePair<string, string>> arguments = [];
        if (open >= 0 && move.EndsWith(')'))
        {
            foreach (string argument in move[(open + 1)..^1].Split(", "))
            {
                int colon = argument.IndexOf(": ", StringComparison.Ordinal);
                if (colon < 0)
                {
                    return null;
                }

                arguments.Add(KeyValuePair.Create(argument[..colon], argument[(colon + 2)..]));
            }
        }

        if (model.Inputs.FirstOrDefault(each => each.Input == name) is not { } input)
        {
            return null;
        }

        // A window never shown has no visual tree yet, and is read by what it holds instead.
        (Control Control, object? Parameter)[] standing = [.. screens
            .SelectMany(screen => new[] { screen }.Concat(screen.GetVisualDescendants()).Concat(screen.GetLogicalDescendants().OfType<Visual>()))
            .Distinct()
            .OfType<Control>()
            .Select(control => (Control: control, Carried: Carried(control)))
            .Where(each => each.Carried is { } carried && ReferenceEquals(carried.Command, input.Apply))
            .Select(each => (each.Control, each.Carried!.Value.Parameter))];
        foreach ((Control source, object? parameter) in standing.OrderBy(each => each.Parameter is null))
        {
            KeyValuePair<string, string>[] fixedHere = [.. arguments.Where(argument => input.Fixes(argument.Key, parameter))];
            if (parameter is not null
                && (fixedHere.Length != 1 || fixedHere[0].Value != Values.Text(parameter)))
            {
                continue;
            }

            if (Words(source) is not { } words)
            {
                return null;
            }

            string[] entered = [.. arguments.Except(fixedHere).Select(argument => argument.Value)];
            return (entered.Length == 0 ? words : $"{words}: {string.Join(", ", entered)}") + drew;
        }

        return null;
    }

    /// <summary>The move a control makes and the argument it passes: a command source's command, or a window's ×.</summary>
    /// <param name="control">The control.</param>
    /// <returns>The command and its parameter, or null where pressing the control makes no move.</returns>
    internal static (ICommand Command, object? Parameter)? Carried(Control control) => control switch
    {
        Window window => RuleWindow.GetCloseWith(window) is { } close ? (close, RuleWindow.GetCloseParameter(window)) : null,
        ICommandSource { Command: { } command } source => (command, source.CommandParameter),
        _ => null,
    };

    /// <summary>What a control says in words: its content or its header, where either is text; a window's × its title and ×.</summary>
    private static string? Words(Control source) => source switch
    {
        Window window => string.IsNullOrWhiteSpace(window.Title) ? "×" : $"{window.Title.Trim()} ×",
        MenuItem { Header: string header } => header,
        ContentControl { Content: string content } => content,
        ContentControl { Content: TextBlock { Text: { } text } } => text,
        _ => null,
    } is { } said && !string.IsNullOrWhiteSpace(said) ? said.Trim() : null;

    /// <summary>The visible controls that carry a command, a window whose × makes a move among them, window by window in the order they are in the visual tree.</summary>
    internal static IEnumerable<Control> Sources(IEnumerable<Visual> screens) =>
        screens.SelectMany(screen => new[] { screen }.Concat(screen.GetVisualDescendants()))
            .OfType<Control>()
            .Where(control => Carried(control) is not null && control.IsEffectivelyVisible);

    private static bool Stands(Control control, RuleModel model, ValidInput move)
    {
        (ICommand command, object? parameter) = Carried(control)!.Value;
        RuleInput? input = model.Inputs.FirstOrDefault(each => ReferenceEquals(each.Apply, command));

        return input is not null && input.StandsFor(move, parameter);
    }
}
