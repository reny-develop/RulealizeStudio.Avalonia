// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as path from 'path';
import * as vscode from 'vscode';
import { run } from './installed';

/** What of a repository of VS Code's git is used here. */
interface GitRepository {
    readonly state: { readonly onDidChange: vscode.Event<void> };
}

/** What of VS Code's git is used here. */
interface GitApi {
    readonly state: 'uninitialized' | 'initialized';
    readonly onDidChangeState: vscode.Event<'uninitialized' | 'initialized'>;
    readonly git: { readonly path: string };
    getRepository(uri: vscode.Uri): GitRepository | null;
    openRepository(root: vscode.Uri): Promise<GitRepository | null>;
}

/**
 * What git says of an application's folder, read and never written: a file as it was last
 * committed, which what changed is read against, and when that changes.
 *
 * Committing, putting a folder back and making a repository are git's, done where the person
 * already does them — VS Code's Source Control, or their agent. Nothing here does them again.
 */
export class Committed {
    private api: Promise<GitApi | undefined> | undefined;

    /** A file of an application's folder as it was last committed; nothing where it was not, or the folder is in no repository. */
    async text(file: string): Promise<string | undefined> {
        const api = await (this.api ??= gitApi());
        const shown = await run(api?.git.path ?? 'git', ['show', `HEAD:./${path.basename(file)}`], path.dirname(file));
        return shown.code === 0 ? shown.out : undefined;
    }

    /** Calls a listener whenever what git says of the repository a folder is in changes — a commit among it. */
    async watch(folder: string, listener: () => void): Promise<vscode.Disposable> {
        const repository = await this.repository(folder);
        return repository ? repository.state.onDidChange(listener) : new vscode.Disposable(() => undefined);
    }

    /** The repository VS Code's git keeps a folder in; nothing where there is none, or git is off. */
    private async repository(folder: string): Promise<GitRepository | undefined> {
        const api = await (this.api ??= gitApi());
        if (!api) {
            return undefined;
        }

        const found = api.getRepository(vscode.Uri.file(folder));
        if (found) {
            return found;
        }

        // A folder outside the workspace is not one VS Code's git looks in by itself.
        const top = await run(api.git.path, ['rev-parse', '--show-toplevel'], folder);
        return top.code === 0 ? (await api.openRepository(vscode.Uri.file(top.out.trim()))) ?? undefined : undefined;
    }
}

/** VS Code's own git, once it has started; nothing where it is turned off. */
async function gitApi(): Promise<GitApi | undefined> {
    const extension = vscode.extensions.getExtension<{ enabled: boolean; getAPI(version: 1): GitApi }>('vscode.git');
    const exported = await extension?.activate();
    if (!exported?.enabled) {
        return undefined;
    }

    const api = exported.getAPI(1);
    if (api.state !== 'initialized') {
        await new Promise<void>(resolve => {
            const listening = api.onDidChangeState(state => {
                if (state === 'initialized') {
                    listening.dispose();
                    resolve();
                }
            });
        });
    }

    return api;
}
