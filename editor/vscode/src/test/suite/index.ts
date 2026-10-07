// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as path from 'path';
import Mocha = require('mocha');

/**
 * What the Extension Host calls once it has started: the suite, run inside it. Which suite is
 * the window's to say — the samples' test cases, or the walkthrough in a folder with nothing in it.
 */
export function run(): Promise<void> {
    // Long enough for ruledger to walk a rule set twice, the server to start once, and the
    // vocabularies to be fetched.
    // RULEALIZE_GREP, where it is set, runs only the tests whose names match it.
    const mocha = new Mocha({ ui: 'tdd', color: true, timeout: 300_000, grep: process.env.RULEALIZE_GREP });
    mocha.addFile(path.resolve(__dirname, `${process.env.RULEALIZE_SUITE ?? 'testCases'}.test.js`));

    return new Promise((resolve, reject) => {
        mocha.run(failures => failures > 0 ? reject(new Error(`${failures} failed.`)) : resolve());
    });
}
