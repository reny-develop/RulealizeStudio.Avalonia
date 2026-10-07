// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import { ChildProcess, spawn } from 'child_process';
import { createHash } from 'crypto';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import { build, buildApart, onUnbuilt, unbuilt } from './built';
import { installed } from './installed';
import { pluginsOf } from './ruleSet';
import { html, sayer } from './words';

/** A control placed on the window, as the designer drew it: which element it is, what holds it, its words, and where it is. */
export interface PlacedControl {
    id: number;
    control: string;
    type: string;
    parent: number | null;
    /** Whether this is a part of its control that holds one, a split view's pane say, and whether that is written yet. */
    part: boolean;
    written: boolean;
    words: string | null;
    sayable: boolean;
    holds: 'many' | 'one' | 'none';
    full: boolean;
    box: { x: number; y: number; width: number; height: number } | null;
}

/**
 * Where something is to go: under a point of the window — for something moved, with where in it it
 * was taken hold of — or into a placed control before one of what it holds.
 */
type Where = { x: number; y: number; dx?: number; dy?: number } | { into: number; index?: number };

/** What the page asks for. */
export type Asked =
    | { type: 'at'; x: number; y: number }
    | ({ type: 'place'; control: string } & Where)
    | ({ type: 'move'; at: number } & Where)
    | { type: 'remove'; at: number }
    | { type: 'size'; at: number; width: number; height: number }
    | { type: 'properties'; at: number }
    | { type: 'set'; at: number; name: string; value: string | null }
    | { type: 'words'; at: number; words: string }
    | { type: 'show'; at: number | null }
    | { type: 'text' };

/**
 * An application's screen, opened in the Studio's own editor and designed without its XAML being
 * read: the window drawn by Avalonia from its XAML — `MainWindow.axaml` or any other — as it is written now — the application
 * drawing itself, by `rulealize-studio design`, never a picture drawn here — with the controls its
 * build loads to place on it, what is on it, and the words of the one chosen.
 *
 * The text is the document. Each change asked for here is one the designer writes as the smallest
 * edit to it, put in place as one edit — so the editor's undo and git see what changed and nothing
 * else — and saved; the window is drawn again from the text whenever it changes, whoever changed
 * it. Nothing here reads XAML, and nothing placed here is bound: what a control stands for is the
 * specification's to say and the agent's to bind once there are rules.
 */
export class Screen {
    static readonly viewType = 'rulealize.screen';

    /** Settles once the window has been drawn the first time, or why it cannot be has been said. */
    readonly ready: Promise<void>;

    private readonly told = new Map<string, unknown>();
    private designer: Designer | undefined;
    private drawing: Promise<void> | undefined;
    private again = false;
    private selecting: number | undefined;
    /** The control chosen on the page, which the window is drawn showing where a list of items shows one at a time. */
    private shown: number | undefined;
    private busy = 0;
    private disposed = false;
    private rebuilding: NodeJS.Timeout | undefined;

    constructor(
        private readonly server: string,
        readonly document: vscode.TextDocument,
        private readonly panel: vscode.WebviewPanel,
    ) {
        panel.webview.options = { enableScripts: true };
        panel.webview.html = page(nonce(), sayer());

        const folder = path.dirname(document.uri.fsPath);
        const built = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(pluginsOf(document.uri.fsPath), `${projectName(folder) ?? '*'}.dll`));
        const sources = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(folder, '*.{json,csproj}'));
        const listening = [
            panel.webview.onDidReceiveMessage((asked: Asked) => void this.answer(asked)),
            vscode.workspace.onDidChangeTextDocument(change => {
                if (change.document === document && change.contentChanges.length > 0 && this.busy === 0) {
                    void this.draw();
                }
            }),
            // Built again — once there are rules, with a model the screen binds — the window is
            // drawn with what the build wrote now.
            built,
            built.onDidChange(() => void this.restart()),
            built.onDidCreate(() => void this.restart()),
            // The rules or the project written again while the screen is open, the folder is built
            // again, and what that build writes draws the window again.
            sources,
            sources.onDidChange(() => void this.rebuild()),
            sources.onDidCreate(() => void this.rebuild()),
            onUnbuilt(() => this.sayUnbuilt()),
        ];
        panel.onDidDispose(() => {
            this.disposed = true;
            listening.forEach(l => l.dispose());
            this.designer?.stop();
        });

        this.ready = this.start();
    }

    /** The last message of a kind the page was sent. */
    said(type: string): unknown {
        return this.told.get(type);
    }

    /** Does what the page asked, and settles once the window has been drawn again. */
    async answer(asked: Asked): Promise<void> {
        await this.ready;
        const designer = this.designer;
        if (!designer) {
            return;
        }

        switch (asked.type) {
            case 'at': {
                const found = await designer.ask({ op: 'at', text: this.document.getText(), x: asked.x, y: asked.y });
                void this.post({ type: 'select', id: found.at ?? null });
                return;
            }

            case 'text':
                await vscode.window.showTextDocument(this.document, { viewColumn: vscode.ViewColumn.Beside });
                return;

            case 'place': {
                const { type: _, control, ...where } = asked;
                return this.edit({ op: 'place', type: control, ...where });
            }

            case 'move': {
                const { type: _, ...rest } = asked;
                return this.edit({ op: 'move', ...rest });
            }

            case 'remove':
                return this.edit({ op: 'remove', at: asked.at });

            case 'size':
                return this.edit({ op: 'size', at: asked.at, width: asked.width, height: asked.height });

            case 'properties': {
                const said = await designer.ask({ op: 'properties', text: this.document.getText(), at: asked.at });
                void this.post({ type: 'properties', at: asked.at, properties: said.properties ?? [] });
                return;
            }

            case 'set':
                return this.edit({ op: 'set', at: asked.at, name: asked.name, value: asked.value });

            case 'words':
                return this.edit({ op: 'words', at: asked.at, words: asked.words });

            case 'show': {
                const at = asked.at ?? undefined;
                if (at === this.shown) {
                    return;
                }

                this.shown = at;
                if (at !== undefined && (await designer.ask({ op: 'show', text: this.document.getText(), at })).shown) {
                    await this.draw();
                }
                return;
            }
        }
    }

    /** Starts the designer on the application's build, tells the page what can be placed, and draws the window. */
    private async start(): Promise<void> {
        const plugins = await this.output();
        if (typeof plugins !== 'string') {
            void this.post({ type: 'trouble', text: plugins.trouble });
            return;
        }

        const dotnet = await installed().runtime() ?? 'dotnet';
        this.designer = new Designer(dotnet, this.server, path.dirname(this.document.uri.fsPath), plugins, why => {
            if (!this.disposed) {
                void this.post({ type: 'trouble', text: vscode.l10n.t('The screen is not drawn any more: {0}', why) });
            }
        });
        const controls = await this.designer.ask({ op: 'controls' });
        void this.post({ type: 'controls', controls: controls.controls ?? [] });
        this.sayUnbuilt();
        await this.draw();
    }

    /** Says above the window, for as long as it is so, that it is drawn with the application as last built, since it does not build as written now, and what the build said. */
    private sayUnbuilt(): void {
        const folder = path.dirname(this.document.uri.fsPath);
        const shown = unbuilt(folder);
        void this.post({
            type: 'unbuilt',
            text: shown ? vscode.l10n.t('This is {0} as it was last built, at {1}: it does not build as it is written now. What the build said:', path.basename(folder), shown.built.toLocaleString(vscode.env.language)) : null,
            said: shown?.said ?? null,
        });
    }

    /**
     * Where the designer reads the application's build from: the folder's own build output where it
     * was built, and otherwise a folder of the screen's own, built there — never the folder's own
     * output, so that its first build is the one made once there are rules.
     */
    private async output(): Promise<string | { trouble: string }> {
        const folder = path.dirname(this.document.uri.fsPath);
        const own = pluginsOf(this.document.uri.fsPath);
        const project = projectName(folder);
        if (project && fs.existsSync(path.join(own, `${project}.dll`))) {
            // Built before what it is built from was last written — by an agent, say — it is built
            // again first, so that the screen is drawn with the model the rules give now.
            await build(folder, path.relative(folder, own), vscode.l10n.t('Drawing the screen'));
            return own;
        }

        const key = process.platform === 'win32' ? path.resolve(folder).toLowerCase() : path.resolve(folder);
        const apart = path.join(os.tmpdir(), 'rulealize-screen', createHash('sha256').update(key).digest('hex').slice(0, 8));
        const trouble = await buildApart(folder, apart, vscode.l10n.t('Drawing the screen'));
        return trouble ? { trouble } : apart;
    }

    /** Builds the folder again where it was built and what it is built from is newer; the build, once written, starts the designer again. */
    private rebuild(): void {
        clearTimeout(this.rebuilding);
        this.rebuilding = setTimeout(() => {
            const folder = path.dirname(this.document.uri.fsPath);
            const own = pluginsOf(this.document.uri.fsPath);
            const project = projectName(folder);
            if (!this.disposed && project && fs.existsSync(path.join(own, `${project}.dll`))) {
                void build(folder, path.relative(folder, own), vscode.l10n.t('Drawing the screen'));
            }
        }, 1000);
    }

    /** Starts the designer again on what the application's own build wrote now, and draws the window with it. */
    private async restart(): Promise<void> {
        await this.ready;
        if (this.disposed) {
            return;
        }

        this.designer?.stop();
        this.designer = undefined;
        await this.start();
    }

    /**
     * Draws the window from the text as it is now. Asked again while drawing, it draws once more
     * when that is done, from the text as it is then, so that the last change is always drawn and
     * none is waited for twice.
     */
    private draw(): Promise<void> {
        if (this.drawing) {
            this.again = true;
            return this.drawing;
        }

        this.drawing = (async () => {
            do {
                this.again = false;
                const designer = this.designer;
                if (!designer) {
                    return;
                }

                const version = this.document.version;
                const drawn = await designer.ask({ op: 'draw', text: this.document.getText(), show: this.selecting ?? this.shown });
                if (drawn.picture === undefined) {
                    void this.post({
                        type: 'trouble',
                        text: vscode.l10n.t('The window cannot be drawn from the screen as it is written now: {0}', drawn.trouble ?? ''),
                        placed: drawn.placed ?? [],
                    });
                    continue;
                }

                void this.post({
                    type: 'drawn',
                    picture: `data:image/png;base64,${drawn.picture}`,
                    size: drawn.size,
                    placed: drawn.placed,
                    select: this.selecting ?? null,
                    version,
                });
                this.selecting = undefined;
            } while (this.again && !this.disposed);
        })().finally(() => {
            this.drawing = undefined;
        });
        return this.drawing;
    }

    /** Has the designer make a change, puts the smallest edit that makes it in place, saves, and settles once it is drawn. */
    private async edit(asked: Record<string, unknown>): Promise<void> {
        const made = await this.designer!.ask({ ...asked, text: this.document.getText() });
        if (made.refused !== undefined) {
            void this.post({ type: 'refused', message: refusal(made.refused) });
            return;
        }

        if (made.text === undefined) {
            void this.post({ type: 'refused', message: made.trouble ?? '' });
            return;
        }

        this.selecting = made.select;
        this.shown = made.select;
        const before = this.document.getText();
        if (made.text !== before) {
            const [start, end, inserted] = difference(before, made.text);
            const edit = new vscode.WorkspaceEdit();
            edit.replace(this.document.uri, new vscode.Range(this.document.positionAt(start), this.document.positionAt(end)), inserted);
            this.busy++;
            try {
                await vscode.workspace.applyEdit(edit);
                await this.document.save();
            } finally {
                this.busy--;
            }
        }

        await this.draw();
        if (this.drawing) {
            await this.drawing;
        }
    }

    private post(message: { type: string; [said: string]: unknown }): Thenable<boolean> {
        this.told.set(message.type, message);
        return this.disposed ? Promise.resolve(false) : this.panel.webview.postMessage(message);
    }
}

/** Opens every window's XAML — any `.axaml` — as {@link Screen}, and keeps every one open. */
export class ScreenProvider implements vscode.CustomTextEditorProvider {
    private readonly open = new Map<vscode.WebviewPanel, Screen>();

    constructor(private readonly server: string, private readonly started: Promise<void>) {}

    static register(context: vscode.ExtensionContext, provider: ScreenProvider): void {
        context.subscriptions.push(vscode.window.registerCustomEditorProvider(Screen.viewType, provider, {
            webviewOptions: { retainContextWhenHidden: true },
        }));
    }

    async resolveCustomTextEditor(document: vscode.TextDocument, panel: vscode.WebviewPanel): Promise<void> {
        await this.started.catch(() => undefined);
        const screen = new Screen(this.server, document, panel);
        this.open.set(panel, screen);
        panel.onDidDispose(() => this.open.delete(panel));
    }

    /** The screen open in this editor on this file, if one is. */
    opened(uri: vscode.Uri): Screen | undefined {
        return [...this.open.values()].find(s => s.document.uri.toString() === uri.toString());
    }
}

/** What the designer answers: a line of JSON, of which each request reads its own part. */
interface Answer {
    trouble?: string;
    refused?: string;
    controls?: { type: string; name: string }[];
    picture?: string;
    size?: { width: number; height: number };
    placed?: PlacedControl[];
    at?: number | null;
    text?: string;
    select?: number;
    shown?: boolean;
    properties?: { name: string; kind: string; choices: string[] | null; written: string | null; bound: boolean; now: string | null }[];
}

/**
 * `rulealize-studio design`, running for as long as a screen is open: asked a line of JSON at a
 * time, and answering each in turn. Where it stops, every question still waiting is answered with
 * why, and so is every one after.
 */
class Designer {
    private readonly process: ChildProcess;
    private readonly waiting: ((answer: Answer) => void)[] = [];
    private read = '';
    private said = '';
    private stopped: string | undefined;

    constructor(dotnet: string, server: string, folder: string, plugins: string, ended: (why: string) => void) {
        this.process = spawn(dotnet, [server, 'design', folder, '--plugins', plugins], { cwd: folder, env: installed().env() });
        this.process.stdout!.setEncoding('utf8');
        this.process.stdout!.on('data', (data: string) => {
            this.read += data;
            for (let end = this.read.indexOf('\n'); end >= 0; end = this.read.indexOf('\n')) {
                const line = this.read.slice(0, end).trim();
                this.read = this.read.slice(end + 1);
                if (line) {
                    this.waiting.shift()?.(JSON.parse(line) as Answer);
                }
            }
        });
        this.process.stderr!.setEncoding('utf8');
        this.process.stderr!.on('data', (data: string) => this.said += data);
        const end = (why: string) => {
            if (this.stopped !== undefined) {
                return;
            }

            this.stopped = why;
            this.waiting.splice(0).forEach(answer => answer({ trouble: why }));
            ended(why);
        };
        this.process.on('error', wrong => end(wrong.message));
        this.process.on('exit', code => end(this.said.trim() || `exit ${code}`));
    }

    ask(asked: Record<string, unknown>): Promise<Answer> {
        if (this.stopped !== undefined) {
            return Promise.resolve({ trouble: this.stopped });
        }

        return new Promise(resolve => {
            this.waiting.push(resolve);
            this.process.stdin!.write(`${JSON.stringify(asked)}\n`);
        });
    }

    stop(): void {
        this.stopped ??= '';
        this.waiting.splice(0).forEach(answer => answer({ trouble: '' }));
        this.process.stdin?.end();
        this.process.kill();
    }
}

/** The one span two texts differ in: where it starts and ends in the first, and what is in the second there. */
function difference(before: string, after: string): [number, number, string] {
    let start = 0;
    while (start < before.length && start < after.length && before[start] === after[start]) {
        start++;
    }

    let end = 0;
    while (end < before.length - start && end < after.length - start && before[before.length - 1 - end] === after[after.length - 1 - end]) {
        end++;
    }

    return [start, before.length - end, after.slice(start, after.length - end)];
}

function refusal(why: string): string {
    switch (why) {
        case 'nowhere': return vscode.l10n.t('Nothing there can hold it. Place a panel first, and put it in the panel.');
        case 'full': return vscode.l10n.t('That holds one thing, and has it already. Put a panel in it to hold more than one.');
        case 'listed': return vscode.l10n.t('Its items are given by a binding, so none is put in it here.');
        case 'part': return vscode.l10n.t('This is a part of the control that holds it, and goes with it. Put a control in it, or delete what is in it.');
        case 'holdsNothing': return vscode.l10n.t('That holds no other control.');
        case 'inside': return vscode.l10n.t('Something cannot be put inside itself.');
        case 'window': return vscode.l10n.t('The window itself stays where it is.');
        case 'wordless': return vscode.l10n.t('This has no words to give.');
        case 'value': return vscode.l10n.t('That is not a value this property takes.');
        case 'bound': return vscode.l10n.t('This is bound to the rules: it is given by them, not here.');
        case 'noProperty': return vscode.l10n.t('This has no such property.');
        case 'unknown': return vscode.l10n.t('That is not among the controls this application\'s build loads.');
        default: return vscode.l10n.t('That is not on the window any more.');
    }
}

function projectName(folder: string): string | undefined {
    const name = fs.existsSync(folder) ? fs.readdirSync(folder).find(n => n.endsWith('.csproj')) : undefined;
    return name && path.basename(name, '.csproj');
}

function nonce(): string {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    return Array.from({ length: 32 }, () => chars[Math.floor(Math.random() * chars.length)]).join('');
}

function page(nonce: string, said: string): string {
    return `<!DOCTYPE html>
<html lang="${html(vscode.env.language)}">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<style nonce="${nonce}">
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); margin: 0; padding: 0 12px 24px; }
  #body { display: flex; gap: 16px; align-items: flex-start; margin-top: 12px; }
  #controls { width: 200px; flex: none; position: sticky; top: 12px; }
  #window { flex: 1; min-width: 0; }
  #tree { width: 240px; flex: none; position: sticky; top: 12px; max-height: calc(100vh - 24px); overflow: auto; }
  #side { width: 300px; flex: none; position: sticky; top: 12px; max-height: calc(100vh - 24px); overflow: auto; }
  @media (max-width: 1100px) { #body { flex-wrap: wrap; } #tree, #side { position: static; max-height: none; } }
  h3 { font-size: 1em; font-weight: 600; margin: 0 0 6px; }
  .hint { color: var(--vscode-descriptionForeground); font-size: 0.9em; margin: 4px 0 8px; }
  input[type=text] { width: 100%; box-sizing: border-box; background: var(--vscode-input-background);
    color: var(--vscode-input-foreground); border: 1px solid var(--vscode-input-border, transparent); padding: 3px 5px; font: inherit; }
  #palette { list-style: none; margin: 6px 0 0; padding: 0; max-height: 70vh; overflow: auto; }
  #palette li { padding: 3px 6px; cursor: grab; border-radius: 3px; }
  #palette li:hover { background: var(--vscode-list-hoverBackground); }
  #caption { font-size: 0.9em; color: var(--vscode-descriptionForeground); margin-bottom: 4px; }
  #frame { position: relative; display: inline-block; border: 1px solid var(--vscode-panel-border); line-height: 0; }
  #frame img { display: block; max-width: 100%; }
  #frame.dropping { outline: 2px dashed var(--vscode-focusBorder); }
  .box { position: absolute; pointer-events: none; box-sizing: border-box; }
  #selected { border: 2px solid var(--vscode-focusBorder); pointer-events: auto; cursor: move; background: transparent; }
  #hover { border: 1px dashed var(--vscode-focusBorder); }
  #grip { position: absolute; width: 9px; height: 9px; box-sizing: border-box; background: var(--vscode-focusBorder);
    border: 1px solid var(--vscode-editor-background); cursor: nwse-resize; }
  #trouble { color: var(--vscode-errorForeground); white-space: pre-wrap; }
  .unbuilt { border: 1px solid var(--vscode-inputValidation-warningBorder, #b89500); background: var(--vscode-inputValidation-warningBackground, #352a05);
    padding: 8px 12px; margin: 0 0 8px; }
  .unbuilt strong { display: block; margin-bottom: 4px; }
  .unbuilt pre { margin: 0; white-space: pre-wrap; font-family: var(--vscode-editor-font-family); font-size: 0.9em; max-height: 12em; overflow: auto; }
  #refused { color: var(--vscode-errorForeground); }
  #trouble:empty, #refused:empty { display: none; }
  #outline { list-style: none; margin: 0 0 12px; padding: 0; }
  #outline li { padding: 2px 4px; cursor: pointer; border-radius: 3px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  #outline li.on { background: var(--vscode-list-activeSelectionBackground); color: var(--vscode-list-activeSelectionForeground); }
  #outline li:not(.on):hover { background: var(--vscode-list-hoverBackground); }
  #outline li.target { outline: 2px dashed var(--vscode-focusBorder); outline-offset: -2px; }
  #outline .said { color: var(--vscode-descriptionForeground); }
  #outline li.part { font-style: italic; }
  #outline li.part.empty:not(.on) { color: var(--vscode-descriptionForeground); }
  #outline li.on .said { color: inherit; }
  label { display: block; margin: 8px 0 2px; color: var(--vscode-descriptionForeground); }
  button { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); border: none;
    padding: 4px 10px; margin: 8px 4px 0 0; cursor: pointer; font: inherit; }
  button:disabled { opacity: 0.5; cursor: default; }
  #text { margin-top: 16px; }
  #props { margin: 12px 0 16px; }
  #props .prop { display: grid; grid-template-columns: 9em 1fr 1.6em; gap: 4px; align-items: center; margin: 2px 0; }
  #props .prop .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--vscode-descriptionForeground); }
  #props .prop.set .name { color: var(--vscode-foreground); font-weight: 600; }
  #props select, #props input { width: 100%; box-sizing: border-box; background: var(--vscode-input-background);
    color: var(--vscode-input-foreground); border: 1px solid var(--vscode-input-border, transparent); padding: 2px 4px; font: inherit; }
  #props .colour { display: grid; grid-template-columns: 2.2em 1fr; gap: 4px; }
  #props input[type=color] { padding: 0; height: 1.8em; }
  #props button { margin: 0; padding: 0 4px; }
  #props .bound { color: var(--vscode-descriptionForeground); font-style: italic; }
</style>
</head>
<body>
<div id="unbuilt" class="unbuilt" hidden><strong id="unbuilt-text"></strong><pre id="unbuilt-said"></pre></div>
<div id="body">
  <div id="controls">
    <h3 id="controls-title"></h3>
    <input type="text" id="filter">
    <p class="hint" id="controls-hint"></p>
    <ul id="palette"></ul>
  </div>
  <div id="window">
    <p id="trouble"></p>
    <div id="caption"></div>
    <div id="frame"><img id="picture" alt=""><div class="box" id="hover" hidden></div><div class="box" id="selected" draggable="true" hidden></div><div id="grip" hidden></div></div>
    <p id="refused"></p>
  </div>
  <div id="tree">
    <h3 id="outline-title"></h3>
    <p class="hint" id="outline-hint"></p>
    <ul id="outline"></ul>
    <button id="text"></button>
  </div>
  <div id="side">
    <div id="chosen"></div>
    <div id="props"></div>
  </div>
</div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  ${said}
  let controls = [];
  let placed = [];
  let size = null;
  let selected = (vscode.getState() || {}).selected ?? null;

  document.getElementById('controls-title').textContent = t('Controls');
  document.getElementById('controls-hint').textContent = t('Drag one onto the window, or click it to put it in what is chosen.');
  document.getElementById('filter').placeholder = t('Find a control');
  document.getElementById('outline-title').textContent = t('On the window');
  document.getElementById('outline-hint').textContent = t('Drag one onto another, here or on the window, to put it in it.');
  document.getElementById('text').textContent = t('Show XAML');
  document.getElementById('text').addEventListener('click', () => vscode.postMessage({ type: 'text' }));
  document.getElementById('filter').addEventListener('input', palette);

  const frame = document.getElementById('frame');
  const picture = document.getElementById('picture');
  const selectedBox = document.getElementById('selected');
  const hoverBox = document.getElementById('hover');
  const grip = document.getElementById('grip');
  grip.title = t('Drag to change its size');
  let sizing = null;
  // The properties of the control chosen, as the designer listed them, and what they are filtered by.
  let properties = null;
  let askedFor = null;
  let propertyFilter = '';

  function scale() {
    return size && picture.clientWidth ? picture.clientWidth / size.width : 1;
  }

  function pointOf(event) {
    const bounds = picture.getBoundingClientRect();
    const by = scale();
    return { x: (event.clientX - bounds.left) / by, y: (event.clientY - bounds.top) / by };
  }

  function ask(message) {
    document.getElementById('refused').textContent = '';
    vscode.postMessage(message);
  }

  function find(id) {
    return placed.find(p => p.id === id);
  }

  function select(id) {
    selected = id;
    vscode.setState({ selected });
    vscode.postMessage({ type: 'show', at: id });
    draw();
  }

  function palette() {
    const list = document.getElementById('palette');
    const wanted = document.getElementById('filter').value.trim().toLowerCase();
    list.replaceChildren();
    for (const c of controls.filter(c => !wanted || c.name.toLowerCase().includes(wanted))) {
      const item = document.createElement('li');
      item.textContent = c.name;
      item.title = c.type;
      item.draggable = true;
      item.dataset.type = c.type;
      item.addEventListener('dragstart', event => event.dataTransfer.setData('text/plain', 'control:' + c.type));
      item.addEventListener('click', () => {
        const where = whereIn(find(selected) ?? find(0));
        if (where) { ask({ type: 'place', control: c.type, ...where }); }
      });
      list.appendChild(item);
    }
  }

  /** Where something put in or on a placed control goes: into it where it can hold it, and otherwise after it, in what holds it. */
  function whereIn(into) {
    if (!into) { return null; }
    if (into.holds === 'many' || (into.holds === 'one' && !into.full)) { return { into: into.id }; }
    if (into.parent === null || into.part) { return null; }
    const siblings = placed.filter(p => p.parent === into.parent && !p.part);
    return { into: into.parent, index: siblings.indexOf(into) + 1 };
  }

  function place(box, at) {
    if (!at || !size) { box.hidden = true; return; }
    const by = scale();
    box.hidden = false;
    box.style.left = (at.x * by) + 'px';
    box.style.top = (at.y * by) + 'px';
    box.style.width = Math.max(at.width * by, 4) + 'px';
    box.style.height = Math.max(at.height * by, 4) + 'px';
  }

  /** The corner a chosen control is sized by, at the bottom right of it. */
  function putGrip(at) {
    if (!at || !size) { grip.hidden = true; return; }
    const by = scale();
    grip.hidden = false;
    grip.style.left = ((at.x + at.width) * by - 5) + 'px';
    grip.style.top = ((at.y + at.height) * by - 5) + 'px';
  }

  function draw() {
    const root = find(0);
    document.getElementById('caption').textContent = root && root.words ? root.words : '';
    const chosen = find(selected);
    place(selectedBox, chosen && chosen.id !== 0 ? chosen.box : null);
    putGrip(chosen && chosen.id !== 0 ? chosen.box : null);

    const outline = document.getElementById('outline');
    outline.replaceChildren();
    const depth = p => p.parent === null ? 0 : 1 + depth(find(p.parent));
    for (const p of placed) {
      const item = document.createElement('li');
      item.style.paddingLeft = (4 + depth(p) * 14) + 'px';
      item.textContent = p.control;
      item.classList.toggle('part', p.part);
      item.classList.toggle('empty', p.part && !p.written);
      if (p.part) { item.title = t('A part of {0} that holds one control', find(p.parent).control); }
      if (p.words) {
        const words = document.createElement('span');
        words.className = 'said';
        words.textContent = ' ' + p.words;
        item.appendChild(words);
      }
      item.classList.toggle('on', p.id === selected);
      item.draggable = p.id !== 0 && !p.part;
      item.addEventListener('dragstart', event => event.dataTransfer.setData('text/plain', 'move:' + p.id));
      // Dropped on a line of the list, a control goes into what that line is where it can hold it, and after it otherwise.
      item.addEventListener('dragover', event => { event.preventDefault(); item.classList.add('target'); });
      item.addEventListener('dragleave', () => item.classList.remove('target'));
      item.addEventListener('drop', event => {
        event.preventDefault();
        event.stopPropagation();
        item.classList.remove('target');
        const said = event.dataTransfer.getData('text/plain');
        const where = whereIn(p);
        if (!where) { return; }
        if (said.startsWith('control:')) {
          ask({ type: 'place', control: said.slice('control:'.length), ...where });
        } else if (said.startsWith('move:')) {
          const id = Number(said.slice('move:'.length).split(':')[0]);
          if (id !== p.id) { ask({ type: 'move', at: id, ...where }); }
        }
      });
      item.addEventListener('click', () => select(p.id));
      outline.appendChild(item);
    }

    inspect(chosen);
    if (!chosen) {
      document.getElementById('props').replaceChildren();
      properties = null;
      askedFor = null;
    } else if ((!properties || properties.at !== chosen.id) && askedFor !== chosen.id) {
      askedFor = chosen.id;
      vscode.postMessage({ type: 'properties', at: chosen.id });
    }
  }

  /** A colour as the colour box takes it: six hex digits, from the colour written or drawn. */
  function boxColour(said) {
    if (!said || said.charAt(0) !== '#') { return '#000000'; }
    const hex = said.slice(1);
    return '#' + (hex.length === 8 ? hex.slice(2) : hex.length === 6 ? hex : '000000');
  }

  /** Every property of the control chosen that XAML gives as text, each with what it is now and a way to change it. */
  function showProperties() {
    const box = document.getElementById('props');
    const focused = document.activeElement && document.activeElement.id === 'property-filter';
    box.replaceChildren();
    if (!properties || properties.at !== selected || properties.list.length === 0) { return; }
    const at = properties.at;
    const head = document.createElement('h3');
    head.textContent = t('Properties');
    box.appendChild(head);
    const filter = document.createElement('input');
    filter.type = 'text';
    filter.id = 'property-filter';
    filter.placeholder = t('Find a property');
    filter.value = propertyFilter;
    filter.addEventListener('input', () => { propertyFilter = filter.value; showProperties(); });
    box.appendChild(filter);
    if (focused) { filter.focus(); filter.setSelectionRange(filter.value.length, filter.value.length); }

    const wanted = propertyFilter.trim().toLowerCase();
    const shown = properties.list
      .filter(p => !wanted || p.name.toLowerCase().includes(wanted))
      .sort((a, b) => (b.written !== null && b.written !== undefined) - (a.written !== null && a.written !== undefined) || a.name.localeCompare(b.name));
    const give = (name, value) => ask({ type: 'set', at, name, value: value === '' ? null : value });
    for (const p of shown) {
      const written = p.written !== null && p.written !== undefined;
      const row = document.createElement('div');
      row.className = 'prop' + (written ? ' set' : '');
      const name = document.createElement('span');
      name.className = 'name';
      name.textContent = p.name;
      name.title = p.name;
      row.appendChild(name);

      let editor;
      if (p.bound) {
        editor = document.createElement('span');
        editor.className = 'bound';
        editor.textContent = t('bound to the rules');
      } else if (p.kind === 'bool' || p.kind === 'choice') {
        editor = document.createElement('select');
        const options = p.kind === 'bool' ? ['True', 'False'] : p.choices;
        const none = document.createElement('option');
        none.value = '';
        none.textContent = p.now ? '(' + p.now + ')' : '';
        editor.appendChild(none);
        for (const choice of options) {
          const option = document.createElement('option');
          option.value = choice;
          option.textContent = choice;
          option.selected = p.written === choice;
          editor.appendChild(option);
        }
        editor.addEventListener('change', () => give(p.name, editor.value));
      } else if (p.kind === 'color') {
        editor = document.createElement('div');
        editor.className = 'colour';
        const picker = document.createElement('input');
        picker.type = 'color';
        picker.value = boxColour(written ? p.written : p.now);
        const text = document.createElement('input');
        text.type = 'text';
        text.value = written ? p.written : '';
        text.placeholder = p.now ?? '';
        picker.addEventListener('change', () => give(p.name, picker.value.toUpperCase()));
        text.addEventListener('change', () => give(p.name, text.value.trim()));
        editor.append(picker, text);
      } else {
        editor = document.createElement('input');
        editor.type = 'text';
        editor.value = written ? p.written : '';
        editor.placeholder = p.now ?? '';
        editor.addEventListener('change', () => give(p.name, editor.value.trim()));
      }
      row.appendChild(editor);

      if (written && !p.bound) {
        const reset = document.createElement('button');
        reset.textContent = '×';
        reset.title = t('Back to what it is without it');
        reset.addEventListener('click', () => give(p.name, ''));
        row.appendChild(reset);
      }
      box.appendChild(row);
    }
  }

  function inspect(chosen) {
    const side = document.getElementById('chosen');
    side.replaceChildren();
    if (!chosen) { return; }

    const title = document.createElement('h3');
    title.textContent = chosen.id === 0 ? t('The window') : chosen.part ? find(chosen.parent).control + ' — ' + chosen.control : chosen.control;
    side.appendChild(title);
    if (chosen.part) {
      const hint = document.createElement('p');
      hint.className = 'hint';
      hint.textContent = chosen.full ? t('A part of {0} that holds one control', find(chosen.parent).control) : t('A part of {0} that holds one control. Click a control on the left to put it here.', find(chosen.parent).control);
      side.appendChild(hint);
      const owner = document.createElement('button');
      owner.textContent = t('Choose {0}', find(chosen.parent).control);
      owner.title = t('Its properties are those of the control it is a part of');
      owner.addEventListener('click', () => select(chosen.parent));
      side.appendChild(owner);
    }

    if (chosen.sayable) {
      const label = document.createElement('label');
      label.textContent = chosen.id === 0 ? t('Its title') : t('Its words');
      const input = document.createElement('input');
      input.type = 'text';
      input.id = 'words';
      input.value = chosen.words ?? '';
      input.addEventListener('change', () => ask({ type: 'words', at: chosen.id, words: input.value }));
      label.appendChild(input);
      side.appendChild(label);
    }

    if (chosen.id === 0) { return; }
    const siblings = placed.filter(p => p.parent === chosen.parent && !p.part);
    const index = siblings.indexOf(chosen);
    const parent = find(chosen.parent);
    const button = (said, tip, enabled, then) => {
      const made = document.createElement('button');
      made.textContent = said;
      made.title = tip;
      made.disabled = !enabled;
      made.addEventListener('click', then);
      side.appendChild(made);
    };
    const many = parent && parent.holds === 'many' && !chosen.part;
    button(t('Move up'), t('Puts it before the one before it, in what holds it'), many && index > 0, () => ask({ type: 'move', at: chosen.id, into: chosen.parent, index: index - 1 }));
    button(t('Move down'), t('Puts it after the one after it, in what holds it'), many && index < siblings.length - 1, () => ask({ type: 'move', at: chosen.id, into: chosen.parent, index: index + 2 }));
    button(t('Delete'), t('Deletes it from the window, and what it holds'), !chosen.part || chosen.written, () => ask({ type: 'remove', at: chosen.id }));
  }

  picture.addEventListener('click', event => ask({ type: 'at', ...pointOf(event) }));
  selectedBox.addEventListener('click', event => ask({ type: 'at', ...pointOf(event) }));
  // Where in it it was taken hold of goes with it, so that it lands where it was let go of as it was held.
  selectedBox.addEventListener('dragstart', event => {
    const chosen = find(selected);
    const at = pointOf(event);
    const held = chosen && chosen.box ? ':' + (at.x - chosen.box.x) + ':' + (at.y - chosen.box.y) : '';
    event.dataTransfer.setData('text/plain', 'move:' + selected + held);
  });
  grip.addEventListener('mousedown', event => {
    const chosen = find(selected);
    if (!chosen || !chosen.box) { return; }
    event.preventDefault();
    event.stopPropagation();
    sizing = { id: chosen.id, box: chosen.box, width: chosen.box.width, height: chosen.box.height };
  });
  window.addEventListener('mousemove', event => {
    if (!sizing) { return; }
    const at = pointOf(event);
    sizing.width = Math.max(1, at.x - sizing.box.x);
    sizing.height = Math.max(1, at.y - sizing.box.y);
    const shown = { ...sizing.box, width: sizing.width, height: sizing.height };
    place(selectedBox, shown);
    putGrip(shown);
  });
  window.addEventListener('mouseup', () => {
    if (!sizing) { return; }
    const done = sizing;
    sizing = null;
    if (Math.round(done.width) !== Math.round(done.box.width) || Math.round(done.height) !== Math.round(done.box.height)) {
      ask({ type: 'size', at: done.id, width: done.width, height: done.height });
    } else {
      draw();
    }
  });
  frame.addEventListener('dragover', event => { event.preventDefault(); frame.classList.add('dropping'); });
  frame.addEventListener('dragleave', () => frame.classList.remove('dropping'));
  frame.addEventListener('drop', event => {
    event.preventDefault();
    frame.classList.remove('dropping');
    const said = event.dataTransfer.getData('text/plain');
    const at = pointOf(event);
    if (said.startsWith('control:')) {
      ask({ type: 'place', control: said.slice('control:'.length), ...at });
    } else if (said.startsWith('move:')) {
      const [id, dx, dy] = said.slice('move:'.length).split(':').map(Number);
      ask({ type: 'move', at: id, ...at, ...(dx === undefined || dy === undefined ? {} : { dx, dy }) });
    }
  });
  frame.addEventListener('mousemove', event => {
    const at = pointOf(event);
    const under = placed.filter(p => p.id !== 0 && p.box && at.x >= p.box.x && at.y >= p.box.y && at.x <= p.box.x + p.box.width && at.y <= p.box.y + p.box.height).pop();
    place(hoverBox, under ? under.box : null);
  });
  frame.addEventListener('mouseleave', () => { hoverBox.hidden = true; });
  window.addEventListener('resize', draw);

  window.addEventListener('message', event => {
    const said = event.data;
    switch (said.type) {
      case 'controls':
        controls = said.controls;
        palette();
        break;
      case 'drawn':
        document.getElementById('trouble').textContent = '';
        size = said.size;
        placed = said.placed;
        picture.src = said.picture;
        picture.style.width = size.width + 'px';
        if (said.select !== null) { selected = said.select; vscode.setState({ selected }); }
        if (!find(selected)) { selected = null; }
        // Drawn again, what the chosen control's properties are is asked again.
        properties = null;
        askedFor = null;
        picture.onload = draw;
        draw();
        break;
      case 'properties':
        if (said.at === selected) {
          properties = { at: said.at, list: said.properties };
          showProperties();
        }
        break;
      case 'unbuilt':
        document.getElementById('unbuilt').hidden = !said.text;
        document.getElementById('unbuilt-text').textContent = said.text ?? '';
        document.getElementById('unbuilt-said').textContent = said.said ?? '';
        break;
      case 'trouble':
        document.getElementById('trouble').textContent = said.text;
        if (said.placed) { placed = said.placed; draw(); }
        break;
      case 'refused':
        document.getElementById('refused').textContent = said.message;
        break;
      case 'select':
        select(said.id);
        break;
    }
  });
</script>
</body>
</html>`;
}
