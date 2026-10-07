# Building

This is for working on this repository. Installing the extension needs none of it: see
[Installing it](../README.md#installing-it). Run from the repository's root:

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
machine with only VS Code is under [Installing it](../README.md#installing-it). The
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
