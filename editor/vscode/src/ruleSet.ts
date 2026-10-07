// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { build } from './built';
import { installed, run } from './installed';

/**
 * Whether a document says it is a rule set. Its `$schema` is what it is, the way VS Code's own
 * JSON support picks a schema by it — the one thing the extension reads of a rule set. What the
 * document states is asked of the server.
 */
export function isRuleSet(document: vscode.TextDocument): boolean {
    return saysRuleSet(document.getText());
}

/** Whether a text says it is a rule set, by its `$schema`, as {@link isRuleSet} reads it. */
export function saysRuleSet(text: string): boolean {
    return /"\$schema"\s*:\s*"rulealize\/ruleset\//.test(text);
}

/** Whether a rule set is open in a text editor with an edit not saved yet, which ruledger, reading the file, does not see. */
export function unsaved(ruleSet: vscode.Uri): boolean {
    return vscode.workspace.textDocuments.some(d => d.uri.toString() === ruleSet.toString() && d.isDirty);
}

/**
 * Fetches the vocabularies a rule set requires into the folder it is compiled against: in an
 * application's folder by building it, since its project lists them; otherwise by `rulealize
 * restore`, into `plugin` beside it.
 */
export async function fetchFor(document: vscode.TextDocument): Promise<void> {
    const said = await fetchInto(document.fileName);
    if (said.code === 0) {
        void vscode.window.showInformationMessage(said.text.split('\n').pop() ?? said.text);
    } else {
        void vscode.window.showErrorMessage(said.text);
    }
}

/**
 * `ruledger derive` on a rule set: walks the rules again and writes the design beside it, carrying
 * the choices already in it. It exits 3 when it has something to report — a choice that could not be
 * carried, say — and the design is written all the same.
 */
export function derive(ruleSet: string): Promise<{ code: number; text: string }> {
    return ruledger(['derive', ruleSet, '--plugins', pluginsOf(ruleSet)], ruleSet, vscode.l10n.t('Writing the test design'));
}

/**
 * The folder of vocabularies a rule set is compiled against: where the setting names it from the
 * rule set's own folder; beside a project, the application's build output, where its build puts
 * the vocabularies the project lists; otherwise `plugin`, where `rulealize restore` puts them.
 */
export function pluginsOf(ruleSet: string): string {
    const named = vscode.workspace.getConfiguration('rulealize').get<string>('plugins')?.trim();
    const folder = path.dirname(ruleSet);
    return path.resolve(folder, named || (application(folder) ? buildOutput : 'plugin'));
}

/** Where `dotnet build` puts an application and the vocabularies its project lists, from its folder. */
export const buildOutput = 'bin/Debug/net10.0';

/** Whether a folder is an application's: a project is in it. */
function application(folder: string): boolean {
    return fs.existsSync(folder) && fs.readdirSync(folder).some(name => name.endsWith('.csproj'));
}

async function fetchInto(ruleSet: string): Promise<{ code: number; text: string }> {
    const folder = path.dirname(ruleSet);
    if (application(folder)) {
        const trouble = await build(folder, path.relative(folder, pluginsOf(ruleSet)), vscode.l10n.t('Fetching the vocabularies it requires'));
        return trouble ? { code: 1, text: trouble } : { code: 0, text: vscode.l10n.t('{0} is built, with the vocabularies it requires.', path.basename(folder)) };
    }

    const rulealize = await installed().tool('rulealize', vscode.l10n.t('Fetching the vocabularies it requires'));
    return rulealize
        ? run(rulealize, ['restore', ruleSet, '--plugins', pluginsOf(ruleSet)], folder, installed().env())
        : { code: 1, text: vscode.l10n.t('rulealize is not fetched, so the vocabularies cannot be.') };
}

/** ruledger, run in the rule set's folder; or why it cannot be. */
async function ruledger(args: string[], ruleSet: string, forWhat: string): Promise<{ code: number; text: string }> {
    const command = await installed().tool('ruledger', forWhat);
    return command
        ? run(command, args, path.dirname(ruleSet), installed().env())
        : { code: 1, text: vscode.l10n.t('ruledger is not fetched, so what is legal cannot be asked.') };
}
