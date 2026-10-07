// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Avalonia;
using Avalonia.Controls;

namespace RulealizeStudio.Server;

/// <summary>
/// Which element of a screen's XAML a control was made from, set on each control of the copy the
/// <see cref="Designer"/> draws — never on the file — so that a control found under a pointer is
/// known by the characters it was written as.
/// </summary>
public sealed class Placed
{
    /// <summary>The element's number, in the order the placed elements are written; -1 on a control no element of the screen made.</summary>
    public static readonly AttachedProperty<int> AtProperty = AvaloniaProperty.RegisterAttached<Placed, Control, int>("At", -1);

    private Placed()
    {
    }

    /// <summary>Which element a control was made from.</summary>
    /// <param name="control">The control.</param>
    /// <returns>The element's number, or -1.</returns>
    public static int GetAt(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(AtProperty);
    }

    /// <summary>Says which element a control was made from.</summary>
    /// <param name="control">The control.</param>
    /// <param name="value">The element's number.</param>
    public static void SetAt(Control control, int value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(AtProperty, value);
    }
}
