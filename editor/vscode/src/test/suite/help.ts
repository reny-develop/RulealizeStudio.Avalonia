// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as vscode from 'vscode';
import type { Api } from '../../extension';

let api: Api | undefined;

/** The extension, started. */
export async function extension(): Promise<Api> {
    api ??= await vscode.extensions.getExtension<Api>('reny-develop.rulealizestudio-avalonia')!.activate();
    return api;
}

/** Waits for something the editor does on its own time — an event it answers, a file a server watches. */
export async function until(condition: () => boolean | Promise<boolean>, otherwise: string, timeout = 30_000): Promise<void> {
    const end = Date.now() + timeout;
    while (!await condition()) {
        if (Date.now() > end) {
            throw new Error(otherwise);
        }

        await new Promise(resolve => setTimeout(resolve, 50));
    }
}

/** Closes every editor, and lets as long pass as a person takes to open one again. */
export async function closeAll(): Promise<void> {
    await vscode.commands.executeCommand('workbench.action.closeAllEditors');
    await new Promise(resolve => setTimeout(resolve, 250));
}
