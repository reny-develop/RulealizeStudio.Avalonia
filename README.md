# RulealizeStudio.Avalonia

A VS Code extension, and the libraries under it, for making an Avalonia application out of a rule
set and XAML. The rule set says what is legal; the screen is ordinary XAML bound to a model
generated from the rule set; and the test design Ruledger derives from the rules is read on the
application's own window, where changes are asked for and read before they are committed.
[doc/practice.md](doc/practice.md) takes somebody from an empty folder to an application that does
what they meant, step by step.

No rule set is named in this program and no control is named in a rule set. What a screen does is
an answer the runtime gave — `GetValidInputs` enables a command, `OpenParameter.Description`
bounds an editor, `InputRejectedException` puts a refusal under its field — and which control an
answer becomes is the XAML's to say.

## Installing it

RulealizeStudio.Avalonia is in the
[Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=reny-develop.rulealizestudio-avalonia):
**Extensions** in VS Code, search for it, and **Install**. The same extension is a `.vsix` on this
repository's [GitHub Releases](https://github.com/reny-develop/RulealizeStudio.Avalonia/releases),
installed with **Extensions**, `…`, **Install from VSIX…**.

On a machine with only VS Code, the extension runs on the .NET runtime already there, or else one
the [.NET Install Tool](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.vscode-dotnet-runtime)
acquires into its own storage, installing that extension first if it is not there. Where an
application is built or made it asks for the .NET SDK 10.0, which the same tool installs for
everybody on the machine; and the first time a test design is derived or compared it asks for
`ruledger` 1.4.0, which it fetches into its own storage — as it does `rulealize` 0.13.0, for a
rule set on its own. Nothing is fetched without asking.

## The extension

[`editor/vscode`](editor/vscode) is the extension, for the person who holds the requirement: they
design the application's screen and write what it should do, an agent writes the rules from them,
and they read on the application's own window everything the rules allow, asking for changes until
it is what they meant. They read no JSON and no XAML.

### Its view

Everything the person does starts from the extension's own view, **RulealizeStudio.Avalonia** in
VS Code's activity bar. With no application in the workspace it offers **New application**, the
example and the walkthrough. With one it is the application, under its folder's name: **Screens**,
**Specifications** and **Test cases**, each with its files below it called by their names — the
test cases one line for each rule set, by its name, once it has rules — so that VS Code's own find
on the view, from the magnifier above it, finds one among many; the walkthrough is a view of its
own below it. It opens things and draws nothing again: each line opens the file, or the page there
is.

VS Code's welcome page carries the walkthrough, **An application, read on its own screen**, from an
empty folder: countdown made there as an application of its own, built as it comes, its test cases
read on its window, and a first change asked of the person's agent.

The extension speaks the language VS Code is set to, by VS Code's own localization: its pages, its
questions, its commands and the walkthrough, with English and Japanese written
(`editor/vscode/l10n`, `package.nls.*.json`, `walkthrough/*.ja.md`). The person's own words — the
screen, the specification, the label document — are theirs and already in their language, and what
`ruledger`, the server and git report is shown as they said it.

### The screen

**New application** makes an application with no rules yet: a window with nothing on it, and a
specification with nothing in it. Its screen is designed on its own window. A window's line under
**Screens**, in the view — each called by its file, another added with the + on **Screens**, and each
deleted from its line — opens the window's XAML in the
extension's own editor as the window Avalonia draws from it as it is written now, with the
controls the application's build loads beside it — read from the build, not listed anywhere — to
drag onto the window or put into what is chosen, and what is on the window, to move — into
another control too, dragged onto it on the window or in its list — size by its corner, delete and give its words. Where it goes is written the way Visual Studio's WPF designer
writes it: dropped on the window, a control is put in a grid, aligned to the top left of its cell
with its distance from there as its margin, and its size is its width and height. Tabs are put
in a tab control as it holds them, each named after its type until it is given words, and a tab chosen is shown, on the window and never in the
text; a part of a control that holds one besides its content — a drawer's pane, a tab's header —
is listed under it, to put a control in. Beside it,
as in Visual Studio's property window, is every property XAML can give the control chosen, or the
window — its colours, its font, its size, its alignment, what holds it says of it — read from what
Avalonia registers for it rather than listed anywhere, each given with what its value is: a colour
picked, one of a set chosen, a number or text typed. Each is the smallest edit to the XAML, made as one edit and saved, so undo and git
see it, and a hand edit to the text is drawn as any other change; the person never reads the XAML.
Nothing placed is bound: that is the rules' to give, once there are any. Where the application was
never built, the editor builds it in a folder of its own, so that the folder's first build is still
the one made with its rules.

### What it should do

What an application should do is written before its rules, as a UML state machine in a format
the Studio owns: `specification.json` in the application's folder, named after nothing since it
comes first, and any more beside it, `checkout.specification.json`, added with the + on
**Specifications** in the view and deleted from its line there. Its states, the transitions between them — what the person does, and what it waits
for — and notes on either are its elements, and each holds what it says and the rules that carry
it out, so no other file binds it:

```json
{
  "$schema": "rulealize-studio/state-machine/v1",
  "initial": "unnamed",
  "states": { "named": { "name": "Named", "says": "…", "rules": ["/state/schema/stage"] } },
  "transitions": {
    "party-size": {
      "from": "named", "to": "named", "name": "Set the party",
      "says": "A party is one to six people, and one to start with.",
      "rules": ["/state/schema/party", "/inputs/setParty", "/inputs/setParty/params/size"]
    }
  },
  "notes": {
    "party-unchanged": {
      "on": "party-size", "says": "Setting the party to the size it already is is refused.",
      "rules": ["/inputs/setParty/validate/party.unchanged"]
    }
  }
}
```

An element is named by an id of its own, and a rule by the names the rule set gave it — a
`validate` clause by its code — so neither is a place in a text, and the binding holds while
either is rewritten. The file opens in the Studio's own editor, never as text to write by hand: as
the diagram, as sentences and as a table, each drawn from the one file, and edited from whichever
is being read — its words, where it stands, the rules it is bound to. The server writes every edit,
the whole file in one layout, which is the layout an agent's is put in too. Each element links to
every rule it is bound to, one click away, which opens the rule set with that rule selected. Signup's is three states, six transitions and six notes, and countdown's two, two and
one, with every rule of theirs bound.

Where the specifications and the rules disagree is marked as a problem on both, whenever either
changes, every specification of the application held to every rule set of it, and shown in the
specification's editor. Where there is more than one rule set, a binding names the one it is to,
`signup#/inputs/book`, and one that names none is marked. What is marked is an element bound to a rule the rules do not
have, and a rule no element asks for — the rule an agent is likeliest to add unasked, and the one
an element taken out leaves behind. Elements bound to nothing are left alone: they are
requirements nothing implements yet.

### Asking for the rules

The person writes the screen and the specification, and commits them where they commit anything. The
rules are written from them by whatever agent the person uses, asked in the agent's own place; the
extension does not hand anything over and starts no agent. Under the screen and the specification,
wherever either differs from what was last committed, the view says what changed of it beyond
binding it to the rules, a line each, as the server reads it against that commit — an element
reworded, added or taken out, a control placed, moved or given a layout — so that whatever the
agent changed of what the person made is read before it is committed and not found after.

### Test cases

A rule set's line under **Test cases**, in the view, opens the test design as test cases on the
application's own window, in two tabs. Under **Test cases**, every situation the walk reached is a
test case — the steps that reach it from where the application starts, and what its window should
be after them — and every value refused there is one of its own, listed fewest steps first, each
step said as the window says it — the words of the control pressed and the value entered beside it,
`Save: alice`, with the rule set's name for it on hover — where a control says it in words, and
marked where it is a value somebody chose, a refusal or an ending; a search and those marks narrow
the list. Choosing one stands the application as last built there, by pressing what the design says
was pressed, and shows its steps, each the window before it with the control pressed marked, and its
expected result: the window where it stands, the next operations legal there and the test case each
leads to, one click away, and the operations refused there; pointing at one marks its control on the
window. Each refusal is its sentence from the label document and the window with the value entered
and the refusal drawn under its field. The pictures are the window as it draws itself — the
replay's, with no window server — and not a drawing of it. The windows are drawn as large as is
asked, all at once, and one clicked opens larger, to be zoomed and moved.

Where the walk cannot name every value — text — it follows only what somebody chose. Under **Chosen
values**, a value is added for an operation at a situation, and every value chosen is listed with
what became of it — taken, refused, or not reached — to be called with another or deleted; an
operation that takes a value no one has chosen yet is listed as having none. A value is said by
what the screen calls the box it is entered in — the box's own AutomationProperties.Name, or the
nearest words written before it, out to three levels of what holds it and never another box's, as
the server's `Captions` reads them from the XAML — and by its name in the rules, said to be that,
where the screen writes none. A choice is written into the design's `edits`, in the form
[Ruledger describes](https://github.com/reny-develop/Ruledger/blob/main/doc/test-design.md#an-edit),
and the design is derived again, so what it led to is listed and shown like everything else;
where the rule set is open in a text editor with an edit not saved yet, deriving is left until it
is saved. Signup's design tries `setName(to: Christabella)` where it starts: the longest name its
rules allow.

### A change

A change the person wants is asked of whatever agent they use, in its own place; the extension
writes nothing down for it and starts no agent. Once the rules as saved decide otherwise than the
rules as last committed — the agent's change, or anybody's — the test cases it changed, added or
removed head the list, and are marked in it too, until the change is committed, whoever derives the
design beside the rules again meanwhile. The design before the change is derived again from the
rules as committed, carrying the choices of the design committed with them, so rules committed
without their design derived again are still read as what they were. It lists what an open
parameter admits now and admitted, and every situation Ruledger's diff names as a test case, by the
steps that reach it: added where the rules arrive there now and did not, removed where they no
longer do, and its expected result changed where they decide otherwise there. Choosing one shows its
expected result before the change and after it side by side — the application as last committed and
as its folder is saved now, each built from what it is written in, in a folder of the extension's
own — with what can be done there now and could not, the reverse, what leads elsewhere, an ending
that moved, and each refusal that came or went with the window that drew it. The account is
Ruledger's, asked of its library as values: nothing here compares two designs.

Committing what was made — the rules, the specification, the screen and the design together — or
putting the folder back is git's, done in VS Code's Source Control or by the person's agent. The
extension commits nothing and puts nothing back.

### What it reads

The extension reads no rule. Whether a rule set compiles comes from
[`RulealizeStudio.Server`](src/RulealizeStudio.Server), a language server over the same Rulealize
the rest of this repository takes, which says where the runtime found a fault in a rule set that
does not compile, in the Problems panel. It reads a screen too: what a window's XAML binds, read
from the XAML alone, is the list of what a rule set behind it has to give, before any rule is
written. The folder of vocabularies is, beside a project, the application's build output, where
its build puts the vocabularies the project lists, and otherwise `plugin`, where `rulealize
restore` puts them; the `rulealize.plugins` setting names another, relative to the rule set.

## An application out of a rule set and XAML

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
none, the runtime's own sentence is shown. [`sample/`](sample) holds two
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

### More than one window, rule set and specification

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
application: three windows over two rule sets, in [`test/…/window`](test/RulealizeStudio.Avalonia.Tests/window).

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

## Building

```sh
dotnet run --project sample/RulealizeStudio.Sample.Signup
dotnet test test/RulealizeStudio.Avalonia.Tests
```

The extension needs the server built, and is run from VS Code with `editor/vscode` open —
**Run the extension on the samples** — after:

```sh
dotnet build src/RulealizeStudio.Server
cd editor/vscode && npm install && npm run compile
```

`npm test` there runs the extension in four Extension Hosts: two opened on the samples, one for
the test cases and one for the specifications, and two on an empty folder, for the walkthrough and for
a new application. So it also needs the samples built,
`rulealize` and `ruledger` on the path — named in the settings the windows start with — somewhere
to fetch vocabularies from, and `feed/` filled.

`npm run package` there writes `rulealizestudio-avalonia-0.2.1.vsix`, which is what somebody installs:
the server published inside it, framework-dependent and for every platform; the newest of each
package in `feed/`, while they are not on nuget.org; and the example the walkthrough starts from,
countdown's own files with the template's project listing its vocabularies; what it fetches on a
machine with only VS Code is under [Installing it](#installing-it). The
`rulealize.server`, `rulealize.ruledger`, `rulealize.rulealize` and `rulealize.feed` settings
still name others, for whoever works on one of them.

**New application**, in the view, makes an application folder from nothing in the folder open —
or, with none open, wherever the person says — with `dotnet new rulealize-avalonia-app`, from
`Rulealize.Templates`. An application references `Binding`,
`Hosting` and `Generator` as packages and runs the server as a local tool, and while none of them
is on nuget.org they, and the template package, come from `feed/` at the root of this repository:

```sh
dotnet pack src/RulealizeStudio.Binding -c Release
dotnet pack src/RulealizeStudio.Hosting -c Release
dotnet pack src/RulealizeStudio.Generator -c Release
dotnet pack src/RulealizeStudio.Server -c Release
dotnet pack <Rulealize.Templates> -c Release -o feed
```

Needs `net10.0`. An application's rule set is fixed, so its project references the vocabularies
that rule set requires as packages, and the model opens them from beside the application.

## License

Apache-2.0. See [LICENSE](LICENSE). The `.vsix` carries it, and beside it `THIRD-PARTY-NOTICES.txt`,
which `npm run package` writes from the licence of every package the server and the extension
bring with them.
