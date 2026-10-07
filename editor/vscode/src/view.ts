// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';
import { ruleSetsIn, specificationFile, specificationsIn, specificationSuffix } from './blueprint';
import { Specification } from './specification';
import { Committed } from './git';
import { Screen } from './screen';

/** One line of the view: what it says, and what clicking it opens. */
export class Item extends vscode.TreeItem {
    /** The application's folder, on the line of an application. */
    folder?: string;

    constructor(label: string, readonly children: Item[] = [], expanded = true) {
        super(label, children.length === 0 ? vscode.TreeItemCollapsibleState.None
            : expanded ? vscode.TreeItemCollapsibleState.Expanded : vscode.TreeItemCollapsibleState.Collapsed);
    }
}

/**
 * The extension's own view, in VS Code's activity bar, where everything somebody does starts. With
 * no application in the workspace it is empty, and VS Code draws there what the manifest's welcome
 * says: making one, the example, and the walkthrough. With one it is the application, under its
 * folder's name: its screens, its specifications and its test cases — one for each rule set — each
 * kind under a line of its own and each file called by its name, so that VS Code's find on the view,
 * from the magnifier above it, finds one among many. The + on the line of the screens adds one, as
 * does the + on the specifications', and each is deleted from its own line, into the trash. The
 * walkthrough is a view of its own below it, collapsed.
 *
 * It opens things and draws nothing again. Each line is what opens it — the file, the application
 * on the screen — and what is shown is what that editor or page shows. Under the screen and the
 * specification, where either differs from what was last committed, is what the server says
 * changed of it beyond binding it to the rules: the bindings are whoever writes the rules' to
 * write, and the rest is the person's to read before they commit it. Committing, and putting a
 * folder back, are git's, done in VS Code's Source Control or by their agent; nothing here does them.
 */
export class Applications implements vscode.TreeDataProvider<Item> {
    static readonly viewId = 'rulealize.view';

    private readonly changed = new vscode.EventEmitter<void>();
    private readonly watched = new Map<string, vscode.Disposable>();
    private refreshing: NodeJS.Timeout | undefined;

    readonly onDidChangeTreeData = this.changed.event;

    private constructor(
        private readonly client: LanguageClient,
        private readonly committed: Committed,
    ) {}

    /** Registers the view, and what makes it look again. */
    static register(context: vscode.ExtensionContext, client: LanguageClient): Applications {
        const view = new Applications(client, new Committed());
        const files = vscode.workspace.createFileSystemWatcher('**/*');
        context.subscriptions.push(
            vscode.window.registerTreeDataProvider(Applications.viewId, view),
            files,
            files.onDidCreate(() => view.refresh()),
            files.onDidChange(() => view.refresh()),
            files.onDidDelete(() => view.refresh()),
            vscode.workspace.onDidChangeWorkspaceFolders(() => view.refresh()),
            vscode.commands.registerCommand('rulealize.newWindow', (item?: Item, name?: string) => added(item, 'window', name)),
            vscode.commands.registerCommand('rulealize.newSpecification', (item?: Item, name?: string) => added(item, 'specification', name)),
            // Finding is VS Code's own find on a tree, opened on this one from the magnifier above it.
            vscode.commands.registerCommand('rulealize.find', async () => {
                await vscode.commands.executeCommand(`${Applications.viewId}.focus`);
                await vscode.commands.executeCommand('list.find');
            }),
            vscode.commands.registerCommand('rulealize.delete', (item?: Item, sure?: boolean) => deleted(item, sure)),
            new vscode.Disposable(() => view.watched.forEach(w => w.dispose())));
        return view;
    }

    /** Looks again, once whatever is changing the folder — a build, an agent — has paused. */
    refresh(): void {
        clearTimeout(this.refreshing);
        this.refreshing = setTimeout(() => this.changed.fire(), 300);
    }

    getTreeItem(item: Item): Item {
        return item;
    }

    async getChildren(item?: Item): Promise<Item[]> {
        if (item) {
            return item.children;
        }

        const found = applications();
        if (found.length === 0) {
            return [];
        }

        // Each application under its folder's name, open; the walkthrough is a view of its own below.
        return Promise.all(found.map(async folder => {
            const item = new Item(path.basename(folder).toUpperCase(), await this.application(folder));
            item.folder = folder;
            item.contextValue = 'application';
            return item;
        }));
    }

    /**
     * The lines of one application: its screens, its specifications and its test cases, each kind
     * under a line of its own, each file called by its name so that the view's find tells them apart.
     * A screen or a specification is added from the + on its kind's line.
     */
    private async application(folder: string): Promise<Item[]> {
        this.watch(folder);

        // Every window: any XAML in the folder; a folder with none yet still has the one New application writes.
        const screens: Item[] = [];
        const windows = fs.readdirSync(folder).filter(name => name.endsWith('.axaml')).sort();
        for (const name of windows.length > 0 ? windows : ['MainWindow.axaml']) {
            const screen = path.join(folder, name);
            const line = await this.blueprint(screen, 'window', { command: 'vscode.openWith', arguments: [vscode.Uri.file(screen), Screen.viewType] });
            line.contextValue = 'window';
            screens.push(line);
        }

        const specifications: Item[] = [];
        for (const specification of specificationsIn(folder)) {
            const line = await this.blueprint(specification, 'book');
            line.contextValue = 'specification';
            specifications.push(line);
        }

        const cases: Item[] = [];
        for (const rules of ruleSetsIn(folder)) {
            // Called by its rule set's name, not its file's: what it opens is the test cases, not the rules.
            const shown = new Item(path.basename(rules, '.json'));
            shown.iconPath = new vscode.ThemeIcon('device-desktop');
            shown.tooltip = vscode.l10n.t('Every situation its rules can bring it to, as its own window');
            shown.command = { title: '', command: 'rulealize.screens', arguments: [vscode.Uri.file(rules)] };
            // Where the rules differ from what was last committed, what that did is at the top of what this opens.
            const committed = await this.committed.text(rules);
            if (committed !== undefined && committed.replace(/\r\n/g, '\n') !== fs.readFileSync(rules, 'utf8').replace(/\r\n/g, '\n')) {
                shown.description = vscode.l10n.t('changed since the last commit');
                shown.tooltip = vscode.l10n.t('The rules differ from what was last committed: what that did is at the top of what this opens.');
            }
            cases.push(shown);
        }

        const tested = kind(vscode.l10n.t('Test cases'), cases, folder);
        if (cases.length === 0) {
            tested.description = vscode.l10n.t('once there are rules');
            tested.tooltip = vscode.l10n.t('There are no rules here yet, so there is no window to stand anywhere.');
        }

        return [
            kind(vscode.l10n.t('Screens'), screens, folder, 'screens'),
            kind(vscode.l10n.t('Specifications'), specifications, folder, 'specifications'),
            tested,
        ];
    }

    /**
     * The line of the screen or the specification: the file, and below it, where it differs from
     * what was last committed, what changed of it beyond binding it to the rules, a line each.
     */
    private async blueprint(at: string, icon: string, opens?: { command: string; arguments: unknown[] }): Promise<Item> {
        const beyond = await this.beyond(at);
        const item = file(at, icon, opens, beyond?.map(said => {
            const line = new Item(said);
            line.iconPath = new vscode.ThemeIcon('edit');
            line.tooltip = said;
            return line;
        }));
        if (beyond) {
            item.description = beyond.length === 0 ? vscode.l10n.t('only bound') : vscode.l10n.t('{0} changed beyond binding', beyond.length);
        }
        return item;
    }

    /**
     * What changed of the specification or a screen beyond binding it since it was last committed,
     * as the server says it; nothing where it is as committed, was never committed, or is in no
     * repository. Each is a sentence, so that it is read before it is committed and not found after.
     */
    private async beyond(file: string): Promise<string[] | undefined> {
        if (!fs.existsSync(file)) {
            return undefined;
        }

        const before = await this.committed.text(file);
        const after = fs.readFileSync(file, 'utf8');
        if (before === undefined || before.replace(/\r\n/g, '\n') === after.replace(/\r\n/g, '\n')) {
            return undefined;
        }

        try {
            return await this.client.sendRequest<string[]>('rulealize/beyond', { name: path.basename(file), before, after });
        } catch {
            return undefined;
        }
    }

    /** Looks again whenever git's account of an application's folder changes: a commit changes what its files are read against. */
    private watch(folder: string): void {
        if (this.watched.has(folder)) {
            return;
        }

        this.watched.set(folder, new vscode.Disposable(() => undefined));
        void this.committed.watch(folder, () => this.refresh()).then(watching => this.watched.set(folder, watching));
    }
}

/** The applications in the workspace: each folder of it that has a project, or holds one that does. */
export function applications(): string[] {
    const found: string[] = [];
    for (const folder of vscode.workspace.workspaceFolders ?? []) {
        const at = folder.uri.fsPath;
        if (projectIn(at)) {
            found.push(at);
            continue;
        }

        if (!fs.existsSync(at)) {
            continue;
        }

        for (const entry of fs.readdirSync(at, { withFileTypes: true })) {
            if (entry.isDirectory() && projectIn(path.join(at, entry.name))) {
                found.push(path.join(at, entry.name));
            }
        }
    }

    return found;
}

/** The first rule set in an application's folder, where there is one: a JSON file that says it is one. */
export function ruleSetIn(folder: string): string | undefined {
    return ruleSetsIn(folder)[0];
}

/**
 * Adds a window or a specification to an application, empty, under the name asked for, and opens
 * it in its editor. A window is shown from the start until its XAML binds when the rules show it;
 * a specification binds nothing until somebody binds it. Which application is the line's it was
 * asked from, or the one in the workspace; what it is called is asked, unless a caller — the tests — says.
 */
async function added(item: Item | undefined, what: 'window' | 'specification', called?: string): Promise<vscode.Uri | undefined> {
    const folder = item?.folder ?? applications()[0];
    if (!folder) {
        void vscode.window.showWarningMessage(vscode.l10n.t('There is no application here. Make one with New application, or open a rule set.'));
        return undefined;
    }

    const first = what === 'specification' && specificationsIn(folder).length === 0;
    const name = called ?? (first ? 'specification' : await vscode.window.showInputBox({
        prompt: what === 'window'
            ? vscode.l10n.t('What the window is called: its file, and its title until it is given another.')
            : vscode.l10n.t('What the specification is called: its file.'),
        validateInput: value => /^[A-Za-z0-9_-]+$/.test(value)
            ? undefined
            : vscode.l10n.t('Letters, digits, hyphens and underscores.'),
    }));
    if (!name) {
        return undefined;
    }

    const file = path.join(folder, what === 'window' ? `${name}.axaml` : first ? specificationFile : `${name}${specificationSuffix}`);
    if (fs.existsSync(file)) {
        void vscode.window.showWarningMessage(vscode.l10n.t('{0} is there already.', file));
        return undefined;
    }

    fs.writeFileSync(file, what === 'window'
        ? [
            '<Window xmlns="https://github.com/avaloniaui"',
            '        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
            `        Title="${name}"`,
            '        Width="480" Height="360">',
            '</Window>',
            '',
        ].join('\n')
        : JSON.stringify({ $schema: 'rulealize-studio/state-machine/v1', states: {}, transitions: {}, notes: {} }, undefined, 2) + '\n');

    const uri = vscode.Uri.file(file);
    await vscode.commands.executeCommand('vscode.openWith', uri, what === 'window' ? Screen.viewType : Specification.viewType);
    return uri;
}

/**
 * The line of one kind of thing in an application — its screens, its specifications, its test
 * cases — open, with them below it. Where something of the kind is added, the line says so in its
 * context, and the + on it adds one to the application in its folder.
 */
function kind(label: string, below: Item[], folder: string, context?: string): Item {
    const item = new Item(label, below);
    item.folder = folder;
    item.contextValue = context;
    return item;
}

/**
 * Deletes a window or a specification from the line it is on, once the person has said yes: into
 * the trash, where it can be had back, and git still has it as last committed. Its editor is closed
 * with it. The last window is not deleted, since an application opens with a window or not at all.
 * Answers whether it was deleted; a caller — the tests — may say yes for the person.
 */
async function deleted(item: Item | undefined, sure = false): Promise<boolean> {
    const uri = item?.resourceUri;
    if (!uri || !fs.existsSync(uri.fsPath)) {
        return false;
    }

    const name = path.basename(uri.fsPath);
    const folder = path.dirname(uri.fsPath);
    if (name.endsWith('.axaml') && fs.readdirSync(folder).filter(n => n.endsWith('.axaml')).length <= 1) {
        void vscode.window.showWarningMessage(vscode.l10n.t('An application needs at least one window, so {0} is not deleted.', name));
        return false;
    }

    const yes = vscode.l10n.t('Delete');
    if (!sure && await vscode.window.showWarningMessage(
        vscode.l10n.t('Delete {0}? It goes to the trash, and git still has it as last committed.', name), { modal: true }, yes) !== yes) {
        return false;
    }

    const open = vscode.window.tabGroups.all.flatMap(group => group.tabs).filter(tab => {
        const input = tab.input as { uri?: vscode.Uri } | undefined;
        return input?.uri?.toString() === uri.toString();
    });
    await vscode.window.tabGroups.close(open);
    await vscode.workspace.fs.delete(uri, { useTrash: true });
    return true;
}

/** The project in a folder, which makes it an application's. */
function projectIn(folder: string): string | undefined {
    const name = fs.existsSync(folder) ? fs.readdirSync(folder).find(n => n.endsWith('.csproj')) : undefined;
    return name && path.join(folder, name);
}

/** A line that opens a file, called by its name, with what is said of it below it, closed. */
function file(at: string, icon?: string, opens?: { command: string; arguments: unknown[] }, below: Item[] = []): Item {
    const item = new Item(path.basename(at), below, false);
    item.resourceUri = vscode.Uri.file(at);
    item.tooltip = at;
    if (icon) {
        item.iconPath = new vscode.ThemeIcon(icon);
    }
    item.command = { title: '', ...(opens ?? { command: 'vscode.open', arguments: [vscode.Uri.file(at)] }) };
    return item;
}
