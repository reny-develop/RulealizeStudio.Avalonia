// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';
import { display, machineOf, ruleOf, ruleSetsBeside } from './blueprint';
import { Disagreements } from './disagreements';
import { html, sayer } from './words';

/** What the page asks for. */
export type Asked =
    | { type: 'edit'; edit: Record<string, unknown> }
    | { type: 'rule'; rule: string }
    | { type: 'text' }
    | { type: 'view'; view: string };

/**
 * An application's specification, opened in the Studio's own editor: a UML state machine, shown as
 * the diagram, as sentences and as a table, and edited from whichever of them is being read.
 *
 * The three are drawn from the one file every time it changes, whoever changed it, and are never
 * kept: what the file has is asked of the server, and an edit made here is an edit the server writes
 * in the format's one layout — the whole text replaced, so the editor's own undo and git see one
 * edit. Each element is shown with the rules it is bound to, each one click away in the rules, and
 * where the specification and the rules disagree is shown where it is read. Nothing here reads
 * meaning out of an element: what it says is the person's, and what the rules do of it is the test
 * design's to show.
 */
export class Specification {
    static readonly viewType = 'rulealize.specification';

    /** Settles once the page has been told what the specification has. */
    readonly ready: Promise<void>;

    private readonly told = new Map<string, unknown>();
    private busy = 0;
    private disposed = false;

    constructor(
        private readonly client: LanguageClient,
        readonly document: vscode.TextDocument,
        private readonly panel: vscode.WebviewPanel,
        private readonly view: { get(): string; set(view: string): void },
    ) {
        panel.webview.options = { enableScripts: true };
        panel.webview.html = page(nonce(), sayer(), view.get());

        // The rules beside it, written or rewritten while they are not open anywhere: which rules
        // there are to bind, and which of them nothing asks for.
        const rules = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(vscode.Uri.joinPath(document.uri, '..'), '*.json'));
        const listening = [
            panel.webview.onDidReceiveMessage((asked: Asked) => void this.answer(asked)),
            vscode.workspace.onDidChangeTextDocument(change => {
                if (change.contentChanges.length > 0 && this.busy === 0
                    && (change.document === document || ruleSetsBeside(document.uri).some(uri => uri.toString() === change.document.uri.toString()))) {
                    void this.refresh();
                }
            }),
            vscode.languages.onDidChangeDiagnostics(change => {
                if (change.uris.some(uri => uri.toString() === document.uri.toString())) {
                    this.problems();
                }
            }),
            rules,
            rules.onDidChange(() => void this.refresh()),
            rules.onDidCreate(() => void this.refresh()),
            rules.onDidDelete(() => void this.refresh()),
        ];
        panel.onDidDispose(() => {
            this.disposed = true;
            listening.forEach(l => l.dispose());
        });

        this.ready = (async () => {
            await this.refresh();
            this.problems();
        })();
    }

    /** The last message of a kind the page was sent. */
    said(type: string): unknown {
        return this.told.get(type);
    }

    /** Does what the page asked, and settles once what that did has been said back to it. */
    async answer(asked: Asked): Promise<void> {
        switch (asked.type) {
            case 'edit':
                await this.edit(asked.edit);
                break;

            case 'rule': {
                const bound = ruleOf(this.document.uri, asked.rule);
                if (bound) {
                    await vscode.commands.executeCommand('rulealize.reveal', bound.rules, bound.rule);
                } else {
                    void this.post({ type: 'refused', message: vscode.l10n.t('There are no rules beside this specification yet.') });
                }
                break;
            }

            case 'text':
                await vscode.window.showTextDocument(this.document, { viewColumn: vscode.ViewColumn.Beside });
                break;

            case 'view':
                this.view.set(asked.view);
                break;
        }
    }

    /** Has the server make an edit, puts the text it answers in place of the document's, and saves it. */
    private async edit(edit: Record<string, unknown>): Promise<void> {
        let made: { text: string; added: string | null };
        try {
            made = await this.client.sendRequest<{ text: string; added: string | null }>('rulealize/edit', { text: this.document.getText(), edit });
        } catch (wrong) {
            void this.post({ type: 'refused', message: wrong instanceof Error ? wrong.message : String(wrong) });
            return;
        }

        if (made.text !== this.document.getText()) {
            this.busy++;
            try {
                const whole = new vscode.WorkspaceEdit();
                whole.replace(this.document.uri, new vscode.Range(this.document.positionAt(0), this.document.positionAt(this.document.getText().length)), made.text);
                await vscode.workspace.applyEdit(whole);
                await this.document.save();
            } finally {
                this.busy--;
            }
        }

        await this.refresh();
        if (made.added) {
            void this.post({ type: 'select', id: made.added });
        }
    }

    private async refresh(): Promise<void> {
        const machine = await machineOf(this.client, this.document.uri);
        void this.post({
            type: 'machine',
            machine,
            rules: machine?.rules.map(name => ({ name, display: display(name) })) ?? [],
            beside: ruleSetsBeside(this.document.uri).length > 0,
        });
    }

    /** Where this and the rules beside it disagree, which nobody sees in a text they are not shown. */
    private problems(): void {
        void this.post({
            type: 'problems',
            problems: vscode.languages.getDiagnostics(this.document.uri)
                .filter(d => d.source === Disagreements.source)
                .map(d => d.message),
        });
    }

    private post(message: { type: string; [said: string]: unknown }): Thenable<boolean> {
        this.told.set(message.type, message);
        return this.disposed ? Promise.resolve(false) : this.panel.webview.postMessage(message);
    }
}

/** Opens every `specification.json` and `*.specification.json` as {@link Specification}, and keeps every one open. */
export class SpecificationProvider implements vscode.CustomTextEditorProvider {
    private readonly open = new Map<vscode.WebviewPanel, Specification>();

    constructor(private readonly client: LanguageClient, private readonly started: Promise<void>, private readonly state: vscode.Memento) {}

    static register(context: vscode.ExtensionContext, provider: SpecificationProvider): void {
        context.subscriptions.push(
            vscode.window.registerCustomEditorProvider(Specification.viewType, provider, {
                webviewOptions: { retainContextWhenHidden: true },
            }));
    }

    async resolveCustomTextEditor(document: vscode.TextDocument, panel: vscode.WebviewPanel): Promise<void> {
        await this.started;
        const view = {
            get: () => this.state.get<string>('rulealize.specification.view', 'diagram'),
            set: (chosen: string) => void this.state.update('rulealize.specification.view', chosen),
        };
        const specification = new Specification(this.client, document, panel, view);
        this.open.set(panel, specification);
        panel.onDidDispose(() => this.open.delete(panel));
        await specification.ready;
    }

    /** The specification open in this editor on this file, if one is. */
    opened(uri: vscode.Uri): Specification | undefined {
        return [...this.open.values()].find(s => s.document.uri.toString() === uri.toString());
    }
}

function nonce(): string {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    return Array.from({ length: 32 }, () => chars[Math.floor(Math.random() * chars.length)]).join('');
}

function page(nonce: string, said: string, view: string): string {
    return `<!DOCTYPE html>
<html lang="${html(vscode.env.language)}">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<style nonce="${nonce}">
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); padding: 0 12px 24px; margin: 0; }
  #top { position: sticky; top: 0; z-index: 2; background: var(--vscode-editor-background); padding: 8px 0;
    border-bottom: 1px solid var(--vscode-panel-border); }
  .bar { display: flex; flex-wrap: wrap; gap: 8px; justify-content: space-between; align-items: center; }
  .tabs button { background: transparent; color: var(--vscode-foreground); border-bottom: 2px solid transparent; border-radius: 0; }
  .tabs button.on { border-bottom-color: var(--vscode-focusBorder); }
  button { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none;
    padding: 4px 10px; margin-right: 4px; cursor: pointer; font: inherit; white-space: nowrap; }
  button.small, .adds button { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
  button.link { background: transparent; color: var(--vscode-textLink-foreground); padding: 0 4px 0 0; text-align: left; }
  button:disabled { opacity: 0.5; cursor: default; }
  #problems { color: var(--vscode-editorWarning-foreground); }
  #problems:empty, #refused:empty { display: none; }
  #problems p, #refused { margin: 4px 0; }
  #refused { color: var(--vscode-errorForeground); }
  #body { display: flex; gap: 16px; align-items: flex-start; margin-top: 12px; }
  #view { flex: 1; min-width: 0; overflow: auto; }
  #view.canvas { height: calc(100vh - 120px); cursor: grab; position: relative; }
  #view.canvas.dragging { cursor: grabbing; }
  .zoom { display: none; align-items: center; gap: 4px; }
  .zoom.shown { display: flex; }
  .zoom span { min-width: 44px; text-align: center; color: var(--vscode-descriptionForeground); }
  #inspector { width: 320px; flex: none; position: sticky; top: 96px; border-left: 1px solid var(--vscode-panel-border); padding-left: 12px; }
  #inspector:empty { display: none; }
  @media (max-width: 760px) { #body { flex-direction: column; } #inspector { width: auto; position: static; border: none; padding: 0; } }
  label { display: block; margin: 8px 0 2px; color: var(--vscode-descriptionForeground); }
  label.check { display: flex; gap: 6px; align-items: center; color: var(--vscode-foreground); }
  input[type=text], textarea, select { width: 100%; box-sizing: border-box; background: var(--vscode-input-background);
    color: var(--vscode-input-foreground); border: 1px solid var(--vscode-input-border, transparent); padding: 3px 5px; font: inherit; }
  textarea { min-height: 5em; resize: vertical; }
  .kind { color: var(--vscode-descriptionForeground); font-size: 0.9em; }
  .bound { margin: 2px 0; display: flex; gap: 4px; align-items: center; }
  .bound .unknown { color: var(--vscode-editorWarning-foreground); font-size: 0.9em; }
  .rules { margin: 2px 0 6px; font-size: 0.9em; }
  .actions { margin-top: 12px; display: flex; flex-wrap: wrap; gap: 4px; }
  .el { cursor: pointer; border-radius: 3px; }
  .el.selected { outline: 2px solid var(--vscode-focusBorder); }
  .sentences section { margin: 0 0 18px; }
  .sentences h3 { font-size: 1.05em; margin: 0 0 4px; padding: 2px 4px; }
  .sentences p { margin: 4px 0; padding: 2px 4px; }
  .sentences .move { margin-left: 16px; }
  .sentences .note, .sentences .rules.note { margin-left: 32px; color: var(--vscode-descriptionForeground); font-style: italic; }
  .sentences .rules.note { font-style: normal; }
  .sentences .top-note { margin-left: 0; }
  table { border-collapse: collapse; width: 100%; }
  th, td { border-bottom: 1px solid var(--vscode-panel-border); padding: 4px; text-align: left; vertical-align: top; }
  th { color: var(--vscode-descriptionForeground); font-weight: normal; }
  td input[type=text] { min-width: 7em; }
  td textarea { min-width: 16em; min-height: 3.2em; }
  td:first-child, th:first-child { white-space: nowrap; }
  tr.selected td { background: var(--vscode-list-inactiveSelectionBackground); }
  .empty { color: var(--vscode-descriptionForeground); }
  svg text { fill: var(--vscode-editor-foreground); font-family: var(--vscode-font-family); font-size: 13px; }
  svg .box { fill: var(--vscode-editorWidget-background, var(--vscode-editor-background)); stroke: var(--vscode-editor-foreground); stroke-width: 1.2; }
  svg .notebox { fill: var(--vscode-editorWidget-background, var(--vscode-editor-background)); stroke: var(--vscode-descriptionForeground); stroke-width: 1; }
  svg .line { fill: none; stroke: var(--vscode-editor-foreground); stroke-width: 1.2; }
  svg .tie { fill: none; stroke: var(--vscode-descriptionForeground); stroke-width: 1; stroke-dasharray: 4 3; }
  svg .dot { fill: var(--vscode-editor-foreground); }
  svg .label-back { fill: var(--vscode-editor-background); opacity: 0.9; }
  svg .label { cursor: pointer; }
  svg .label:hover .label-back { fill: var(--vscode-list-hoverBackground); opacity: 1; }
  svg .selected .label-back { stroke: var(--vscode-focusBorder); stroke-width: 1.5; opacity: 1; }
  svg .hit { fill: transparent; stroke: transparent; stroke-width: 12; }
  svg .selected .box, svg .selected .notebox, svg .selected .line { stroke: var(--vscode-focusBorder); stroke-width: 2.5; }
  svg .selected text { font-weight: bold; }
  svg .unknown .box, svg .unknown .notebox { stroke: var(--vscode-editorWarning-foreground); }
  svg .note-text { fill: var(--vscode-descriptionForeground); }
</style>
</head>
<body>
<div id="top">
  <div class="bar">
    <div class="tabs">
      <button data-view="diagram"></button><button data-view="sentences"></button><button data-view="table"></button>
    </div>
    <div class="zoom" id="zoom">
      <button id="zoom-out" class="small">−</button><span id="zoom-shown"></span><button id="zoom-in" class="small">+</button>
      <button id="zoom-fit" class="small"></button>
    </div>
    <div class="adds">
      <button id="add-state"></button><button id="add-transition"></button><button id="add-note"></button><button id="text" class="small"></button>
    </div>
  </div>
  <div id="problems"></div>
  <p id="refused"></p>
</div>
<div id="body"><div id="view"></div><div id="inspector"></div></div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  ${said}
  const svgNs = 'http://www.w3.org/2000/svg';
  let machine = null;
  let rules = [];
  let beside = false;
  let selected = (vscode.getState() || {}).selected || null;
  let view = ${JSON.stringify(view)};

  const tabs = { diagram: t('Diagram'), sentences: t('Sentences'), table: t('Table') };
  for (const button of document.querySelectorAll('.tabs button')) {
    button.textContent = tabs[button.dataset.view];
    button.addEventListener('click', () => {
      view = button.dataset.view;
      vscode.postMessage({ type: 'view', view });
      draw();
    });
  }

  document.getElementById('add-state').textContent = t('+ State');
  document.getElementById('zoom-fit').textContent = t('Fit');
  document.getElementById('zoom-in').title = t('Larger');
  document.getElementById('zoom-out').title = t('Smaller');
  document.getElementById('zoom').title = t('Ctrl and the wheel, or a pinch on a touchpad, zoom it; dragging where nothing is moves it.');
  document.getElementById('add-transition').textContent = t('+ Transition');
  document.getElementById('add-note').textContent = t('+ Note');
  document.getElementById('text').textContent = t('Show JSON');
  document.getElementById('add-state').addEventListener('click', () => edit({ op: 'add', kind: 'state' }));
  document.getElementById('add-transition').addEventListener('click', () => {
    const at = element(selected);
    const from = at && at.kind === 'state' ? at.id : at && at.kind === 'transition' ? at.to : undefined;
    edit({ op: 'add', kind: 'transition', from });
  });
  document.getElementById('add-note').addEventListener('click', () => edit({ op: 'add', kind: 'note', on: selected || undefined }));
  document.getElementById('text').addEventListener('click', () => vscode.postMessage({ type: 'text' }));

  const viewEl = document.getElementById('view');
  const inspector = document.getElementById('inspector');

  function edit(asked) {
    document.getElementById('refused').textContent = '';
    vscode.postMessage({ type: 'edit', edit: asked });
  }

  function element(id) {
    return machine && id ? machine.elements.find(e => e.id === id) : undefined;
  }

  function kindOf(e) {
    return e.kind === 'state' ? t('State') : e.kind === 'transition' ? t('Transition') : t('Note');
  }

  function called(e) {
    if (!e) { return ''; }
    if (e.name) { return e.name; }
    if (e.says) { return e.says.length > 40 ? e.says.slice(0, 39) + '…' : e.says; }
    return e.id;
  }

  function stateName(id) {
    const e = element(id);
    return e ? called(e) : (id || '?');
  }

  function select(id) {
    selected = id;
    vscode.setState({ ...(vscode.getState() || {}), selected });
    draw();
  }

  function unknown(e) {
    return beside && e.rules.some(r => !r.known);
  }

  function draw() {
    for (const button of document.querySelectorAll('.tabs button')) {
      button.classList.toggle('on', button.dataset.view === view);
    }

    const ready = machine !== null;
    for (const id of ['add-state', 'add-transition', 'add-note']) {
      document.getElementById(id).disabled = !ready;
    }
    document.getElementById('add-transition').disabled = !ready || !machine.elements.some(e => e.kind === 'state');

    viewEl.replaceChildren();
    if (!ready) {
      viewEl.appendChild(text('p', t('This is not a specification the Studio can read: it is not JSON just now, or its $schema is not one the Studio knows.'), 'empty'));
      inspector.replaceChildren();
      return;
    }

    if (machine.elements.length === 0) {
      viewEl.appendChild(text('p', t('Nothing is specified yet. Add a state, for where the application starts.'), 'empty'));
    } else if (view === 'sentences') {
      viewEl.appendChild(sentences());
    } else if (view === 'table') {
      viewEl.appendChild(table());
    } else {
      viewEl.appendChild(diagram());
    }
    viewEl.classList.toggle('canvas', view === 'diagram' && machine.elements.length > 0);
    document.getElementById('zoom').classList.toggle('shown', view === 'diagram' && machine.elements.length > 0);
    if (view === 'diagram') {
      scaleDiagram();
    }

    inspect();
  }

  function text(tag, said, className) {
    const made = document.createElement(tag);
    made.textContent = said;
    if (className) { made.className = className; }
    return made;
  }

  function selectable(made, e) {
    made.classList.add('el');
    made.dataset.id = e.id;
    if (e.id === selected) { made.classList.add('selected'); }
    made.addEventListener('click', event => { event.stopPropagation(); select(e.id); });
    return made;
  }

  function ruleLinks(e) {
    const block = document.createElement('div');
    block.className = 'rules';
    if (e.rules.length === 0) {
      block.appendChild(text('span', t('Bound to no rule yet'), 'kind'));
    }
    for (const rule of e.rules) {
      const link = text('button', '→ ' + display(rule.name), 'link');
      link.title = t('Show it in the rules');
      link.addEventListener('click', event => { event.stopPropagation(); vscode.postMessage({ type: 'rule', rule: rule.name }); });
      block.appendChild(link);
      if (beside && !rule.known) {
        block.appendChild(text('span', t('not in the rules'), 'unknown'));
      }
    }
    return block;
  }

  function display(name) {
    const found = rules.find(r => r.name === name);
    return found ? found.display : name.split('/').slice(1).join(' › ');
  }

  // ── The sentences: each state, what moves it and where, and what is noted on either. ──

  function sentences() {
    const all = document.createElement('div');
    all.className = 'sentences';
    const states = machine.elements.filter(e => e.kind === 'state');
    const notesOn = id => machine.elements.filter(e => e.kind === 'note' && e.on === id);
    const shown = new Set();

    const notes = (id, into) => {
      for (const note of notesOn(id)) {
        shown.add(note.id);
        const p = selectable(text('p', note.says || t('(says nothing yet)'), 'note'), note);
        into.appendChild(p);
        const links = ruleLinks(note);
        links.classList.add('note');
        into.appendChild(links);
        notes(note.id, into);
      }
    };

    for (const state of states) {
      shown.add(state.id);
      const section = document.createElement('section');
      const marks = [machine.initial === state.id ? t('It starts here.') : '', state.final ? t('It ends here.') : ''].filter(m => m).join(' ');
      section.appendChild(selectable(text('h3', (state.name || state.id) + (marks ? ' — ' + marks : '')), state));
      if (state.says) {
        section.appendChild(selectable(text('p', state.says), state));
      }
      section.appendChild(ruleLinks(state));
      notes(state.id, section);

      for (const move of machine.elements.filter(e => e.kind === 'transition' && e.from === state.id)) {
        shown.add(move.id);
        const head = move.to === move.from
          ? t('{0}, staying where it is', move.name || move.id)
          : t('{0}, to {1}', move.name || move.id, stateName(move.to));
        const p = selectable(document.createElement('p'), move);
        p.classList.add('move');
        const strong = text('strong', head + (move.guard ? ' ' + t('— only when {0}', move.guard) : ''));
        p.appendChild(strong);
        if (move.says) {
          p.appendChild(document.createTextNode('. ' + move.says));
        }
        section.appendChild(p);
        const links = ruleLinks(move);
        links.classList.add('move');
        section.appendChild(links);
        notes(move.id, section);
      }

      all.appendChild(section);
    }

    const rest = machine.elements.filter(e => !shown.has(e.id));
    if (rest.length > 0) {
      const section = document.createElement('section');
      section.appendChild(text('h3', t('About the whole of it')));
      for (const e of rest) {
        section.appendChild(selectable(text('p', e.says || called(e), e.kind === 'note' ? 'note top-note' : ''), e));
        section.appendChild(ruleLinks(e));
      }
      all.appendChild(section);
    }

    return all;
  }

  // ── The table: one row an element, its words edited where they are. ──

  function table() {
    const made = document.createElement('table');
    const head = document.createElement('tr');
    for (const column of [t('Kind'), t('Name'), t('Where'), t('Only when'), t('What it says'), t('Bound to')]) {
      head.appendChild(text('th', column));
    }
    made.appendChild(head);

    for (const e of machine.elements) {
      const row = document.createElement('tr');
      row.dataset.id = e.id;
      if (e.id === selected) { row.classList.add('selected'); }
      row.addEventListener('click', () => { if (selected !== e.id) { select(e.id); } });
      row.appendChild(text('td', kindOf(e)));
      row.appendChild(cell(e, 'name', e.kind !== 'note'));
      row.appendChild(text('td', e.kind === 'transition' ? stateName(e.from) + ' → ' + stateName(e.to)
        : e.kind === 'note' ? (e.on ? t('on {0}', called(element(e.on))) : t('the whole of it'))
        : [machine.initial === e.id ? t('starts here') : '', e.final ? t('ends here') : ''].filter(m => m).join(', ')));
      row.appendChild(cell(e, 'guard', e.kind === 'transition'));
      row.appendChild(cell(e, 'says', true));
      const bound = document.createElement('td');
      bound.appendChild(ruleLinks(e));
      row.appendChild(bound);
      made.appendChild(row);
    }

    return made;
  }

  function cell(e, field, editable) {
    const td = document.createElement('td');
    if (!editable) { return td; }
    const box = document.createElement(field === 'says' ? 'textarea' : 'input');
    if (field !== 'says') { box.type = 'text'; }
    box.value = e[field] || '';
    box.dataset.field = field;
    box.dataset.id = e.id;
    box.addEventListener('change', () => edit({ op: 'set', id: e.id, field, value: box.value }));
    td.appendChild(box);
    return td;
  }

  // ── The diagram: states in rows by how far they are from where it starts, notes beside. ──

  function svg(tag, attributes, parent) {
    const made = document.createElementNS(svgNs, tag);
    for (const [key, value] of Object.entries(attributes || {})) { made.setAttribute(key, String(value)); }
    if (parent) { parent.appendChild(made); }
    return made;
  }

  // How wide a text is drawn, near enough to lay it out: a wide character is a square.
  // How wide words are drawn, measured in the font the diagram draws them in.
  const measure = document.createElement('canvas').getContext('2d');
  function width(s) {
    measure.font = '13px ' + getComputedStyle(document.body).fontFamily;
    return Math.ceil(measure.measureText(s).width);
  }

  function wrap(s, most) {
    const lines = [];
    let line = '';
    for (const word of s.split(/(\\s+)/)) {
      if (width(line + word) <= most) { line += word; continue; }
      if (line.trim()) { lines.push(line.trim()); line = ''; }
      for (const c of word.trim() ? word : '') {
        if (width(line + c) > most) { lines.push(line); line = ''; }
        line += c;
      }
    }
    if (line.trim()) { lines.push(line.trim()); }
    return lines.length ? lines : [''];
  }

  function label(move) {
    return (move.name || move.id) + (move.guard ? ' [' + move.guard + ']' : '');
  }

  function diagram() {
    const elements = machine.elements;
    const states = elements.filter(e => e.kind === 'state');
    const moves = elements.filter(e => e.kind === 'transition');
    const isState = id => states.some(s => s.id === id);

    // How far each state is from where it starts; one nothing reaches comes after the rest.
    const rank = new Map();
    const start = isState(machine.initial) ? machine.initial : states.length ? states[0].id : null;
    if (start) {
      rank.set(start, 0);
      const queue = [start];
      while (queue.length) {
        const at = queue.shift();
        for (const m of moves) {
          if (m.from === at && isState(m.to) && !rank.has(m.to)) { rank.set(m.to, rank.get(at) + 1); queue.push(m.to); }
        }
      }
    }
    let next = rank.size ? Math.max(...rank.values()) + 1 : 0;
    for (const s of states) { if (!rank.has(s.id)) { rank.set(s.id, next++); } }

    const rows = [];
    for (const s of states) { (rows[rank.get(s.id)] = rows[rank.get(s.id)] || []).push(s); }

    const loops = id => moves.filter(m => m.from === id && m.to === id);
    const W = 190, lineHeight = 20, gap = 80, left = 40;
    const at = new Map();
    let y = 60, right = 0;
    for (const row of rows.filter(r => r)) {
      let x = left + 120;
      let tallest = 0;
      for (const s of row) {
        const own = loops(s.id);
        const h = Math.max(48, 16 + own.length * lineHeight);
        const loopWidth = own.length ? 50 + Math.max(...own.map(m => width(label(m)))) : 0;
        // As wide as its name, and never narrower than the rest.
        const w = Math.max(W, width(s.name || s.id) + 32);
        at.set(s.id, { x, y, w, h });
        x += w + loopWidth + 60;
        tallest = Math.max(tallest, h);
      }
      right = Math.max(right, x);
      y += tallest + gap;
    }

    const made = svg('svg', {});
    const defs = svg('defs', {}, made);
    const marker = svg('marker', { id: 'arrow', viewBox: '0 0 10 10', refX: 9, refY: 5, markerWidth: 9, markerHeight: 9, markerUnits: 'userSpaceOnUse', orient: 'auto-start-reverse' }, defs);
    svg('path', { d: 'M 0 0 L 10 5 L 0 10 z', class: 'dot' }, marker);

    const anchors = new Map();
    const group = e => {
      const g = svg('g', {}, made);
      g.dataset.id = e.id;
      if (e.id === selected) { g.classList.add('selected'); }
      if (unknown(e)) { g.classList.add('unknown'); }
      g.style.cursor = 'pointer';
      g.addEventListener('click', event => { event.stopPropagation(); select(e.id); });
      const title = svg('title', {}, g);
      title.textContent = kindOf(e) + ': ' + (e.says || called(e));
      return g;
    };

    // Where it starts.
    if (start) {
      const s = at.get(start);
      const cx = s.x + 30;
      svg('circle', { cx, cy: s.y - 34, r: 7, class: 'dot' }, made);
      svg('path', { d: 'M ' + cx + ' ' + (s.y - 27) + ' L ' + cx + ' ' + (s.y - 1), class: 'line', 'marker-end': 'url(#arrow)' }, made);
    }

    // Moves between two states: down the page in a curve, or back up it round the left. Moves
    // between the same two states are drawn apart, and their names one under another, so that
    // each can be pointed at; every name is drawn above every line, where nothing covers it.
    const pairs = new Map();
    const count = new Map();
    for (const m of moves) {
      if (isState(m.from) && isState(m.to) && m.from !== m.to) {
        const key = m.from + '>' + m.to;
        count.set(key, (count.get(key) || 0) + 1);
      }
    }
    const names = [];
    for (const m of moves) {
      if (!isState(m.from) || !isState(m.to) || m.from === m.to) { continue; }
      const key = m.from + '>' + m.to;
      const index = pairs.get(key) || 0;
      pairs.set(key, index + 1);
      const spread = index - ((count.get(key) || 1) - 1) / 2;
      const a = at.get(m.from), b = at.get(m.to);
      const g = group(m);
      let d, lx, ly;
      const down = b.y > a.y;
      if (down) {
        const sx = a.x + a.w / 2 + spread * 36, sy = a.y + a.h, tx = b.x + b.w / 2 + spread * 36, ty = b.y;
        d = 'M ' + sx + ' ' + sy + ' C ' + sx + ' ' + (sy + gap / 2) + ' ' + tx + ' ' + (ty - gap / 2) + ' ' + tx + ' ' + ty;
        // Right of the rightmost of them, so that no name lies across another's line.
        lx = (a.x + a.w / 2 + b.x + b.w / 2) / 2 + ((count.get(key) || 1) - 1) / 2 * 36 + 10; ly = (sy + ty) / 2 + 4 + spread * 20;
      } else {
        const sx = a.x, sy = a.y + a.h / 2 + spread * 12, tx = b.x, ty = b.y + b.h / 2 + spread * 12, out = 80 + index * 60;
        d = 'M ' + sx + ' ' + sy + ' C ' + (sx - out) + ' ' + sy + ' ' + (tx - out) + ' ' + ty + ' ' + tx + ' ' + ty;
        lx = Math.min(sx, tx) - out * 0.75 - 6; ly = (sy + ty) / 2 + 4 + spread * 20;
      }
      svg('path', { d, class: 'hit' }, g);
      svg('path', { d, class: 'line', 'marker-end': 'url(#arrow)' }, g);
      const said = label(m);
      names.push({ m, said, lx, ly, down });
      anchors.set(m.id, { x: down ? lx + width(said) : lx, y: ly - 4 });
    }
    const drawNames = () => {
      for (const { m, said, lx, ly, down } of names) {
        const g = group(m);
        g.classList.add('label');
        svg('rect', { x: down ? lx - 3 : lx - width(said) - 3, y: ly - 14, width: width(said) + 6, height: 19, rx: 3, class: 'label-back' }, g);
        const words = svg('text', { x: lx, y: ly, 'text-anchor': down ? 'start' : 'end' }, g);
        words.textContent = said;
      }
    };

    // Each state, with the moves that leave it where it is drawn as loops on its right.
    for (const s of states) {
      const p = at.get(s.id);
      const g = group(s);
      svg('rect', { x: p.x, y: p.y, width: p.w, height: p.h, rx: 10, class: 'box' }, g);
      if (s.final) {
        svg('rect', { x: p.x + 4, y: p.y + 4, width: p.w - 8, height: p.h - 8, rx: 7, class: 'box' }, g);
      }
      const name = svg('text', { x: p.x + p.w / 2, y: p.y + p.h / 2 + 5, 'text-anchor': 'middle' }, g);
      name.textContent = s.name || s.id;
      anchors.set(s.id, { x: p.x + p.w, y: p.y + 14 });

      loops(s.id).forEach((m, i) => {
        const ly = p.y + 18 + i * lineHeight;
        const lg = group(m);
        const d = 'M ' + (p.x + p.w) + ' ' + (ly - 5) + ' C ' + (p.x + p.w + 34) + ' ' + (ly - 14) + ' ' + (p.x + p.w + 34) + ' ' + (ly + 10) + ' ' + (p.x + p.w) + ' ' + (ly + 3);
        svg('path', { d, class: 'hit' }, lg);
        svg('path', { d, class: 'line', 'marker-end': 'url(#arrow)' }, lg);
        const words = svg('text', { x: p.x + p.w + 40, y: ly + 3 }, lg);
        words.textContent = label(m);
        anchors.set(m.id, { x: p.x + p.w + 40 + width(label(m)), y: ly - 1 });
      });

      if (s.final) {
        const cx = p.x + p.w / 2, top = p.y + p.h;
        svg('path', { d: 'M ' + cx + ' ' + top + ' L ' + cx + ' ' + (top + 26), class: 'line', 'marker-end': 'url(#arrow)' }, made);
        svg('circle', { cx, cy: top + 36, r: 9, class: 'box' }, made);
        svg('circle', { cx, cy: top + 36, r: 5, class: 'dot' }, made);
      }
    }

    drawNames();

    // Notes, in a column of their own, each tied to what it is on.
    const notes = elements.filter(e => e.kind === 'note');
    const column = Math.max(right, ...[...anchors.values()].map(a => a.x + 40));
    const noteWidth = 240;
    let bottom = y;
    let below = 30;
    const placed = notes
      .map(n => ({ n, anchor: anchors.get(n.on) }))
      .sort((a, b) => (a.anchor ? a.anchor.y : 1e9) - (b.anchor ? b.anchor.y : 1e9));
    for (const { n, anchor } of placed) {
      const lines = wrap(n.says || called(n), noteWidth - 20);
      const h = lines.length * 17 + 14;
      const ny = Math.max(anchor ? anchor.y - 12 : 0, below);
      const g = group(n);
      if (anchor) {
        svg('path', { d: 'M ' + anchor.x + ' ' + anchor.y + ' L ' + column + ' ' + (ny + 12), class: 'tie' }, g);
      }
      svg('path', { d: 'M ' + column + ' ' + ny + ' h ' + (noteWidth - 12) + ' l 12 12 v ' + (h - 12) + ' h -' + noteWidth + ' z', class: 'notebox' }, g);
      lines.forEach((line, i) => {
        const words = svg('text', { x: column + 10, y: ny + 20 + i * 17, class: 'note-text' }, g);
        words.textContent = line;
      });
      anchors.set(n.id, { x: column + noteWidth, y: ny + 12 });
      below = ny + h + 12;
      bottom = Math.max(bottom, below);
    }

    // Drawn at its own size, and scaled to how large it is asked to be.
    // From the leftmost of what is drawn — a name of a move back up the page lies left of its line.
    const leftmost = Math.min(0, ...names.filter(n => !n.down).map(n => n.lx - width(n.said) - 12));
    const size = { width: column + noteWidth + 20 - leftmost, height: bottom + 20 };
    made.setAttribute('viewBox', leftmost + ' 0 ' + size.width + ' ' + size.height);
    made.dataset.width = String(size.width);
    made.dataset.height = String(size.height);
    made.addEventListener('click', () => { if (selected && !dragged) { select(null); } });
    return made;
  }

  // ── How large the diagram is drawn: zoomed by the buttons, by Ctrl and the wheel or a pinch
  // on a touchpad, around where it is pointed at; moved by dragging where nothing is. ──

  let zoom = (vscode.getState() || {}).zoom || 1;
  let dragged = false;

  function scaleDiagram(around) {
    const made = viewEl.querySelector('svg');
    if (!made) { return; }
    const before = { width: made.clientWidth || 1, left: viewEl.scrollLeft, top: viewEl.scrollTop };
    made.setAttribute('width', String(Number(made.dataset.width) * zoom));
    made.setAttribute('height', String(Number(made.dataset.height) * zoom));
    document.getElementById('zoom-shown').textContent = Math.round(zoom * 100) + '%';
    if (around) {
      const ratio = made.clientWidth / before.width;
      viewEl.scrollLeft = (before.left + around.x) * ratio - around.x;
      viewEl.scrollTop = (before.top + around.y) * ratio - around.y;
    }
  }

  function zoomTo(next, around) {
    zoom = Math.min(4, Math.max(0.25, next));
    vscode.setState({ ...(vscode.getState() || {}), zoom });
    scaleDiagram(around);
  }

  function fitDiagram() {
    const made = viewEl.querySelector('svg');
    if (!made) { return; }
    zoomTo(Math.min((viewEl.clientWidth - 8) / Number(made.dataset.width), (viewEl.clientHeight - 8) / Number(made.dataset.height), 2));
  }

  document.getElementById('zoom-in').addEventListener('click', () => zoomTo(zoom * 1.25));
  document.getElementById('zoom-out').addEventListener('click', () => zoomTo(zoom / 1.25));
  document.getElementById('zoom-fit').addEventListener('click', fitDiagram);
  viewEl.addEventListener('wheel', event => {
    if (!viewEl.classList.contains('canvas') || !event.ctrlKey) { return; }
    event.preventDefault();
    const box = viewEl.getBoundingClientRect();
    zoomTo(zoom * Math.exp(-event.deltaY * 0.0025), { x: event.clientX - box.left, y: event.clientY - box.top });
  }, { passive: false });
  let pan = null;
  viewEl.addEventListener('mousedown', event => {
    if (!viewEl.classList.contains('canvas') || event.button !== 0 || event.target.closest('g[data-id]')) { return; }
    pan = { x: event.clientX, y: event.clientY, left: viewEl.scrollLeft, top: viewEl.scrollTop };
    dragged = false;
  });
  window.addEventListener('mousemove', event => {
    if (!pan) { return; }
    const dx = event.clientX - pan.x, dy = event.clientY - pan.y;
    if (Math.abs(dx) + Math.abs(dy) > 3) {
      dragged = true;
      viewEl.classList.add('dragging');
    }
    viewEl.scrollLeft = pan.left - dx;
    viewEl.scrollTop = pan.top - dy;
  });
  window.addEventListener('mouseup', () => {
    pan = null;
    viewEl.classList.remove('dragging');
    setTimeout(() => { dragged = false; }, 0);
  });

  // ── The element chosen, edited: its words, where it stands, and the rules it is bound to. ──

  function inspect() {
    const e = element(selected);
    const focused = document.activeElement && inspector.contains(document.activeElement) ? document.activeElement : null;
    const kept = focused && focused.dataset.field ? { field: focused.dataset.field, value: focused.value } : null;
    inspector.replaceChildren();
    if (!e) { return; }

    inspector.appendChild(text('div', kindOf(e) + ' · ' + e.id, 'kind'));

    const field = (labelText, name, multiline) => {
      inspector.appendChild(text('label', labelText));
      const box = document.createElement(multiline ? 'textarea' : 'input');
      if (!multiline) { box.type = 'text'; }
      box.value = e[name] || '';
      box.dataset.field = name;
      box.addEventListener('change', () => edit({ op: 'set', id: e.id, field: name, value: box.value }));
      inspector.appendChild(box);
      return box;
    };

    const choice = (labelText, name, options) => {
      inspector.appendChild(text('label', labelText));
      const box = document.createElement('select');
      for (const [value, shown] of options) {
        const option = text('option', shown);
        option.value = value;
        option.selected = (e[name] || '') === value;
        box.appendChild(option);
      }
      box.addEventListener('change', () => edit({ op: 'set', id: e.id, field: name, value: box.value }));
      inspector.appendChild(box);
    };

    const check = (labelText, checked, disabled, then) => {
      const row = text('label', '', 'check');
      const box = document.createElement('input');
      box.type = 'checkbox';
      box.checked = checked;
      box.disabled = disabled;
      box.addEventListener('change', () => then(box.checked));
      row.appendChild(box);
      row.appendChild(document.createTextNode(labelText));
      inspector.appendChild(row);
    };

    const states = machine.elements.filter(x => x.kind === 'state').map(x => [x.id, called(x)]);
    if (e.kind === 'state') {
      field(t('Name'), 'name');
      check(t('It starts here'), machine.initial === e.id, machine.initial === e.id, () => edit({ op: 'initial', id: e.id }));
      check(t('It ends here'), e.final, false, on => edit({ op: 'set', id: e.id, field: 'final', value: on }));
    } else if (e.kind === 'transition') {
      field(t('What the person does'), 'name');
      choice(t('From'), 'from', states);
      choice(t('To'), 'to', states);
      field(t('Only when'), 'guard');
    } else {
      choice(t('On'), 'on', [['', t('the whole of it')], ...machine.elements.filter(x => x.id !== e.id).map(x => [x.id, kindOf(x) + ': ' + called(x)])]);
    }

    field(t('What it says'), 'says', true);

    inspector.appendChild(text('label', t('Bound to')));
    if (e.rules.length === 0) {
      inspector.appendChild(text('div', t('Bound to no rule yet'), 'kind'));
    }
    for (const rule of e.rules) {
      const row = document.createElement('div');
      row.className = 'bound';
      const link = text('button', display(rule.name), 'link');
      link.title = t('Show it in the rules');
      link.addEventListener('click', () => vscode.postMessage({ type: 'rule', rule: rule.name }));
      row.appendChild(link);
      if (beside && !rule.known) {
        row.appendChild(text('span', t('not in the rules'), 'unknown'));
      }
      const out = text('button', '×', 'small');
      out.title = t('Unbind');
      out.addEventListener('click', () => edit({ op: 'unbind', id: e.id, rule: rule.name }));
      row.appendChild(out);
      inspector.appendChild(row);
    }

    const free = rules.filter(r => !e.rules.some(b => b.name === r.name));
    if (free.length > 0) {
      const row = document.createElement('div');
      row.className = 'bound';
      const box = document.createElement('select');
      const unasked = new Set(machine.unasked);
      for (const [title, list] of [[t('Nothing asks for these yet'), free.filter(r => unasked.has(r.name))], [t('Asked for elsewhere too'), free.filter(r => !unasked.has(r.name))]]) {
        if (list.length === 0) { continue; }
        const group = document.createElement('optgroup');
        group.label = title;
        for (const r of list) {
          const option = text('option', r.display);
          option.value = r.name;
          group.appendChild(option);
        }
        box.appendChild(group);
      }
      row.appendChild(box);
      const bind = text('button', t('Bind'), 'small');
      bind.addEventListener('click', () => edit({ op: 'bind', id: e.id, rule: box.value }));
      row.appendChild(bind);
      inspector.appendChild(row);
    } else if (!beside) {
      inspector.appendChild(text('div', t('There are no rules beside this specification yet, so there is nothing to bind it to.'), 'kind'));
    }

    const actions = document.createElement('div');
    actions.className = 'actions';
    if (e.kind === 'state') {
      const move = text('button', t('+ Transition from here'), 'small');
      move.addEventListener('click', () => edit({ op: 'add', kind: 'transition', from: e.id }));
      actions.appendChild(move);
    }
    const note = text('button', t('+ Note on this'), 'small');
    note.addEventListener('click', () => edit({ op: 'add', kind: 'note', on: e.id }));
    actions.appendChild(note);
    const remove = text('button', t('Delete'), 'small');
    remove.title = e.kind === 'state' ? t('Deletes the state, the transitions from and to it, and what is noted on them') : t('Deletes it, and what is noted on it');
    remove.addEventListener('click', () => edit({ op: 'remove', id: e.id }));
    actions.appendChild(remove);
    inspector.appendChild(actions);

    // What was being typed when the specification changed under it stays as it was typed.
    if (kept) {
      const again = inspector.querySelector('[data-field="' + kept.field + '"]');
      if (again) { again.value = kept.value; again.focus(); }
    }
  }

  window.addEventListener('message', event => {
    const said = event.data;
    switch (said.type) {
      case 'machine':
        machine = said.machine;
        rules = said.rules;
        beside = said.beside;
        if (machine && selected && !element(selected)) { selected = null; }
        draw();
        break;
      case 'problems':
        document.getElementById('problems').replaceChildren(...said.problems.map(p => text('p', p)));
        break;
      case 'select': {
        select(said.id);
        const shown = viewEl.querySelector('[data-id="' + CSS.escape(said.id) + '"]') || viewEl.querySelector('.selected');
        if (shown && shown.scrollIntoView) { shown.scrollIntoView({ block: 'center' }); }
        break;
      }
      case 'refused':
        document.getElementById('refused').textContent = said.message;
        break;
    }
  });

  draw();
</script>
</body>
</html>`;
}
