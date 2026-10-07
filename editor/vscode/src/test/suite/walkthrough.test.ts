// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as assert from 'assert';
import { execFileSync } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { extension, until } from './help';

// The walkthrough on VS Code's welcome page, followed in a folder with nothing in it: each step's
// links run as the walkthrough writes them, and what a step leaves to the person's agent is
// written into the files the way an agent writes it. Needs the example pack.js writes, the SDK, ruledger on the path, the packages
// packed into feed/, and somewhere to fetch from.

interface Step { id: string; description: string }

const root = path.resolve(__dirname, '..', '..', '..');
const read = (file: string) => JSON.parse(fs.readFileSync(path.join(root, file), 'utf8'));
const manifest = read('package.json');
const nls: Record<string, string> = read('package.nls.json');
const nlsJa: Record<string, string> = read('package.nls.ja.json');

/** A manifest string in the words of a language's nls file: `%key%` is what the file says for the key. */
const worded = (words: Record<string, string>) => (text: string) => text.replace(/^%([\w.-]+)%$/, (whole, key: string) => words[key] ?? whole);
const steps: Step[] = manifest.contributes.walkthroughs[0].steps.map((s: Step) => ({ ...s, description: worded(nls)(s.description) }));

suite('The walkthrough', () => {
    suiteSetup(() => extension());

    test('every link in it, and in the view before there is an application, is a command the extension or VS Code has', async () => {
        const known = new Set(await vscode.commands.getCommands(true));
        for (const step of steps) {
            for (const { command } of links(step)) {
                assert.ok(known.has(command), `${step.id} links to ${command}, which nothing registers.`);
            }
        }

        // The view's welcome: what to begin with, in English and Japanese alike, the walkthrough among it.
        for (const welcome of manifest.contributes.viewsWelcome as { contents: string; when: string }[]) {
            const english = links({ id: welcome.when, description: worded(nls)(welcome.contents) });
            assert.ok(english.some(l => l.command === 'workbench.action.openWalkthrough' && l.args[0] === `${manifest.publisher}.${manifest.name}#rules`),
                `The view's welcome where ${welcome.when} has no link to the walkthrough.`);
            for (const { command } of english) {
                assert.ok(known.has(command), `The view's welcome where ${welcome.when} links to ${command}, which nothing registers.`);
            }
            assert.deepStrictEqual(
                links({ id: welcome.when, description: worded(nlsJa)(welcome.contents) }).map(l => JSON.stringify(l)),
                english.map(l => JSON.stringify(l)),
                `The Japanese of the view's welcome where ${welcome.when} links elsewhere than the English.`);
        }
    });

    test('is called RulealizeStudio.Avalonia, and nothing calls it Rulealize Studio', () => {
        assert.strictEqual(manifest.displayName, 'RulealizeStudio.Avalonia');
        const shown = [
            'package.json', 'package.nls.json', 'package.nls.ja.json', path.join('l10n', 'bundle.l10n.ja.json'),
            ...fs.readdirSync(path.join(root, 'walkthrough')).map(name => path.join('walkthrough', name)),
            ...fs.readdirSync(path.join(root, 'src')).filter(name => name.endsWith('.ts')).map(name => path.join('src', name)),
        ];
        for (const file of shown) {
            assert.doesNotMatch(fs.readFileSync(path.join(root, file), 'utf8'), /Rulealize Studio/, file);
        }
    });

    test('every word the extension shows has a Japanese one', () => {
        // Every sentence the host says through vscode.l10n.t and a page says through its own t,
        // found by the literal each is called with, the way the keys of a bundle are found.
        const src = path.join(root, 'src');
        const keys = new Set<string>();
        const unescape = (text: string) => text.replace(/\\(.)/g, (_, c: string) => ({ n: '\n', r: '\r', t: '\t' } as Record<string, string>)[c] ?? c);
        for (const file of fs.readdirSync(src).filter(name => name.endsWith('.ts'))) {
            const text = fs.readFileSync(path.join(src, file), 'utf8');
            for (const said of [/l10n\.t\(\s*'((?:[^'\\]|\\.)*)'/g, /\bt\(\s*'((?:[^'\\]|\\.)*)'/g]) {
                for (const m of text.matchAll(said)) {
                    keys.add(unescape(m[1]));
                }
            }
        }

        assert.ok(keys.size > 0, 'No sentence is said through l10n.');
        const ja: Record<string, string> = read(path.join('l10n', 'bundle.l10n.ja.json'));
        const placeholders = (text: string) => (text.match(/\{\d+\}/g) ?? []).sort().join();
        for (const key of keys) {
            assert.ok(key in ja, `l10n/bundle.l10n.ja.json has no Japanese for: ${key}`);
            assert.strictEqual(placeholders(ja[key]), placeholders(key), `The Japanese for "${key}" does not take the same values.`);
        }

        // The manifest's own words: each %key% in it is in the English, and each English one in the Japanese,
        // with the same links in a walkthrough step.
        const used = [...JSON.stringify(manifest).matchAll(/"%([\w.-]+)%"/g)].map(m => m[1]);
        for (const key of used) {
            assert.ok(key in nls, `package.json says %${key}%, which package.nls.json does not have.`);
        }
        for (const key of Object.keys(nls)) {
            assert.ok(key in nlsJa, `package.nls.ja.json has no Japanese for ${key}.`);
        }
        for (const step of manifest.contributes.walkthroughs[0].steps as Step[]) {
            const commands = (words: Record<string, string>) => links({ ...step, description: worded(words)(step.description) }).map(l => JSON.stringify(l));
            assert.deepStrictEqual(commands(nlsJa), commands(nls), `The Japanese of ${step.id} links elsewhere than the English.`);
        }
    });

    test("takes a folder with nothing in it to an application's test cases, and what a first change did", async () => {
        const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
        assert.ok(folder, 'The window was not opened on a folder.');
        assert.deepStrictEqual(fs.readdirSync(folder), [], 'The folder is not empty.');

        // Opening a folder is the step before this window, and following its link would reload it.
        assert.deepStrictEqual(steps.map(s => s.id), ['folder', 'start', 'screens', 'change', 'own']);

        // The example is an application made from the template, with its own files in it, built;
        // no repository is made for it.
        await follow('start');
        const application = path.join(folder, 'Countdown');
        assert.ok(fs.existsSync(path.join(application, 'Countdown.csproj')), 'Starting from an example did not make an application.');
        assert.match(fs.readFileSync(path.join(application, 'specification.json'), 'utf8'), /"counting"/, "The template's empty specification is where the example's own should be.");
        assert.ok(!fs.existsSync(path.join(application, '.git')), 'Starting from an example made a repository.');

        const ruleSet = path.join(application, 'countdown.json');

        await follow('screens');
        const screens = (await extension()).screens(vscode.Uri.file(ruleSet));
        assert.ok(screens, 'The step did not open the test cases.');
        await screens.ready;
        const { situations } = screens.said('situations') as { situations: { state: string; route: string[] }[] };
        assert.ok(situations.length > 1, JSON.stringify(screens.said('situations')));
        const chosen = situations.find(s => s.route.length === 1)!;
        await screens.choose(chosen.state);
        const shown = screens.said('shown') as { presses: unknown[]; picture?: string; trouble?: string };
        assert.strictEqual(shown.trouble, undefined, shown.trouble);
        assert.strictEqual(shown.presses.length, 1);

        // Committed as it comes, the way the change step says to, in Source Control; a change is
        // read against that.
        const git = (...args: string[]) => execFileSync('git', args, { cwd: folder, encoding: 'utf8' });
        git('init', '-q', '-b', 'main');
        git('add', '-A');
        git('-c', 'user.name=Rulealize', '-c', 'user.email=rulealize@example.com', '-c', 'commit.gpgsign=false', 'commit', '-q', '-m', 'Countdown, as it comes');

        // The change the person asks of their agent, written into the rules as the agent writes it,
        // and read at the top of the test cases.
        fs.writeFileSync(ruleSet, fs.readFileSync(ruleSet, 'utf8').replace('"count": 3', '"count": 4'));
        type Changes = { changed?: { gained: { step: string }[] }[] };
        await until(() => ((screens.said('changes') as Changes).changed?.length ?? 0) > 0, 'What the change did was not listed.', 60_000);
        assert.ok((screens.said('changes') as Changes).changed!.some(c => c.gained.some(m => m.step === 'add(n: 4)')), JSON.stringify(screens.said('changes')));
    });
});

/** Runs a step's links, as clicking them would. */
async function follow(id: string): Promise<void> {
    const step = steps.find(s => s.id === id)!;
    for (const { command, args } of links(step)) {
        await vscode.commands.executeCommand(command, ...args);
    }
}

function links(step: Step): { command: string; args: unknown[] }[] {
    return [...step.description.matchAll(/\]\(command:([\w.]+)(?:\?([^)]+))?\)/g)].map(m => ({
        command: m[1],
        args: m[2] ? JSON.parse(decodeURIComponent(m[2])) : [],
    }));
}
