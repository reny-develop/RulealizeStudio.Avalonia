// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as assert from 'assert';
import { execFile, execFileSync } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import type { PlacedControl } from '../../screen';
import { extension, until } from './help';

// An application folder made from nothing, in a folder with nothing in it: what it holds, that it
// builds as it is, and that the checks its instructions name are the ones its tasks run. Needs the
// packages and the server packed into feed/, and nuget.org for the rest.

suite('A new application', () => {
    suiteSetup(() => extension());

    test('is a project, an entry point, an empty window, an empty specification and instructions, and builds', async () => {
        const parent = vscode.workspace.workspaceFolders?.[0]?.uri;
        assert.ok(parent, 'The window was not opened on a folder.');

        const made = await vscode.commands.executeCommand<vscode.Uri | undefined>('rulealize.application', parent, 'Trial');
        assert.ok(made, 'No application was made.');

        // nuget.config is there while RulealizeStudio's packages are not on nuget.org, and goes with
        // them. No repository is made: whether it goes into one is the person's, in git.
        assert.deepStrictEqual(fs.readdirSync(made.fsPath).sort(), [
            '.config', '.gitignore', '.vscode', 'AGENTS.md', 'CLAUDE.md', 'MainWindow.axaml', 'Program.cs', 'Trial.csproj',
            'nuget.config', 'specification.json',
        ]);
        assert.match(fs.readFileSync(path.join(made.fsPath, 'Program.cs'), 'utf8'), /RuleApp\.Run\(typeof\(Program\)\.Assembly, args\)/);

        // What an agent is told is written once, for every agent: CLAUDE.md only reads it. And
        // every check the folder's tasks show the person is one the instructions name.
        assert.strictEqual(fs.readFileSync(path.join(made.fsPath, 'CLAUDE.md'), 'utf8').trim(), '@AGENTS.md');
        const told = fs.readFileSync(path.join(made.fsPath, 'AGENTS.md'), 'utf8');
        const tasks = JSON.parse(fs.readFileSync(path.join(made.fsPath, '.vscode', 'tasks.json'), 'utf8')
            .replace(/^\s*\/\/.*$/gm, '')) as { tasks: { command: string; args: string[] }[] };
        for (const task of tasks.tasks) {
            const command = [task.command, ...task.args].join(' ');
            assert.ok(told.includes('`' + command + '`'), `AGENTS.md does not name '${command}'.`);
        }

        // Nothing that outlives the build — a build server, Avalonia's telemetry — holding the
        // folder the test is to delete.
        const built = await new Promise<{ code: number; text: string }>(resolve => {
            const env = { ...process.env, AVALONIA_TELEMETRY_OPTOUT: '1' };
            execFile('dotnet', ['build', '-nologo', '--disable-build-servers'], { cwd: made.fsPath, env }, (error, stdout, stderr) =>
                resolve({ code: error ? 1 : 0, text: `${stdout}${stderr}` }));
        });
        assert.strictEqual(built.code, 0, built.text);

        // The tools the checks are run with restore from where the libraries do.
        const restored = await new Promise<{ code: number; text: string }>(resolve => {
            execFile('dotnet', ['tool', 'restore'], { cwd: made.fsPath }, (error, stdout, stderr) =>
                resolve({ code: error ? 1 : 0, text: `${stdout}${stderr}` }));
        });
        assert.strictEqual(restored.code, 0, restored.text);
    });

    // The first turn of the loop from the extension's own view, in a folder New application made:
    // its screen and specification opened from the view; what the person made of them committed
    // the way they commit anything, in git; the rules an agent writes — here the example's files —
    // opened on the screen from the view; and, under the screen and the specification, what the
    // agent changed of them beyond binding them. Each line is clicked by running its command.

    test('is begun from its own view, and says what an agent changed of its screen and specification beyond binding them', async () => {
        const parent = vscode.workspace.workspaceFolders?.[0]?.uri;
        assert.ok(parent, 'The window was not opened on a folder.');
        const made = await vscode.commands.executeCommand<vscode.Uri | undefined>('rulealize.application', parent, 'Countdown');
        assert.ok(made, 'No application was made.');
        const folder = made.fsPath;
        const git = (...args: string[]) => execFileSync('git', args, { cwd: folder, encoding: 'utf8' }).trim();
        const api = await extension();
        // The application's kinds of thing, each under a line of its own, and what is under them.
        const kinds = async () => (await api.view.getChildren()).find(item => item.label === 'COUNTDOWN')?.children ?? [];
        const under = async (kind: string) => (await kinds()).find(item => item.label === kind)?.children ?? [];
        const lines = async () => (await kinds()).flatMap(item => item.children);
        const line = async (label: string) => (await lines()).find(item => item.label === label);
        const click = async (label: string) => {
            const item = await line(label);
            assert.ok(item?.command, `${label} is not in the view, or opens nothing: ${JSON.stringify((await lines()).map(i => i.label))}`);
            await vscode.commands.executeCommand(item.command.command, ...(item.command.arguments ?? []));
        };

        // With no rules: its screen and its specification to open, and nothing yet on the screen.
        assert.deepStrictEqual((await kinds()).map(item => [item.label, item.contextValue ?? null]), [['Screens', 'screens'], ['Specifications', 'specifications'], ['Test cases', null]]);
        assert.deepStrictEqual((await under('Screens')).map(item => item.label), ['MainWindow.axaml']);
        assert.deepStrictEqual((await under('Specifications')).map(item => item.label), ['specification.json']);
        assert.deepStrictEqual(await under('Test cases'), []);
        assert.strictEqual((await kinds())[2].description, 'once there are rules');
        // The screen opens as the window drawn, built apart from the folder: its own first build
        // is the one made once there are rules.
        await click('MainWindow.axaml');
        const window = vscode.Uri.file(path.join(folder, 'MainWindow.axaml'));
        await until(() => api.screen(window) !== undefined, "The screen did not open in the Studio's editor.");
        await api.screen(window)!.ready;
        assert.ok(api.screen(window)!.said('drawn'), JSON.stringify(api.screen(window)!.said('trouble')));
        assert.ok(!fs.existsSync(path.join(folder, 'bin')), "Drawing the screen built the folder's own output.");
        await click('specification.json');
        const specification = vscode.Uri.file(path.join(folder, 'specification.json'));
        await until(() => api.specification(specification) !== undefined, 'The specification did not open in the Studio\'s editor.');
        await api.specification(specification)!.ready;

        // What the person made of it before there are rules — here the example's specification,
        // bound to nothing — committed the way they commit anything.
        const example = path.resolve(__dirname, '..', '..', '..', 'example', 'Countdown');
        const unbound = JSON.parse(fs.readFileSync(path.join(example, 'specification.json'), 'utf8')) as Record<string, unknown>;
        for (const kind of ['states', 'transitions', 'notes']) {
            for (const element of Object.values((unbound[kind] ?? {}) as Record<string, { rules?: string[] }>)) {
                delete element.rules;
            }
        }
        fs.writeFileSync(path.join(folder, 'specification.json'), JSON.stringify(unbound, null, 2) + '\n');
        git('init', '-q', '-b', 'main');
        git('add', '-A');
        git('-c', 'user.name=Rulealize', '-c', 'user.email=rulealize@example.com', '-c', 'commit.gpgsign=false', 'commit', '-q', '-m', 'Countdown, as made');

        // The rules, as an agent writes them — the design last — and the application on its screen.
        for (const name of fs.readdirSync(example).filter(name => fs.statSync(path.join(example, name)).isFile()).sort((a, b) => Number(a.endsWith('.test-design.json')) - Number(b.endsWith('.test-design.json')))) {
            fs.copyFileSync(path.join(example, name), path.join(folder, name));
        }
        await until(async () => (await line('countdown'))?.command !== undefined, 'The rules are there and the view does not open them on the screen.');
        await click('countdown');
        const rules = vscode.Uri.file(path.join(folder, 'countdown.json'));
        const screens = api.screens(rules);
        assert.ok(screens, 'Test cases did not open on the application.');
        await screens.ready;
        const { situations } = screens.said('situations') as { situations: unknown[] };
        assert.ok(situations.length > 1, JSON.stringify(screens.said('situations')));

        // Under the screen and the specification, what the agent changed of them beyond binding
        // them: nothing of the specification, and the controls the screen was given.
        await until(async () => (await line('specification.json'))?.description === 'only bound', 'The specification is not said to be only bound.');
        const screened = (await line('MainWindow.axaml'))!;
        assert.strictEqual(screened.description, `${screened.children.length} changed beyond binding`);
        assert.ok(screened.children.some(c => /^Button "[^"]+" placed in /.test(String(c.label))), JSON.stringify(screened.children.map(c => c.label)));

        // And nothing is committed but what the person committed.
        assert.strictEqual(git('rev-list', '--count', 'HEAD'), '1');
    });

    // An application is as many windows and specifications as it needs, each added from the
    // + on its kind's line in the view, opened in its editor, listed there, and deleted from its line.

    test('is given another window and another specification from its own view, each opened in its editor, and has them deleted', async () => {
        const parent = vscode.workspace.workspaceFolders?.[0]?.uri;
        assert.ok(parent, 'The window was not opened on a folder.');
        const made = await vscode.commands.executeCommand<vscode.Uri | undefined>('rulealize.application', parent, 'Windows');
        assert.ok(made, 'No application was made.');
        const api = await extension();
        const kinds = async () => (await api.view.getChildren()).find(item => item.label === 'WINDOWS')!.children;
        const under = async (kind: string) => (await kinds()).find(item => item.label === kind)!;

        // Pressed as the + on the screens' and the specifications' lines is: with that line, which names its application.
        const window = await vscode.commands.executeCommand<vscode.Uri | undefined>('rulealize.newWindow', await under('Screens'), 'Confirm');
        assert.ok(window && fs.existsSync(window.fsPath), 'No window was made.');
        assert.strictEqual(path.basename(window.fsPath), 'Confirm.axaml');
        await until(() => api.screen(window) !== undefined, "The new window did not open in the Studio's editor.");

        const specification = await vscode.commands.executeCommand<vscode.Uri | undefined>('rulealize.newSpecification', await under('Specifications'), 'checkout');
        assert.ok(specification && fs.existsSync(specification.fsPath), 'No specification was made.');
        assert.strictEqual(path.basename(specification.fsPath), 'checkout.specification.json');
        await until(() => api.specification(specification) !== undefined, "The new specification did not open in the Studio's editor.");

        await until(async () => (await under('Screens')).children.length === 2, 'The view does not list both windows.');
        assert.deepStrictEqual((await kinds()).map(item => [item.label, item.children.map(line => [line.label, line.contextValue ?? null])]), [
            ['Screens', [['Confirm.axaml', 'window'], ['MainWindow.axaml', 'window']]],
            ['Specifications', [['checkout.specification.json', 'specification'], ['specification.json', 'specification']]],
            ['Test cases', []],
        ]);

        // Deleted from its own line, with the yes said for the person: into the trash, its editor closed.
        const line = async (file: string) => (await kinds()).flatMap(item => item.children).find(item => item.label === file)!;
        assert.ok(await vscode.commands.executeCommand<boolean>('rulealize.delete', await line('Confirm.axaml'), true));
        assert.ok(await vscode.commands.executeCommand<boolean>('rulealize.delete', await line('checkout.specification.json'), true));
        assert.ok(!fs.existsSync(window.fsPath) && !fs.existsSync(specification.fsPath), 'What was deleted is still there.');
        await until(async () => (await under('Screens')).children.length === 1, 'The view still lists the window deleted.');

        // The last window is kept: an application opens with a window or not at all.
        assert.ok(!await vscode.commands.executeCommand<boolean>('rulealize.delete', await line('MainWindow.axaml'), true));
        assert.ok(fs.existsSync(path.join(made.fsPath, 'MainWindow.axaml')), 'The last window was deleted.');

        // One among many is found by VS Code's own find on the view, opened from the magnifier above it.
        await vscode.commands.executeCommand('rulealize.find');
    });

    // From the window the template writes, signup's screen — every control signup's
    // window has, in Japanese — made in the screen's editor without writing XAML, following each
    // change within a second, and built. What is made is the list the .NET suite holds to signup's
    // own window; each step is asked the way the page asks it.

    test("has signup's screen made on its window without writing XAML, following each change within a second, and builds", async () => {
        const parent = vscode.workspace.workspaceFolders?.[0]?.uri;
        assert.ok(parent, 'The window was not opened on a folder.');
        const made = await vscode.commands.executeCommand<vscode.Uri | undefined>('rulealize.application', parent, 'Signup');
        assert.ok(made, 'No application was made.');
        const api = await extension();
        const window = vscode.Uri.file(path.join(made.fsPath, 'MainWindow.axaml'));
        await vscode.commands.executeCommand('vscode.openWith', window, 'rulealize.screen');
        await until(() => api.screen(window) !== undefined, "The screen did not open in the Studio's editor.");
        const screen = api.screen(window)!;
        await screen.ready;

        type Drawn = { placed: PlacedControl[]; select: number | null; version: number; picture: string };
        const drawn = () => screen.said('drawn') as Drawn;
        assert.ok(drawn(), JSON.stringify(screen.said('trouble')));
        assert.deepStrictEqual(drawn().placed.map(p => p.control), ['Window']);
        const offered = (screen.said('controls') as { controls: { type: string }[] }).controls.map(c => c.type);

        const steps = JSON.parse(fs.readFileSync(path.resolve(__dirname, '..', '..', '..', '..', '..', 'test', 'RulealizeStudio.Avalonia.Tests', 'screen', 'signup.ja.json'), 'utf8')) as
            { title: string; controls: { control: string; in: number; words?: string }[] };
        const took: number[] = [];
        const ask = async (asked: Parameters<typeof screen.answer>[0], chosen: number) => {
            const from = Date.now();
            await screen.answer(asked);
            took.push(Date.now() - from);
            assert.strictEqual(screen.said('refused'), undefined, JSON.stringify(screen.said('refused')));
            assert.strictEqual(drawn().select, chosen);
            assert.strictEqual(drawn().version, screen.document.version);
        };

        await ask({ type: 'words', at: 0, words: steps.title }, 0);
        for (const [i, step] of steps.controls.entries()) {
            assert.ok(offered.includes(step.control), `${step.control} is not among the controls offered.`);
            await ask({ type: 'place', control: step.control, into: step.in }, i + 1);
            if (step.words) {
                await ask({ type: 'words', at: i + 1, words: step.words }, i + 1);
            }
        }

        assert.deepStrictEqual(
            drawn().placed.map(p => [p.type, p.parent, p.words]),
            [['Avalonia.Controls.Window', null, steps.title], ...steps.controls.map(step => [step.control, step.in, step.words ?? null])]);
        assert.ok(Math.max(...took) < 1000, `A change took ${Math.max(...took)} ms to be drawn: ${took.join(', ')}`);
        assert.ok(!screen.document.isDirty, 'What was made is not saved.');

        // A control dragged onto the window lands where it was dropped, and a hand edit to the text
        // is drawn as any other change is.
        const book = drawn().placed.find(p => p.words === '予約する')!;
        await screen.answer({ type: 'place', control: 'Avalonia.Controls.Separator', x: book.box!.x + 2, y: book.box!.y + 2 });
        assert.strictEqual(drawn().placed[book.id].control, 'Separator');
        await screen.answer({ type: 'remove', at: book.id });
        assert.strictEqual(drawn().placed[book.id].words, '予約する');
        const from = Date.now();
        const edit = new vscode.WorkspaceEdit();
        const at = screen.document.getText().indexOf('席の予約');
        edit.replace(window, new vscode.Range(screen.document.positionAt(at), screen.document.positionAt(at + 4)), '予約');
        await vscode.workspace.applyEdit(edit);
        await until(() => drawn().version === screen.document.version, 'A hand edit to the text was not drawn.');
        assert.ok(Date.now() - from < 1000, `A hand edit took ${Date.now() - from} ms to be drawn.`);
        assert.strictEqual(drawn().placed[0].words, '予約');
        await screen.document.save();

        const built = await new Promise<{ code: number; text: string }>(resolve => {
            const env = { ...process.env, AVALONIA_TELEMETRY_OPTOUT: '1' };
            execFile('dotnet', ['build', '-nologo', '--disable-build-servers'], { cwd: made.fsPath, env }, (error, stdout, stderr) =>
                resolve({ code: error ? 1 : 0, text: `${stdout}${stderr}` }));
        });
        assert.strictEqual(built.code, 0, built.text);
    });
});
