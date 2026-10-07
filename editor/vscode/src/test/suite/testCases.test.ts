// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as assert from 'assert';
import { execFileSync } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import type { Screens } from '../../screens';
import { build, unbuilt } from '../../built';
import { derive } from '../../ruleSet';
import { closeAll, extension, until } from './help';

// A rule set's test cases, read on the application's own window: every situation the design names,
// stood in by pressing what reached it, a value chosen to try, and what a change to the rules did,
// read before and after. A change is written into the files the way the person's agent writes it;
// nothing here edits a rule any other way. The page is not clicked: the page's object is sent what
// the page sends, and what it says back is what the page would draw.

const sample = path.resolve(__dirname, '..', '..', '..', '..', '..', 'sample');
const countdown = path.join(sample, 'RulealizeStudio.Sample.Countdown', 'countdown.json');
const signup = path.join(sample, 'RulealizeStudio.Sample.Signup', 'signup.json');
const signupDesign = signup.replace(/\.json$/, '.test-design.json');

suite("A rule set's test cases", () => {
    const written = new Map<string, string>();

    suiteSetup(async () => {
        for (const file of [countdown, signup, signupDesign]) {
            written.set(file, fs.readFileSync(file, 'utf8'));
        }

        await extension();
    });

    teardown(async () => {
        await closeAll();
        for (const [file, text] of written) {
            if (fs.readFileSync(file, 'utf8') !== text) {
                fs.writeFileSync(file, text);
            }
        }
    });

    test("choosing a situation of signup's design shows signup's window there, after the window each press that reached it was made on", async () => {
        const screens = await testCases(signup);

        const { situations } = screens.said('situations') as { situations: { state: string; route: string[]; words: string[] }[] };
        const design = JSON.parse(fs.readFileSync(signupDesign, 'utf8')) as { states: unknown[] };
        assert.strictEqual(situations.length, design.states.length);
        const chosen = situations.find(s => s.route.join(' → ') === 'setName(to: alice) → chooseSeat(seat: window)');
        assert.ok(chosen, 'No situation is reached by saving a name and choosing the window seat.');

        // Said as signup's window says it: the words of the control pressed, and the value entered.
        assert.deepStrictEqual(chosen.words, ['Save: alice', 'Choose: window']);

        await screens.choose(chosen.state);

        const shown = screens.said('shown') as { presses: { step: string; picture: string }[]; picture: string; divergences: string[]; trouble?: string };
        assert.strictEqual(shown.trouble, undefined, shown.trouble);
        assert.deepStrictEqual(shown.divergences, []);
        assert.deepStrictEqual(shown.presses.map(p => p.step), chosen.route);
        const pictures = [...shown.presses.map(p => p.picture), shown.picture];
        assert.strictEqual(new Set(pictures).size, 3);
        assert.ok(pictures.every(p => p.endsWith('.png')));
    });

    test("a situation of signup's design shows every move from it, where each leads, and its refusal as the window drew it, in the label document's words", async () => {
        const screens = await testCases(signup);

        type Listed = { state: string; route: string[]; ending: { result: string | null } | null; moves: { step: string; to: string | null; admits: Record<string, { bound: string; value: string }[]> }[]; refused: { step: string; said: string[] }[] };
        const { situations } = screens.said('situations') as { situations: Listed[] };
        const named = situations.find(s => s.route.join(' → ') === 'setName(to: alice)')!;
        const two = named.moves.find(m => m.step === 'setParty(size: 2)')!;
        assert.deepStrictEqual(situations.find(s => s.state === two.to)!.route, ['setName(to: alice)', 'setParty(size: 2)']);
        assert.deepStrictEqual(two.admits.size.map(b => `${b.bound} ${b.value}`), ['op type.int', 'min 1', 'max 6']);
        assert.deepStrictEqual(named.refused, [{ step: 'setParty(size: 1)', input: 'setParty', codes: ['party.unchanged'], said: ['The party is already that size.'], words: 'Set: 1' }]);
        assert.strictEqual(situations.filter(s => s.ending?.result === 'booked').length, 72);

        await screens.choose(named.state);

        const shown = screens.said('shown') as { moves: { step: string; at: unknown }[]; refused: { step: string; picture: string }[]; picture: string; divergences: string[]; trouble?: string };
        assert.strictEqual(shown.trouble, undefined, shown.trouble);
        assert.deepStrictEqual(shown.divergences, []);
        assert.deepStrictEqual(shown.moves.map(m => m.step), named.moves.map(m => m.step));
        assert.ok(shown.moves.every(m => m.at !== null), 'A move legal there has nothing on the screen standing for it.');
        assert.deepStrictEqual(shown.refused.map(r => r.step), ['setParty(size: 1)']);
        assert.notStrictEqual(shown.refused[0].picture, shown.picture);
    });

    test("the longest name signup's rules allow, tried where it starts, is written into the design, carried by deriving again, and read as the rest is", async () => {
        const screens = await testCases(signup);
        const committed = fs.readFileSync(signupDesign, 'utf8');

        type Listed = { state: string; route: string[]; moves: { step: string; to: string | null; admits: Record<string, { bound: string; value: string }[]> }[]; chosen: { at: number; step: string; carried: string }[] };
        const listed = () => (screens.said('situations') as { situations: Listed[] }).situations;
        const start = () => listed().find(s => s.route.length === 0)!;
        const longest = 'Christabella';
        const maxLength = start().moves.find(m => m.step === 'setName(to: ?)')!.admits.to.find(b => b.bound === 'maxLength')!.value;
        assert.strictEqual(String(longest.length), maxLength);

        // As committed, the choice is there and carried; taken out, nothing it led to is.
        const chosen = start().chosen.find(c => c.step === `setName(to: ${longest})`);
        assert.strictEqual(chosen?.carried, 'yes');
        await screens.answer({ type: 'takeOut', at: chosen.at });
        assert.ok(!fs.readFileSync(signupDesign, 'utf8').includes(longest));
        assert.strictEqual(listed().length, 91);

        // Tried where it starts, the way the page asks: written, derived again, and the design is
        // the one committed, byte for byte.
        await screens.answer({ type: 'try', state: start().state, input: 'setName', args: { to: longest } });
        const tried = screens.said('tried') as { ok: boolean; text: string };
        assert.ok(tried.ok, tried.text);
        assert.match(tried.text, /setName\(to: admin\) is not legal in #0/);
        assert.strictEqual(fs.readFileSync(signupDesign, 'utf8'), committed);
        assert.strictEqual(listed().length, 181);

        // What it led to reads as the rest does: on signup's window, by the presses that reach it.
        const seated = listed().find(s => s.route.join(' → ') === `setName(to: ${longest}) → chooseSeat(seat: window)`);
        assert.ok(seated, `Nothing is reached by saving ${longest} and choosing the window seat.`);
        await screens.choose(seated.state);
        const shown = screens.said('shown') as { presses: { step: string }[]; divergences: string[]; trouble?: string };
        assert.strictEqual(shown.trouble, undefined, shown.trouble);
        assert.deepStrictEqual(shown.divergences, []);
        assert.deepStrictEqual(shown.presses.map(p => p.step), seated.route);
    });

    // Three changes, each written into the rules as an agent writes them and read as the person
    // reads them: the situations each moved, by the moves that reach them, and one of them as the
    // window before and after — each the application built from what it was written in then, not
    // taken from the folder's build.

    test("widening countdown's domain to four shows, after the first +1, the window before and after, and that nothing on it stands for add(n: 4)", async () => {
        const { compared, entry } = await moved(countdown, text => text.replace('"count": 3', '"count": 4'), 'add(n: 1)');

        assert.deepStrictEqual(entry.gained.map(m => m.step), ['add(n: 4)']);
        assert.ok(compared.before.moves.every(m => m.step !== 'add(n: 4)'));
        assert.deepStrictEqual(compared.after.moves.find(m => m.step === 'add(n: 4)'), { step: 'add(n: 4)', window: null, at: null });
        assert.ok(compared.after.moves.find(m => m.step === 'add(n: 1)')?.at, 'The +1 the window offers after the change is not on it.');
    });

    test("raising signup's party bound to eight shows where alice has saved her name a party of seven and of eight, on the window after the change", async () => {
        const { changes, compared, entry } = await moved(signup, text => text.replace('"party": { "op": "type.int", "min": 1, "max": 6 }', '"party": { "op": "type.int", "min": 1, "max": 8 }'), 'setName(to: alice)');

        assert.deepStrictEqual(entry.gained.map(m => m.step), ['setParty(size: 7)', 'setParty(size: 8)']);
        assert.deepStrictEqual(changes.admits.map(a => [a.input, a.parameter, a.now.find(b => b.bound === 'max')?.value, a.was.find(b => b.bound === 'max')?.value]), [['setParty', 'size', '8', '6']]);
        assert.ok(compared.after.moves.find(m => m.step === 'setParty(size: 7)')?.at, 'Nothing on the window after the change stands for a party of seven.');
    });

    test("taking party.unchanged out shows where alice has saved her name a party of one taken now, and the window that refused it before", async () => {
        const unchanged = /\s*\{ "require": \{ "op": "logic\.not", "value": \{ "op": "cmp\.eq", "left": "@size", "right": "\$party" \} \},\s*"code": "party\.unchanged" \}/;
        const { compared, entry } = await moved(signup, text => text.replace(unchanged, ''), 'setName(to: alice)');

        assert.deepStrictEqual(entry.gained.map(m => m.step), ['setParty(size: 1)']);
        assert.ok(compared.before.refused.some(r => r.step === 'setParty(size: 1)'), 'The window before the change did not refuse a party of one.');
        assert.ok(compared.after.refused.every(r => r.step !== 'setParty(size: 1)'));
    });

    // A change written into the files the way an agent the person uses writes it — the element that
    // said six says eight, and so does the rule bound to it — in a copy of signup in a repository of
    // its own. What it did is listed against the design as last committed; deriving the design again
    // from the rules leaves it listed, and nothing here commits it, since committing is git's; once
    // it is committed, there is nothing left to list.

    test("a party of up to eight, written into the specification and the rules, is listed as what it did until it is committed, whoever derives the design again", async () => {
        const kept = repository();
        try {
            const screens = await testCases(path.join(kept.folder, 'signup.json'));
            const head = kept.git('rev-parse', 'HEAD');

            change(kept, 'eight', 8);
            await until(() => ((screens.said('changes') as { changed?: unknown[] }).changed?.length ?? 0) > 0, 'What the change did was not listed.', 60_000);

            const derived = await derive(path.join(kept.folder, 'signup.json'));
            assert.ok(derived.code === 0 || derived.code === 3, derived.text);
            assert.match(fs.readFileSync(path.join(kept.folder, 'signup.test-design.json'), 'utf8'), /"max": 8/);
            await screens.ready;
            assert.ok(((screens.said('changes') as { changed?: unknown[] }).changed?.length ?? 0) > 0, 'Deriving the design again took the change off the list before it was committed.');
            assert.strictEqual(kept.git('rev-parse', 'HEAD'), head);
            assert.deepStrictEqual(kept.git('diff', '--name-only').split('\n').sort(), ['signup.json', 'signup.test-design.json', 'specification.json']);

            kept.git('commit', '-q', '-am', 'A party of up to eight');
            await until(() => (screens.said('changes') as { changed?: unknown[] }).changed?.length === 0, 'Committing the change left it listed.', 60_000);
        } finally {
            await kept.dispose();
        }
    });

    // The application as it is written now is what is shown: built again once what it is built from
    // was written after its build — by an agent that did not build it, or a build that failed — and
    // built only then. Where it does not build as written, it is shown as last built.

    test("countdown written after its last build is built again before it is shown, only then, and shown as last built where it does not build", async () => {
        const folder = path.dirname(countdown);
        const output = path.join(folder, 'bin', 'Debug', 'net10.0', 'RulealizeStudio.Sample.Countdown.dll');
        const text = fs.readFileSync(countdown, 'utf8');
        const later = () => new Date(Math.max(Date.now(), fs.statSync(output).mtimeMs + 2000));
        try {
            fs.utimesSync(countdown, later(), later());
            const before = fs.statSync(output).mtimeMs;
            assert.strictEqual(await build(folder, 'bin/Debug/net10.0', 'test'), undefined);
            const rebuilt = fs.statSync(output).mtimeMs;
            assert.ok(rebuilt > before, 'Written after its build, countdown was not built again.');

            assert.strictEqual(await build(folder, 'bin/Debug/net10.0', 'test'), undefined);
            assert.strictEqual(fs.statSync(output).mtimeMs, rebuilt, 'Built again with nothing written since.');

            fs.writeFileSync(countdown, text.replace('"$schema"', '"$schema" "'));
            fs.utimesSync(countdown, later(), later());
            assert.strictEqual(await build(folder, 'bin/Debug/net10.0', 'test'), undefined, 'A folder that was built is shown as last built where it does not build now.');
            assert.strictEqual(fs.statSync(output).mtimeMs, rebuilt);
            // Said, for the pages that show it, until it builds: when it was last built, and what the build said.
            const shown = unbuilt(folder);
            assert.ok(shown, 'Nothing says countdown is shown as last built.');
            assert.ok(Math.abs(shown.built.getTime() - rebuilt) <= 1, 'What is said of when countdown was last built is not when it was.');
            assert.match(shown.said, /countdown\.json/);
        } finally {
            fs.writeFileSync(countdown, text);
            fs.utimesSync(countdown, later(), later());
            assert.strictEqual(await build(folder, 'bin/Debug/net10.0', 'test'), undefined);
        }
        assert.strictEqual(unbuilt(folder), undefined, 'Built again, countdown is still said to be shown as last built.');
    });
});

interface Standing { picture: string; divergences: string[]; moves: { step: string; at: unknown }[]; refused: { step: string; picture: string }[]; trouble?: string }
interface Moved {
    changed: { route: string[]; gained: { step: string }[] }[];
    gone: { route: string[] }[];
    appeared: { route: string[] }[];
    admits: { input: string; parameter: string; was: { bound: string; value: string }[]; now: { bound: string; value: string }[] }[];
}

/** A rule set's test cases, opened as the view's line for it opens them, once they are listed. */
async function testCases(ruleSet: string): Promise<Screens> {
    const uri = vscode.Uri.file(ruleSet);
    await vscode.commands.executeCommand('rulealize.screens', uri);
    const api = await extension();
    await until(() => api.screens(uri) !== undefined, `The test cases of ${ruleSet} did not open.`);
    const screens = api.screens(uri)!;
    await screens.ready;
    return screens;
}

/**
 * Writes one change into a rule set, as an agent writes it, and reads what it did on its test cases:
 * the situations it moved, and the one reached by the moves named stood in before and after. Puts
 * the rule set back afterwards, and holds that nothing is listed then.
 */
async function moved(file: string, change: (text: string) => string, route: string): Promise<{ changes: Moved; compared: { before: Standing; after: Standing }; entry: Moved['changed'][number] }> {
    const screens = await testCases(file);
    const listed = () => screens.said('changes') as Moved & { trouble?: string };
    const written = fs.readFileSync(file, 'utf8');
    assert.strictEqual(listed().changed.length, 0, 'Something is listed as moved before anything was changed.');

    try {
        const changed = change(written);
        assert.notStrictEqual(changed, written, 'The change did not change the rule set.');
        fs.writeFileSync(file, changed);
        await until(() => (listed()?.changed?.length ?? 0) > 0, 'The change saved was not read as having moved anything.', 60_000);
        const changes = listed();
        for (const situation of [...changes.changed, ...changes.gone, ...changes.appeared]) {
            assert.ok(Array.isArray(situation.route), 'A situation the change moved is not said by its route.');
        }

        const index = changes.changed.findIndex(c => c.route.join(' → ') === route);
        assert.ok(index >= 0, `${route} is not among the situations the change moved.`);
        await screens.compare(`changed:${index}`);
        const compared = screens.said('compared') as { before: Standing; after: Standing; trouble?: string };
        assert.ok(!compared.trouble, compared.trouble);
        for (const side of [compared.before, compared.after]) {
            assert.ok(!side.trouble, side.trouble);
            assert.deepStrictEqual(side.divergences, []);
            assert.match(side.picture, /\.png$/);
        }

        return { changes, compared, entry: changes.changed[index] };
    } finally {
        fs.writeFileSync(file, written);
        await until(() => (listed()?.changed?.length ?? 1) === 0, 'Putting the rule set back left the change listed.', 60_000);
    }
}

/** A copy of signup in a git repository of its own, committed once, with its build; and git run there. */
interface Kept {
    folder: string;
    git(...args: string[]): string;
    dispose(): Promise<void>;
}

function repository(): Kept {
    const folder = fs.mkdtempSync(path.join(os.tmpdir(), 'rulealize-kept-'));
    const from = path.dirname(signup);
    for (const name of fs.readdirSync(from).filter(n => n !== 'bin' && n !== 'obj')) {
        fs.cpSync(path.join(from, name), path.join(folder, name), { recursive: true });
    }
    fs.cpSync(path.join(from, 'bin', 'Debug', 'net10.0'), path.join(folder, 'bin', 'Debug', 'net10.0'), { recursive: true });
    fs.writeFileSync(path.join(folder, '.gitignore'), 'bin/\nobj/\n');

    const git = (...args: string[]) => execFileSync('git', args, { cwd: folder, encoding: 'utf8' }).trim();
    git('init', '-q', '-b', 'main');
    for (const [name, value] of [['user.name', 'Rulealize'], ['user.email', 'rulealize@example.com'], ['commit.gpgsign', 'false'], ['core.autocrlf', 'false']]) {
        git('config', name, value);
    }
    git('add', '-A');
    git('commit', '-q', '-m', 'signup');

    return {
        folder,
        git,
        dispose: async () => {
            await closeAll();
            try {
                fs.rmSync(folder, { recursive: true, force: true });
            } catch {
                // Something the window started may still hold a file there; the temporary folder is the system's to clear.
            }
        },
    };
}

/**
 * Makes a change the way an agent writes it, into the files: the element of the specification that
 * says how large a party is, and the rule bound to it.
 */
function change(kept: Kept, words: string, max: number): void {
    const specification = path.join(kept.folder, 'specification.json');
    const rules = path.join(kept.folder, 'signup.json');
    fs.writeFileSync(specification, fs.readFileSync(specification, 'utf8').replace(/one to \w+ people/, `one to ${words} people`));
    fs.writeFileSync(rules, fs.readFileSync(rules, 'utf8').replace(/("party": \{ "op": "type.int", "min": 1, "max": )\d+/, `$1${max}`));
}
