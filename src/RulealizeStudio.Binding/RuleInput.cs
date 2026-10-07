// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections;
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Rulealize;

namespace RulealizeStudio.Binding;

/// <summary>One input a rule set declares, as something a screen binds to.</summary>
/// <remarks>
/// <para>
/// The base of every input type generated from a rule set. The derived class has a property per
/// parameter, named as the document names it; this class holds the values, applies them, and
/// puts a refusal against the property whose parameter the clause was about.
/// </para>
/// <para>
/// A parameter comes in two kinds and a screen treats them differently. One with a
/// <c>domain</c> is <em>settled</em>: its values are enumerated, so what is legal is a list to
/// choose from, and a button may pass one as its command parameter. One left <c>open</c> is
/// waiting for a value nobody enumerated, and what it may hold arrives as
/// <see cref="Limits"/>. Whether the value is acceptable is still the rules' to say, and they
/// say it when the input is applied.
/// </para>
/// </remarks>
public abstract class RuleInput : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private readonly RuleCommand _apply;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);
    private readonly List<string> _settled = [];
    private readonly Dictionary<string, Func<JsonNode?, object?>> _readers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="RuleInput"/> class.</summary>
    /// <param name="model">The model the input belongs to.</param>
    /// <param name="input">The input's name, as the rule set declares it.</param>
    protected RuleInput(RuleModel model, string input)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrEmpty(input);

        Model = model;
        Input = input;
        _apply = new RuleCommand(CanApply, Execute);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <summary>Gets the input's name, as the rule set declares it.</summary>
    public string Input { get; }

    /// <summary>Gets the command that applies the input with the values it holds.</summary>
    /// <remarks>
    /// <para>
    /// Enabled exactly when <c>GetValidInputs</c> offers one move of this input that the values
    /// held pick out. An input whose settled parameter has no value yet is enabled only where
    /// the rules leave one choice; a button that passes the value as its command parameter
    /// picks one itself.
    /// </para>
    /// <para>
    /// A command parameter stands for the one settled parameter, where the input has exactly
    /// one. It is compared as text with what the runtime offered, so <c>CommandParameter="3"</c>
    /// and an argument of three agree.
    /// </para>
    /// </remarks>
    public ICommand Apply => _apply;

    /// <summary>Gets whether the rules offer this input from the position at all.</summary>
    public bool IsOffered => Model.Moves.Any(move => move.Input == Input);

    /// <summary>Gets what the rules said about the last attempt that was not about one parameter.</summary>
    /// <remarks>
    /// Each code's sentence from the model's <see cref="RuleModel.Labels"/>, or the code where there
    /// is none — wording belongs to a label document — and the runtime's message where nothing refused
    /// with a code. A value a parameter's schema does not admit is refused under its <c>invalid</c>, a
    /// code like any clause's; where the rule set gave it none, the runtime's sentence is said here.
    /// </remarks>
    public string? Refusal { get; private set; }

    /// <summary>Gets every code the last attempt was refused with, in the order the clauses are written.</summary>
    /// <remarks>
    /// The whole of the answer, whichever property each code was put against. Empty until an
    /// attempt is refused by a clause, and again once another attempt is made.
    /// </remarks>
    public IReadOnlyList<string> RefusedWith { get; private set; } = [];

    /// <inheritdoc />
    public bool HasErrors => _errors.Count > 0;

    /// <summary>Gets the model the input belongs to.</summary>
    protected RuleModel Model { get; }

    /// <inheritdoc />
    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _errors.TryGetValue(propertyName, out List<string>? errors)
            ? errors
            : Array.Empty<string>();

    /// <summary>Holds a value for a parameter, named as the rule set names it.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="value">The value, in its JSON form; an argument's text for a settled parameter.</param>
    /// <exception cref="ArgumentException">The input declares no parameter of that name.</exception>
    /// <remarks>
    /// For a caller that knows the rule set and not the screen — a runner replaying a test design.
    /// The value lands in the same property an editor on the screen is bound to, so the screen
    /// shows it and pressing the control applies it.
    /// </remarks>
    public void Hold(string parameter, JsonNode? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(parameter);

        if (!_readers.TryGetValue(parameter, out Func<JsonNode?, object?>? read))
        {
            throw new ArgumentException($"'{Input}' declares no parameter '{parameter}'.", nameof(parameter));
        }

        Set(parameter, read(value));
    }

    /// <summary>Says whether a control with this command parameter stands for a move.</summary>
    /// <param name="move">A move, as the runtime offered it.</param>
    /// <param name="parameter">The control's command parameter, or null where it passes none.</param>
    /// <returns>
    /// Whether the move is one of this input's and the parameter, where it fixes the one settled
    /// parameter, is that move's argument. A control that passes nothing stands for every move of
    /// the input: its values come from the editors bound beside it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="move"/> is null.</exception>
    public bool StandsFor(ValidInput move, object? parameter)
    {
        ArgumentNullException.ThrowIfNull(move);

        if (move.Input != Input)
        {
            return false;
        }

        return parameter is null || _settled.Count != 1
            || string.Equals(move.Arguments[_settled[0]], Values.Text(parameter), StringComparison.Ordinal);
    }

    /// <summary>Says whether a control's command parameter is what fixes a parameter's value.</summary>
    /// <param name="parameter">The parameter, as the rule set names it.</param>
    /// <param name="commandParameter">The control's command parameter, or null where it passes none.</param>
    /// <returns>
    /// Whether the control passes a value and the parameter is the input's one settled
    /// parameter, which is the one a command parameter stands for. Any other parameter takes
    /// its value from an editor bound beside the control.
    /// </returns>
    public bool Fixes(string parameter, object? commandParameter) =>
        commandParameter is not null && _settled.Count == 1 && _settled[0] == parameter;

    /// <summary>Declares a parameter whose values the rule set enumerates.</summary>
    /// <typeparam name="T">The property's type.</typeparam>
    /// <param name="parameter">The parameter, as the rule set names it.</param>
    /// <param name="property">The property that holds its value.</param>
    protected void Settled<T>(string parameter, string property)
    {
        _settled.Add(parameter);
        Open<T>(parameter, property);
    }

    /// <summary>Declares a parameter the rule set leaves open.</summary>
    /// <typeparam name="T">The property's type.</typeparam>
    /// <param name="parameter">The parameter, as the rule set names it.</param>
    /// <param name="property">The property that holds its value.</param>
    protected void Open<T>(string parameter, string property)
    {
        _properties[parameter] = property;
        _readers[parameter] = node => Values.As<T>(node);
    }

    /// <summary>Gets the value a parameter holds.</summary>
    /// <typeparam name="T">The property's type.</typeparam>
    /// <param name="parameter">The parameter, as the rule set names it.</param>
    /// <returns>The value, or the default where there is none.</returns>
    protected T Get<T>(string parameter) =>
        _values.TryGetValue(parameter, out object? value) && value is T held ? held : default!;

    /// <summary>Sets the value a parameter holds, and forgets what was said about the last one.</summary>
    /// <typeparam name="T">The property's type.</typeparam>
    /// <param name="parameter">The parameter, as the rule set names it.</param>
    /// <param name="value">The value.</param>
    protected void Set<T>(string parameter, T value)
    {
        if (Equals(Get<T>(parameter), value))
        {
            return;
        }

        _values[parameter] = value;
        string property = _properties[parameter];

        Forget(property);
        Changed(property);
        _apply.Changed();
    }

    /// <summary>Gets what a settled parameter may be from the position, in the order the runtime offered it.</summary>
    /// <param name="parameter">The parameter, as the rule set names it.</param>
    /// <returns>Each value as the runtime renders it.</returns>
    protected IReadOnlyList<string> Options(string parameter) =>
    [
        .. Model.Moves
            .Where(move => move.Input == Input)
            .Select(move => move.Arguments[parameter])
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>Gets what an open parameter may hold, as the schema it is open to declares.</summary>
    /// <param name="parameter">The parameter, as the rule set names it.</param>
    /// <returns>The bounds, or none where the input is not offered from the position.</returns>
    protected Limits Limits(string parameter)
    {
        foreach (ValidInput move in Model.Moves)
        {
            if (move.Input == Input && move.Open.TryGetValue(parameter, out OpenParameter? open))
            {
                return Binding.Limits.Of(open);
            }
        }

        return Binding.Limits.None;
    }

    /// <summary>Tells everybody bound to the input that the position moved.</summary>
    internal void Moved()
    {
        Changed(string.Empty);
        _apply.Changed();
    }

    private bool CanApply(object? parameter) => Chosen(parameter) is not null;

    /// <summary>The one offered move the values held pick out, where they pick out one.</summary>
    private ValidInput? Chosen(object? parameter)
    {
        string? given = parameter is null || _settled.Count != 1 ? null : Values.Text(parameter);
        ValidInput? found = null;

        foreach (ValidInput move in Model.Moves)
        {
            if (move.Input != Input || !Matches(move, given))
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = move;
        }

        return found;
    }

    private bool Matches(ValidInput move, string? given)
    {
        foreach (string parameter in _settled)
        {
            string? wanted = given ?? (Get<object?>(parameter) is object held ? Values.Text(held) : null);

            if (wanted is not null && !string.Equals(move.Arguments[parameter], wanted, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void Execute(object? parameter)
    {
        ValidInput? move = Chosen(parameter);
        if (move is null)
        {
            return;
        }

        foreach (string property in _errors.Keys.ToList())
        {
            Forget(property);
        }

        Refusal = null;
        RefusedWith = [];
        Changed(nameof(Refusal));
        Changed(nameof(RefusedWith));

        string document = move.IsComplete
            ? move.ToInputDocument(Model.RuleSet)
            : move.ToInputDocument(
                Model.RuleSet,
                move.Open.ToDictionary(
                    open => open.Name,
                    open => Values.ToJson(Get<object?>(open.Name)),
                    StringComparer.Ordinal));

        switch (Model.Apply(document))
        {
            case Applied.Landed:
                // What was typed was for the position that is gone.
                _values.Clear();
                Changed(string.Empty);
                break;

            case Applied.Refused refused:
                Refuse(refused);
                break;
        }
    }

    /// <summary>Puts each clause of a refusal against the property its parameter is held in.</summary>
    /// <remarks>Each clause already knows which parameter it was about, so nothing here works it out from the code.</remarks>
    private void Refuse(Applied.Refused refused)
    {
        RefusedWith = [.. refused.Rejections.Select(rejection => rejection.Code)];
        Changed(nameof(RefusedWith));

        if (refused.Rejections.IsEmpty)
        {
            Refusal = refused.Message;
            Changed(nameof(Refusal));
            return;
        }

        List<string> general = [];

        foreach (InputRejection rejection in refused.Rejections)
        {
            if (rejection.Parameter is string parameter && _properties.TryGetValue(parameter, out string? property))
            {
                if (!_errors.TryGetValue(property, out List<string>? errors))
                {
                    _errors[property] = errors = [];
                }

                errors.Add(Labels.Say(Model.Labels, Input, rejection.Code));
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(property));
            }
            else
            {
                general.Add(Labels.Say(Model.Labels, Input, rejection.Code));
            }
        }

        // A schema's refusal the rule set gave no code: the runtime's sentence, since there is nothing to label.
        general.AddRange(refused.Unexplained);

        Refusal = general.Count == 0 ? null : string.Join(" ", general);
        Changed(nameof(Refusal));
        Changed(nameof(HasErrors));
    }

    private void Forget(string property)
    {
        if (_errors.Remove(property))
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(property));
        }
    }

    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
