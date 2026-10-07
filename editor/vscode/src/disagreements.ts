// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';
import * as path from 'path';
import { ruleSetsIn, saysSpecification, specificationsIn } from './blueprint';

/** What the server says to mark, on one of the two documents. */
interface Mark {
    range: { start: { line: number; character: number }; end: { line: number; character: number } };
    message: string;
}

/**
 * Where the specifications of each application in the workspace and its rules disagree, as problems
 * on both: an element bound to a rule the rules do not have, a rule no element of any of them asks
 * for, and what a specification refers to and does not have. An application is every specification
 * and rule set in its folder, all held to all. The comparison is the server's; asked again whenever
 * a document changes, since any of them may be the one.
 */
export class Disagreements {
    static readonly source = 'blueprint';

    private readonly marks = vscode.languages.createDiagnosticCollection(Disagreements.source);
    private marked = new Set<string>();
    private timer: NodeJS.Timeout | undefined;
    private running: Promise<void> = Promise.resolve();
    private waiting: (() => void)[] = [];

    constructor(private readonly client: LanguageClient, private readonly started: Promise<void>) {}

    static register(context: vscode.ExtensionContext, disagreements: Disagreements): void {
        const specifications = vscode.workspace.createFileSystemWatcher('**/*.json');
        const again = () => disagreements.schedule();
        context.subscriptions.push(
            disagreements.marks,
            specifications,
            specifications.onDidChange(again),
            specifications.onDidCreate(again),
            specifications.onDidDelete(again),
            vscode.workspace.onDidChangeTextDocument(change => {
                if (change.document.uri.scheme === 'file' && change.contentChanges.length > 0) {
                    again();
                }
            }),
            vscode.workspace.onDidSaveTextDocument(again));
        again();
    }

    /** Settles once what was last changed has been compared and marked. */
    settled(): Promise<void> {
        return this.timer ? new Promise(resolve => this.waiting.push(resolve)) : this.running;
    }

    private schedule(): void {
        clearTimeout(this.timer);
        this.timer = setTimeout(() => {
            this.timer = undefined;
            const waiting = this.waiting;
            this.waiting = [];
            this.running = this.running.then(() => this.all()).catch(() => undefined);
            void this.running.then(() => waiting.forEach(resolve => resolve()));
        }, 250);
    }

    private async all(): Promise<void> {
        try {
            await this.started;
        } catch {
            return;
        }

        const marked = new Set<string>();
        const folders = new Set<string>();
        for (const file of await vscode.workspace.findFiles('**/*.json', '**/{node_modules,bin,obj}/**')) {
            const open = vscode.workspace.textDocuments.find(d => d.uri.toString() === file.toString());
            const text = open?.getText() ?? Buffer.from(await vscode.workspace.fs.readFile(file)).toString('utf8');
            if (saysSpecification(text)) {
                folders.add(path.dirname(file.fsPath));
            }
        }

        for (const folder of folders) {
            for (const uri of await this.mark(folder)) {
                marked.add(uri);
            }
        }

        for (const uri of this.marked) {
            if (!marked.has(uri)) {
                this.marks.delete(vscode.Uri.parse(uri));
            }
        }

        this.marked = marked;
    }

    /** Marks every specification and rule set of the application in one folder, and says which documents carry its marks. */
    private async mark(folder: string): Promise<string[]> {
        const specifications = specificationsIn(folder).map(file => vscode.Uri.file(file).toString());
        const ruleSets = ruleSetsIn(folder).map(file => vscode.Uri.file(file).toString());
        const found = await this.client.sendRequest<{ documents: { uri: string; marks: Mark[] }[] } | null>(
            'rulealize/disagreements', { specifications, ruleSets });
        if (!found) {
            // One of them is not JSON just now: what was marked stays until it is again.
            return [...this.marked].filter(uri => specifications.includes(uri) || ruleSets.includes(uri));
        }

        for (const { uri, marks } of found.documents) {
            this.marks.set(vscode.Uri.parse(uri), marks.map(diagnostic));
        }

        return found.documents.map(({ uri }) => uri);
    }
}

function diagnostic(mark: Mark): vscode.Diagnostic {
    const { start, end } = mark.range;
    const made = new vscode.Diagnostic(
        new vscode.Range(start.line, start.character, end.line, end.character), mark.message, vscode.DiagnosticSeverity.Warning);
    made.source = Disagreements.source;
    return made;
}
