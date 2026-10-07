// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import { execFile } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';

// What this extension runs, on a machine that had only VS Code on it. The server is inside the
// .vsix, framework-dependent, one package for every platform. A .NET runtime to run it is the
// one on the machine, or else acquired by the .NET Install Tool into that extension's own
// storage, as the C# extension acquires its own. The SDK is asked for only where something is
// built or made, and installed by the same tool. ruledger and rulealize are fetched at the
// versions pinned here into this extension's own storage, never globally, since a design's form
// follows the Ruledger that wrote it. The SDK and the two tools are fetched after asking; nothing
// is fetched before it is needed. A setting naming the server, a tool or a folder of packages
// still wins, for whoever works on one of them.

/** The .NET everything here runs on, and an application is built with. */
const channel = '10.0';

/** The .NET Install Tool, which acquires a runtime and installs the SDK. */
const installTool = 'ms-dotnettools.vscode-dotnet-runtime';

/** The two tools, at the versions this extension was tried with; ruledger at the one an application's template pins. */
const pinned = {
    ruledger: { package: 'Ruledger.Cli', version: '1.4.0' },
    rulealize: { package: 'Rulealize.Cli', version: '0.13.0' },
};

/** What is fetched, as the person is asked for it: the SDK's name here, and what it is said as by {@link named}. */
const sdk = 'the .NET SDK 10.0';

/**
 * Where each program this extension runs comes from, and fetching what is not there yet. One per
 * window, made when the extension starts.
 */
export class Installed {
    private runtimeFound: Promise<string | undefined> | undefined;
    private sdkFound: string | undefined;
    private readonly declined = new Set<string>();
    private queue: Promise<unknown> = Promise.resolve();

    constructor(private readonly context: vscode.ExtensionContext) {}

    /**
     * The server: a setting naming it; while the extension is worked on, the one built in this
     * repository beside it; otherwise the one packed inside the .vsix.
     */
    server(): string {
        const named = setting('server');
        if (named) {
            return named;
        }

        return this.packed()
            ? path.join(this.context.extensionPath, 'server', 'RulealizeStudio.Server.dll')
            : path.join(this.context.extensionPath, '..', '..', 'src', 'RulealizeStudio.Server', 'bin', 'Debug', 'net10.0', 'RulealizeStudio.Server.dll');
    }

    /**
     * The folder of packages a new application restores RulealizeStudio's libraries and the server's
     * tool from, while they are not on nuget.org: a setting naming it; while the extension is worked
     * on, feed/ in this repository, where packing them puts them; otherwise the ones packed inside
     * the .vsix, copied where they outlive an update of it, since an application's nuget.config
     * names the folder. Nothing where there is none.
     */
    feed(): string | undefined {
        const named = setting('feed');
        if (named) {
            return named;
        }

        if (!this.packed()) {
            const here = path.join(this.context.extensionPath, '..', '..', 'feed');
            return fs.existsSync(here) ? here : undefined;
        }

        const inside = path.join(this.context.extensionPath, 'feed');
        const kept = path.join(this.context.globalStorageUri.fsPath, 'feed');
        fs.mkdirSync(kept, { recursive: true });
        for (const name of fs.readdirSync(inside)) {
            if (!fs.existsSync(path.join(kept, name))) {
                fs.copyFileSync(path.join(inside, name), path.join(kept, name));
            }
        }

        return kept;
    }

    /** An example packed with the extension, by its path from the extension's own folder, as the walkthrough names it. */
    example(name: string): string {
        return path.resolve(this.context.extensionPath, name);
    }

    /**
     * A dotnet that runs what targets .NET 10 — the server, and the tools — without asking: the one
     * on the path or where an installer put one, where it has that runtime, or else one the .NET
     * Install Tool acquires into its own storage, which nothing else on the machine sees. Nothing
     * where none can be had, and why is said.
     */
    runtime(): Promise<string | undefined> {
        return this.runtimeFound ??= (async () => {
            for (const dotnet of ['dotnet', ...installedGlobally()]) {
                if ((dotnet === 'dotnet' || fs.existsSync(dotnet)) && await has(dotnet, '--list-runtimes', /^Microsoft\.NETCore\.App 10\./m)) {
                    return dotnet;
                }
            }

            return vscode.window.withProgress(
                { location: vscode.ProgressLocation.Notification, title: vscode.l10n.t('Fetching the .NET runtime Rulealize runs on') },
                async () => {
                    try {
                        await this.installTool();
                        const acquired = await vscode.commands.executeCommand<{ dotnetPath: string } | undefined>('dotnet.acquire', {
                            version: channel,
                            requestingExtensionId: this.context.extension.id,
                            mode: 'runtime',
                        });
                        if (!acquired?.dotnetPath) {
                            throw new Error(vscode.l10n.t('the .NET Install Tool answered with no runtime.'));
                        }

                        return acquired.dotnetPath;
                    } catch (error) {
                        void vscode.window.showErrorMessage(
                            vscode.l10n.t('The .NET runtime Rulealize runs on could not be fetched, and nothing of Rulealize works without it: {0}', error instanceof Error ? error.message : String(error)));
                        return undefined;
                    }
                });
        })();
    }

    /**
     * A dotnet with the SDK, for building an application or making one; asked for first where
     * there is none, saying what for. Nothing where the person said no, or it could not be had.
     */
    sdk(forWhat: string): Promise<string | undefined> {
        return this.alone(async () => {
            const found = await this.sdkOnMachine();
            if (found) {
                return found;
            }

            if (!await this.ask(forWhat, [sdk])) {
                return undefined;
            }

            return this.installSdk();
        });
    }

    /**
     * ruledger or rulealize: a setting naming it, or the pinned version in this extension's own
     * storage — fetched there with the SDK after asking, saying what for, and the SDK with it where
     * there is none yet. Nothing where the person said no, or it could not be had.
     */
    tool(name: keyof typeof pinned, forWhat: string): Promise<string | undefined> {
        const named = setting(name);
        if (named) {
            return Promise.resolve(named);
        }

        const { package: id, version } = pinned[name];
        const folder = path.join(this.context.globalStorageUri.fsPath, 'tools', `${id.toLowerCase()}.${version}`);
        const command = path.join(folder, process.platform === 'win32' ? `${name}.exe` : name);
        if (fs.existsSync(command)) {
            return Promise.resolve(command);
        }

        return this.alone(async () => {
            if (fs.existsSync(command)) {
                return command;
            }

            const found = await this.sdkOnMachine();
            if (!await this.ask(forWhat, found ? [`${name} ${version}`] : [sdk, `${name} ${version}`])) {
                return undefined;
            }

            const dotnet = found ?? await this.installSdk();
            if (!dotnet) {
                return undefined;
            }

            // VS Code names the extension's storage but does not make it, and dotnet is started there.
            fs.mkdirSync(this.context.globalStorageUri.fsPath, { recursive: true });
            const fetched = await vscode.window.withProgress(
                { location: vscode.ProgressLocation.Notification, title: vscode.l10n.t('Fetching {0} {1}', name, version) },
                () => run(dotnet, ['tool', 'install', id, '--version', version, '--tool-path', folder], this.context.globalStorageUri.fsPath, this.env()));
            if (fetched.code !== 0) {
                void vscode.window.showErrorMessage(vscode.l10n.t('{0} {1} was not fetched: {2}', name, version, fetched.text));
                return undefined;
            }

            return command;
        });
    }

    /**
     * The environment a program here is started in: where the SDK was installed in this session,
     * it is put first on the path, and is where an apphost — a tool's — finds the runtime.
     */
    env(): NodeJS.ProcessEnv {
        if (!this.sdkFound || this.sdkFound === 'dotnet') {
            return process.env;
        }

        const root = path.dirname(this.sdkFound);
        const name = Object.keys(process.env).find(key => key.toUpperCase() === 'PATH') ?? 'PATH';
        return { ...process.env, DOTNET_ROOT: root, [name]: `${root}${path.delimiter}${process.env[name] ?? ''}` };
    }

    /** Whether this is the extension as installed from a .vsix, rather than run from this repository. */
    private packed(): boolean {
        return this.context.extensionMode === vscode.ExtensionMode.Production;
    }

    /** A dotnet with a .NET 10 SDK already on the machine: on the path, or where an installer put one that this window's path does not have yet. */
    private async sdkOnMachine(): Promise<string | undefined> {
        if (this.sdkFound) {
            return this.sdkFound;
        }

        for (const dotnet of ['dotnet', ...installedGlobally()]) {
            if ((dotnet === 'dotnet' || fs.existsSync(dotnet)) && await has(dotnet, '--list-sdks', /^10\./m)) {
                return this.use(dotnet);
            }
        }

        return undefined;
    }

    /** Installs the SDK for everybody on the machine, by the .NET Install Tool, without asking again. */
    private async installSdk(): Promise<string | undefined> {
        return vscode.window.withProgress(
            { location: vscode.ProgressLocation.Notification, title: vscode.l10n.t('Installing {0}', named(sdk)) },
            async () => {
                try {
                    await this.installTool();
                    const installed = await vscode.commands.executeCommand<{ dotnetPath: string } | undefined>('dotnet.acquireGlobalSDK', {
                        version: channel,
                        requestingExtensionId: this.context.extension.id,
                        installType: 'global',
                    });
                    return installed?.dotnetPath ? this.use(installed.dotnetPath) : undefined;
                } catch (error) {
                    void vscode.window.showErrorMessage(vscode.l10n.t('{0} was not installed: {1}', named(sdk), error instanceof Error ? error.message : String(error)));
                    return undefined;
                }
            });
    }

    /**
     * The SDK found or installed is the one used from here on; where it is not on this window's
     * path, a terminal opened in it — an agent's — has it first on its own.
     */
    private use(dotnet: string): string {
        this.sdkFound = dotnet;
        if (dotnet !== 'dotnet') {
            this.context.environmentVariableCollection.prepend('PATH', `${path.dirname(dotnet)}${path.delimiter}`);
            this.context.environmentVariableCollection.replace('DOTNET_ROOT', path.dirname(dotnet));
        }

        return dotnet;
    }

    /** The .NET Install Tool, installed from the marketplace where it is not there yet, as the C# extension depends on it. */
    private async installTool(): Promise<void> {
        if (!vscode.extensions.getExtension(installTool)) {
            await vscode.commands.executeCommand('workbench.extensions.installExtension', installTool);
            const end = Date.now() + 60_000;
            while (!vscode.extensions.getExtension(installTool) && Date.now() < end) {
                await new Promise(resolve => setTimeout(resolve, 200));
            }
        }

        const tool = vscode.extensions.getExtension(installTool);
        if (!tool) {
            throw new Error(vscode.l10n.t('the .NET Install Tool, {0}, is not installed, and installing it did not finish.', installTool));
        }

        await tool.activate();
    }

    /**
     * Asks before fetching anything, saying what it is for and where each goes. A no is kept for
     * the rest of the window's life, rather than asked again at every change.
     */
    private async ask(forWhat: string, needed: string[]): Promise<boolean> {
        if (needed.some(n => this.declined.has(n))) {
            void vscode.window.showWarningMessage(vscode.l10n.t('{0} needs {1}, which you chose not to fetch. Reload the window to be asked again.', forWhat, list(needed.map(named))));
            return false;
        }

        const where = needed.map(n => n === sdk
            ? vscode.l10n.t('{0} is installed for everybody on this computer, by the .NET Install Tool; the computer may ask whether it may.', capital(named(sdk)))
            : vscode.l10n.t('{0} goes into this extension\'s own folder, and nowhere else.', capital(n)));
        const fetch = vscode.l10n.t('Fetch');
        const chosen = await vscode.window.showInformationMessage(
            needed.length === 1
                ? vscode.l10n.t('{0} needs {1}, which is not on this computer yet. Fetch it?', forWhat, list(needed.map(named)))
                : vscode.l10n.t('{0} needs {1}, which are not on this computer yet. Fetch them?', forWhat, list(needed.map(named))),
            { modal: true, detail: where.join('\n') },
            fetch);
        if (chosen !== fetch) {
            needed.forEach(n => this.declined.add(n));
            return false;
        }

        return true;
    }

    /** One fetch at a time, so that two things asking for the same tool ask once. */
    private alone<T>(work: () => Promise<T>): Promise<T> {
        const next = this.queue.then(work);
        this.queue = next.catch(() => undefined);
        return next;
    }
}

let current: Installed | undefined;

/** Made once, when the extension starts. */
export function install(context: vscode.ExtensionContext): Installed {
    return current = new Installed(context);
}

/** The one made when the extension started. */
export function installed(): Installed {
    if (!current) {
        throw new Error(vscode.l10n.t('The extension has not started.'));
    }

    return current;
}

/** A program started where it is asked, and everything it said. */
export function run(command: string, args: string[], cwd: string, env?: NodeJS.ProcessEnv): Promise<{ code: number; out: string; text: string }> {
    return new Promise(resolve => {
        execFile(command, args, { cwd, env, maxBuffer: 16 * 1024 * 1024 }, (error, stdout, stderr) => {
            const code = error ? (typeof error.code === 'number' ? error.code : 1) : 0;
            resolve({ code, out: stdout, text: `${stdout}${stderr}`.trim() || (error?.message ?? '') });
        });
    });
}

/** Where an installer puts a dotnet for everybody, which a window started before it ran does not have on its path. */
function installedGlobally(): string[] {
    switch (process.platform) {
        case 'win32':
            return [path.join(process.env.ProgramFiles ?? 'C:\\Program Files', 'dotnet', 'dotnet.exe')];
        case 'darwin':
            return ['/usr/local/share/dotnet/dotnet'];
        default:
            return ['/usr/share/dotnet/dotnet', '/usr/lib/dotnet/dotnet'];
    }
}

async function has(dotnet: string, listing: string, line: RegExp): Promise<boolean> {
    const said = await run(dotnet, [listing], process.cwd());
    return said.code === 0 && line.test(said.out);
}

function setting(name: string): string {
    return vscode.workspace.getConfiguration('rulealize').get<string>(name)?.trim() ?? '';
}

function list(items: string[]): string {
    return items.length < 2 ? items.join('') : vscode.l10n.t('{0} and {1}', items.slice(0, -1).join(', '), items[items.length - 1]);
}

/** What something fetched is said as: the SDK in the words of the language VS Code is set to, a tool by its name. */
function named(needed: string): string {
    return needed === sdk ? vscode.l10n.t('the .NET SDK 10.0') : needed;
}

function capital(text: string): string {
    return text.charAt(0).toUpperCase() + text.slice(1);
}
