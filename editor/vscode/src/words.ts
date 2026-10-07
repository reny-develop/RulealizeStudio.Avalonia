// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as vscode from 'vscode';

// Where a page drawn in a webview gets its words: the bundle VS Code loaded for the language it is
// set to, handed to the page's script, which says each sentence through its own `t` as the host
// says its own through `vscode.l10n.t`.

/** The loaded bundle as a script literal, safe inside a script element. */
export function bundle(): string {
    return JSON.stringify(vscode.l10n.bundle ?? {}).replace(/</g, '\\u003c');
}

/** What the page's script defines first: its `t`, which says a sentence in the bundle's words. */
export function sayer(): string {
    return `const bundled = ${bundle()};
  function t(s, ...a) { return (bundled[s] ?? s).replace(/\\{(\\d+)\\}/g, (_, i) => String(a[Number(i)])); }`;
}

/** Text for the page's markup, escaped. */
export function html(text: string): string {
    return text.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]!);
}
