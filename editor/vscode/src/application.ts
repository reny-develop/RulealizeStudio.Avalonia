// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { installed, run } from './installed';

// An application folder from nothing: what the samples are, made by `dotnet new` out of the
// template Rulealize.Templates publishes rather than written here. Nothing in it is code
// somebody is then meant to edit, and nothing here says what goes in it.

/** The template package, at the version whose application this extension was tried with. */
const templates = 'Rulealize.Templates::0.4.0';

/** The template in it. */
const template = 'rulealize-avalonia-app';

/** What `dotnet new install` says when that version is installed already, which is not a failure here. */
const installedAlready = 106;

/**
 * Makes an application folder and offers to open it. Where and what it is called are asked,
 * unless a caller — the tests — says. Answers the folder, or nothing where nobody said where.
 *
 * While RulealizeStudio's packages are not on nuget.org, they, the server's tool the folder's
 * checks are run with, and the template package come from a folder of packages — the one
 * {@link Installed.feed} names, packed inside the .vsix. The template is handed it as --feed and
 * writes it into the application's nuget.config. Once they are published, the setting, the
 * folder and --feed go.
 */
export async function application(where?: vscode.Uri, name?: string): Promise<vscode.Uri | undefined> {
    const parent = where ?? (await vscode.window.showOpenDialog({
        canSelectFiles: false,
        canSelectFolders: true,
        openLabel: vscode.l10n.t('Make the application in here'),
    }))?.[0];
    if (!parent) {
        return undefined;
    }

    const called = name ?? await vscode.window.showInputBox({
        prompt: vscode.l10n.t('What the application is called: its folder, its project and its namespace.'),
        value: 'MyApplication',
        validateInput: value => /^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$/.test(value)
            ? undefined
            : vscode.l10n.t('Letters, digits and underscores, in parts separated by dots, each starting with a letter.'),
    });
    if (!called) {
        return undefined;
    }

    const folder = path.join(parent.fsPath, called);
    if (fs.existsSync(folder)) {
        void vscode.window.showWarningMessage(vscode.l10n.t('{0} is there already.', folder));
        return undefined;
    }

    const dotnet = await installed().sdk(vscode.l10n.t('Making {0}', called));
    if (!dotnet) {
        return undefined;
    }

    const feed = installed().feed();
    const from = feed && fs.existsSync(feed) ? feed : undefined;
    const env = installed().env();

    const made = await vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: vscode.l10n.t('Making {0}', called) },
        async () => {
            const there = await run(dotnet, ['new', 'install', templates, ...(from ? ['--add-source', from] : [])], parent.fsPath, env);
            if (there.code !== 0 && there.code !== installedAlready) {
                return there;
            }

            return run(dotnet, ['new', template, '--name', called, '--output', folder, ...(from ? ['--feed', from] : [])], parent.fsPath, env);
        });
    if (made.code !== 0) {
        void vscode.window.showErrorMessage(vscode.l10n.t('The application was not made: {0}', made.text));
        return undefined;
    }

    const uri = vscode.Uri.file(folder);
    if (!where) {
        const open = vscode.l10n.t('Open it');
        if (await vscode.window.showInformationMessage(vscode.l10n.t('{0} is made, with nothing on its window yet. Open it, and begin from the RulealizeStudio.Avalonia view.', called), open) === open) {
            await vscode.commands.executeCommand('vscode.openFolder', uri);
        }
    }

    return uri;
}
