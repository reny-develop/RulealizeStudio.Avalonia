// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Rulealize;
using Rulealize.Abstraction.Value;
using RulealizeStudio.Binding;

namespace RulealizeStudio.Hosting;

/// <summary>Replays a test design Ruledger derived from a rule set against the screen made of it.</summary>
/// <remarks>
/// <para>
/// A design is a walk over named states, and for each it records what is legal there, whether it
/// is final, and where each legal input leads. A replay stands the screen in each of those states
/// and asks the same three things of it — with every move taken by pressing the control that
/// stands for it, found by <see cref="Reach"/>, so the design is held against what somebody could
/// actually do rather than against the model underneath. Each value the design records as refused
/// is pressed too, and is expected back with the same codes, said on the window in the sentence a
/// label document gives each.
/// </para>
/// <para>
/// Nothing here is a case anybody wrote. If the screen does what the rule set does and the rule
/// set does what the design says, there is nothing to report; a rule set changed since the design
/// was committed, or a screen that fell behind the one it was written for, comes back as a
/// <see cref="Divergence"/> at the state where it showed.
/// </para>
/// <para>
/// An application of several windows is walked on all of them at once (<see cref="RuleWindows"/>): a
/// control is looked for on every window shown that is bound to the design's rule set, so a window
/// the rules show is pressed on as soon as it is, and one they hide is not. A window's × is pressed
/// as <see cref="RuleWindow.Close"/>, the move it makes, since a window drawn with no window server
/// has no × to point at.
/// </para>
/// </remarks>
public static class Replay
{
    /// <summary>Walks a test design on a screen and reports where the two disagree.</summary>
    /// <param name="screen">The window, bound to <paramref name="model"/> and at the position the rule set starts in.</param>
    /// <param name="model">The model the screen is bound to.</param>
    /// <param name="design">A <c>ruledger/test-design/v1</c> document.</param>
    /// <param name="press">How to press a control, the way somebody would; a headless pointer, in a test.</param>
    /// <returns>Every divergence, in the order the walk met them; empty when the screen does what the design says.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="FormatException"><paramref name="design"/> is not a <c>ruledger/test-design/v1</c> document.</exception>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    /// <remarks>
    /// <para>
    /// The model is stepped back to the state it started in afterwards: every move taken is undone
    /// with <see cref="RuleModel.Back"/>, which is the replay's bookkeeping and not something it tests.
    /// </para>
    /// <para>
    /// A refusal is expected to be said on the screen in the model's own <see cref="RuleModel.Labels"/>.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Divergence> Run(Visual screen, RuleModel model, string design, Action<Control> press)
    {
        ArgumentNullException.ThrowIfNull(model);

        return Run(screen, model, design, model.Labels, press);
    }

    /// <summary>Walks a test design on a screen, and holds what it says at a refusal to a label document.</summary>
    /// <param name="screen">The window, bound to <paramref name="model"/> and at the position the rule set starts in.</param>
    /// <param name="model">The model the screen is bound to.</param>
    /// <param name="design">A <c>ruledger/test-design/v2</c> or <c>v1</c> document.</param>
    /// <param name="labels">
    /// The label document the screen is expected to speak in, read from beside the rule set rather
    /// than out of the application, so an application built before a sentence changed is caught;
    /// null where there is none, and a refusal is then expected to be said by its code.
    /// </param>
    /// <param name="press">How to press a control, the way somebody would; a headless pointer, in a test.</param>
    /// <returns>Every divergence, in the order the walk met them; empty when the screen does what the design says.</returns>
    /// <exception cref="FormatException"><paramref name="design"/> is not a test design.</exception>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    /// <remarks>
    /// Each value the design has refused is pressed through the screen, and the refusal is expected
    /// where the person would read it: every code's sentence drawn as text somewhere on the window.
    /// A screen that says another, or nothing, is a divergence, as a refusal with another code is.
    /// </remarks>
    public static IReadOnlyList<Divergence> Run(Visual screen, RuleModel model, string design, Labels? labels, Action<Control> press)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(press);

        Action<Control> pressed = Pressed(press);
        return new Walk(Looked.At(screen), model, TestDesign.Read(design), labels, (_, control) => pressed(control), (_, control) => pressed(control)).Run();
    }

    /// <summary>Walks a test design on every window of an application bound to its rule set, and holds what it says at a refusal to a label document.</summary>
    /// <param name="screens">The application's windows, shown as they are where the rule set starts.</param>
    /// <param name="model">The model of the design's rule set, one of <paramref name="screens"/>' models.</param>
    /// <param name="design">A <c>ruledger/test-design/v2</c> or <c>v1</c> document.</param>
    /// <param name="labels">The label document the windows are expected to speak in, as <see cref="Run(Visual, RuleModel, string, Labels?, Action{Control})"/> takes it.</param>
    /// <param name="press">How to press a control, the way somebody would; never given a window, whose × is pressed here.</param>
    /// <returns>Every divergence, in the order the walk met them; empty when the windows do what the design says.</returns>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="labels"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="design"/> is not a test design.</exception>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    public static IReadOnlyList<Divergence> Run(RuleWindows screens, RuleModel model, string design, Labels? labels, Action<Control> press)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(press);

        Action<Control> pressed = Pressed(press);
        return new Walk(Looked.On(screens, model), model, TestDesign.Read(design), labels, (_, control) => pressed(control), (_, control) => pressed(control)).Run();
    }

    /// <summary>Every state a test design names, each with the moves that first reached it and what the design records there.</summary>
    /// <param name="design">A <c>ruledger/test-design/v2</c> or <c>v1</c> document.</param>
    /// <returns>One per state, in the design's order, which starts where the rule set does.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="design"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="design"/> is not a test design.</exception>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    public static IReadOnlyList<Situation> Situations(string design)
    {
        ArgumentNullException.ThrowIfNull(design);

        TestDesign read = TestDesign.Read(design);
        return [.. read.States.Select(state => new Situation(state.Name, [.. read.Hops(state).Select(hop => hop.Step)])
        {
            IsEnding = state.Terminal,
            Result = state.Result,
            Moves = [.. state.Moves.SelectMany(move => move.Lands is null
                ? [new SituationMove(move.ToString(), move.Input) { To = move.To, Followed = move.Followed, Admits = Admitted(read, move) }]
                : move.Lands.Select(landing => new SituationMove($"{move} drawing {string.Join(", ", landing.Draws)}", move.Input) { To = landing.To, Followed = true }))],
            Refused = [.. state.Refused.Select(refusal => new SituationRefusal(refusal.ToString(), refusal.Input, refusal.Codes))],
        })];
    }

    /// <summary>Stands a screen in a state a test design names, by pressing what first reached it there.</summary>
    /// <param name="screen">The window, bound to <paramref name="model"/> and at the position the rule set starts in.</param>
    /// <param name="model">The model the screen is bound to.</param>
    /// <param name="design">A <c>ruledger/test-design/v2</c> or <c>v1</c> document.</param>
    /// <param name="state">The state, by the name the design gives it.</param>
    /// <param name="press">
    /// How to press a control, given the step it is pressed for as the route writes it: called once
    /// the move's values are entered on the screen and before anything is pressed, so the window
    /// it sees is the one the press is made on.
    /// </param>
    /// <returns>Where the screen did not go where the design says, which stops it there; empty when it stands in <paramref name="state"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The design names no state <paramref name="state"/>.</exception>
    /// <exception cref="FormatException"><paramref name="design"/> is not a test design.</exception>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    /// <remarks>
    /// Every move is taken as <see cref="Run(Visual, RuleModel, string, Labels?, Action{Control})"/> takes it — its values entered into whatever on the
    /// screen shows them, the control <see cref="Reach"/> finds for it pressed, and what was drawn
    /// picked by the control that stands for it — and the screen is left where the last one took it.
    /// </remarks>
    public static IReadOnlyList<Divergence> Stand(Visual screen, RuleModel model, string design, string state, Action<string, Control> press)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(press);

        TestDesign read = TestDesign.Read(design);
        if (!read.Named.TryGetValue(state, out DesignState? target))
        {
            throw new ArgumentException($"The design names no state '{state}'.", nameof(state));
        }

        Action<string, Control> pressed = Pressed(press);
        Walk walk = new(Looked.At(screen), model, read, model.Labels, pressed, pressed);
        walk.Stand(target);
        return walk.Found;
    }

    /// <summary>
    /// Stands a screen in a state a test design names, by pressing what first reached it there, and
    /// tells what it stood in: the control each legal move stands for, and each refusal as the window drew it.
    /// </summary>
    /// <param name="screen">The window, bound to <paramref name="model"/> and at the position the rule set starts in.</param>
    /// <param name="model">The model the screen is bound to.</param>
    /// <param name="design">A <c>ruledger/test-design/v2</c> or <c>v1</c> document.</param>
    /// <param name="state">The state, by the name the design gives it.</param>
    /// <param name="labels">The label document a refusal is expected to be said in, as <see cref="Run(Visual, RuleModel, string, Labels?, Action{Control})"/> takes it.</param>
    /// <param name="press">How to press a control, the way somebody would.</param>
    /// <param name="witness">What is told along the way.</param>
    /// <returns>Where the screen did not do what the design says: on the way, which stops it there, or at a refusal.</returns>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="labels"/> is null.</exception>
    /// <exception cref="ArgumentException">The design names no state <paramref name="state"/>.</exception>
    /// <exception cref="FormatException"><paramref name="design"/> is not a test design.</exception>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    /// <remarks>
    /// The refusals are tried last, each entered, pressed and held to the same account as
    /// <see cref="Run(Visual, RuleModel, string, Labels?, Action{Control})"/> holds them; a refusal
    /// moves nothing, so the window is still in <paramref name="state"/> afterwards.
    /// </remarks>
    public static IReadOnlyList<Divergence> Stand(Visual screen, RuleModel model, string design, string state, Labels? labels, Action<Control> press, Witness witness)
    {
        ArgumentNullException.ThrowIfNull(screen);

        return Stand(Looked.At(screen), model, design, state, labels, press, witness);
    }

    /// <summary>
    /// Stands every window of an application bound to a design's rule set in a state the design names,
    /// by pressing what first reached it there, and tells what it stood in.
    /// </summary>
    /// <param name="screens">The application's windows, shown as they are where the rule set starts.</param>
    /// <param name="model">The model of the design's rule set, one of <paramref name="screens"/>' models.</param>
    /// <param name="design">A <c>ruledger/test-design/v2</c> or <c>v1</c> document.</param>
    /// <param name="state">The state, by the name the design gives it.</param>
    /// <param name="labels">The label document a refusal is expected to be said in.</param>
    /// <param name="press">How to press a control, the way somebody would; never given a window, whose × is pressed here.</param>
    /// <param name="witness">What is told along the way: a window among the controls, where its × is pressed.</param>
    /// <returns>Where the windows did not do what the design says: on the way, which stops it there, or at a refusal.</returns>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="labels"/> is null.</exception>
    /// <exception cref="ArgumentException">The design names no state <paramref name="state"/>.</exception>
    /// <exception cref="FormatException"><paramref name="design"/> is not a test design.</exception>
    /// <exception cref="JsonException"><paramref name="design"/> is not JSON.</exception>
    public static IReadOnlyList<Divergence> Stand(RuleWindows screens, RuleModel model, string design, string state, Labels? labels, Action<Control> press, Witness witness)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(model);

        return Stand(Looked.On(screens, model), model, design, state, labels, press, witness);
    }

    private static IReadOnlyList<Divergence> Stand(Looked looked, RuleModel model, string design, string state, Labels? labels, Action<Control> press, Witness witness)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(press);
        ArgumentNullException.ThrowIfNull(witness);

        press = Pressed(press);
        TestDesign read = TestDesign.Read(design);
        if (!read.Named.TryGetValue(state, out DesignState? target))
        {
            throw new ArgumentException($"The design names no state '{state}'.", nameof(state));
        }

        Walk walk = new(
            looked,
            model,
            read,
            labels,
            (step, control) =>
            {
                witness.Pressing(step, control);
                press(control);
            },
            (step, control) =>
            {
                press(control);
                witness.Refused(step, control);
            });

        if (walk.Stand(target))
        {
            witness.Arrived();
            walk.Look(target, witness.Offered);
        }

        return walk.Found;
    }

    /// <summary>A press that presses a window's × as the move it makes, and anything else as it was given.</summary>
    private static Action<Control> Pressed(Action<Control> press) => control =>
    {
        if (control is Window window)
        {
            RuleWindow.Close(window);
            Dispatcher.UIThread.RunJobs();
        }
        else
        {
            press(control);
        }
    };

    /// <summary>A press given the step, that presses a window's × as the move it makes.</summary>
    private static Action<string, Control> Pressed(Action<string, Control> press) => (step, control) =>
    {
        if (control is Window window)
        {
            RuleWindow.Close(window);
            Dispatcher.UIThread.RunJobs();
        }
        else
        {
            press(step, control);
        }
    };

    /// <summary>What each parameter the move's input leaves open admitted when the design was derived, as text.</summary>
    /// <remarks>
    /// Whether the walk could name every value — a party of one to six — or waits for one — a name —
    /// the bound is the same answer, and it is where a widened bound shows.
    /// </remarks>
    private static Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>> Admitted(TestDesign design, DesignMove move)
    {
        Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>> admitted = new(StringComparer.Ordinal);

        foreach (((string input, string parameter), JsonNode? schema) in design.Admits)
        {
            if (input == move.Input)
            {
                admitted[parameter] = schema is JsonObject bounds
                    ? [.. bounds.Select(bound => KeyValuePair.Create(bound.Key, Bound(bound.Value)))]
                    : [];
            }
        }

        return admitted;
    }

    /// <summary>A bound as words: text as it is, a list as its items, anything else as the runtime wrote it.</summary>
    private static string Bound(JsonNode? value) => value switch
    {
        JsonArray items => string.Join(", ", items.Select(Bound)),
        _ => TestDesign.Text(value) ?? "null",
    };

    /// <summary>How far a value entered through the screen got.</summary>
    private enum Entered
    {
        /// <summary>A control took it and passed it back to the input.</summary>
        Returned,

        /// <summary>A control shows it and none passes it back.</summary>
        NotReturned,

        /// <summary>Typed into the control that shows it, it arrived as something else.</summary>
        Mistyped,

        /// <summary>Nothing on the screen shows it.</summary>
        Nowhere,
    }

    /// <summary>What a walk looks at: the windows shown now, and all of them, shown or not.</summary>
    /// <param name="Shown">The windows a control is pressed on and a sentence read from.</param>
    /// <param name="All">The windows a move is said from, wherever it is listed.</param>
    private sealed record Looked(Func<IReadOnlyList<Visual>> Shown, Func<IReadOnlyList<Visual>> All)
    {
        /// <summary>One window, or any part of it, shown or not.</summary>
        public static Looked At(Visual screen) => new(() => [screen], () => [screen]);

        /// <summary>Every window of an application bound to a model: those shown now, as the rules show them.</summary>
        public static Looked On(RuleWindows screens, RuleModel model) =>
            new(() => [.. screens.Of(model).Where(window => window.IsVisible)], () => [.. screens.Of(model)]);
    }

    /// <summary>One walk over a design, on the windows it looks at.</summary>
    /// <param name="screen">The windows.</param>
    /// <param name="model">The model the window is bound to.</param>
    /// <param name="design">The design.</param>
    /// <param name="labels">The label document a refusal is expected to be said in, or null for its code.</param>
    /// <param name="press">How a move is pressed, given the step it is for.</param>
    /// <param name="refuse">How a value the design has refused is pressed, given what was tried.</param>
    private sealed class Walk(Looked screen, RuleModel model, TestDesign design, Labels? labels, Action<string, Control> press, Action<string, Control> refuse)
    {
        private readonly List<Divergence> _found = [];
        private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
        private readonly HashSet<(string Input, string Parameter)> _admitted = [];

        public IReadOnlyList<Divergence> Run()
        {
            DesignState start = design.States[0];

            if (design.RuleSet != model.RuleSet)
            {
                Say(start, $"the design is of {design.RuleSet}, and the screen is bound to {model.RuleSet}");
                return _found;
            }

            if (Arrived(start, "the rule set starts"))
            {
                Visit(start);
            }

            return _found;
        }

        public IReadOnlyList<Divergence> Found => _found;

        /// <summary>
        /// Takes the moves that first reached a state, one after another, stops at the first that
        /// does not do what the design says, and says whether it got there.
        /// </summary>
        public bool Stand(DesignState target)
        {
            DesignState start = design.States[0];

            if (design.RuleSet != model.RuleSet)
            {
                Say(start, $"the design is of {design.RuleSet}, and the screen is bound to {model.RuleSet}");
                return false;
            }

            if (!Arrived(start, "the rule set starts"))
            {
                return false;
            }

            foreach ((DesignState from, DesignMove move, DesignLanding? landing, string step) in design.Hops(target))
            {
                if (model.Moves.FirstOrDefault(each => Same(each, move)) is not ValidInput legal)
                {
                    Say(from, $"{move} is legal in the design, and the rules no longer offer it");
                    return false;
                }

                if (!Press(from, move, legal))
                {
                    return false;
                }

                if (model.IsWaitingForOutcome != landing is not null)
                {
                    Say(from, landing is null
                        ? $"{move} waits for something to be drawn, and the design has it settle"
                        : $"{move} settles, and the design has it draw");
                    return false;
                }

                if ((landing is not null && !Draw(from, step, landing))
                    || !Arrived(design.Named[(landing?.To ?? move.To)!], $"{from.Name} + {step}"))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Tells the control each move legal in a state stands for, then tries each value the design has refused there.</summary>
        public void Look(DesignState state, Action<string, Control?> offered)
        {
            ValidInput[] legal = [.. model.Moves];

            foreach (DesignMove move in state.Moves)
            {
                ValidInput? found = legal.FirstOrDefault(each => Same(each, move));
                offered(move.ToString(), found is null ? null : Reach.For(screen.Shown(), model, found).FirstOrDefault());
            }

            foreach (DesignRefusal refusal in state.Refused)
            {
                Refused(state, refusal, legal);
            }
        }

        private void Visit(DesignState state)
        {
            _visited.Add(state.Name);

            if (state.Terminal != model.IsTerminal)
            {
                Say(state, state.Terminal ? $"the design has it final ({state.Result ?? "no result"}), and the rules do not" : "the rules call it final, and the design does not");
            }
            else if (state.Terminal && state.Result != model.Ending)
            {
                Say(state, $"it ends {model.Ending ?? "with no result"}, and the design has it end {state.Result ?? "with no result"}");
            }

            ValidInput[] offered = [.. model.Moves];

            foreach (DesignMove move in state.Moves.Where(move => !offered.Any(each => Same(each, move))))
            {
                Say(state, $"{move} is legal in the design, and the rules no longer offer it");
            }

            if (!state.Truncated)
            {
                foreach (ValidInput each in offered.Where(each => !state.Moves.Any(move => Same(each, move))))
                {
                    Say(state, $"the rules offer {Written(each)}, which the design does not have");
                }

            }

            foreach (ValidInput each in offered.Where(each => !each.IsComplete))
            {
                Admits(state, each);
            }

            foreach (DesignRefusal refusal in state.Refused)
            {
                Refused(state, refusal, offered);
            }

            foreach (ValidInput each in Reach.Unreachable(screen.Shown(), model))
            {
                Say(state, $"{Written(each)} is legal, and nothing on the screen stands for it");
            }

            foreach (DesignMove move in state.Moves.Where(move => move.Followed))
            {
                if (offered.FirstOrDefault(each => Same(each, move)) is not ValidInput legal)
                {
                    continue;
                }

                if (move.Lands is null)
                {
                    Take(state, move, legal, null, move.To);
                    continue;
                }

                foreach (DesignLanding landing in move.Lands)
                {
                    Take(state, move, legal, landing, landing.To);
                }
            }
        }

        /// <summary>Takes one move from a state, follows it where the design has not been yet, and steps back.</summary>
        private void Take(DesignState state, DesignMove move, ValidInput legal, DesignLanding? landing, string? to)
        {
            string step = landing is null ? move.ToString() : $"{move} drawing {string.Join(", ", landing.Draws)}";

            if (!Press(state, move, legal))
            {
                return;
            }

            if (model.IsWaitingForOutcome != landing is not null)
            {
                Say(state, landing is null
                    ? $"{move} waits for something to be drawn, and the design has it settle"
                    : $"{move} settles, and the design has it draw");

                if (model.IsWaitingForOutcome)
                {
                    model.Outcomes[0].Choose.Execute(null);
                }

                model.Back.Execute(null);
                return;
            }

            if (landing is not null && !Draw(state, step, landing))
            {
                return;
            }

            if (to is not null && design.Named.TryGetValue(to, out DesignState? target) && Arrived(target, $"{state.Name} + {step}"))
            {
                if (!_visited.Contains(target.Name))
                {
                    Visit(target);
                }
            }

            model.Back.Execute(null);
        }

        /// <summary>Picks the outcome a branch drew, by the control that stands for it.</summary>
        private bool Draw(DesignState state, string step, DesignLanding landing)
        {
            string drawn = string.Join(", ", landing.Draws);
            OutcomeChoice? choice = model.Outcomes.FirstOrDefault(each => each.Draws == drawn);

            if (choice is null)
            {
                Say(state, $"{step}: nothing like that can be drawn any more");
                model.Outcomes[0].Choose.Execute(null);
                model.Back.Execute(null);
                return false;
            }

            Dispatcher.UIThread.RunJobs();
            Control? control = Reach.Sources(screen.Shown()).FirstOrDefault(each => ReferenceEquals(Reach.Carried(each)?.Command, choice.Choose));

            if (control is null)
            {
                // Taken anyway, so that what lies past it is still walked and said.
                Say(state, $"{step}: nothing on the screen picks what was drawn");
                choice.Choose.Execute(null);
            }
            else
            {
                press(step, control);
            }

            return true;
        }

        /// <summary>Says where what an open parameter admits is not what it admitted when the design was derived.</summary>
        /// <remarks>
        /// A move waiting for a value is one move to the runtime whatever its schema allows, so a
        /// bound widened in the rule set leaves the list of legal moves where it was, while the
        /// screen's editor, bounded by the same answer, offers the new values. The design carries
        /// what the runtime answered then; this holds it against what it answers now, once per
        /// parameter, at the first state it is met in.
        /// </remarks>
        private void Admits(DesignState state, ValidInput legal)
        {
            foreach (OpenParameter open in legal.Open)
            {
                if (!design.Admits.TryGetValue((legal.Input, open.Name), out JsonNode? was) || !_admitted.Add((legal.Input, open.Name)))
                {
                    continue;
                }

                JsonObject now = new() { ["op"] = open.Op };
                foreach (KeyValuePair<string, RuleValue> field in open.Description.Fields)
                {
                    now[field.Key] = Json(field.Value);
                }

                if (!Same(now, was))
                {
                    Say(state, $"{legal.Input}({open.Name}) admits {now.ToJsonString()}, and the design has it admit {was?.ToJsonString() ?? "null"}");
                }
            }
        }

        /// <summary>Presses a value the design has the rules refuse, and says where they take it or refuse it with something else.</summary>
        /// <remarks>
        /// The values a design says were tried and turned away are the ones a later version can
        /// take back without the list of legal moves showing it — a `validate` clause relaxed —
        /// so each is pressed through the screen like any move, and the codes it comes back with
        /// are held against the ones the design recorded.
        /// </remarks>
        private void Refused(DesignState state, DesignRefusal refusal, IReadOnlyList<ValidInput> offered)
        {
            ValidInput? legal = offered.FirstOrDefault(each => each.Input == refusal.Input
                && Settled(each).All(arg => refusal.Args.TryGetValue(arg.Key, out string? value) && value == arg.Value)
                && each.Open.All(open => refusal.Args.ContainsKey(open.Name)));

            // An offer that is gone was said already, as a move the rules no longer offer.
            if (legal is null)
            {
                return;
            }

            RuleInput input = model.Inputs.First(each => each.Input == refusal.Input);
            foreach ((string parameter, string argument) in refusal.Args)
            {
                input.Hold(parameter, Value(legal, parameter, argument));
            }

            Dispatcher.UIThread.RunJobs();
            if (Reach.For(screen.Shown(), model, legal).FirstOrDefault(each => each.IsEffectivelyEnabled) is not Control control)
            {
                return;
            }

            bool moved = false;
            void Moved(object? sender, PropertyChangedEventArgs args) => moved |= string.IsNullOrEmpty(args.PropertyName);

            model.PropertyChanged += Moved;
            try
            {
                refuse(refusal.ToString(), control);
            }
            finally
            {
                model.PropertyChanged -= Moved;
            }

            string codes = string.Join(", ", refusal.Codes);

            if (moved)
            {
                Say(state, $"the rules take {refusal}, which the design has them refuse{(codes.Length == 0 ? string.Empty : ": " + codes)}");

                if (model.IsWaitingForOutcome)
                {
                    model.Outcomes[0].Choose.Execute(null);
                }

                model.Back.Execute(null);
                return;
            }

            if (!input.RefusedWith.SequenceEqual(refusal.Codes, StringComparer.Ordinal))
            {
                Say(state, $"{refusal} is refused with {(input.RefusedWith.Count == 0 ? "no code" : string.Join(", ", input.RefusedWith))}, and the design has it refused with {(codes.Length == 0 ? "no code" : codes)}");
                return;
            }

            // Where the person would read it: a sentence the window draws, wherever the screen put it.
            Dispatcher.UIThread.RunJobs();
            HashSet<string> said = Said();
            foreach (string code in refusal.Codes)
            {
                string sentence = Labels.Say(labels, refusal.Input, code);
                if (!said.Contains(sentence))
                {
                    Say(state, $"{refusal} is refused with {code}, and the screen does not say \"{sentence}\"");
                }
            }
        }

        /// <summary>Every piece of text the window draws.</summary>
        private HashSet<string> Said() =>
        [
            .. screen.Shown().SelectMany(window => new[] { window }.Concat(window.GetVisualDescendants()))
                .OfType<TextBlock>()
                .Where(each => each.IsEffectivelyVisible && !string.IsNullOrEmpty(each.Text))
                .Select(each => each.Text!),
        ];

        private static JsonNode? Json(RuleValue value) => value switch
        {
            TextValue text => JsonValue.Create(text.Value),
            NumberValue number => JsonValue.Create(number.Value),
            BooleanValue flag => JsonValue.Create(flag.Value),
            SequenceValue sequence => new JsonArray([.. sequence.Select(Json)]),
            RecordValue record => new JsonObject(record.Fields.Select(field => KeyValuePair.Create(field.Key, Json(field.Value)))),
            _ => null,
        };

        /// <summary>Enters a move's arguments through the screen where it can, and presses the control that stands for it.</summary>
        private bool Press(DesignState state, DesignMove move, ValidInput legal)
        {
            RuleInput input = model.Inputs.First(each => each.Input == move.Input);
            IReadOnlyList<Control> controls = Reach.For(screen.Shown(), model, legal);
            List<string> unedited = [];
            List<string> unreturned = [];
            List<(string Parameter, string Typed)> mistyped = [];

            foreach ((string parameter, string argument) in move.Args)
            {
                (Entered how, string? typed) = Enter(input, parameter, Value(legal, parameter, argument), argument);
                switch (how)
                {
                    case Entered.Nowhere:
                        unedited.Add(parameter);
                        break;

                    case Entered.NotReturned:
                        unreturned.Add(parameter);
                        break;

                    case Entered.Mistyped:
                        mistyped.Add((parameter, typed ?? string.Empty));
                        break;
                }
            }

            Dispatcher.UIThread.RunJobs();

            if (controls.FirstOrDefault(each => each.IsEffectivelyEnabled) is not Control control)
            {
                // With no control at all it was already said, as a legal move the screen does not offer.
                if (controls.Count > 0)
                {
                    Say(state, $"{move} is legal, and what stands for it on the screen cannot be pressed");
                }

                return false;
            }

            // A value the control passes itself needs nothing to type it into.
            foreach (string parameter in unedited.Where(parameter => !input.Fixes(parameter, Reach.Carried(control)?.Parameter)))
            {
                Say(state, $"{move}: nothing on the screen shows the {parameter} it is to be applied with");
            }

            foreach (string parameter in unreturned.Where(parameter => !input.Fixes(parameter, Reach.Carried(control)?.Parameter)))
            {
                Say(state, $"{move}: the {parameter} the screen shows does not reach the rules when it is entered there");
            }

            foreach ((string parameter, string typed) in mistyped)
            {
                Say(state, $"{move}: typing the {parameter} into the screen gives '{typed}'");
            }

            bool moved = false;
            void Moved(object? sender, PropertyChangedEventArgs args) => moved |= string.IsNullOrEmpty(args.PropertyName);

            model.PropertyChanged += Moved;
            try
            {
                press(move.ToString(), control);
            }
            finally
            {
                model.PropertyChanged -= Moved;
            }

            if (input.HasErrors || input.Refusal is not null)
            {
                Say(state, $"{move} was refused{(input.Refusal is null ? string.Empty : ": " + input.Refusal)}");
                return false;
            }

            if (!moved)
            {
                Say(state, $"pressing what stands for {move} did nothing");
                return false;
            }

            return true;
        }

        /// <summary>Says whether the screen is at the state the design names, and says so where it is not.</summary>
        private bool Arrived(DesignState state, string how)
        {
            JsonObject position = JsonNode.Parse(model.Document)?["data"] as JsonObject ?? [];
            string[] differ = [.. design.Observed.Where(field => !Same(position[field], state.Data[field]))];

            if (differ.Length == 0)
            {
                return true;
            }

            Say(state, $"{how} at {Describe(position, differ)}, and the design has it at {Describe(state.Data, differ)}");
            return false;
        }

        private void Say(DesignState state, string what) => _found.Add(new Divergence(state.Name, design.Route(state), what));

        /// <summary>
        /// Enters a value the way somebody would — into whatever on the screen shows it — and says
        /// how far it got.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Which control is the editor is found rather than named: the value is held, and every
        /// property of every visible control that took it up is a candidate. Then it is let go of
        /// again and set on each candidate in turn, as the control's own current value, which is
        /// how a control records what was typed or picked; the first whose value reaches the input
        /// is the editor. A display bound one way shows the value and passes nothing back, and a
        /// list that does not hold the value refuses it, so neither is mistaken for one. Text is
        /// typed rather than set, so a limit only the keyboard meets is met here too.
        /// </para>
        /// <para>
        /// Where nothing takes it back the value is held after all, so that the walk goes on and
        /// says what lies past this move as well.
        /// </para>
        /// </remarks>
        private (Entered How, string? Typed) Enter(RuleInput input, string parameter, JsonNode? value, string argument)
        {
            input.Hold(parameter, null);
            Dispatcher.UIThread.RunJobs();
            Dictionary<(Control, AvaloniaProperty), object?> before = Snapshot();

            input.Hold(parameter, value);
            Dispatcher.UIThread.RunJobs();

            (Control Control, AvaloniaProperty Property, object Value)[] shown =
            [
                .. Snapshot()
                    .Where(each => each.Value is object now
                        && !each.Key.Item2.IsReadOnly
                        && Values.Text(now) == argument
                        && !(before.TryGetValue(each.Key, out object? then) && Equals(then, now)))
                    .Select(each => (each.Key.Item1, each.Key.Item2, each.Value!)),
            ];

            if (shown.Length == 0)
            {
                return (Entered.Nowhere, null);
            }

            input.Hold(parameter, null);
            Dispatcher.UIThread.RunJobs();

            foreach ((Control control, AvaloniaProperty property, object now) in shown)
            {
                // Text is typed, as keystrokes arrive: whatever the control does with typing —
                // a length it stops at, characters it will not take — happens here as it would
                // to somebody at the keyboard. A control that takes no typing is given the value
                // as its current value instead, which is where picking from a list ends up.
                if (now is string text)
                {
                    if (Reaches(input, () =>
                    {
                        control.Focus();
                        control.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
                    }))
                    {
                        object? typed = control.GetValue(property);
                        if (typed is not null && Values.Text(typed) == argument)
                        {
                            return (Entered.Returned, null);
                        }

                        input.Hold(parameter, value);
                        Dispatcher.UIThread.RunJobs();
                        return (Entered.Mistyped, typed is null ? string.Empty : Values.Text(typed));
                    }
                }

                if (Reaches(input, () => control.SetCurrentValue(property, now)))
                {
                    return (Entered.Returned, null);
                }
            }

            input.Hold(parameter, value);
            Dispatcher.UIThread.RunJobs();
            return (Entered.NotReturned, null);
        }

        /// <summary>Does something to the screen and says whether it reached the input.</summary>
        private static bool Reaches(RuleInput input, Action act)
        {
            bool reached = false;
            void Reached(object? sender, PropertyChangedEventArgs args) => reached |= !string.IsNullOrEmpty(args.PropertyName);

            input.PropertyChanged += Reached;
            try
            {
                act();
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                input.PropertyChanged -= Reached;
            }

            return reached;
        }

        private Dictionary<(Control, AvaloniaProperty), object?> Snapshot()
        {
            Dictionary<(Control, AvaloniaProperty), object?> values = [];

            foreach (Control control in screen.Shown().SelectMany(window => new[] { window }.Concat(window.GetVisualDescendants())).OfType<Control>().Where(each => each.IsEffectivelyVisible))
            {
                foreach (AvaloniaProperty property in AvaloniaPropertyRegistry.Instance.GetRegistered(control))
                {
                    values[(control, property)] = control.GetValue(property);
                }
            }

            return values;
        }

        /// <summary>A design's argument as the value an input holds: a settled one as its text, an open one as the JSON it reads as.</summary>
        private static JsonNode? Value(ValidInput legal, string parameter, string argument)
        {
            if (!legal.Open.ContainsKey(parameter))
            {
                return JsonValue.Create(argument);
            }

            // The design writes an argument as text and "2" is not 2; the property the value
            // lands in is typed by the schema, and reads either form of what it can hold.
            try
            {
                return JsonNode.Parse(argument);
            }
            catch (JsonException)
            {
                return JsonValue.Create(argument);
            }
        }

        /// <summary>
        /// Whether a design's move is one the runtime offered: the same input and mover, the same
        /// settled arguments, and either a value for every parameter the runtime left open or,
        /// for a move the design has waiting, the same parameters left open.
        /// </summary>
        private static bool Same(ValidInput legal, DesignMove move)
        {
            if (legal.Input != move.Input || legal.Actor != move.Actor)
            {
                return false;
            }

            string[] open = [.. legal.Open.Select(each => each.Name)];
            KeyValuePair<string, string>[] settled = [.. Settled(legal)];

            return settled.All(arg => move.Args.TryGetValue(arg.Key, out string? value) && value == arg.Value)
                && (move.Open.Count > 0
                    ? move.Args.Count == settled.Length && move.Open.Order(StringComparer.Ordinal).SequenceEqual(open.Order(StringComparer.Ordinal))
                    : move.Args.Count == settled.Length + open.Length && open.All(move.Args.ContainsKey));
        }

        private static IEnumerable<KeyValuePair<string, string>> Settled(ValidInput legal) =>
            legal.Arguments.Where(arg => !legal.Open.ContainsKey(arg.Key));

        private static string Written(ValidInput legal) => DesignMove.Written(legal.Input, Settled(legal), legal.Open.Select(open => open.Name));

        private static string Describe(JsonObject data, IEnumerable<string> fields) =>
            string.Join(", ", fields.Select(field => $"{field} {data[field]?.ToJsonString() ?? "null"}"));

        /// <summary>Two JSON values agree, with a number compared as a number rather than as how it was written.</summary>
        private static bool Same(JsonNode? left, JsonNode? right) => (left, right) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            (JsonObject a, JsonObject b) => a.Count == b.Count && a.All(field => b.ContainsKey(field.Key) && Same(field.Value, b[field.Key])),
            (JsonArray a, JsonArray b) => a.Count == b.Count && a.Zip(b).All(pair => Same(pair.First, pair.Second)),
            _ when left.GetValueKind() == JsonValueKind.Number && right.GetValueKind() == JsonValueKind.Number =>
                decimal.Parse(left.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture)
                == decimal.Parse(right.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            _ => left.ToJsonString() == right.ToJsonString(),
        };
    }
}
