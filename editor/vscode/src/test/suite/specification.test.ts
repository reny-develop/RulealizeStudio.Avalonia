// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import type { Machine } from '../../blueprint';
import type { Specification } from '../../specification';
import { closeAll, extension, until } from './help';

// Signup's specification, a state machine in the Studio's own format, opened in the Studio's own
// editor: every element bound to rules of signup, every rule reached from the elements bound to it,
// and an edit made in the editor written into the file. Nothing
// is clicked: the page's links are followed by sending what the page sends.

const folder = path.resolve(__dirname, '..', '..', '..', '..', '..', 'sample', 'RulealizeStudio.Sample.Signup');
const signup = path.join(folder, 'signup.json');
const specification = path.join(folder, 'specification.json');

suite('A specification bound to its rules', () => {
    let written = '';

    suiteSetup(async () => {
        written = fs.readFileSync(specification, 'utf8');
        await extension();
    });

    teardown(async () => {
        await closeAll();
        if (fs.readFileSync(specification, 'utf8') !== written) {
            fs.writeFileSync(specification, written);
        }
    });

    test("signup's specification opens in the Studio's editor, a state machine with every rule of signup bound", async () => {
        const shown = await specificationOf(specification);
        const machine = drawn(shown);

        assert.strictEqual((vscode.window.tabGroups.activeTabGroup.activeTab?.input as vscode.TabInputCustom).viewType, 'rulealize.specification');
        assert.strictEqual(machine.initial, 'unnamed');
        assert.deepStrictEqual(machine.elements.filter(e => e.kind === 'state').map(e => e.name), ['Nobody named yet', 'Named', 'Booked']);
        assert.strictEqual(machine.elements.length, 15);
        assert.strictEqual(machine.rules.length, 31);
        assert.deepStrictEqual(machine.unasked, []);
        for (const element of machine.elements) {
            assert.ok(element.rules.length > 0 && element.rules.every(r => r.known), `${element.id} is not bound to rules signup has.`);
        }
    });

    test('from every element the rules bound to it are reached, selected where signup.json writes them', async () => {
        const shown = await specificationOf(specification);

        for (const element of drawn(shown).elements) {
            for (const { name } of element.rules) {
                await shown.answer({ type: 'rule', rule: name });

                const editor = vscode.window.activeTextEditor;
                assert.strictEqual(editor?.document.uri.fsPath, vscode.Uri.file(signup).fsPath, `${name} did not open signup.json.`);
                assert.ok(!editor.selection.isEmpty, `${name} is not selected in signup.json.`);
            }
        }
    });

    test('an edit made in any of its views is written into the file, in the one layout the format has', async () => {
        const shown = await specificationOf(specification);

        await shown.answer({ type: 'edit', edit: { op: 'set', id: 'summary', field: 'says', value: '画面には予約の中身が出る。' } });
        await shown.answer({ type: 'edit', edit: { op: 'add', kind: 'note', on: 'ready' } });

        const text = fs.readFileSync(specification, 'utf8');
        assert.ok(text.includes('"says": "画面には予約の中身が出る。"'), text);
        assert.strictEqual((shown.said('select') as { id: string }).id, 'note-1');
        await until(() => drawn(shown).elements.some(e => e.id === 'note-1' && e.on === 'ready'), 'The note added is not drawn.');
        assert.strictEqual(shown.document.isDirty, false);

        // An edit the format does not allow is said on the page and makes none.
        await shown.answer({ type: 'edit', edit: { op: 'set', id: 'ready', field: 'to', value: 'nowhere' } });
        assert.match((shown.said('refused') as { message: string }).message, /'nowhere' is not a state/);
        assert.strictEqual(fs.readFileSync(specification, 'utf8'), text);
    });
});

// Where the specification and the rules disagree, marked as problems on both: nothing for signup or
// countdown as committed; the rules an element was the only one bound to, once it is taken out; and
// a clause written into the rules that no element asks for.
suite('Where a specification and its rules disagree', () => {
    const files = [signup, specification];
    const written = new Map<string, string>();

    suiteSetup(async () => {
        for (const file of files) {
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

    test("signup's specification and rules agree, and so do countdown's", async () => {
        await (await extension()).marked();

        const countdown = path.resolve(folder, '..', 'RulealizeStudio.Sample.Countdown');
        for (const file of [signup, specification, path.join(countdown, 'countdown.json'), path.join(countdown, 'specification.json')]) {
            assert.deepStrictEqual(marks(file).map(d => d.message), [], path.basename(file));
        }
    });

    test('taking an element out of the specification marks the rules only it was bound to', async () => {
        const shown = await specificationOf(specification);
        await shown.answer({ type: 'edit', edit: { op: 'remove', id: 'party-unchanged' } });
        await shown.answer({ type: 'edit', edit: { op: 'remove', id: 'name-length' } });

        // name-length's one rule, the field `name`, is asked for by name-first as well.
        const marked = await settle(() => marks(signup));
        assert.deepStrictEqual(marked.map(d => d.message), ["Nothing in the specification asks for '/inputs/setParty/validate/party.unchanged'."]);
        const text = fs.readFileSync(signup, 'utf8').split(/\r?\n/)[marked[0].range.start.line];
        assert.match(text, /"require".*"@size"/);
        assert.deepStrictEqual(marks(specification).map(d => d.message), [marked[0].message]);

        // And where somebody who does not read the text sees it: in the specification.
        await until(() => problems(shown).some(m => m.includes('party.unchanged')), 'The specification does not say nothing asks for the clause.');
        assert.ok(drawn(shown).unasked.includes('/inputs/setParty/validate/party.unchanged'));
    });

    test('a validate clause added to the rules with no element bound to it is marked, and bound from the specification', async () => {
        // Written into the rules the way an agent writes it: the clause before it again, with a code of its own.
        const clause = '"code": "party.unchanged" }';
        const text = fs.readFileSync(signup, 'utf8');
        const at = text.indexOf(clause) + clause.length;
        const before = text.slice(text.lastIndexOf('{ "require"', at), at);
        fs.writeFileSync(signup, text.slice(0, at) + ',\n        ' + before.replace('party.unchanged', 'party.tooMany') + text.slice(at));
        await vscode.workspace.openTextDocument(signup);

        const marked = await settle(() => marks(signup));
        assert.deepStrictEqual(marked.map(d => d.message), ["Nothing in the specification asks for '/inputs/setParty/validate/party.tooMany'."]);
        assert.match(fs.readFileSync(signup, 'utf8').split(/\r?\n/)[marked[0].range.start.line], /"require".*"@size"/);
        assert.deepStrictEqual(marks(specification).map(d => d.message), [marked[0].message]);

        // Asked for first in the specification, which is where the blueprint says it has to be.
        const shown = await specificationOf(specification);
        await shown.answer({ type: 'edit', edit: { op: 'add', kind: 'note', on: 'party-size' } });
        await shown.answer({ type: 'edit', edit: { op: 'set', id: 'note-1', field: 'says', value: 'A party of more than four is refused.' } });
        await shown.answer({ type: 'edit', edit: { op: 'bind', id: 'note-1', rule: '/inputs/setParty/validate/party.tooMany' } });

        const settled = await settle(() => marks(signup), true);
        assert.deepStrictEqual(settled.map(d => d.message), []);
    });
});

/** Opens a specification the way the explorer does, in the Studio's editor. */
async function specificationOf(file: string, open = true): Promise<Specification> {
    const uri = vscode.Uri.file(file);
    if (open) {
        await vscode.commands.executeCommand('vscode.open', uri);
    }

    const api = await extension();
    await until(() => api.specification(uri) !== undefined, `${file} was not opened in the Studio's editor.`);
    const shown = api.specification(uri)!;
    await shown.ready;
    return shown;
}

/** What the specification's page was last told it has. */
function drawn(shown: Specification): Machine {
    return (shown.said('machine') as { machine: Machine }).machine;
}

/** What is marked on a document where it and its specification disagree. */
function marks(file: string): vscode.Diagnostic[] {
    return vscode.languages.getDiagnostics(vscode.Uri.file(file)).filter(d => d.source === 'blueprint');
}

/** What is marked once the last change has been compared, and has stopped changing. */
async function settle(read: () => vscode.Diagnostic[], empty = false): Promise<vscode.Diagnostic[]> {
    const api = await extension();
    let last = '';
    for (let i = 0; i < 20; i++) {
        await api.marked();
        await new Promise(resolve => setTimeout(resolve, 300));
        const now = JSON.stringify(read().map(d => d.message));
        if (now === last && (empty ? now === '[]' : now !== '[]')) {
            break;
        }

        last = now;
    }

    return read();
}

/** The problems a page was last told of. */
function problems(page: Specification): string[] {
    const told = page.said('problems') as { problems: ({ message: string } | string)[] } | undefined;
    return (told?.problems ?? []).map(p => typeof p === 'string' ? p : p.message);
}
