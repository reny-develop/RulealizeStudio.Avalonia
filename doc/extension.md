# The extension

[`editor/vscode`](../editor/vscode) is the extension, for the person who holds the requirement: they
design the application's screen and write what it should do, an agent writes the rules from them,
and they read on the application's own window everything the rules allow, asking for changes until
it is what they meant. They read no JSON and no XAML.

## Its view

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

## The screen

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

## What it should do

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

## Asking for the rules

The person writes the screen and the specification, and commits them where they commit anything. The
rules are written from them by whatever agent the person uses, asked in the agent's own place; the
extension does not hand anything over and starts no agent. Under the screen and the specification,
wherever either differs from what was last committed, the view says what changed of it beyond
binding it to the rules, a line each, as the server reads it against that commit — an element
reworded, added or taken out, a control placed, moved or given a layout — so that whatever the
agent changed of what the person made is read before it is committed and not found after.

## Test cases

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

## A change

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

## What it reads

The extension reads no rule. Whether a rule set compiles comes from
[`RulealizeStudio.Server`](../src/RulealizeStudio.Server), a language server over the same Rulealize
the rest of this repository takes, which says where the runtime found a fault in a rule set that
does not compile, in the Problems panel. It reads a screen too: what a window's XAML binds, read
from the XAML alone, is the list of what a rule set behind it has to give, before any rule is
written. The folder of vocabularies is, beside a project, the application's build output, where
its build puts the vocabularies the project lists, and otherwise `plugin`, where `rulealize
restore` puts them; the `rulealize.plugins` setting names another, relative to the rule set.
