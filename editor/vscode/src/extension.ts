// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';
import { application } from './application';
import { Disagreements } from './disagreements';
import { install, installed } from './installed';
import { build } from './built';
import { buildOutput, fetchFor, isRuleSet } from './ruleSet';
import { Screen, ScreenProvider } from './screen';
import { Screens } from './screens';
import { Specification, SpecificationProvider } from './specification';
import { applications, Applications, ruleSetIn } from './view';

// A client and nothing more. What a document states, where it is and whether it compiles are
// answered by RulealizeStudio.Server, which is the Rulealize runtime; what changed about what
// is legal is answered by ruledger; what a specification has, by the server again; and keeping
// anything is a commit, made in VS Code's own Source Control. Nothing here reads a rule. Where each of those programs comes
// from on a machine that had only VS Code is Installed's to say.

let client: LanguageClient | undefined;

/** The extension's place in the activity bar, as the manifest names it. */
const viewContainer = 'rulealizestudio';

/** What the extension hands to whoever asks for it — its tests — rather than anything a person uses. */
export interface Api {
    /** Settles once where the specifications and their rules disagree has been marked, for what was last changed. */
    marked(): Promise<void>;

    /** The specification open in the Studio's editor on this file, once it has been drawn. */
    specification(uri: vscode.Uri): Specification | undefined;

    /** The test design of this rule set, open on the screen. */
    screens(uri: vscode.Uri): Screens | undefined;

    /** The screen open in the Studio's editor on this file, once it is open. */
    screen(uri: vscode.Uri): Screen | undefined;

    /** The extension's own view. */
    view: Applications;
}

export async function activate(context: vscode.ExtensionContext): Promise<Api> {
    const config = vscode.workspace.getConfiguration('rulealize');
    const installed = install(context);
    const server = installed.server();
    const dotnet = await installed.runtime();

    // A vocabulary arrives as a file: 'rulealize restore' writes one, and so does a build.
    const vocabularies = vscode.workspace.createFileSystemWatcher('**/*.dll');
    const started = new LanguageClient(
        'rulealize',
        'Rulealize',
        { command: dotnet ?? 'dotnet', args: [server], options: { env: installed.env() } },
        {
            documentSelector: [
                { scheme: 'file', language: 'json' },
                { scheme: 'file', language: 'jsonc' },
                // A screen, for what its bindings ask of the rules: rulealize/asks.
                { scheme: 'file', pattern: '**/*.axaml' },
            ],
            initializationOptions: { plugins: config.get<string>('plugins') || undefined },
            synchronize: { fileEvents: vocabularies },
        });
    client = started;

    // Registered before the server is started, so that a server that does not start is said
    // in words where somebody asked for it, rather than as a command that is not there; and one
    // still starting is waited for.
    const starting = started.start();
    const specifications = new SpecificationProvider(started, starting, context.workspaceState);
    SpecificationProvider.register(context, specifications);
    const designs = new ScreenProvider(server, starting);
    ScreenProvider.register(context, designs);
    const screens = new Map<string, Screens>();
    const show = (ruleSet: vscode.Uri) =>
        Screens.open(context, server, started, screens, ruleSet);
    const disagreements = new Disagreements(started, starting);
    Disagreements.register(context, disagreements);
    const view = Applications.register(context, started);

    context.subscriptions.push(
        started,
        vocabularies,
        vscode.commands.registerCommand('rulealize.reveal', (rules: vscode.Uri, rule: string) => reveal(started, rules, rule)),
        vscode.commands.registerCommand('rulealize.screens', (uri?: vscode.Uri) => {
            const current = vscode.window.activeTextEditor?.document;
            const folder = uri && fs.existsSync(uri.fsPath) && fs.statSync(uri.fsPath).isDirectory() ? uri.fsPath : undefined;
            const target = folder ? undefined : uri ?? (current && isRuleSet(current) ? current.uri : undefined);
            if (target) {
                show(target);
                return;
            }

            // An application's folder, or with no rule set open the application in the workspace.
            const found = folder ? [folder] : applications();
            if (found.length === 0) {
                void vscode.window.showWarningMessage(vscode.l10n.t('There is no application here. Make one with New application, or open a rule set.'));
                return;
            }

            const rules = ruleSetIn(found[0]);
            if (rules) {
                show(vscode.Uri.file(rules));
            } else {
                void vscode.window.showInformationMessage(vscode.l10n.t('{0} has no rules yet, so there is no window to stand anywhere.', path.basename(found[0])));
            }
        }),
        vscode.commands.registerCommand('rulealize.start', (example: string) => start(example)),
        // Made in the folder open, where the view shows it; with none open, wherever somebody says.
        vscode.commands.registerCommand('rulealize.application', (where?: vscode.Uri, name?: string) =>
            application(where ?? vscode.workspace.workspaceFolders?.[0]?.uri, name)),
        vscode.commands.registerCommand('rulealize.restore', async () => {
            const current = vscode.window.activeTextEditor?.document;
            if (current && isRuleSet(current)) {
                await fetchFor(current);
            } else {
                void vscode.window.showWarningMessage(vscode.l10n.t('Open a rule set first.'));
            }
        }),
        vscode.workspace.onDidChangeConfiguration(change => {
            if (change.affectsConfiguration('rulealize')) {
                void vscode.window.showInformationMessage(vscode.l10n.t('Reload the window for a RulealizeStudio.Avalonia setting to take effect.'));
            }
        }));

    try {
        await starting;
    } catch {
        void notRunning(started, server);
    }

    // An application with no rules yet — one New application has just made and opened — is begun
    // from the view, so the view is where its window opens.
    if (applications().some(folder => !ruleSetIn(folder))) {
        void vscode.commands.executeCommand(`workbench.view.extension.${viewContainer}`);
    }

    return {
        marked: () => disagreements.settled(),
        specification: uri => specifications.opened(uri),
        screens: uri => screens.get(uri.toString()),
        screen: uri => designs.opened(uri),
        view,
    };
}

export function deactivate(): Thenable<void> | undefined {
    return client?.stop();
}

/**
 * Makes an example packed with the extension into an application in the open folder, and shows it
 * in the extension's view — the walkthrough's first step. What is packed is the application's own files — its
 * rule set, screen, specification, test design and the project listing its vocabularies — and the
 * rest is the template's, made as any new application is, with the example's specification in
 * place of the template's empty one. Which example is the
 * walkthrough's to say, as a path from the extension's own folder, so that no rule set is named
 * here.
 *
 * It is built, so that its vocabularies are there and its window can be shown. Whether it goes into
 * a repository, and when anything of it is committed, is the person's, in git.
 */
async function start(example: string): Promise<void> {
    const folder = vscode.workspace.workspaceFolders?.[0];
    if (!folder) {
        void vscode.window.showWarningMessage(vscode.l10n.t('Open a folder first: an application is a folder in it.'));
        return;
    }

    const from = installed().example(example);
    const name = path.basename(from);
    const to = path.join(folder.uri.fsPath, name);
    if (!fs.existsSync(to)) {
        if (!await application(folder.uri, name)) {
            return;
        }

        // Its files, and nothing a build or an editor left beside them.
        for (const file of fs.readdirSync(from, { withFileTypes: true }).filter(f => f.isFile())) {
            fs.copyFileSync(path.join(from, file.name), path.join(to, file.name));
        }

        const trouble = await build(to, buildOutput, vscode.l10n.t('Building {0}', name));
        if (trouble) {
            void vscode.window.showErrorMessage(trouble);
        }

    }

    await vscode.commands.executeCommand(`workbench.view.extension.${viewContainer}`);
}

/** Shows a rule of a rule set, for somebody who came from an element of the specification that asks for it: selected in its text. */
async function reveal(client: LanguageClient, rules: vscode.Uri, rule: string): Promise<void> {
    const document = await vscode.workspace.openTextDocument(rules);
    const found = (await client.sendRequest<{ name: string; range: vscode.Range }[]>('rulealize/rules', { uri: rules.toString() }))
        .find(r => r.name === rule);
    await vscode.window.showTextDocument(document, {
        selection: found && new vscode.Range(found.range.start.line, found.range.start.character, found.range.end.line, found.range.end.character),
    });
}

async function notRunning(started: LanguageClient, server: string): Promise<void> {
    const show = vscode.l10n.t('Show output');
    const chosen = await vscode.window.showErrorMessage(
        fs.existsSync(server)
            ? vscode.l10n.t('The Rulealize server is not running. It is started as \'dotnet {0}\', and needs a .NET 10 runtime; what it said is in its output.', server)
            : vscode.l10n.t('The Rulealize server is not running: {0} is not there. Build it with \'dotnet build src/RulealizeStudio.Server\', or set rulealize.server.', server),
        show);
    if (chosen === show) {
        started.outputChannel.show();
    }
}
