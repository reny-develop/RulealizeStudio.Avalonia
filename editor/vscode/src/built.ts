// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import { createHash } from 'crypto';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { saysSpecification } from './blueprint';
import { installed, run } from './installed';

/** An application's folder built somewhere of its own: where its folder is there, and where its build wrote. */
export interface Built {
    folder: string;
    plugins: string;
}

/**
 * The application before a change and after it, each built from what it is written in — the rule
 * set, the screen, the label documents, the project — rather than taken from whatever was last
 * built in its folder.
 *
 * Before is the folder as last committed: the repository at `HEAD`, written out with `git archive`
 * and built there. After is the same, with the application's folder as it is saved now put over
 * it. Neither touches the folder's own build, and neither can show a screen built before the rules
 * it is shown with changed. Each is kept by the commit it was written out at, so asking again
 * builds only what changed since.
 *
 * Git is asked here and not by the person: they never run it for this.
 *
 * Kept near the root of the drive's temporary folder, by repository and by commit, because a
 * build writes folders inside folders and Windows still stops a path at 260 characters for some
 * of the tools a build runs.
 */
export class Builds {
    constructor(private readonly storage: string) { }

    /** The application as last committed, built; or why it cannot be. */
    async before(folder: string, plugins: string, said: (what: string) => void): Promise<Built | string> {
        const at = await this.written(folder, 'before', said);
        if (typeof at === 'string') {
            return at;
        }

        return this.build(at, plugins, said, vscode.l10n.t('as last committed'));
    }

    /** The application as its folder is saved now, built; or why it cannot be. */
    async after(folder: string, plugins: string, said: (what: string) => void): Promise<Built | string> {
        const at = await this.written(folder, 'after', said);
        if (typeof at === 'string') {
            return at;
        }

        copy(folder, at.folder, plugins);
        return this.build(at, plugins, said, vscode.l10n.t('as saved now'));
    }

    /** The repository at `HEAD`, written out once per commit and per side, and where the application's folder is in it. */
    private async written(folder: string, side: 'before' | 'after', said: (what: string) => void): Promise<{ folder: string } | string> {
        const top = await run('git', ['rev-parse', '--show-toplevel'], folder);
        const head = await run('git', ['rev-parse', 'HEAD'], folder);
        if (top.code !== 0 || head.code !== 0) {
            return vscode.l10n.t('{0} has no commit to show the window before the change from: what was last committed is what a change is read against.', path.basename(folder));
        }

        const commit = head.out.trim().slice(0, 12);
        const repository = path.join(this.storage, createHash('sha256').update(path.resolve(top.out.trim()).toLowerCase()).digest('hex').slice(0, 8));
        const root = path.join(repository, commit, side);
        const inside = path.join(root, path.relative(top.out.trim(), folder));
        if (!fs.existsSync(root)) {
            // What an earlier commit was built as is not asked for again once there is a later one.
            for (const earlier of fs.existsSync(repository) ? fs.readdirSync(repository) : []) {
                if (earlier !== commit) {
                    try {
                        fs.rmSync(path.join(repository, earlier), { recursive: true, force: true });
                    } catch {
                        // Held by something still running; it is taken away the next time.
                    }
                }
            }

            said(vscode.l10n.t('Writing out {0} as last committed…', path.basename(folder)));
            fs.mkdirSync(root, { recursive: true });
            const tar = `${root}.tar`;
            const archived = await run('git', ['archive', '--format=tar', '-o', tar, 'HEAD'], top.out.trim());
            // Named from where it is extracted, so that no tar takes a drive letter for a host to reach.
            const extracted = archived.code === 0 ? await run('tar', ['-xf', path.join('..', path.basename(tar))], root) : archived;
            fs.rmSync(tar, { force: true });
            if (extracted.code !== 0) {
                fs.rmSync(root, { recursive: true, force: true });
                return extracted.text;
            }
        }

        if (side === 'before' && !fs.existsSync(inside)) {
            return vscode.l10n.t('{0} is not in what was last committed, so there is no window before the change to show.', path.basename(folder));
        }

        return { folder: inside };
    }

    private async build(at: { folder: string }, plugins: string, said: (what: string) => void, as: string): Promise<Built | string> {
        const dotnet = await installed().sdk(vscode.l10n.t('Showing the window before and after a change'));
        if (!dotnet) {
            return vscode.l10n.t('{0} {1} is not built: building it needs the .NET SDK.', path.basename(at.folder), as);
        }

        said(vscode.l10n.t('Building {0} {1}…', path.basename(at.folder), as));
        // No build server is left running afterwards, holding files in a folder that is taken away
        // once a later commit is built.
        const built = await run(dotnet, ['build', at.folder, '-nologo', '-v', 'q', '-nodeReuse:false', '-p:UseSharedCompilation=false'], at.folder, installed().env());
        if (built.code !== 0) {
            return vscode.l10n.t('{0} {1} does not build:\n{2}', path.basename(at.folder), as, built.text);
        }

        return { folder: at.folder, plugins: path.join(at.folder, plugins) };
    }
}

/**
 * The application's folder as it is saved now, put over the one written out: every file in it
 * written where it differs, and every file only the written-out one has taken away — but nothing
 * either build wrote, which is the build's and not the application's.
 */
function copy(from: string, to: string, plugins: string): void {
    const built = new Set(['bin', 'obj', plugins.split(/[\\/]/)[0]]);
    const walk = (relative: string): void => {
        const source = path.join(from, relative);
        const target = path.join(to, relative);
        fs.mkdirSync(target, { recursive: true });
        const here = new Set<string>();
        for (const entry of fs.readdirSync(source, { withFileTypes: true })) {
            if (relative === '' && built.has(entry.name)) {
                continue;
            }

            here.add(entry.name);
            if (entry.isDirectory()) {
                walk(path.join(relative, entry.name));
            } else if (entry.isFile()) {
                const text = fs.readFileSync(path.join(source, entry.name));
                const there = path.join(target, entry.name);
                if (!fs.existsSync(there) || !fs.readFileSync(there).equals(text)) {
                    fs.writeFileSync(there, text);
                }
            }
        }

        for (const entry of fs.readdirSync(target, { withFileTypes: true })) {
            if (!here.has(entry.name) && !(relative === '' && built.has(entry.name))) {
                fs.rmSync(path.join(target, entry.name), { recursive: true, force: true });
            }
        }
    };

    walk('');
}

/**
 * Builds an application's folder where it is, as `dotnet build` there does, where it was never
 * built — a folder just made, or one somebody was given — or where what it is built from was written
 * after it was last built: its project, its rules and what is said beside them, its screen. So its
 * window is shown as it is written, and its vocabularies are beside what it built, without anybody
 * building it — a build that failed, or one an agent did not make, is not what is shown. Where it
 * was built and does not build again, it is shown as last built: {@link unbuilt} says so, and why,
 * until it builds, and {@link onUnbuilt} says when that changes. Answers nothing once there is a
 * build, or why there is not.
 *
 * Asked again for a folder while it is being built — the page listing it once on opening and again
 * as an agent writes the files it lists — it waits for that build rather than start a second over
 * the same output, which the compiler would refuse.
 */
export function build(folder: string, plugins: string, forWhat: string): Promise<string | undefined> {
    const key = process.platform === 'win32' ? path.resolve(folder).toLowerCase() : path.resolve(folder);
    const next = (building.get(key) ?? Promise.resolve(undefined))
        .catch(() => undefined)
        .then(() => buildOnce(folder, plugins, forWhat));
    building.set(key, next);
    void next.finally(() => {
        if (building.get(key) === next) {
            building.delete(key);
        }
    });
    return next;
}

/**
 * Builds an application's folder into a folder of its own, as `dotnet build --output` there does,
 * leaving the folder's own build output as it was: for drawing its screen where it was never built.
 * Built where it is, a build made before there are any rules would stand for the application once
 * there are, since {@link build} shows a folder as it was last built; built apart, the first build
 * in the folder is still the one made with its rules. Answers nothing once there is a build, or why
 * there is not.
 */
export function buildApart(folder: string, output: string, forWhat: string): Promise<string | undefined> {
    const key = process.platform === 'win32' ? path.resolve(folder).toLowerCase() : path.resolve(folder);
    const next = (building.get(key) ?? Promise.resolve(undefined))
        .catch(() => undefined)
        .then(async () => {
            const dotnet = await installed().sdk(forWhat);
            if (!dotnet) {
                return vscode.l10n.t('{0} is not built: building it needs the .NET SDK.', path.basename(folder));
            }

            const built = await vscode.window.withProgress(
                { location: vscode.ProgressLocation.Notification, title: vscode.l10n.t('Building {0}', path.basename(folder)) },
                () => run(dotnet, ['build', '-nologo', '-v', 'q', '--output', output, '-nodeReuse:false', '-p:UseSharedCompilation=false'], folder, installed().env()));
            return built.code === 0 ? undefined : vscode.l10n.t('{0} does not build:\n{1}', path.basename(folder), built.text);
        });
    building.set(key, next);
    void next.finally(() => {
        if (building.get(key) === next) {
            building.delete(key);
        }
    });
    return next;
}


/** The build of each folder under way, by its path. */
const building = new Map<string, Promise<string | undefined>>();

/** What a folder was last built from where building it again was tried, by its path: so that a folder that does not build is not built again until something it is built from changes. */
const tried = new Map<string, string>();

/** A folder shown as last built, since it does not build as it is written now: when it was last built, and what the build said. */
export interface Unbuilt {
    built: Date;
    said: string;
}

const failed = new Map<string, Unbuilt>();
const failing = new vscode.EventEmitter<string>();

/** Fires with a folder's path whenever whether it builds as written changes, for a page that shows it to say so. */
export const onUnbuilt = failing.event;

/** Whether a folder is shown as last built, since it does not build as it is written now; nothing where it builds, or was never built. */
export function unbuilt(folder: string): Unbuilt | undefined {
    return failed.get(keyOf(folder));
}

/** The same folder written two ways is one folder. */
function keyOf(folder: string): string {
    return process.platform === 'win32' ? path.resolve(folder).toLowerCase() : path.resolve(folder);
}

function settle(key: string, now: Unbuilt | undefined): void {
    const was = failed.get(key);
    if (now) {
        failed.set(key, now);
    } else {
        failed.delete(key);
    }
    if (was?.said !== now?.said) {
        failing.fire(key);
    }
}

/** What a folder's build is made from, as written now: its project, its rules and what is said beside them, and its windows — not a specification nor a test design, which no build reads. */
function sources(folder: string): string[] {
    return fs.readdirSync(folder)
        .filter(name => name.endsWith('.csproj') || name.endsWith('.axaml') || (name.endsWith('.json') && !name.endsWith('.test-design.json')))
        .map(name => path.join(folder, name))
        .filter(file => !file.endsWith('.json') || !saysSpecification(fs.readFileSync(file, 'utf8')));
}

async function buildOnce(folder: string, plugins: string, forWhat: string): Promise<string | undefined> {
    const projects = fs.readdirSync(folder).filter(name => name.endsWith('.csproj'));
    if (projects.length !== 1) {
        return undefined;
    }

    const output = path.join(folder, plugins, `${path.basename(projects[0], '.csproj')}.dll`);
    const before = fs.existsSync(output);
    const key = process.platform === 'win32' ? path.resolve(folder).toLowerCase() : path.resolve(folder);
    const written = sources(folder).map(file => `${file}:${fs.statSync(file).mtimeMs}`).join('|');
    if (before && !sources(folder).some(file => fs.statSync(file).mtimeMs > fs.statSync(output).mtimeMs)) {
        settle(key, undefined);
        return undefined;
    }
    if (before && tried.get(key) === written) {
        return undefined;
    }
    tried.set(key, written);

    const dotnet = await installed().sdk(forWhat);
    if (!dotnet) {
        return vscode.l10n.t('{0} is not built: building it needs the .NET SDK.', path.basename(folder));
    }

    const built = await vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: vscode.l10n.t('Building {0}', path.basename(folder)) },
        () => run(dotnet, ['build', '-nologo', '-v', 'q'], folder, installed().env()));
    if (built.code === 0) {
        settle(key, undefined);
        return undefined;
    }

    if (before) {
        settle(key, { built: fs.statSync(output).mtime, said: built.text });
        return undefined;
    }

    return vscode.l10n.t('{0} does not build:\n{1}', path.basename(folder), built.text);
}
