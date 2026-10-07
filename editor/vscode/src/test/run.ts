// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { runTests } from '@vscode/test-electron';

// Runs the suite in four Extension Hosts. Two are opened on the samples, the folder the launch
// configuration opens too: the rule sets, their designs, and the vocabularies their build output
// ships — one for the test cases, and one for the specifications. The third and fourth are each opened
// on a folder with nothing in it, for the walkthrough and for a new application. Needs the server and the samples built, RulealizeStudio's
// packages packed into feed/, the example pack.js writes, rulealize and ruledger on the path, and
// somewhere to fetch from. The two are named in the settings the windows start with, as a developer
// names their own: left to the extension, it asks before fetching them, and nobody is there to say yes.
async function main(): Promise<void> {
    const extension = path.resolve(__dirname, '..', '..');
    const empty = fs.mkdtempSync(path.join(os.tmpdir(), 'rulealize-walkthrough-'));
    const nothing = fs.mkdtempSync(path.join(os.tmpdir(), 'rulealize-application-'));
    const sample = path.resolve(extension, '..', '..', 'sample');
    const user = fs.mkdtempSync(path.join(os.tmpdir(), 'rulealize-user-'));
    fs.mkdirSync(path.join(user, 'User'));
    fs.writeFileSync(path.join(user, 'User', 'settings.json'), JSON.stringify({ 'rulealize.ruledger': 'ruledger', 'rulealize.rulealize': 'rulealize' }));

    // Set in a terminal inside VS Code, and passed on it would start the editor under test as Node.
    delete process.env.ELECTRON_RUN_AS_NODE;
    try {
        const runs: [string, string, string[]][] = [
            ['testCases', sample, [extension]],
            ['specification', sample, [extension]],
            ['walkthrough', empty, [extension]],
            ['application', nothing, [extension]],
        ];
        // RULEALIZE_SUITES, where it is set, names the ones to run, separated by commas.
        const only = process.env.RULEALIZE_SUITES?.split(',').map(s => s.trim());
        for (const [suite, folder, extensions] of runs.filter(([suite]) => !only || only.includes(suite))) {
            await runTests({
                extensionDevelopmentPath: extensions,
                extensionTestsPath: path.resolve(__dirname, 'suite', 'index'),
                extensionTestsEnv: { RULEALIZE_SUITE: suite },
                launchArgs: [folder, '--disable-extensions', `--user-data-dir=${user}`],
            });
        }
    } catch {
        process.exitCode = 1;
    } finally {
        for (const folder of [empty, nothing, user]) {
            try {
                fs.rmSync(folder, { recursive: true, force: true });
            } catch {
                // A build server the walkthrough's build started may still hold a file there; the
                // temporary folder is the system's to clear.
            }
        }
    }
}

void main();
