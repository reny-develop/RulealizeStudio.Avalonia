// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Avalonia.Controls;

namespace RulealizeStudio.Hosting;

/// <summary>What is told, as a window is stood in a situation, to whoever is looking at it.</summary>
/// <param name="Pressing">
/// Each press on the way there, given the step it is pressed for as the route writes it: called
/// once the move's values are entered on the screen and before the control is pressed, so the
/// window it sees is the one the press is made on.
/// </param>
/// <param name="Arrived">Called once, when the window stands where the design says, before anything is tried there.</param>
/// <param name="Offered">
/// Each move legal there, as the route writes it, with the control that stands for it on the
/// screen, or null where nothing does.
/// </param>
/// <param name="Refused">
/// Each value the design has the rules refuse there, as the route writes a move, with the control
/// it was pressed on: called after the press, so the window it sees has the refusal on it. The
/// window is left as the refusal drew it until the next value is entered.
/// </param>
/// <remarks>Nothing told here presses anything; the press is the one <see cref="Replay"/> is given.</remarks>
public sealed record Witness(
    Action<string, Control> Pressing,
    Action Arrived,
    Action<string, Control?> Offered,
    Action<string, Control> Refused);
