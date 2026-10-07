// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Windows.Input;

namespace RulealizeStudio.Binding;

/// <summary>A command whose being enabled is somebody else's answer.</summary>
internal sealed class RuleCommand(Func<object?, bool> can, Action<object?> execute) : ICommand
{
    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => can(parameter);

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (can(parameter))
        {
            execute(parameter);
        }
    }

    /// <summary>Tells whoever is bound that the answer may have changed.</summary>
    public void Changed() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
