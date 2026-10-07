// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Rulealize;

namespace RulealizeStudio.Binding;

/// <summary>One rule set and its position, as something a screen binds to.</summary>
/// <remarks>
/// <para>
/// The base of every model generated from a rule set. What is here is the part every document
/// has — the position moving, the way back, whether it is over, and the outcomes an input left
/// for somebody to pick. What differs between documents — the fields, the inputs, what the rule
/// set says about a position — is generated beside it, so a screen binds each by the name the
/// document gave it and a renamed field breaks the build rather than the screen.
/// </para>
/// <para>
/// Nothing here decides anything. Whether an input may be applied is <c>GetValidInputs</c>, why
/// one was refused is <c>InputRejectedException</c>, and what the position amounts to is
/// <c>Project</c>; this class tells a screen when those answers changed, and that is all it adds.
/// </para>
/// </remarks>
public abstract class RuleModel : INotifyPropertyChanged
{
    private readonly List<RuleInput> _inputs = [];
    private readonly RuleCommand _back;
    private readonly RuleCommand _forward;
    private ValidInputSet? _moves;
    private JsonObject? _position;
    private Applied.NeedsOutcome? _pending;
    private TerminalStatus? _terminal;
    private readonly List<string> _troubles = [];

    /// <summary>Initializes a new instance of the <see cref="RuleModel"/> class.</summary>
    /// <param name="session">The rule set and the position it starts at.</param>
    /// <remarks>
    /// A derived class registers its inputs and then calls <see cref="Moved"/>, which is what
    /// reads the first position; the base cannot, because what there is to read is the derived
    /// class's.
    /// </remarks>
    protected RuleModel(Session session)
        : this(session, [])
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RuleModel"/> class, speaking in one of a set of label documents.</summary>
    /// <param name="session">The rule set and the position it starts at.</param>
    /// <param name="labels">Every label document beside the rule set; the one for the person's language is spoken in.</param>
    protected RuleModel(Session session, IEnumerable<Labels> labels)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(labels);
        Session = session;
        Labels = Labels.Choose(labels, System.Globalization.CultureInfo.CurrentUICulture);

        _back = new RuleCommand(_ => CanGoBack, _ => Step(back: true));
        _forward = new RuleCommand(_ => CanGoForward, _ => Step(back: false));
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the session underneath, for a caller that wants the answers themselves.</summary>
    public Session Session { get; }

    /// <summary>Gets the label document a refusal is said in, or null where the application has none.</summary>
    public Labels? Labels { get; }

    /// <summary>Gets every input the rule set declares, in document order.</summary>
    public IReadOnlyList<RuleInput> Inputs => _inputs;

    /// <summary>Gets the rule set's identity, as <c>id@version</c>.</summary>
    public string RuleSet => Session.RuleSet;

    /// <summary>Gets the position, as the state document.</summary>
    public string Document => Session.State;

    /// <summary>Gets whether the position is final.</summary>
    public bool IsTerminal => Terminal.IsTerminal;

    /// <summary>Gets what the rule set calls the ending, where the position is final.</summary>
    public string? Ending => Terminal.IsTerminal ? Terminal.Result : null;

    /// <summary>Gets whether there is a position before this one to go back to.</summary>
    public bool CanGoBack => _pending is null && Session.CanGoBack;

    /// <summary>Gets whether there is a position that was stepped back from.</summary>
    public bool CanGoForward => _pending is null && Session.CanGoForward;

    /// <summary>Gets the command that goes back to the position before this one.</summary>
    public ICommand Back => _back;

    /// <summary>Gets the command that goes forward to a position stepped back from.</summary>
    public ICommand Forward => _forward;

    /// <summary>Gets whether an input resolved something nobody chose, and is waiting to be told which.</summary>
    /// <remarks>Every input is disabled until then: the position is not settled, so nothing is legal from it yet.</remarks>
    public bool IsWaitingForOutcome => _pending is not null;

    /// <summary>Gets what could have happened, most likely first, while <see cref="IsWaitingForOutcome"/>.</summary>
    public IReadOnlyList<OutcomeChoice> Outcomes { get; private set; } = [];

    /// <summary>Gets why the rule set could not say something it declares about the position, where it could not.</summary>
    /// <remarks>
    /// A projection is worked out when it is asked for, so a position the rules fault over is an
    /// ordinary place to arrive at. The answer is then absent and this says why.
    /// </remarks>
    public string? Trouble => _troubles.Count == 0 ? null : string.Join(Environment.NewLine, _troubles);

    /// <summary>Gets what is legal from the position, asked once per position.</summary>
    /// <remarks>Nothing while <see cref="IsWaitingForOutcome"/>: the position is not settled yet.</remarks>
    public ValidInputSet Moves => _moves ??= _pending is null ? Session.Moves() : new ValidInputSet([], 0, false);

    private TerminalStatus Terminal => _terminal ??= Session.Terminal();

    /// <summary>Reads one state field out of the position.</summary>
    /// <typeparam name="T">What the field's schema says it holds.</typeparam>
    /// <param name="field">The field, by the name the rule set gave it.</param>
    /// <returns>The value.</returns>
    protected T Field<T>(string field)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);

        // A state document keeps the fields under `data`, beside what identifies it.
        _position ??= JsonNode.Parse(Session.State)?["data"] as JsonObject ?? [];
        return Values.As<T>(_position[field]);
    }

    /// <summary>Asks the rule set what it says about the position.</summary>
    /// <typeparam name="T">The shape the answer is read into.</typeparam>
    /// <param name="projection">The name, as the rule set declares it.</param>
    /// <param name="read">How to read the answer into that shape.</param>
    /// <returns>The answer, or the default where the rules fault over this position.</returns>
    protected T Answer<T>(string projection, Func<JsonNode?, T> read)
    {
        ArgumentException.ThrowIfNullOrEmpty(projection);
        ArgumentNullException.ThrowIfNull(read);

        switch (Session.Project(projection))
        {
            case Said.Answer answer:
                return read(JsonNode.Parse(answer.Json));

            case Said.Trouble trouble:
                _troubles.Add($"{projection}: {trouble.Message}");
                return default!;

            default:
                return default!;
        }
    }

    /// <summary>Reads what the derived class exposes out of the position.</summary>
    /// <remarks>Called by <see cref="Moved"/>, before anybody is told the position changed.</remarks>
    protected abstract void Read();

    /// <summary>Takes an input the derived class exposes into account.</summary>
    /// <typeparam name="T">The input's own type.</typeparam>
    /// <param name="input">The input.</param>
    /// <returns>The same input.</returns>
    protected T Register<T>(T input)
        where T : RuleInput
    {
        ArgumentNullException.ThrowIfNull(input);
        _inputs.Add(input);
        return input;
    }

    /// <summary>Reads the position again and tells everybody bound to it.</summary>
    /// <remarks>
    /// One notification for everything, because a move can change any of it: what is legal,
    /// every field, every answer. Working out which parts moved would be a second account of
    /// the rules, and it would be wrong exactly where the rules are interesting.
    /// </remarks>
    protected void Moved()
    {
        _moves = null;
        _position = null;
        _terminal = null;
        _troubles.Clear();

        Outcomes = _pending is null
            ? []
            : [.. _pending.Outcomes.Select(outcome => new OutcomeChoice(this, outcome))];

        Read();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        _back.Changed();
        _forward.Changed();

        foreach (RuleInput input in _inputs)
        {
            input.Moved();
        }
    }

    /// <summary>Applies an input document to the position.</summary>
    internal Applied Apply(string document)
    {
        Applied applied = Session.Apply(document);

        switch (applied)
        {
            case Applied.Landed:
                Moved();
                break;

            case Applied.NeedsOutcome needs:
                _pending = needs;
                Moved();
                break;
        }

        return applied;
    }

    /// <summary>Takes one of the outcomes an input left open.</summary>
    internal void Choose(Outcome outcome)
    {
        Session.Choose(outcome);
        _pending = null;
        Moved();
    }

    private void Step(bool back)
    {
        if (back)
        {
            Session.Back();
        }
        else
        {
            Session.Forward();
        }

        Moved();
    }
}
