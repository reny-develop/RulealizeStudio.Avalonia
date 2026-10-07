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

## Further

- [doc/practice.md](doc/practice.md) — building an application with the extension, step by step.
- [doc/extension.md](doc/extension.md) — what the extension does: its view, the screen, the
  specification, the test cases and a change, and what it reads.
- [doc/application.md](doc/application.md) — an application as a rule set and XAML: the model
  generated from the rule set, what each binding answers, label documents, more than one window,
  and the tools in an application's folder.
- [doc/building.md](doc/building.md) — building, testing and packaging this repository.

The parts are [`editor/vscode`](editor/vscode), the extension;
[`src/RulealizeStudio.Server`](src/RulealizeStudio.Server), what it asks about a document;
[`src/RulealizeStudio.Binding`](src/RulealizeStudio.Binding),
[`src/RulealizeStudio.Generator`](src/RulealizeStudio.Generator) and
[`src/RulealizeStudio.Hosting`](src/RulealizeStudio.Hosting), what an application is built from;
[`sample/`](sample), two such applications; and [`test/`](test), one headless suite over the .NET side.

## License

Apache-2.0. See [LICENSE](LICENSE). The `.vsix` carries it, and beside it `THIRD-PARTY-NOTICES.txt`,
which `npm run package` writes from the licence of every package the server and the extension
bring with them.
