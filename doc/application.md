# An application out of a rule set and XAML

An application is a rule set and a `MainWindow.axaml` — or as many of either as it needs, below.
The C# is the entry point:

```csharp
public static class Program
{
    [STAThread]
    public static int Main(string[] args) => RuleApp.Run(typeof(Program).Assembly, args);

    public static AppBuilder BuildAvaloniaApp() => RuleApp.Build(typeof(Program).Assembly);
}
```

It names neither the model nor the rule set, and is the same in every application: `RuleApp`
opens every window in the application's assembly on the model of the rule set it binds, and the
window with nothing bound where there is no rule set yet. The model is not written by anybody. The project lists `signup.json` as `AdditionalFiles`, and a
source generator reads it at build time and writes a model whose names are the ones the document
gave, so the window binds them with `x:DataType` and compiled bindings. Each binding below says
which answer it came from:

```xml
<!-- the state field `name`, typed by its schema -->
<TextBlock Text="{Binding State.Name}" />

<!-- an open parameter, as a property. A refusal lands on it as a validation error, because
     the clause knew which parameter it was about. Its bounds are OpenParameter.Description -->
<TextBox Text="{Binding SetName.To}" MaxLength="{Binding SetName.ToLimits.MaxLength}" />

<!-- a command, enabled exactly when GetValidInputs offers a move the values held pick out -->
<Button Content="Save" Command="{Binding SetName.Apply}" />

<!-- what a parameter with a domain may be from this position -->
<ComboBox ItemsSource="{Binding ChooseSeat.SeatOptions}" SelectedItem="{Binding ChooseSeat.Seat}" />

<!-- a settled parameter's value, passed by the button that stands for it -->
<Button Content="+3" Command="{Binding Add.Apply}" CommandParameter="3" />

<!-- the projection `booking`, a type per record in it. A field the rules work out, like
     `ready`, is object: saying more would take evaluating the rules at build time -->
<TextBlock Text="{Binding Booking.Who}" />

<!-- the same for every rule set -->
<Button Content="Undo" Command="{Binding Back}" />
```

Renaming a field in the rule set breaks the build rather than the screen, and the model carries
the document it was generated from, so the two cannot disagree.

A rule set refuses with a code, and what the application says for it is the application's: a
label document beside the rule set, one per language, which the generator builds in beside the
rule set without the project listing it. `signup.labels.en.json` holds signup's, each keyed by the
name the clause has in its specification:

```json
{
  "$schema": "rulealize-studio/labels/v1",
  "labels": {
    "/inputs/setName/validate/name.reserved": "Nobody books under that name."
  }
}
```

The model speaks the one in the person's language, or the nearest, or else the first, and the
sentence is what lands under the field; a code with no label is shown as the code. A value an
open parameter's schema does not admit is refused under the parameter's `invalid`, a code like a
clause's and labelled the same way, `/inputs/<input>/validate/<code>`; where the rule set gives it
none, the runtime's own sentence is shown. [`sample/`](../sample) holds two
applications made this way, signup and countdown, over the same libraries with nothing added
to them for either.

Nobody names a control for a test's sake either. A control stands for a move when its
`Command` is that input's `Apply`, so `Reach.For` finds the button for a move a test names in
the rule set's terms, and `Reach.Unreachable` lists the legal moves a screen has no control
for — the way a screen can fall behind its rule set, since it cannot get ahead of it.

Nor does anybody write the cases. [Ruledger](https://github.com/reny-develop/Ruledger) walks a
rule set and writes a test design down, which is committed beside the rule set; `Replay.Run`
stands the screen in every state the design names, presses the control for every move it
followed, and returns a `Divergence` wherever the screen did not do what the design says — at
the state, with the route there. A change to the rule set that nobody derived the design again
for fails that way, and so does a screen that fell behind — a legal move with no control, a
value to apply it with that nothing on the screen shows or passes back, or a refusal the window
does not say in the label document's sentence.

## More than one window, rule set and specification

An application is as many windows, rule sets and specifications as it needs, none counted against
another. Every `.axaml` whose root is a window is one of its windows, opened on the model of the
rule set its `x:DataType` names (`RuleWindows`); windows binding the same rule set share where it
stands. When a window is shown, and what its × does, are the rules' answers, bound in its XAML:

```xml
<Window x:DataType="m:OrderModel"
        rs:RuleWindow.ShowWhen="{Binding Change.IsOffered}"
        rs:RuleWindow.CloseWith="{Binding Change.Apply}">
```

`ShowWhen` shows it while what it is bound to is true, and a window without it is shown from the
start. `CloseWith` makes the × a move, found by `Reach` as a button is and pressed by `Replay`, and
the window goes only where that move makes `ShowWhen` false; a window bound to a rule set without it
cannot be closed by its × at all. So every way a window appears or goes is a move the test design
walks, and a × the rules offer with nothing standing for it is reported as any other legal move is.
When no window is shown, the application ends. Compiling a window's XAML drops its `x:DataType`, so
the generator writes it into the assembly beside the window's file. The suite is itself such an
application: three windows over two rule sets, in [`test/…/window`](../test/RulealizeStudio.Avalonia.Tests/window).

## The tools in an application's folder

An application folder made by **New application** holds `AGENTS.md`, and a `CLAUDE.md`
that only reads it. It says what is in the folder, how each file is read — Rulealize's guides
linked for the rule set — and what each tool does and reports, each one the program the editor
runs to show the person the same thing:

```sh
dotnet tool restore                # once: rulealize-studio and ruledger, pinned in the folder
dotnet build                       # builds the design against the rules
dotnet rulealize-studio check      # compiles the rule set
dotnet rulealize-studio agree      # compares every specification with every rule set
dotnet ruledger derive <rule set>.json --plugins bin/Debug/net10.0
dotnet ruledger diff <rule set>.test-design.json <rule set>.json --plugins bin/Debug/net10.0
dotnet rulealize-studio replay     # holds the screen to the test design
```

It says nothing about what an agent should do, or when it is finished. Whoever uses an agent
decides what it does, and how an application like this is worked on is
[Rule-Derived Test Design](https://github.com/reny-develop/rule-derived-test-design)'s to say;
`AGENTS.md` links there rather than restating it. An exit code of 3 is a tool with something to
report — a rule that decides differently, a choice that could not be carried — which is for
somebody to read, not a failure to put right.

`rulealize-studio` is the server, run with a check on its command line instead of none: the same
compile, the same comparison of a specification with its rules, and `Replay` on the application as
last built, with no window server. Each writes what it found the way a compiler does, and exits
as `ruledger diff` does: 0 nothing to report, 1 it could not be done, 2 the command line was not
understood, 3 something to report. Where the folder has more than one rule set, `--rules` says
which. `rulealize-studio show` is the one that is not a check: what **Test cases** runs, it lists
the situations, and with `--state` and `--out` writes pictures of every window shown in one — of
another design than the one beside the rules with `--design`. `rulealize-studio changes` is
what the test cases changed run: Ruledger's diff of the design beside the rules against the rules
as saved — or, with `--was`, of the design derived from another rule set, carrying the choices of
`--design` and written out with `--was-out`, which the extension gives as last committed — each
situation by its route, as JSON, and with `--out` the design the rules give now.
`rulealize-studio design` is what the screen's editor runs for as long as it is open: the window
drawn from the XAML as written, the controls the build loads, and each change as an edit to the
text, asked a line of JSON at a time. The other three are shown as they change; for the build and the replay,
the folder has tasks, **build** and **replay the test design**, which put what they say in the
Problems panel.
