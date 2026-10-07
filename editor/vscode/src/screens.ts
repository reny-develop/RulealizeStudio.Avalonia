// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import { execFile } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';
import { build, Built, Builds, onUnbuilt, unbuilt } from './built';
import { Committed } from './git';
import { installed } from './installed';
import { derive, pluginsOf, unsaved } from './ruleSet';
import { html, sayer } from './words';

/** A situation of the test design, as the server listed it: a state, the moves that first reached it, and what the design records there. */
interface Situation {
    state: string;
    route: string[];
    ending: { result: string | null } | null;
    moves: { step: string; input: string; to: string | null; followed: boolean; admits: Record<string, { bound: string; value: string }[]> }[];
    refused: { step: string; input: string; codes: string[]; said: string[] }[];
    chosen: Choice[];
}

/** A value somebody chose to try, as the design's `edits` hold it and the server listed it: which edit it is, and what became of it. */
interface Choice {
    at: number;
    state: string;
    step: string;
    input: string;
    args: Record<string, string>;
    carried: string;
}

/** Where a control is on the window, in the window's own units. */
interface At {
    x: number;
    y: number;
    width: number;
    height: number;
}

/** How large a window is drawn. */
interface Size {
    width: number;
    height: number;
}

/**
 * A situation stood in, as the server answered it: a picture of the window pressed on before each
 * press, one of each window shown where it stands, where the control for each legal move is and on
 * which window, and a picture of each refusal as it was drawn. Each window is named by its file;
 * `picture` and `size` are the first shown, the whole of it where there is one window.
 */
interface Shown {
    state: string;
    size: Size;
    presses: { step: string; window: string | null; size: Size; at: At; picture: string }[];
    picture: string;
    windows: { window: string | null; title: string | null; size: Size; picture: string }[];
    moves: { step: string; window: string | null; at: At | null }[];
    refused: { step: string; window: string | null; size: Size; at: At; picture: string }[];
    divergences: string[];
}

/** Where one move from a situation lands, as the server said it: what was drawn, where the rules draw, and the route to where it lands. */
interface Landed {
    drew: string | null;
    to: string[] | null;
}

/** A refusal that came or went at a situation, with what it is said as. */
interface Refusing {
    step: string;
    codes: string[];
    said: string[];
}

/** One situation both designs have, and what the rules decide otherwise there, as the server said Ruledger found it. */
interface Change {
    route: string[];
    before: string;
    after: string;
    gained: { step: string; to: Landed[] }[];
    lost: { step: string; to: Landed[] }[];
    moved: { step: string; was: Landed[]; now: Landed[] }[];
    ending: { was: { ending: boolean; result: string | null }; now: { ending: boolean; result: string | null } } | null;
    refusing: Refusing[];
    notRefusing: Refusing[];
    truncated: boolean | null;
}

/** What a change to the rules did, as `rulealize-studio changes` answered it: Ruledger's diff, each situation by its route. */
interface Changed {
    comparedTheSameWay: boolean;
    changed: Change[];
    gone: { route: string[]; state: string }[];
    appeared: { route: string[]; state: string }[];
    admits: { input: string; parameter: string; was: { bound: string; value: string }[]; now: { bound: string; value: string }[] }[];
    choices: { step: string; from: string; carried: string }[];
}

/**
 * What the page asks for: a situation to stand in, or a value to try — from a situation, for an
 * input — a choice already made called with another, or one taken out.
 */
export type Chosen =
    | { type: 'choose'; state: string }
    | { type: 'try'; state: string; input: string; args: Record<string, string> }
    | { type: 'change'; at: number; args: Record<string, string> }
    | { type: 'takeOut'; at: number }
    | { type: 'compare'; key: string };

/**
 * The test design of a rule set, read as the application's own window: every situation the walk
 * reached, listed by the moves that reached it, and the one chosen drawn as the window standing
 * there, after the window each press was made on with the control pressed marked. Below it, all
 * the design records there — whether it is an ending, every move and where it leads, what each
 * open parameter admits — and each refusal as the window drew it, with what the design expects it
 * to say.
 *
 * Nothing here draws a screen. The pictures are the application as last built, stood in the
 * situation by `rulealize-studio show` — the replay, pressing what the design says was pressed —
 * and what the page adds is the mark on the control and the words of the move it stands for. A
 * state's number is what the server is asked for and is not shown: a situation is said by its route,
 * and each move of it as the window says it, as the server read the words off the window.
 *
 * Where the walk cannot name every value — text — it follows only what somebody chose, and a value
 * to try is chosen here, at the situation and for the input: written into the design's `edits` in
 * Ruledger's form, by the server, and the design derived again, so that what it led to is read as
 * the rest is. The choices are the design's, where Ruledger carries them and a reviewer sees them;
 * nothing of them is kept here.
 *
 * Once the rules as saved decide otherwise than the rules as last committed — the design those
 * give, derived again from them with the choices of the design committed beside them, so that rules
 * committed without their design derived again are still read as what they were — what that changed
 * heads the list: every situation Ruledger's diff names, by its route, as `rulealize-studio changes`
 * answers it, and the one chosen as the window before the change and after it — the application
 * as last committed and as saved now, each built from what it is written in by {@link Builds}. It
 * stays there until it is committed, whoever derives the design beside the rules again meanwhile:
 * what a change did is read against what was last committed, as the screen and the specification
 * are. Nothing here compares two designs, and nothing here commits or puts anything back: that is
 * git's, done where the person already does it.
 */
export class Screens {
    static readonly viewType = 'rulealize.screens';

    /** Settles once the page has been told the situations. */
    ready: Promise<void>;

    private readonly told = new Map<string, unknown>();
    private readonly id = nonce().slice(0, 12);
    private shown = 0;
    private standing: string | undefined;
    private situations: Situation[] = [];
    private changed: Changed | undefined;
    private compared = 0;
    private readonly builds: Builds;
    private readonly committed = new Committed();
    private asking: Promise<void> = Promise.resolve();
    private disposed = false;

    private constructor(
        private readonly server: string,
        private readonly client: LanguageClient,
        private readonly storage: vscode.Uri,
        readonly ruleSet: vscode.Uri,
        private readonly panel: vscode.WebviewPanel,
    ) {
        this.builds = new Builds(path.join(os.tmpdir(), 'rulealize-built'));
        panel.webview.options = { enableScripts: true, localResourceRoots: [storage] };
        panel.webview.html = page(nonce(), panel.webview.cspSource, sayer());

        const design = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(vscode.Uri.joinPath(ruleSet, '..'), path.basename(designOf(ruleSet.fsPath))));
        const listening = [
            panel.webview.onDidReceiveMessage((asked: Chosen) => void this.answer(asked)),
            design,
            design.onDidChange(() => void (this.ready = this.list())),
            design.onDidCreate(() => void (this.ready = this.list())),
        ];

        // The rules saved — by an agent, by anybody — are what a change did is
        // asked of again.
        const rules = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(vscode.Uri.joinPath(ruleSet, '..'), path.basename(ruleSet.fsPath)));
        listening.push(rules, rules.onDidChange(() => void (this.ready = this.changes())));

        // Built again elsewhere — by the screen's editor — or found not to build: said here too,
        // and listed again once it builds.
        listening.push(onUnbuilt(() => {
            this.sayUnbuilt();
            if (!unbuilt(path.dirname(ruleSet.fsPath))) {
                this.ready = this.list();
            }
        }));

        // A commit changes what a change is read against.
        void this.committed.watch(path.dirname(ruleSet.fsPath), () => void (this.ready = this.changes()))
            .then(watching => this.disposed ? watching.dispose() : listening.push(watching));
        panel.onDidDispose(() => {
            this.disposed = true;
            listening.forEach(l => l.dispose());
            if (fs.existsSync(storage.fsPath)) {
                fs.readdirSync(storage.fsPath).filter(name => name.startsWith(`${this.id}-`))
                    .forEach(name => fs.rmSync(path.join(storage.fsPath, name), { recursive: true, force: true }));
            }
        });

        this.ready = this.list();
    }

    /** Opens the screens of a rule set's test design, or brings forward the ones already open. */
    static open(
        context: vscode.ExtensionContext,
        server: string,
        client: LanguageClient,
        open: Map<string, Screens>,
        ruleSet: vscode.Uri,
    ): Screens {
        const already = open.get(ruleSet.toString());
        if (already) {
            already.panel.reveal();
            return already;
        }

        const storage = vscode.Uri.joinPath(context.storageUri ?? context.globalStorageUri, 'screens');
        const panel = vscode.window.createWebviewPanel(
            Screens.viewType,
            vscode.l10n.t('Test cases of {0}', path.basename(ruleSet.fsPath, '.json')),
            vscode.ViewColumn.Active,
            { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [storage] });
        const screens = new Screens(server, client, storage, ruleSet, panel);
        open.set(ruleSet.toString(), screens);
        panel.onDidDispose(() => open.delete(ruleSet.toString()));
        return screens;
    }

    /** The last message of a kind the page was sent. */
    said(type: string): unknown {
        return this.told.get(type);
    }

    /** Answers what the page asked for, and settles once the page has been told what came of it. */
    async answer(asked: Chosen): Promise<void> {
        switch (asked.type) {
            case 'choose':
                return this.choose(asked.state);
            case 'try':
                return this.write({ state: asked.state, input: asked.input, args: asked.args });
            case 'change':
                return this.write({ at: asked.at, args: asked.args });
            case 'takeOut':
                return this.write({ at: asked.at });
            case 'compare':
                return this.compare(asked.key);
        }
    }

    /** Stands the application's window in a situation, and settles once the page has been told how it got there. */
    async choose(state: string): Promise<void> {
        this.standing = state;
        void this.post({ type: 'standing', state });

        fs.rmSync(this.folder(), { recursive: true, force: true });
        const folder = this.folder(++this.shown);
        const said = await this.show(['--state', state, '--out', folder]);
        if (said.code !== 0 && said.code !== 3) {
            void this.post({ type: 'shown', state, trouble: said.text });
            return;
        }

        const shown = JSON.parse(said.out) as Shown;
        const picture = (file: string) => this.panel.webview.asWebviewUri(vscode.Uri.file(file)).toString();
        void this.post({
            type: 'shown',
            state,
            size: shown.size,
            presses: shown.presses.map(press => ({ ...press, picture: picture(press.picture) })),
            picture: picture(shown.picture),
            windows: shown.windows.map(window => ({ ...window, picture: picture(window.picture) })),
            moves: shown.moves,
            refused: shown.refused.map(refusal => ({ ...refusal, picture: picture(refusal.picture) })),
            divergences: shown.divergences,
        });
    }

    private async list(): Promise<void> {
        const folder = path.dirname(this.ruleSet.fsPath);
        const trouble = await build(folder, path.relative(folder, pluginsOf(this.ruleSet.fsPath)), vscode.l10n.t('Showing the test design on the screen'));
        this.sayUnbuilt();
        const said = trouble ? { code: 1, out: '', text: trouble } : await this.show([]);
        const listed = said.code === 0 ? JSON.parse(said.out) as { situations: Situation[]; unplaced: Choice[] } : undefined;
        this.situations = listed?.situations ?? [];
        void this.post(listed ? { type: 'situations', ...listed } : { type: 'situations', situations: [], unplaced: [], trouble: said.text });
        await this.changes();
    }

    /**
     * Asks what the rules as saved decide otherwise than the rules as last committed — Ruledger's
     * diff, as values, each situation by its route — and tells the page, which lists those situations
     * apart from the rest. Where nothing moved there is nothing to list: the rules are what was last
     * committed, which is where committing leaves them. Where the rules were never committed there is
     * nothing to read a change against, and nothing is listed either.
     */
    private changes(): Promise<void> {
        // One at a time: each writes the folder as committed out to the same place.
        return this.asking = this.asking.then(() => this.changesOnce(), () => this.changesOnce());
    }

    private async changesOnce(): Promise<void> {
        const rules = await this.committed.text(this.ruleSet.fsPath);
        if (rules === undefined) {
            this.changed = undefined;
            void this.post({ type: 'changes', comparedTheSameWay: true, changed: [], gone: [], appeared: [], admits: [], choices: [] });
            return;
        }

        const was = this.committedFile(path.basename(this.ruleSet.fsPath));
        const committed = this.committedFile(path.basename(designOf(this.ruleSet.fsPath)));
        fs.writeFileSync(was, rules);
        const design = await this.committed.text(designOf(this.ruleSet.fsPath));
        if (design !== undefined) {
            fs.writeFileSync(committed, design);
        }

        const said = await this.run('changes', [
            path.dirname(this.ruleSet.fsPath), ...this.rules(), '--plugins', pluginsOf(this.ruleSet.fsPath),
            '--was', was, ...(design !== undefined ? ['--design', committed] : []), '--was-out', this.beforeDesign(),
            '--out', this.afterDesign(),
        ]);
        if (said.code !== 0 && said.code !== 3) {
            this.changed = undefined;
            void this.post({ type: 'changes', trouble: said.text });
            return;
        }

        const changed = JSON.parse(said.out) as Changed;
        const moved = changed.changed.length + changed.gone.length + changed.appeared.length + changed.admits.length > 0 || !changed.comparedTheSameWay;
        this.changed = moved ? changed : undefined;
        if (moved) {
            void vscode.commands.executeCommand('setContext', 'rulealize.diffed', true);
        }

        void this.post({ type: 'changes', ...(moved ? changed : { comparedTheSameWay: true, changed: [], gone: [], appeared: [], admits: [], choices: [] }) });
    }

    /**
     * Stands the application before the change and after it in one situation the change moved, side
     * by side: before, the application as last committed, in the design its rules give; after, the
     * application as its folder is saved now, in the design the rules give now. Each is built from
     * what it is written in, so neither is a screen built before the rules it is shown with. A
     * situation only one of the two designs has is shown on that side alone.
     */
    async compare(key: string): Promise<void> {
        const changed = this.changed;
        const [kind, at] = key.split(':');
        const index = Number(at);
        const before = kind === 'changed' ? changed?.changed[index]?.before : kind === 'gone' ? changed?.gone[index]?.state : undefined;
        const after = kind === 'changed' ? changed?.changed[index]?.after : kind === 'appeared' ? changed?.appeared[index]?.state : undefined;
        if (!changed || (before ?? after) === undefined) {
            void this.post({ type: 'compared', key, trouble: vscode.l10n.t('That is not among what the change did any more.') });
            return;
        }

        const compared = ++this.compared;
        const progress = (text: string) => void this.post({ type: 'comparing', key, text });
        for (const name of fs.existsSync(this.storage.fsPath) ? fs.readdirSync(this.storage.fsPath) : []) {
            if (name.startsWith(`${this.id}-compared-`)) {
                fs.rmSync(vscode.Uri.joinPath(this.storage, name).fsPath, { recursive: true, force: true });
            }
        }

        const folder = path.dirname(this.ruleSet.fsPath);
        const plugins = path.relative(folder, pluginsOf(this.ruleSet.fsPath));
        const was = before === undefined ? undefined : await this.builds.before(folder, plugins, progress);
        const now = after === undefined ? undefined : await this.builds.after(folder, plugins, progress);
        progress(vscode.l10n.t('Standing the window there, before and after…'));

        const stand = async (built: Built | string | undefined, design: string, state: string | undefined, side: string) => {
            if (built === undefined || state === undefined) {
                return undefined;
            }
            if (typeof built === 'string') {
                return { trouble: built };
            }

            const out = vscode.Uri.joinPath(this.storage, `${this.id}-compared-${compared}-${side}`).fsPath;
            const said = await this.run('show', [built.folder, ...this.rules(), '--plugins', built.plugins, '--design', design, '--state', state, '--out', out]);
            if (said.code !== 0 && said.code !== 3) {
                return { trouble: said.text };
            }

            const shown = JSON.parse(said.out) as Shown;
            const picture = (file: string) => this.panel.webview.asWebviewUri(vscode.Uri.file(file)).toString();
            return {
                size: shown.size,
                picture: picture(shown.picture),
                windows: shown.windows.map(window => ({ ...window, picture: picture(window.picture) })),
                moves: shown.moves,
                refused: shown.refused.map(refusal => ({ ...refusal, picture: picture(refusal.picture) })),
                divergences: shown.divergences,
            };
        };

        const [standingBefore, standingAfter] = await Promise.all([
            stand(was, this.beforeDesign(), before, 'before'),
            stand(now, this.afterDesign(), after, 'after'),
        ]);
        if (compared === this.compared) {
            void this.post({ type: 'compared', key, before: standingBefore, after: standingAfter });
        }
    }

    /** Where a file of the folder as last committed is written, for the design before a change to be derived from: this page's own storage. */
    private committedFile(name: string): string {
        const folder = vscode.Uri.joinPath(this.storage, `${this.id}-committed`).fsPath;
        fs.mkdirSync(folder, { recursive: true });
        return path.join(folder, name);
    }

    /** Where the design the rules as last committed give is written, to stand the window before a change in: this page's own storage. */
    private beforeDesign(): string {
        fs.mkdirSync(this.storage.fsPath, { recursive: true });
        return vscode.Uri.joinPath(this.storage, `${this.id}-before.test-design.json`).fsPath;
    }

    /** Where the design the rules give now is written, to stand the window after a change in: this page's own storage, never beside the rules. */
    private afterDesign(): string {
        fs.mkdirSync(this.storage.fsPath, { recursive: true });
        return vscode.Uri.joinPath(this.storage, `${this.id}-after.test-design.json`).fsPath;
    }

    /**
     * Writes a choice into the design — one added, one called with other values, or one taken out,
     * as the server writes it — and derives the design again, so that what it led to is there to
     * read; then lists the situations again and stands the window where it stood — found by the
     * moves that reach it, since deriving numbers the states again.
     *
     * Deriving reads the rules as they are saved. Where a text editor holds an edit to them not saved
     * yet, deriving now would leave it out of the design, so the choice is written and the deriving
     * left until they are saved. A change already saved is taken into the design, and
     * stays listed above all the same: it is read against what was last committed.
     */
    private async write(choice: { state?: string; input?: string; at?: number; args?: Record<string, string> }): Promise<void> {
        const route = this.situations.find(s => s.state === this.standing)?.route.join(' → ');
        const design = designOf(this.ruleSet.fsPath);
        const named = path.basename(design);
        try {
            fs.writeFileSync(design, await this.client.sendRequest<string>('rulealize/choose', { text: fs.readFileSync(design, 'utf8'), ...choice }));
        } catch (wrong) {
            void this.post({ type: 'tried', ok: false, text: wrong instanceof Error ? wrong.message : String(wrong) });
            return;
        }

        if (unsaved(this.ruleSet)) {
            void this.post({
                type: 'tried',
                ok: true,
                text: vscode.l10n.t('The choice is written into {0}. The rules hold an edit not saved yet, so it is not derived again here, which would leave that edit out: saving the rules and deriving the design from them carries the choice.', named),
            });
        } else {
            const said = await derive(this.ruleSet.fsPath);
            const derived = vscode.l10n.t('{0} was derived again, carrying the choices in it.', named);
            void this.post({
                type: 'tried',
                ok: said.code === 0 || said.code === 3,
                text: said.code === 0 ? derived : said.code === 3 ? `${derived}

${said.text}` : said.text,
            });
        }

        await (this.ready = this.list());
        const again = this.situations.find(s => s.route.join(' → ') === route) ?? this.situations.find(s => s.route.length === 0);
        if (route !== undefined && again) {
            await this.choose(again.state);
        }
    }

    /** Says above everything, for as long as it is so, that what is shown is the application as last built, since it does not build as written now, and what the build said. */
    private sayUnbuilt(): void {
        const folder = path.dirname(this.ruleSet.fsPath);
        const shown = unbuilt(folder);
        void this.post({
            type: 'unbuilt',
            text: shown ? vscode.l10n.t('This is {0} as it was last built, at {1}: it does not build as it is written now. What the build said:', path.basename(folder), shown.built.toLocaleString(vscode.env.language)) : null,
            said: shown?.said ?? null,
        });
    }

    /** `rulealize-studio show` in the rule set's folder, against the application built there. */
    private show(more: string[]): Promise<{ code: number; out: string; text: string }> {
        return this.run('show', [path.dirname(this.ruleSet.fsPath), ...this.rules(), '--plugins', pluginsOf(this.ruleSet.fsPath), ...more]);
    }

    /** Which rule set of the folder is asked about, since an application may have more than one. */
    private rules(): string[] {
        return ['--rules', path.basename(this.ruleSet.fsPath)];
    }

    /** One of the server's commands, run in the rule set's folder. */
    private async run(verb: string, args: string[]): Promise<{ code: number; out: string; text: string }> {
        const folder = path.dirname(this.ruleSet.fsPath);
        const dotnet = await installed().runtime() ?? 'dotnet';
        return new Promise(resolve => {
            execFile(dotnet, [this.server, verb, ...args], { cwd: folder, env: installed().env(), maxBuffer: 16 * 1024 * 1024 }, (error, stdout, stderr) => {
                const code = error ? (typeof error.code === 'number' ? error.code : 1) : 0;
                resolve({ code, out: stdout, text: stderr.trim() || (error?.message ?? '') });
            });
        });
    }

    /** Where the pictures of one showing are written; each is a folder of its own, so the page never shows one it has cached. */
    private folder(shown = this.shown): string {
        return vscode.Uri.joinPath(this.storage, `${this.id}-${shown}`).fsPath;
    }

    private post(message: { type: string; [said: string]: unknown }): Thenable<boolean> {
        this.told.set(message.type, message);
        // Git's account of the folder can arrive after the page is closed.
        return this.disposed ? Promise.resolve(false) : this.panel.webview.postMessage(message);
    }
}

/** Where `ruledger derive` writes a rule set's design: beside it, named after it. */
function designOf(file: string): string {
    return path.join(path.dirname(file), `${path.basename(file, path.extname(file))}.test-design.json`);
}

function nonce(): string {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    return Array.from({ length: 32 }, () => chars[Math.floor(Math.random() * chars.length)]).join('');
}

function page(nonce: string, pictures: string, said: string): string {
    return `<!DOCTYPE html>
<html lang="${html(vscode.env.language)}">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${pictures}; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<style nonce="${nonce}">
  :root {
    --line: var(--vscode-panel-border, #2b2b2b);
    --muted: var(--vscode-descriptionForeground, #9d9d9d);
    --refused-fg: #f0b4ae; --refused-bg: #5a2d2a; --refused: #f85149;
    --edit-fg: #9cdcfe; --edit-bg: #1e3a5f; --edit: #4daafc;
    --ended-fg: #a8d5ae; --ended-bg: #2d4a31; --ended: #81b88b;
    --changed: #e2c08d; --removed: #c74e39;
  }
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background);
    padding: 0; margin: 0; display: flex; flex-direction: column; height: 100vh; font-size: 12.5px; }
  .unbuilt { border: 1px solid var(--vscode-inputValidation-warningBorder, #b89500); background: var(--vscode-inputValidation-warningBackground, #352a05);
    padding: 8px 12px; margin: 8px; flex: none; }
  .unbuilt strong { display: block; margin-bottom: 4px; }
  .unbuilt pre { margin: 0; white-space: pre-wrap; font-family: var(--vscode-editor-font-family); font-size: 0.9em; max-height: 10em; overflow: auto; }
  header { flex: none; height: 44px; display: flex; align-items: center; gap: 8px; padding: 0 16px 0 12px; border-bottom: 1px solid var(--line); }
  .tab { position: relative; height: 44px; display: flex; align-items: center; gap: 8px; padding: 0 4px; margin-right: 16px; background: none; border: none;
    color: var(--muted); font: inherit; font-size: 13.5px; cursor: pointer; }
  .tab.on { color: var(--vscode-foreground); font-weight: 600; }
  .tab.on::after { content: ''; position: absolute; left: 0; right: 0; bottom: -1px; height: 2px; background: var(--vscode-focusBorder, #0078d4); }
  .count { min-width: 8px; padding: 0 7px; height: 18px; line-height: 18px; border-radius: 9px; font-size: 11px; font-weight: 500;
    background: #3a3a3a; color: var(--muted); }
  .tab.on .count { background: var(--vscode-badge-background, #616161); color: var(--vscode-badge-foreground, #fff); }
  .spacer { flex: 1; }
  input.search, select, input.value { font: inherit; font-size: 12.5px; color: var(--vscode-input-foreground); background: var(--vscode-input-background, #313131);
    border: 1px solid var(--vscode-input-border, #3c3c3c); border-radius: 2px; height: 26px; box-sizing: border-box; padding: 0 8px; }
  input.search { width: 186px; }
  input.value:focus, input.search:focus, select:focus { outline: none; border-color: var(--vscode-focusBorder, #0078d4); }
  .chip { font: inherit; font-size: 12px; height: 26px; padding: 0 10px; display: inline-flex; align-items: center; gap: 6px; cursor: pointer;
    color: var(--vscode-foreground); background: none; border: 1px solid #3c3c3c; border-radius: 2px; }
  .chip.on { background: var(--vscode-input-background, #313131); border-color: #5a5a5a; }
  .dot { width: 8px; height: 8px; border-radius: 4px; }
  main { flex: 1; min-height: 0; display: grid; grid-template-columns: 520px 1fr; }
  main.edits { display: block; overflow: auto; }
  #list { overflow: auto; background: var(--vscode-sideBar-background, #1b1b1b); border-right: 1px solid var(--line); }
  .row { display: flex; align-items: center; gap: 6px; height: 28px; padding: 0 14px; cursor: pointer; white-space: nowrap; border-left: 2px solid transparent; }
  .row:hover { background: var(--vscode-list-hoverBackground, #2a2d2e); }
  .row.on { background: var(--vscode-list-activeSelectionBackground, #04395e); border-left-color: var(--vscode-focusBorder, #0078d4); }
  .row .name { overflow: hidden; text-overflow: ellipsis; flex: 1; }
  .group { font-size: 11px; font-weight: 600; color: var(--muted); text-transform: uppercase; letter-spacing: 0.04em;
    padding: 10px 14px 4px; border-top: 1px solid var(--line); }
  .group:first-child { border-top: none; }
  .group { cursor: pointer; display: flex; align-items: center; gap: 6px; user-select: none; }
  .group:hover { color: var(--vscode-foreground); }
  .group .fold { width: 10px; text-align: center; text-transform: none; }
  .row .name.start, .start { color: var(--muted); }
  .row .name.refused { color: var(--refused-fg); }
  .badge { flex: none; height: 18px; line-height: 18px; padding: 0 7px; border-radius: 9px; font-size: 10.5px; font-weight: 500; white-space: nowrap; }
  .badge.refused { background: var(--refused-bg); color: var(--refused-fg); }
  .badge.edit { background: var(--edit-bg); color: var(--edit-fg); }
  .badge.ended { background: var(--ended-bg); color: var(--ended-fg); }
  .badge.changed { background: var(--changed); color: #1f1f1f; font-weight: 600; }
  .badge.added { background: var(--ended); color: #1f1f1f; font-weight: 600; }
  .badge.removed { background: var(--removed); color: #1f1f1f; font-weight: 600; }
  .badge.unreached { background: #3a3a3a; color: var(--muted); }
  #detail { overflow: auto; padding: 14px 24px 24px; }
  h1 { font-size: 16px; font-weight: 600; margin: 0 0 18px; display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
  h2 { font-size: 12px; font-weight: 600; color: var(--muted); margin: 0 0 8px; }
  hr { border: none; border-top: 1px solid var(--line); margin: 14px 0; }
  .quiet { color: var(--muted); }
  .pad { padding: 8px 14px; }
  .trouble { color: var(--vscode-errorForeground); white-space: pre-wrap; }
  .note { white-space: pre-wrap; border-left: 3px solid var(--vscode-focusBorder, #0078d4); padding: 4px 8px; margin: 0 0 12px; }
  .note.trouble { border-left-color: var(--vscode-errorForeground); }
  .steps { display: flex; flex-wrap: wrap; gap: 8px; }
  #detail { --zoom: 1; }
  .card { width: calc(112px * var(--zoom) + 32px); box-sizing: border-box; background: #252526; border: 1px solid var(--line); border-radius: 4px; padding: 10px 14px 12px; }
  .card figure { width: calc(112px * var(--zoom)); }
  .num { flex: none; width: 18px; height: 18px; line-height: 18px; border-radius: 9px; text-align: center; font-size: 11px; font-weight: 700;
    background: var(--vscode-focusBorder, #0078d4); color: #fff; }
  .num.refused { background: var(--refused); }
  .said-step { display: flex; align-items: center; gap: 6px; margin-top: 10px; color: var(--vscode-foreground); }
  .said-step span:last-child { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .said-step.refused span:last-child { color: var(--refused-fg); }
  .inline-steps { display: flex; flex-wrap: wrap; gap: 6px 24px; }
  .inline-steps .said-step { margin-top: 0; }
  .expected { display: flex; flex-wrap: wrap; gap: 22px; align-items: flex-start; }
  .expected > figure, .shown > figure { width: calc(258px * var(--zoom)); flex: none; }
  .shown { display: flex; flex-direction: column; gap: 6px; flex: none; }
  .shown > .caption { font-size: 12px; opacity: 0.8; margin-top: 6px; }
  .zoom { position: sticky; top: 0; float: right; display: flex; align-items: center; gap: 4px; z-index: 2;
    background: var(--vscode-editor-background); padding: 2px 0 2px 8px; }
  .zoom button { font: inherit; width: 24px; height: 24px; border: 1px solid #3c3c3c; border-radius: 2px; background: none; color: var(--vscode-foreground); cursor: pointer; }
  .zoom button:hover { background: var(--vscode-list-hoverBackground, #2a2d2e); }
  .zoom span { min-width: 42px; text-align: center; color: var(--muted); font-size: 12px; }
  figure { cursor: zoom-in; }
  #viewer { position: fixed; inset: 0; z-index: 10; background: rgba(0, 0, 0, 0.82); display: flex; flex-direction: column; }
  #viewer[hidden] { display: none; }
  #viewer .bar { flex: none; display: flex; align-items: center; gap: 6px; padding: 8px 12px; }
  #viewer .bar .title { flex: 1; color: #e7e7e7; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  #viewer .bar button { font: inherit; height: 26px; min-width: 28px; padding: 0 10px; border: 1px solid #5a5a5a; border-radius: 2px;
    background: #2b2b2b; color: #e7e7e7; cursor: pointer; }
  #viewer .bar span.scale { min-width: 48px; text-align: center; color: #cccccc; }
  #viewer .stage { flex: 1; overflow: auto; display: flex; cursor: grab; }
  #viewer .stage.dragging { cursor: grabbing; }
  #viewer .stage figure { margin: auto; flex: none; cursor: inherit; }
  .aside { flex: 1; min-width: 220px; }
  figure { margin: 0; position: relative; display: block; }
  figure img { display: block; width: 100%; border: 1px solid #3c3c3c; box-sizing: border-box; }
  .mark { position: absolute; box-sizing: border-box; border: 2px solid var(--vscode-focusBorder, #0078d4); border-radius: 2px; }
  .mark.refused { border-color: var(--refused); }
  .mark.gained { border-color: var(--ended); }
  ul.next { list-style: none; margin: 0 0 4px; padding: 0; }
  ul.next li { display: flex; align-items: center; gap: 8px; height: 24px; padding: 0 8px; border-radius: 3px; cursor: pointer; }
  ul.next li:hover, ul.next li:focus { background: var(--vscode-list-hoverBackground, #2a2d2e); outline: none; }
  ul.next li .what { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  ul.next li .go { color: var(--muted); font-size: 14px; }
  ul.next li.waits { cursor: default; }
  ul.refusals { list-style: none; margin: 0; padding: 0; }
  ul.refusals li { padding: 3px 8px; border-radius: 3px; cursor: pointer; }
  ul.refusals li:hover { background: var(--vscode-list-hoverBackground, #2a2d2e); }
  ul.refusals .step { color: var(--refused-fg); }
  ul.refusals .why { color: var(--muted); font-size: 12px; }
  .wording { background: #2a1f1f; border-left: 3px solid var(--refused); border-radius: 3px; padding: 11px 14px; font-size: 13px; margin-bottom: 6px; }
  .sides { display: flex; flex-wrap: wrap; gap: 38px; align-items: flex-start; }
  .sides > div { width: calc(258px * var(--zoom)); }
  .sides h3 { font-size: 12.5px; font-weight: 400; margin: 0 0 6px; }
  .diff { margin: 0 0 12px; }
  .diff .line { display: flex; align-items: center; gap: 8px; border-left: 3px solid var(--ended); padding: 2px 10px; margin: 2px 0; cursor: pointer; }
  .diff .line.lost { border-left-color: var(--removed); }
  .diff .line.moved { border-left-color: var(--changed); }
  .diff .line .what { flex: 1; }
  .diff .line .why { display: block; color: var(--muted); font-size: 12px; padding-left: 16px; }
  .edits-page { padding: 18px 16px; }
  .add { display: flex; gap: 12px; align-items: flex-end; flex-wrap: wrap; padding-bottom: 18px; border-bottom: 1px solid var(--line); }
  .add label { display: flex; flex-direction: column; gap: 4px; font-size: 11.5px; color: var(--muted); }
  .add select.scene { width: 300px; }
  .add select.input { width: 160px; }
  .add input.value { width: 240px; }
  button.act { font: inherit; font-size: 12.5px; height: 26px; padding: 0 18px; border: none; border-radius: 2px; cursor: pointer;
    color: var(--vscode-button-foreground, #fff); background: var(--vscode-button-background, #0078d4); }
  button.act:hover { background: var(--vscode-button-hoverBackground); }
  button.act.second { color: var(--vscode-button-secondaryForeground); background: var(--vscode-button-secondaryBackground); }
  h1 button.act { font-weight: 400; margin-left: 6px; }
  table.choices { width: calc(100% + 32px); margin: 12px -16px 0; border-collapse: collapse; table-layout: fixed; }
  table.choices col.scene { width: 314px; }
  table.choices col.input { width: 140px; }
  table.choices col.value { width: 290px; }
  table.choices col.acts { width: 120px; }
  table.choices th { text-align: left; font-size: 11.5px; font-weight: 600; color: var(--muted); height: 28px; padding: 0 16px;
    border-top: 1px solid var(--line); border-bottom: 1px solid var(--line); }
  table.choices td { height: 32px; padding: 0 16px; border-bottom: 1px solid #262626; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  table.choices tr:hover td { background: var(--vscode-list-hoverBackground, #2a2d2e); }
  table.choices .param { color: var(--muted); margin-right: 8px; }
  table.choices td.acts { text-align: right; }
  a.link { color: var(--vscode-textLink-foreground, #4daafc); cursor: pointer; margin-left: 16px; font-size: 12px; }
  table.choices tr:not(:hover) a.link { visibility: hidden; }
</style>
</head>
<body>
<div id="unbuilt" class="unbuilt" hidden><strong id="unbuilt-text"></strong><pre id="unbuilt-said"></pre></div>
<header id="head"></header>
<div id="viewer" hidden>
  <div class="bar"><span class="title" id="viewer-title"></span>
    <button id="viewer-out" title="${html(vscode.l10n.t('Smaller'))}">−</button><span class="scale" id="viewer-scale"></span>
    <button id="viewer-in" title="${html(vscode.l10n.t('Larger'))}">+</button>
    <button id="viewer-actual">${html(vscode.l10n.t('Actual size'))}</button><button id="viewer-fit">${html(vscode.l10n.t('Fit'))}</button>
    <button id="viewer-close" title="${html(vscode.l10n.t('Close'))}">✕</button></div>
  <div class="stage" id="viewer-stage"></div>
</div>
<main id="main">
  <div id="list"><p class="quiet pad">${html(vscode.l10n.t('Asking for the test cases…'))}</p></div>
  <section id="detail"><p class="quiet">${html(vscode.l10n.t('Choose a test case, and the application is taken through its steps on its own window.'))}</p></section>
</main>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  ${said}
  const head = document.getElementById('head');
  const main = document.getElementById('main');

  // What the server listed: every situation the walk reached, and the values chosen where it did not.
  let situations = [];
  let unplaced = [];
  // What the screen calls the box each parameter is entered in, by input and parameter.
  let captions = {};
  function caption(input, parameter) {
    return captions[input]?.[parameter] ?? null;
  }
  // How large the windows are drawn, kept for this page.
  let zoom = (vscode.getState() || {}).zoom ?? 1;
  const named = new Map();
  const byRoute = new Map();
  // Each move as the application's window says it — the words of the control and the value entered
  // beside it — as the server read them off the window; a move it could not be said for is said by
  // its name.
  const spoken = new Map();
  function say(step) {
    return spoken.get(step) ?? step;
  }

  // The test cases: one for each situation, the steps that reach it from where the application
  // starts and what its window should be there; and one for each value refused there.
  let cases = [];
  // What the change to the rules did, as Ruledger found it, and each situation it names as a row.
  let changed = null;
  let rows = [];
  let tab = 'cases';
  let filter = 'all';
  let query = '';
  let current = null;
  let chosen = null;
  let comparing = null;
  let lastShown = null;
  let note = null;
  let shownFor = null;

  function text(tag, words, className) {
    const element = document.createElement(tag);
    element.textContent = words;
    if (className) {
      element.className = className;
    }
    return element;
  }

  function badge(kind, words) {
    return text('span', words, 'badge ' + kind);
  }

  // A test case said as its steps, the way the window says each; the one where it starts said so.
  function steps(route, refusal) {
    const all = refusal ? [...route, refusal] : route;
    return all.length === 0 ? t('As it starts') : all.map(say).join('  ›  ');
  }

  // The operation of a move without the value entered for it: the words of its control.
  function operation(step) {
    const words = say(step);
    const colon = words.indexOf(': ');
    return colon < 0 ? words : words.slice(0, colon);
  }

  function choices() {
    return [...situations.flatMap(s => s.chosen.map(c => ({ ...c, situation: s }))), ...unplaced.map(c => ({ ...c, situation: null }))];
  }

  // ---- The header: the two tabs, and over the test cases, what they are found and narrowed by.

  function drawHead() {
    head.replaceChildren();
    const tabOf = (name, words, count) => {
      const button = document.createElement('button');
      button.className = 'tab' + (tab === name ? ' on' : '');
      button.appendChild(text('span', words));
      button.appendChild(text('span', String(count), 'count'));
      button.addEventListener('click', () => { tab = name; draw(); });
      return button;
    };
    head.appendChild(tabOf('cases', t('Test cases'), cases.length));
    head.appendChild(tabOf('edits', t('Chosen values'), choices().length));
    head.appendChild(text('span', '', 'spacer'));
    if (tab !== 'cases') {
      return;
    }

    const search = document.createElement('input');
    search.className = 'search';
    search.placeholder = '⌕  ' + t('Search');
    search.value = query;
    search.addEventListener('input', () => { query = search.value; drawList(); });
    head.appendChild(search);
    const chip = (name, words, count, colour) => {
      const button = document.createElement('button');
      button.className = 'chip' + (filter === name ? ' on' : '');
      if (colour) {
        const dot = text('span', '', 'dot');
        dot.style.background = colour;
        button.appendChild(dot);
      }
      button.appendChild(text('span', words + ' ' + count));
      button.addEventListener('click', () => { filter = name; drawHead(); drawList(); });
      head.appendChild(button);
    };
    chip('all', t('All'), cases.length, null);
    chip('edits', t('Chosen values'), cases.filter(c => c.edit).length, 'var(--edit)');
    chip('refused', t('Refused'), cases.filter(c => c.refusal !== null).length, 'var(--refused)');
    chip('ended', t('Ended'), cases.filter(c => c.ended).length, 'var(--ended)');
  }

  function draw() {
    drawHead();
    main.classList.toggle('edits', tab === 'edits');
    if (tab === 'edits') {
      drawEdits();
      return;
    }
    if (!document.getElementById('list')) {
      main.replaceChildren();
      const list = document.createElement('div');
      list.id = 'list';
      const detail = document.createElement('section');
      detail.id = 'detail';
      main.append(list, detail);
      if (lastShown && comparing === null) {
        show(lastShown);
      } else {
        detail.appendChild(text('p', t('Choose a test case, and the application is taken through its steps on its own window.'), 'quiet'));
      }
    }
    drawList();
  }

  // ---- The list of test cases, or of what the change did.

  function drawList() {
    const list = document.getElementById('list');
    if (!list) {
      return;
    }
    list.replaceChildren();
    const wanted = query.trim().toLowerCase();

    // What the change since the last commit did heads the list, whatever narrows the rest: every
    // test case it changed, added or removed — one it removed is in no list but this.
    if (changed && changed.trouble) {
      list.appendChild(text('p', t('What the change did could not be asked: {0}', changed.trouble), 'trouble pad'));
    }
    const touched = rows.filter(row => !wanted || steps(row.route, null).toLowerCase().includes(wanted));
    const grouped = touched.length > 0 || (changed && !changed.comparedTheSameWay);
    if (grouped) {
      list.appendChild(group('changes', t('Since the last commit ({0})', rows.length)));
    }
    if (grouped && !folded.has('changes')) {
      if (changed && !changed.comparedTheSameWay) {
        list.appendChild(text('p', t('The rules now tell one situation from another by other things than before, so some of what moved here is the walk arriving elsewhere rather than the rules deciding otherwise.'), 'quiet pad'));
      }
      for (const row of touched) {
        const div = document.createElement('div');
        div.className = 'row' + (comparing === row.key ? ' on' : '');
        div.appendChild(badge(row.kind, row.said));
        div.appendChild(text('span', steps(row.route, null), 'name'));
        div.title = row.route.join(' → ');
        div.addEventListener('click', () => compare(row.key));
        list.appendChild(div);
      }
    }

    const shown = cases.filter(c => (filter === 'all' || (filter === 'edits' && c.edit) || (filter === 'refused' && c.refusal !== null) || (filter === 'ended' && c.ended))
      && (!wanted || steps(c.route, c.step).toLowerCase().includes(wanted)));
    if (grouped) {
      list.appendChild(group('cases', t('Test cases') + ' (' + shown.length + ')'));
      if (folded.has('cases')) {
        return;
      }
    }

    for (const c of shown) {
      const div = document.createElement('div');
      div.className = 'row' + (current === c.key && comparing === null ? ' on' : '');
      div.dataset.case = c.key;
      const touched = touchedBy(c);
      if (touched) {
        div.appendChild(badge(touched.kind, touched.said));
      }
      const name = text('span', steps(c.route, c.step), 'name' + (c.refusal !== null ? ' refused' : c.route.length === 0 ? ' start' : ''));
      div.appendChild(name);
      if (c.edit) {
        div.appendChild(badge('edit', t('Chosen value')));
      }
      if (c.refusal !== null) {
        div.appendChild(badge('refused', t('Refused')));
      }
      if (c.ended) {
        div.appendChild(badge('ended', t('Ended')));
      }
      div.title = [...c.route, ...(c.step ? [c.step] : [])].join(' → ');
      div.addEventListener('click', () => open(c.key));
      list.appendChild(div);
    }
    if (shown.length === 0) {
      list.appendChild(text('p', t('No test case is like that.'), 'quiet'));
    }
  }

  // The situations listed, as test cases in the order of the routes that reach them: a situation,
  // then those one step on from it.
  function listed(said) {
    situations = said.situations ?? [];
    unplaced = said.unplaced ?? [];
    captions = said.captions ?? {};
    named.clear();
    byRoute.clear();
    for (const situation of situations) {
      named.set(situation.state, situation);
      byRoute.set(situation.route.join(' → '), situation);
      const words = [
        ...situation.route.map((step, i) => [step, situation.words?.[i]]),
        ...situation.moves.map(m => [m.step, m.words]),
        ...situation.refused.map(r => [r.step, r.words]),
        ...situation.chosen.map(c => [c.step, c.words]),
      ];
      for (const [step, word] of words) {
        if (word) {
          spoken.set(step, word);
        }
      }
    }

    const under = new Map();
    for (const situation of situations) {
      const from = situation.route.slice(0, -1).join(' → ');
      under.set(from, [...(under.get(from) ?? []), situation]);
    }
    const ordered = [];
    const add = situation => {
      ordered.push(situation);
      const route = situation.route.join(' → ');
      for (const next of situation.route.length === 0 ? (under.get('') ?? []).filter(s => s.route.length > 0) : under.get(route) ?? []) {
        add(next);
      }
    };
    for (const start of situations.filter(s => s.route.length === 0)) {
      add(start);
    }
    // The fewest steps first, and those one step on from the same situation together.
    ordered.sort((a, b) => a.route.length - b.route.length);

    cases = [];
    for (const situation of ordered) {
      // A case is a chosen value's where the step that ends it is one somebody chose to try.
      const from = situation.route.length === 0 ? null : byRoute.get(situation.route.slice(0, -1).join(' → '));
      const last = situation.route[situation.route.length - 1];
      cases.push({
        key: 's:' + situation.state, state: situation.state, route: situation.route, refusal: null, step: null,
        ended: !!situation.ending, edit: !!from && from.chosen.some(c => c.step === last),
      });
      situation.refused.forEach((refusal, i) => cases.push({
        key: 'r:' + situation.state + ':' + i, state: situation.state, route: situation.route, refusal: i, step: refusal.step,
        ended: false, edit: situation.chosen.some(c => c.step === refusal.step),
      }));
    }
    // The fewest steps first, a value refused counted as one step more.
    cases.sort((a, b) => (a.route.length + (a.refusal === null ? 0 : 1)) - (b.route.length + (b.refusal === null ? 0 : 1)));

    if (said.trouble) {
      draw();
      document.getElementById('list')?.replaceChildren(text('p', said.trouble, 'trouble'));
      return;
    }
    draw();
  }

  function reaching(state) {
    return cases.find(c => c.state === state && c.refusal === null);
  }

  function open(key) {
    const c = cases.find(x => x.key === key);
    if (!c) {
      return;
    }
    current = key;
    comparing = null;
    note = null;
    tab = 'cases';
    draw();
    document.querySelector('.row.on')?.scrollIntoView({ block: 'nearest' });
    if (lastShown && lastShown.state === c.state && chosen === c.state) {
      show(lastShown);
      return;
    }
    chosen = c.state;
    document.getElementById('detail').replaceChildren(text('p', t('Standing the window there…'), 'quiet'));
    vscode.postMessage({ type: 'choose', state: c.state });
  }

  function choose(state) {
    const c = reaching(state);
    if (c) {
      open(c.key);
    }
  }

  // ---- A test case: its steps, each the window it is taken on with what is pressed marked, and
  // its expected result — the window it should stand as, what can be done there and the test case
  // each leads to, and what is refused there; or, for a value refused, the window as it says so.

  function show(said) {
    lastShown = said;
    const detail = document.getElementById('detail');
    if (!detail || tab !== 'cases') {
      return;
    }
    detail.replaceChildren(zoomer());
    if (note) {
      detail.appendChild(text('p', note.text, note.ok ? 'note' : 'note trouble'));
    }
    if (said.trouble) {
      detail.appendChild(text('p', said.trouble, 'trouble'));
      return;
    }

    const c = cases.find(x => x.key === current && x.state === said.state) ?? reaching(said.state);
    const situation = named.get(said.state);
    if (!c || !situation) {
      return;
    }
    const refusal = c.refusal !== null ? situation.refused[c.refusal] : null;
    const drawn = refusal ? said.refused[c.refusal] : null;

    const title = text('h1', '');
    title.appendChild(text('span', steps(situation.route, refusal ? refusal.step : null)));
    if (refusal) {
      title.appendChild(badge('refused', t('Refused')));
    } else if (situation.ending) {
      title.appendChild(badge('ended', t('Ended')));
    }
    const touched = touchedBy(c);
    if (touched) {
      title.prepend(badge(touched.kind, touched.said));
      const against = text('button', t('Compare with before the change'), 'act second');
      against.addEventListener('click', () => compare(touched.key));
      title.appendChild(against);
    }
    detail.appendChild(title);

    detail.appendChild(text('h2', t('Steps')));
    const cards = document.createElement('div');
    cards.className = 'steps';
    said.presses.forEach((press, i) => cards.appendChild(card(i + 1, press.step, picture(press.picture, press.size || said.size, press.at, '', ''), false)));
    if (refusal) {
      const on = windowsOf(said).find(w => drawn && w.window === drawn.window) || windowsOf(said)[0];
      cards.appendChild(card(said.presses.length + 1, refusal.step, picture(on.picture, on.size, drawn ? drawn.at : null, '', 'refused'), true));
    }
    detail.appendChild(said.presses.length === 0 && !refusal ? text('p', t('None: the window as it starts.'), 'quiet') : cards);

    for (const divergence of said.divergences) {
      detail.appendChild(text('p', divergence, 'trouble'));
    }

    detail.appendChild(document.createElement('hr'));
    detail.appendChild(text('h2', t('Expected result')));
    const expected = document.createElement('div');
    expected.className = 'expected';
    detail.appendChild(expected);
    const aside = document.createElement('div');
    aside.className = 'aside';

    if (refusal) {
      expected.appendChild(drawn && drawn.step === refusal.step ? picture(drawn.picture, drawn.size || said.size, drawn.at, t('The window, with {0} refused', refusal.step), 'refused') : text('p', '', ''));
      expected.appendChild(aside);
      aside.appendChild(text('h2', t('What the refusal says')));
      for (const sentence of refusal.said) {
        aside.appendChild(text('div', sentence, 'wording'));
      }
      return;
    }

    const standing = figures(windowsOf(said), t('The window'));
    expected.appendChild(standing.element);
    expected.appendChild(aside);
    if (situation.ending) {
      aside.appendChild(text('p', situation.ending.result === null ? t('It ends here, with no result.') : t('It ends here: {0}.', situation.ending.result), 'quiet'));
    }

    aside.appendChild(text('h2', situation.moves.length === 0 ? t('Nothing can be done here') : t('Next operations')));
    const next = document.createElement('ul');
    next.className = 'next';
    for (const move of situation.moves) {
      const li = document.createElement('li');
      li.tabIndex = 0;
      li.title = move.step;
      li.appendChild(text('span', say(move.step), 'what'));
      if (move.to === null) {
        li.classList.add('waits');
        li.appendChild(text('span', move.followed ? t('→ the walk went no further') : situation.ending ? t('→ not followed: it has ended') : t('→ waits for a value'), 'quiet'));
      } else if (move.to === situation.state) {
        li.appendChild(text('span', t('No change'), 'quiet'));
      } else {
        if (named.get(move.to)?.ending) {
          li.appendChild(badge('ended', t('Ended')));
        }
        li.appendChild(text('span', '›', 'go'));
        li.addEventListener('click', () => choose(move.to));
      }
      const stands = said.moves.find(m => m.step === move.step.split(' drawing ')[0]);
      const at = stands?.at ?? null;
      li.addEventListener('mouseenter', () => standing.mark(stands?.window, at, ''));
      li.addEventListener('focus', () => standing.mark(stands?.window, at, ''));
      li.addEventListener('mouseleave', () => standing.mark(null, null, ''));
      if (at === null) {
        li.appendChild(text('span', t(' — nothing on the screen stands for it'), 'trouble'));
      }
      next.appendChild(li);
    }
    aside.appendChild(next);

    if (situation.refused.length > 0) {
      aside.appendChild(document.createElement('hr'));
      aside.appendChild(text('h2', t('Refused operations')));
      const refusals = document.createElement('ul');
      refusals.className = 'refusals';
      situation.refused.forEach((r, i) => {
        const li = document.createElement('li');
        li.appendChild(text('div', say(r.step), 'step'));
        for (const sentence of r.said) {
          li.appendChild(text('div', sentence, 'why'));
        }
        const at = said.refused[i]?.at ?? null;
        li.addEventListener('mouseenter', () => standing.mark(said.refused[i]?.window, at, 'refused'));
        li.addEventListener('mouseleave', () => standing.mark(null, null, ''));
        li.addEventListener('click', () => open('r:' + situation.state + ':' + i));
        refusals.appendChild(li);
      });
      aside.appendChild(refusals);
    }
  }

  function card(number, step, figure, refused) {
    const div = document.createElement('div');
    div.className = 'card';
    div.title = step;
    div.appendChild(figure);
    const said = document.createElement('div');
    said.className = 'said-step' + (refused ? ' refused' : '');
    said.appendChild(text('span', String(number), 'num' + (refused ? ' refused' : '')));
    said.appendChild(text('span', say(step)));
    div.appendChild(said);
    return div;
  }

  // ---- What the change did, as test cases: added, removed, or with their expected result changed.

  function listChanges(said) {
    changed = said;
    rows = said.trouble ? [] : [
      ...said.changed.map((change, i) => ({ key: 'changed:' + i, route: change.route, kind: 'changed', said: t('Changed') })),
      ...said.appeared.map((appeared, i) => ({ key: 'appeared:' + i, route: appeared.route, kind: 'added', said: t('Added') })),
      ...said.gone.map((gone, i) => ({ key: 'gone:' + i, route: gone.route, kind: 'removed', said: t('Removed') })),
    ];
    if (tab === 'cases') {
      drawHead();
      drawList();
    }
  }

  // A heading of the list, which folds what is under it away and opens it again, remembered.
  const folded = new Set((vscode.getState() || {}).folded ?? []);
  function group(name, words) {
    const heading = document.createElement('div');
    heading.className = 'group';
    heading.tabIndex = 0;
    heading.setAttribute('role', 'button');
    heading.setAttribute('aria-expanded', String(!folded.has(name)));
    heading.appendChild(text('span', folded.has(name) ? '›' : '⌄', 'fold'));
    heading.appendChild(text('span', words));
    const toggle = () => {
      if (folded.has(name)) {
        folded.delete(name);
      } else {
        folded.add(name);
      }
      vscode.setState({ ...(vscode.getState() || {}), folded: [...folded] });
      drawList();
    };
    heading.addEventListener('click', toggle);
    heading.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); toggle(); } });
    return heading;
  }

  // What the change since the last commit did to a test case of the list, where it did something:
  // its expected result changed, or it is new — a situation the rules arrive at now, or a value
  // they refuse now. One the change took away is not in the list, and is listed under Changed alone.
  function touchedBy(c) {
    if (!changed || changed.trouble) {
      return null;
    }
    const route = c.route.join(' → ');
    if (c.refusal === null) {
      const appeared = changed.appeared.findIndex(a => a.route.join(' → ') === route);
      if (appeared >= 0) {
        return { kind: 'added', said: t('Added'), key: 'appeared:' + appeared };
      }
      const moved = changed.changed.findIndex(x => x.route.join(' → ') === route);
      return moved >= 0 ? { kind: 'changed', said: t('Changed'), key: 'changed:' + moved } : null;
    }
    const at = changed.changed.findIndex(x => x.route.join(' → ') === route);
    return at >= 0 && changed.changed[at].refusing.some(r => r.step === c.step) ? { kind: 'added', said: t('Added'), key: 'changed:' + at } : null;
  }

  function compare(key) {
    comparing = key;
    current = null;
    note = null;
    drawList();
    document.getElementById('detail').replaceChildren(text('p', t('Standing the window there, before and after…'), 'quiet'));
    vscode.postMessage({ type: 'compare', key });
  }

  function admitted(admits) {
    const bounds = list => list.map(b => b.bound + ' ' + b.value).join(', ');
    return t('{0}: {1} admits {2} — it admitted {3}', admits.input, admits.parameter, bounds(admits.now), bounds(admits.was));
  }

  function ended(ending) {
    return ending.ending ? (ending.result === null ? t('it ends, with no result') : t('it ends: {0}', ending.result)) : t('it does not end');
  }

  function showCompared(said) {
    const detail = document.getElementById('detail');
    if (!detail) {
      return;
    }
    detail.replaceChildren(zoomer());
    if (said.trouble) {
      detail.appendChild(text('p', said.trouble, 'trouble'));
      return;
    }

    const [kind, at] = said.key.split(':');
    const entry = kind === 'changed' ? changed.changed[at] : kind === 'gone' ? changed.gone[at] : changed.appeared[at];
    const row = rows.find(r => r.key === said.key);
    const title = text('h1', '');
    title.appendChild(badge(row.kind, row.said));
    title.appendChild(text('span', steps(entry.route, null)));
    detail.appendChild(title);
    detail.appendChild(text('p', kind === 'changed' ? t('The steps are the same; what the window should be after them is not.')
      : kind === 'gone' ? t('The rules do not arrive here by these steps now, so this test case is no longer one.')
      : t('The rules arrive here by these steps now, and did not, so this test case is new.'), 'quiet'));

    detail.appendChild(text('h2', t('Steps')));
    const inline = document.createElement('div');
    inline.className = 'inline-steps';
    entry.route.forEach((step, i) => {
      const s = document.createElement('div');
      s.className = 'said-step';
      s.title = step;
      s.appendChild(text('span', String(i + 1), 'num'));
      s.appendChild(text('span', say(step)));
      inline.appendChild(s);
    });
    detail.appendChild(entry.route.length === 0 ? text('p', t('None: the window as it starts.'), 'quiet') : inline);

    detail.appendChild(document.createElement('hr'));
    detail.appendChild(text('h2', t('Expected result')));
    const sides = document.createElement('div');
    sides.className = 'sides';
    const side = (heading, standing, absent, marks) => {
      const div = document.createElement('div');
      div.appendChild(text('h3', heading));
      if (absent !== null || !standing) {
        div.appendChild(text('p', absent ?? t('Not stood in.'), 'quiet'));
      } else if (standing.trouble) {
        div.appendChild(text('p', standing.trouble, 'trouble'));
      } else {
        for (const divergence of standing.divergences) {
          div.appendChild(text('p', divergence, 'trouble'));
        }
        const shown = figures(windowsOf(standing), heading);
        for (const m of marks) {
          const found = standing.moves?.find(x => x.step === m.split(' drawing ')[0]);
          if (found?.at) {
            shown.add(found.window, found.at, 'gained');
          }
        }
        div.appendChild(shown.element);
      }
      sides.appendChild(div);
      return div;
    };
    side(t('Before the change'), said.before, kind === 'appeared' ? t('The rules did not arrive here by these moves.') : null, []);
    side(t('After it'), said.after, kind === 'gone' ? t('The rules do not arrive here by these moves now.') : null, kind === 'changed' ? entry.gained.map(m => m.step) : []);
    detail.appendChild(sides);
    if (kind !== 'changed') {
      return;
    }

    detail.appendChild(document.createElement('hr'));
    detail.appendChild(text('h2', t('Difference')));
    const inputs = new Set([...entry.gained, ...entry.lost, ...entry.moved].map(m => m.step.split('(')[0]));
    for (const admits of changed.admits.filter(a => inputs.has(a.input))) {
      detail.appendChild(text('p', admitted(admits), 'quiet'));
    }
    const group = (heading, items) => {
      if (items.length === 0) {
        return;
      }
      const div = document.createElement('div');
      div.className = 'diff';
      div.appendChild(text('div', heading, 'quiet'));
      for (const item of items) {
        const line = document.createElement('div');
        line.className = 'line ' + item.kind;
        line.title = item.step ?? '';
        const what = text('span', item.words, 'what');
        for (const why of item.why ?? []) {
          what.appendChild(text('span', why, 'why'));
        }
        line.appendChild(what);
        line.appendChild(text('span', '›', 'go quiet'));
        div.appendChild(line);
      }
      detail.appendChild(div);
    };
    const was = named.get(entry.before);
    group(t('Can be done now, and could not'), entry.gained.map(m => ({ kind: 'gained', step: m.step, words: '+  ' + say(m.step) })));
    group(t('Could be done, and cannot now'), entry.lost.map(m => ({ kind: 'lost', step: m.step, words: '−  ' + say(m.step) })));
    group(t('Leads somewhere else now'), entry.moved.map(m => ({ kind: 'moved', step: m.step, words: '→  ' + say(m.step) })));
    group(t('Refused now, and was not'), entry.refusing.map(r => ({ kind: 'gained', step: r.step, words: '+  ' + say(r.step), why: r.said })));
    group(t('Not refused now, and was'), entry.notRefusing.map(r => ({ kind: 'lost', step: r.step, words: '−  ' + say(r.step), why: r.said })));
    if (entry.ending) {
      group(t('How it ends here'), [{ kind: 'moved', words: t('Now {0}; before, {1}.', ended(entry.ending.now), ended(entry.ending.was)) }]);
    }
    if (entry.truncated !== null) {
      group(t('How much was looked at here'), [{ kind: 'moved', words: entry.truncated
        ? t('Looking for what can be done here stopped at the limit now, so what is said about it is part of what there is.')
        : t('Looking for what can be done here no longer stops at the limit.') }]);
    }
  }

  // ---- The values chosen to try: where the walk cannot name every value — text — it follows
  // only what somebody chose. Each is written into the design's edits and the design derived again.

  function drawEdits() {
    main.replaceChildren();
    const page = document.createElement('div');
    page.className = 'edits-page';
    main.appendChild(page);
    if (note) {
      page.appendChild(text('p', note.text, note.ok ? 'note' : 'note trouble'));
    }

    page.appendChild(text('h2', t('Add')));
    const form = document.createElement('form');
    form.className = 'add';
    page.appendChild(form);
    const open = situations.filter(s => !s.ending && s.moves.some(m => Object.keys(m.admits).length > 0));
    const field = (label, control) => {
      const wrap = text('label', label);
      wrap.appendChild(control);
      form.appendChild(wrap);
      return control;
    };
    const scene = field(t('Situation'), document.createElement('select'));
    scene.className = 'scene';
    for (const s of open) {
      const option = text('option', steps(s.route, null));
      option.value = s.state;
      scene.appendChild(option);
    }
    const input = field(t('Operation'), document.createElement('select'));
    input.className = 'input';
    const boxes = document.createElement('span');
    boxes.style.display = 'contents';
    form.appendChild(boxes);
    let values = {};
    const fill = () => {
      const s = named.get(scene.value);
      input.replaceChildren();
      for (const move of (s?.moves ?? []).filter(m => Object.keys(m.admits).length > 0)) {
        if ([...input.options].some(o => o.value === move.input)) {
          continue;
        }
        const option = text('option', operation(move.step));
        option.value = move.input;
        input.appendChild(option);
      }
      params();
    };
    const params = () => {
      const s = named.get(scene.value);
      const move = s?.moves.find(m => m.input === input.value && Object.keys(m.admits).length > 0);
      boxes.replaceChildren();
      values = {};
      for (const [parameter, bounds] of Object.entries(move?.admits ?? {})) {
        const box = document.createElement('input');
        box.className = 'value';
        box.title = t('{0} admits {1}', parameter, bounds.map(b => b.bound + ' ' + b.value).join(', '));
        box.setAttribute('aria-label', t('A value to try for {0} of {1}', parameter, input.value));
        values[parameter] = box;
        const wrap = text('label', caption(input.value, parameter) ?? parameter);
        if (!caption(input.value, parameter)) {
          wrap.title = t('Nothing on the screen is written for this box, so it is said by its name in the rules.');
        }
        wrap.appendChild(box);
        boxes.appendChild(wrap);
      }
    };
    scene.addEventListener('change', fill);
    input.addEventListener('change', params);
    const go = text('button', t('Add'), 'act');
    go.type = 'submit';
    form.appendChild(go);
    form.addEventListener('submit', event => {
      event.preventDefault();
      if (!scene.value || !input.value) {
        return;
      }
      vscode.postMessage({ type: 'try', state: scene.value, input: input.value, args: Object.fromEntries(Object.entries(values).map(([p, box]) => [p, box.value])) });
      note = { ok: true, text: t('Writing it into the design, and deriving it again…') };
      drawEdits();
    });
    fill();

    const table = document.createElement('table');
    table.className = 'choices';
    const columns = document.createElement('colgroup');
    for (const name of ['scene', 'input', 'value', 'result', 'acts']) {
      const col = document.createElement('col');
      col.className = name;
      columns.appendChild(col);
    }
    table.appendChild(columns);
    const header = table.createTHead().insertRow();
    for (const heading of [t('Situation'), t('Operation'), t('Value'), t('Result'), '']) {
      header.appendChild(text('th', heading));
    }
    const body = table.createTBody();
    for (const choice of choices()) {
      const tr = body.insertRow();
      tr.appendChild(text('td', choice.situation ? steps(choice.situation.route, null) : choice.state));
      tr.title = choice.situation ? choice.situation.route.join(' → ') : choice.state;
      tr.appendChild(text('td', operation(choice.step)));
      const value = document.createElement('td');
      for (const [parameter, v] of Object.entries(choice.args)) {
        value.appendChild(said(choice.input, parameter));
        value.appendChild(text('span', v));
      }
      tr.appendChild(value);
      const result = document.createElement('td');
      result.appendChild(choice.carried === 'yes' ? badge('ended', t('Accepted'))
        : choice.carried === 'not legal here' ? badge('refused', t('Refused'))
        : badge('unreached', t('Not reached')));
      tr.appendChild(result);
      const acts = document.createElement('td');
      acts.className = 'acts';
      const edit = text('a', t('Edit'), 'link');
      edit.addEventListener('click', () => editRow(tr, value, choice));
      const out = text('a', t('Delete'), 'link');
      out.addEventListener('click', () => {
        vscode.postMessage({ type: 'takeOut', at: choice.at });
        note = { ok: true, text: t('Taking it out of the design, and deriving it again…') };
        drawEdits();
      });
      acts.append(edit, out);
      tr.appendChild(acts);
    }

    // Where a value is typed and none is chosen for it anywhere yet, the walk waits there: said,
    // where it first does, to be chosen.
    const valued = new Set(choices().map(c => c.input));
    for (const s of situations.slice().sort((a, b) => a.route.length - b.route.length)) {
      for (const move of s.moves.filter(m => m.to === null && !m.followed && !s.ending && Object.keys(m.admits).length > 0)) {
        if (valued.has(move.input)) {
          continue;
        }
        valued.add(move.input);
        const tr = body.insertRow();
        tr.appendChild(text('td', steps(s.route, null)));
        tr.appendChild(text('td', operation(move.step)));
        const value = document.createElement('td');
        for (const parameter of Object.keys(move.admits)) {
          value.appendChild(said(move.input, parameter));
        }
        tr.appendChild(value);
        const result = document.createElement('td');
        result.appendChild(badge('unreached', t('No value')));
        tr.appendChild(result);
        const acts = document.createElement('td');
        acts.className = 'acts';
        const add = text('a', t('Add'), 'link');
        add.addEventListener('click', () => {
          scene.value = s.state;
          fill();
          input.value = move.input;
          params();
          Object.values(values)[0]?.focus();
        });
        acts.appendChild(add);
        tr.appendChild(acts);
      }
    }
    page.appendChild(table);
  }

  // A box said by what the screen calls it; where nothing on the screen is written for it, by its
  // name in the rules, and said to be that.
  function said(input, parameter) {
    const words = caption(input, parameter);
    const span = text('span', words ?? parameter, 'param');
    span.title = words ? input + '.' + parameter : t('Nothing on the screen is written for this box, so it is said by its name in the rules.');
    return span;
  }

  // ---- How large the windows are drawn: on the page, for all at once, and one at a time, opened.

  function zoomer() {
    const bar = document.createElement('div');
    bar.className = 'zoom';
    const out = text('button', '−');
    out.title = t('Smaller');
    const shown = text('span', Math.round(zoom * 100) + '%');
    const into = text('button', '+');
    into.title = t('Larger');
    const set = next => {
      zoom = Math.min(4, Math.max(0.5, Math.round(next * 4) / 4));
      vscode.setState({ ...(vscode.getState() || {}), zoom });
      document.getElementById('detail').style.setProperty('--zoom', String(zoom));
      shown.textContent = Math.round(zoom * 100) + '%';
    };
    out.addEventListener('click', () => set(zoom - 0.25));
    into.addEventListener('click', () => set(zoom + 0.25));
    shown.title = t('How large the windows are drawn; a window clicked opens larger.');
    bar.append(out, shown, into);
    document.getElementById('detail').style.setProperty('--zoom', String(zoom));
    return bar;
  }

  const viewer = document.getElementById('viewer');
  const stage = document.getElementById('viewer-stage');
  let viewed = null;
  let viewScale = 1;

  function view(figure, size, title) {
    viewed = { figure, size };
    document.getElementById('viewer-title').textContent = title ?? '';
    viewer.hidden = false;
    fitView();
  }

  function drawView() {
    stage.replaceChildren();
    if (!viewed) {
      return;
    }
    const copy = viewed.figure.cloneNode(true);
    copy.style.width = (viewed.size.width * viewScale) + 'px';
    stage.appendChild(copy);
    document.getElementById('viewer-scale').textContent = Math.round(viewScale * 100) + '%';
  }

  function scaleView(next) {
    viewScale = Math.min(6, Math.max(0.1, next));
    drawView();
  }

  function fitView() {
    const room = { width: stage.clientWidth - 24, height: stage.clientHeight - 24 };
    viewScale = Math.min(room.width / viewed.size.width, room.height / viewed.size.height, 3);
    drawView();
  }

  document.getElementById('viewer-in').addEventListener('click', () => scaleView(viewScale * 1.25));
  document.getElementById('viewer-out').addEventListener('click', () => scaleView(viewScale / 1.25));
  document.getElementById('viewer-actual').addEventListener('click', () => scaleView(1));
  document.getElementById('viewer-fit').addEventListener('click', fitView);
  document.getElementById('viewer-close').addEventListener('click', () => { viewer.hidden = true; });
  stage.addEventListener('click', event => { if (event.target === stage) { viewer.hidden = true; } });
  window.addEventListener('keydown', event => {
    if (viewer.hidden) {
      return;
    }
    if (event.key === 'Escape') { viewer.hidden = true; }
    if (event.key === '+' || event.key === '=') { scaleView(viewScale * 1.25); }
    if (event.key === '-') { scaleView(viewScale / 1.25); }
    if (event.key === '0') { scaleView(1); }
  });
  stage.addEventListener('wheel', event => {
    if (!event.ctrlKey) {
      return;
    }
    event.preventDefault();
    scaleView(viewScale * (event.deltaY < 0 ? 1.1 : 1 / 1.1));
  }, { passive: false });
  let dragging = null;
  stage.addEventListener('mousedown', event => {
    dragging = { x: event.clientX, y: event.clientY, left: stage.scrollLeft, top: stage.scrollTop };
    stage.classList.add('dragging');
  });
  window.addEventListener('mousemove', event => {
    if (dragging) {
      stage.scrollLeft = dragging.left - (event.clientX - dragging.x);
      stage.scrollTop = dragging.top - (event.clientY - dragging.y);
    }
  });
  window.addEventListener('mouseup', () => { dragging = null; stage.classList.remove('dragging'); });

  function editRow(tr, cell, choice) {
    cell.replaceChildren();
    const boxes = {};
    for (const [parameter, v] of Object.entries(choice.args)) {
      cell.appendChild(said(choice.input, parameter));
      const box = document.createElement('input');
      box.className = 'value';
      box.value = v;
      box.setAttribute('aria-label', t('{0} of {1}', parameter, choice.step));
      boxes[parameter] = box;
      cell.appendChild(box);
    }
    const save = text('button', t('Change'), 'act');
    save.style.marginLeft = '8px';
    save.addEventListener('click', () => {
      vscode.postMessage({ type: 'change', at: choice.at, args: Object.fromEntries(Object.entries(boxes).map(([p, box]) => [p, box.value])) });
      note = { ok: true, text: t('Writing it into the design, and deriving it again…') };
      drawEdits();
    });
    cell.appendChild(save);
    Object.values(boxes)[0]?.focus();
  }

  // ---- Pictures: the window as the application drew itself, and a mark on a control of it.

  /** Every window shown where a situation stands: those the server named, or the one picture where it named none. */
  function windowsOf(said) {
    return said.windows && said.windows.length
      ? said.windows
      : [{ window: null, title: null, size: said.size, picture: said.picture }];
  }

  /**
   * The windows as figures, one under another, each under its title where there is more than one;
   * marked on the window a control is on.
   */
  function figures(windows, alt) {
    const element = document.createElement('div');
    element.className = 'shown';
    const drawn = windows.map(w => {
      const figure = picture(w.picture, w.size, null, windows.length > 1 ? (w.title || w.window || alt) : alt, '');
      if (windows.length > 1) {
        element.appendChild(text('div', w.title || w.window || '', 'caption'));
      }
      element.appendChild(figure);
      return { window: w.window, size: w.size, figure };
    });
    const on = window => drawn.find(d => d.window === window) || (drawn.length === 1 ? drawn[0] : null);
    return {
      element,
      mark(window, at, kind) {
        drawn.forEach(d => mark(d.figure, d.size, null, ''));
        const there = on(window);
        if (there && at) {
          mark(there.figure, there.size, at, kind);
        }
      },
      add(window, at, kind) {
        const there = on(window);
        if (there) {
          there.figure.appendChild(marker(there.size, at, kind));
        }
      },
    };
  }

  function mark(figure, size, at, kind) {
    figure.querySelector('.mark')?.remove();
    if (at) {
      figure.appendChild(marker(size, at, kind));
    }
  }

  function picture(src, size, at, alt, kind) {
    const figure = document.createElement('figure');
    const img = document.createElement('img');
    img.src = src;
    img.alt = alt;
    figure.appendChild(img);
    if (at) {
      figure.appendChild(marker(size, at, kind));
    }
    figure.title = t('Click to open it larger');
    figure.addEventListener('click', () => view(figure, size, alt || document.querySelector('#detail h1')?.textContent));
    return figure;
  }

  function marker(size, at, kind) {
    const mark = document.createElement('div');
    mark.className = 'mark' + (kind ? ' ' + kind : '');
    mark.style.left = (at.x / size.width * 100) + '%';
    mark.style.top = (at.y / size.height * 100) + '%';
    mark.style.width = (at.width / size.width * 100) + '%';
    mark.style.height = (at.height / size.height * 100) + '%';
    return mark;
  }

  draw();

  window.addEventListener('message', event => {
    const said = event.data;
    switch (said.type) {
      case 'unbuilt':
        document.getElementById('unbuilt').hidden = !said.text;
        document.getElementById('unbuilt-text').textContent = said.text ?? '';
        document.getElementById('unbuilt-said').textContent = said.said ?? '';
        break;
      case 'situations':
        listed(said);
        break;
      case 'standing':
        // Deriving numbers the states again, so the one stood in may be asked for by a new name.
        chosen = said.state;
        comparing = null;
        lastShown = null;
        if (!cases.some(c => c.key === current && c.state === chosen)) {
          current = reaching(chosen)?.key ?? null;
        }
        if (tab === 'cases') {
          drawList();
          document.getElementById('detail')?.replaceChildren(text('p', t('Standing the window there…'), 'quiet'));
        }
        break;
      case 'tried':
        note = said;
        if (tab === 'edits') {
          drawEdits();
        }
        break;
      case 'shown':
        if (comparing === null && (said.state === chosen || chosen === null)) {
          show(said);
        }
        break;
      case 'changes':
        listChanges(said);
        break;
      case 'comparing':
        if (said.key === comparing) {
          document.getElementById('detail')?.replaceChildren(text('p', said.text, 'quiet'));
        }
        break;
      case 'compared':
        if (said.key === comparing) {
          showCompared(said);
        }
        break;
    }
  });
</script>
</body>
</html>`;
}
