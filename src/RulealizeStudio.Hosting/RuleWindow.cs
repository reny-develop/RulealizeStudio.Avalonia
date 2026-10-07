// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace RulealizeStudio.Hosting;

/// <summary>When a window of an application is shown, and what its × does: each an answer of the rules, bound in its XAML.</summary>
/// <remarks>
/// <para>
/// A window coming and going is something the application does, so it is the rules' to decide, as
/// whether a button can be pressed is. Its XAML binds the answer, and nothing names a window for it:
/// </para>
/// <code>
/// &lt;Window x:DataType="m:SignupModel"
///         rs:RuleWindow.ShowWhen="{Binding Book.IsOffered}"
///         rs:RuleWindow.CloseWith="{Binding Back.Apply}"&gt;
/// </code>
/// <para>
/// <see cref="ShowWhenProperty"/> shows the window while what it is bound to is true and hides it
/// when it is not; a window without it is shown from the start. <see cref="CloseWithProperty"/>
/// makes the × a move, as a button's <c>Command</c> makes a press one, with
/// <see cref="CloseParameterProperty"/> as its <c>CommandParameter</c>: the × makes the move where
/// the rules offer it, and the window goes only if that makes <see cref="ShowWhenProperty"/> false.
/// A window bound to a rule set without it cannot be closed by its × at all — what somebody can do
/// on the screen is a move the test design walks, or nothing. <see cref="RuleWindows"/> keeps all of it.
/// </para>
/// </remarks>
public sealed class RuleWindow
{
    /// <summary>Whether the window is shown: bound to something the rules answer true or false.</summary>
    public static readonly AttachedProperty<bool> ShowWhenProperty =
        AvaloniaProperty.RegisterAttached<RuleWindow, Window, bool>("ShowWhen");

    /// <summary>The move the window's × makes: bound to an input's <c>Apply</c>.</summary>
    public static readonly AttachedProperty<ICommand?> CloseWithProperty =
        AvaloniaProperty.RegisterAttached<RuleWindow, Window, ICommand?>("CloseWith");

    /// <summary>The argument the × passes the move, as a button's <c>CommandParameter</c> does.</summary>
    public static readonly AttachedProperty<object?> CloseParameterProperty =
        AvaloniaProperty.RegisterAttached<RuleWindow, Window, object?>("CloseParameter");

    private RuleWindow()
    {
    }

    /// <summary>Gets whether the window is shown.</summary>
    /// <param name="window">The window.</param>
    /// <returns>What <see cref="ShowWhenProperty"/> is bound to, now.</returns>
    public static bool GetShowWhen(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.GetValue(ShowWhenProperty);
    }

    /// <summary>Sets whether the window is shown.</summary>
    /// <param name="window">The window.</param>
    /// <param name="value">Whether it is.</param>
    public static void SetShowWhen(Window window, bool value)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetValue(ShowWhenProperty, value);
    }

    /// <summary>Gets the move the window's × makes.</summary>
    /// <param name="window">The window.</param>
    /// <returns>The command, or null where the × makes none.</returns>
    public static ICommand? GetCloseWith(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.GetValue(CloseWithProperty);
    }

    /// <summary>Sets the move the window's × makes.</summary>
    /// <param name="window">The window.</param>
    /// <param name="value">The command.</param>
    public static void SetCloseWith(Window window, ICommand? value)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetValue(CloseWithProperty, value);
    }

    /// <summary>Gets the argument the × passes its move.</summary>
    /// <param name="window">The window.</param>
    /// <returns>The argument, or null.</returns>
    public static object? GetCloseParameter(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.GetValue(CloseParameterProperty);
    }

    /// <summary>Sets the argument the × passes its move.</summary>
    /// <param name="window">The window.</param>
    /// <param name="value">The argument.</param>
    public static void SetCloseParameter(Window window, object? value)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetValue(CloseParameterProperty, value);
    }

    /// <summary>Whether a window is shown only while the rules say: <see cref="ShowWhenProperty"/> is bound or set on it.</summary>
    /// <param name="window">The window.</param>
    /// <returns><see langword="true"/> where it is; <see langword="false"/> for a window shown from the start.</returns>
    public static bool IsShownByRules(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.IsSet(ShowWhenProperty);
    }

    /// <summary>Presses the window's ×: makes the move it is bound to, where the rules offer it.</summary>
    /// <param name="window">The window.</param>
    /// <returns><see langword="true"/> where a move was made; <see langword="false"/> where the × makes none, or none the rules offer now.</returns>
    /// <remarks>The window is not closed here: it goes if the move makes <see cref="ShowWhenProperty"/> false.</remarks>
    public static bool Close(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        object? parameter = GetCloseParameter(window);
        if (GetCloseWith(window) is not { } move || !move.CanExecute(parameter))
        {
            return false;
        }

        move.Execute(parameter);
        return true;
    }
}
