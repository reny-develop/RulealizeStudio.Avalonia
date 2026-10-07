// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize;
using Rulealize.Abstraction;

namespace RulealizeStudio.Binding;

/// <summary>What became of applying an input.</summary>
/// <remarks>
/// Three answers rather than a boolean, because a screen does three different things with
/// them: move on, ask somebody which outcome happened, or say what was wrong and leave the
/// position alone.
/// </remarks>
public abstract record Applied
{
    private Applied()
    {
    }

    /// <summary>The position moved, and everything about it was settled by the input.</summary>
    public sealed record Landed : Applied;

    /// <summary>The input resolves something nobody chose, so where it lands is not settled yet.</summary>
    /// <param name="Outcomes">What could have happened, most likely first.</param>
    /// <param name="InputDocument">The input, kept so that the chosen outcome can be replayed against it.</param>
    public sealed record NeedsOutcome(OutcomeSet Outcomes, string InputDocument) : Applied;

    /// <summary>The rules did not allow it, and nothing was written.</summary>
    /// <param name="Rejections">
    /// What refused it where the rule set said so in codes — a <c>validate</c> clause, or a value
    /// an open parameter's schema does not admit, under that parameter's <c>invalid</c>. Empty
    /// where the refusal was of another kind, and then <paramref name="Message"/> is all there
    /// is.
    /// </param>
    /// <param name="Message">The refusal in words, for a refusal that has no code.</param>
    public sealed record Refused(ImmutableArray<InputRejection> Rejections, string Message) : Applied
    {
        /// <summary>Gets what a schema refused beside <see cref="Rejections"/>, where the rule set gave it no code, as the runtime words it.</summary>
        public ImmutableArray<string> Unexplained { get; init; } = [];
    }
}

/// <summary>What a rule set makes of a position.</summary>
/// <remarks>
/// Two answers rather than one string, because an answer and the reason there is not one both
/// arrive as text and a screen reading either would have no way to tell which it had. A
/// projection is worked out when it is asked for, so a position the rules fault over is an
/// ordinary place to arrive at and not a reason to stop drawing the position.
/// </remarks>
public abstract record Said
{
    private Said()
    {
    }

    /// <summary>The rule set worked one out.</summary>
    /// <param name="Json">The answer, as the rule set assembled it.</param>
    public sealed record Answer(string Json) : Said;

    /// <summary>The rules fault over this position, so there is nothing to show.</summary>
    /// <param name="Message">What went wrong.</param>
    public sealed record Trouble(string Message) : Said;
}

/// <summary>One rule set, one position, and the positions it came from.</summary>
/// <remarks>
/// <para>
/// Everything this screen knows about a rule set, and nothing about a screen. That division
/// is the point: what is legal, what a move is waiting for and why one was refused are all
/// the runtime's answers, and a host that mixed them with controls would have to be read to
/// find out which was which.
/// </para>
/// <para>
/// History is free and therefore kept. A position is a document, so going back is holding on
/// to the one before rather than undoing anything.
/// </para>
/// </remarks>
public sealed class Session
{
    /// <summary>The most outcomes to ask for. A truncated set no longer sums to one, and the
    /// screen says so rather than hiding it.</summary>
    private const int OutcomeLimit = 256;

    private readonly RuleContext _rules;
    private readonly List<string> _history = [];
    private int _at;

    private Session(RuleRuntime runtime, RuleContext rules, string source)
    {
        _rules = rules;
        Source = source;
        Skipped = runtime.Skipped;
        Plugins = runtime.Plugins.Length;
        _history.Add(rules.InitialState);
    }

    /// <summary>Gets where the rule set was read from.</summary>
    public string Source { get; }

    /// <summary>Gets the rule set's identity, as <c>id@version</c>.</summary>
    public string RuleSet => _rules.RuleSet;

    /// <summary>Gets how many vocabularies the folder yielded.</summary>
    public int Plugins { get; }

    /// <summary>Gets what the plugin folder held and could not be used.</summary>
    /// <remarks>
    /// Surfaced rather than swallowed. An entry here explains a <c>requires</c> that is about
    /// to fail over a file sitting in the folder, and there is no other symptom of it.
    /// </remarks>
    public ImmutableArray<SkippedPlugin> Skipped { get; }

    /// <summary>Gets the position.</summary>
    public string State => _history[_at];

    /// <summary>Gets whether there is a position before this one.</summary>
    public bool CanGoBack => _at > 0;

    /// <summary>Gets whether a position was stepped back from.</summary>
    public bool CanGoForward => _at < _history.Count - 1;

    /// <summary>Opens a rule set against a folder of vocabularies.</summary>
    /// <param name="ruleSetPath">The document.</param>
    /// <param name="pluginFolder">The folder to sweep.</param>
    /// <returns>The session.</returns>
    /// <exception cref="ArgumentException"><paramref name="ruleSetPath"/> is null or empty.</exception>
    /// <exception cref="PluginLoadException">The folder is not one that can be swept.</exception>
    /// <exception cref="RuleSetBuildException">The document is not a rule set this folder can compile.</exception>
    /// <exception cref="IOException">The document could not be read.</exception>
    public static Session Open(string ruleSetPath, string pluginFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(ruleSetPath);
        ArgumentException.ThrowIfNullOrEmpty(pluginFolder);

        return Read(File.ReadAllText(ruleSetPath), Path.GetFileName(ruleSetPath), pluginFolder);
    }

    /// <summary>Opens a rule set held as text against a folder of vocabularies.</summary>
    /// <param name="document">The rule set document itself.</param>
    /// <param name="source">What to call where it came from.</param>
    /// <param name="pluginFolder">The folder to sweep.</param>
    /// <returns>The session.</returns>
    /// <exception cref="ArgumentException">An argument is null or empty.</exception>
    /// <exception cref="PluginLoadException">The folder is not one that can be swept.</exception>
    /// <exception cref="RuleSetBuildException">The document is not a rule set this folder can compile.</exception>
    /// <remarks>
    /// For a document that travels inside an assembly: a model generated from a rule set carries
    /// the text it was generated from, so the types and the rules it opens cannot come from two
    /// different documents.
    /// </remarks>
    public static Session Read(string document, string source, string pluginFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(pluginFolder);

        RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom(pluginFolder);
        RuleContext rules = runtime.CreateContext(document);

        return new Session(runtime, rules, source);
    }

    /// <summary>Asks what is legal from the position.</summary>
    /// <param name="limit">The most candidates whose guard may be evaluated.</param>
    /// <returns>The moves, and whether the search saw all of them.</returns>
    public ValidInputSet Moves(int limit = 4096) => _rules.GetValidInputs(State, limit);

    /// <summary>Gets the names of what the rule set will say about a position, in the order written.</summary>
    /// <remarks>
    /// Empty where the document declares none, which is an ordinary shape rather than an
    /// omission: the three questions every rule set answers are asked through the members
    /// above, and a document only writes projections where it has something further to say.
    /// </remarks>
    public ImmutableArray<string> Projections => _rules.Projections;

    /// <summary>Asks the rule set what it says about the position.</summary>
    /// <param name="projection">The name, out of <see cref="Projections"/>.</param>
    /// <returns>The answer, or why there is not one.</returns>
    /// <exception cref="ArgumentException"><paramref name="projection"/> is null or empty, or is
    /// not one this rule set declares.</exception>
    /// <remarks>
    /// A function of the position and nothing else, so what comes back may be held against the
    /// position it was asked about — and is, by a screen that redraws from it.
    /// </remarks>
    public Said Project(string projection)
    {
        ArgumentException.ThrowIfNullOrEmpty(projection);

        try
        {
            return new Said.Answer(_rules.Project(projection, State));
        }
        catch (RuleEvaluationException wrong)
        {
            return new Said.Trouble(wrong.Message);
        }
    }

    /// <summary>Asks whether the position is final.</summary>
    /// <returns>Whether it is, and what the rule set calls the ending.</returns>
    public TerminalStatus Terminal() => _rules.GetTerminalStatus(State);

    /// <summary>Applies an input to the position.</summary>
    /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
    /// <returns>What became of it.</returns>
    /// <remarks>
    /// Nothing is written unless it lands. Evaluation is pure until a transition commits, so
    /// a refusal costs the position nothing and a screen may apply an input to find out
    /// whether it is acceptable.
    /// </remarks>
    public Applied Apply(string inputDocument)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputDocument);

        try
        {
            Arrive(_rules.ApplyToState(inputDocument, State).State);
            return new Applied.Landed();
        }
        catch (InvalidOperationException)
        {
            // The input resolves something nobody chose. Which outcome happened is not this
            // program's to invent, and not the runtime's either.
            return new Applied.NeedsOutcome(_rules.GetOutcomes(inputDocument, State, OutcomeLimit), inputDocument);
        }
        catch (InputRejectedException rejected)
        {
            return new Applied.Refused(rejected.Rejections, rejected.Message) { Unexplained = rejected.Unexplained };
        }
        catch (IllegalInputException refused)
        {
            return new Applied.Refused([], refused.Message);
        }
    }

    /// <summary>Takes one of the outcomes an input opened up.</summary>
    /// <param name="chosen">The outcome, out of the set that was handed back.</param>
    /// <exception cref="ArgumentNullException"><paramref name="chosen"/> is null.</exception>
    public void Choose(Outcome chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        Arrive(chosen.Result.State);
    }

    /// <summary>Goes back to the position before this one.</summary>
    public void Back()
    {
        if (CanGoBack)
        {
            _at--;
        }
    }

    /// <summary>Goes forward to a position stepped back from.</summary>
    public void Forward()
    {
        if (CanGoForward)
        {
            _at++;
        }
    }

    /// <summary>Records a position arrived at, and drops anything stepped back from.</summary>
    /// <remarks>
    /// Moving from a position that was stepped back to makes what came after it something
    /// that did not happen, so it goes. The alternative is a tree, and a tree is a different
    /// program.
    /// </remarks>
    private void Arrive(string state)
    {
        _history.RemoveRange(_at + 1, _history.Count - _at - 1);
        _history.Add(state);
        _at = _history.Count - 1;
    }
}
