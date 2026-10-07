// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

// Packs the extension as one .vsix with everything it runs that is this repository's inside it:
//
//   server/   the server, published framework-dependent: one package for every platform
//   feed/     RulealizeStudio's packages and the template, while they are not on nuget.org — the
//             newest of each in the repository's feed/, where packing them puts them
//   example/  the example the walkthrough starts from: countdown's own files, made into an
//             application of its own — its screen under the application's namespace, and the
//             template's project with the vocabularies countdown's project lists
//   LICENSE   the repository's
//   THIRD-PARTY-NOTICES.txt
//             the licence of every package the server and the extension bring with them, read
//             from the packages themselves: a package with no licence to read stops the packing
//
// `node pack.js example` writes only the example, which the Extension Host tests start from too.
// Needs the SDK, the packages packed into feed/, and somewhere to fetch from.

'use strict';

const { execFileSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const here = __dirname;
const repository = path.resolve(here, '..', '..');

/** The template an application is made from, as the extension names it. */
const templates = /const templates = '([^']+)'/.exec(fs.readFileSync(path.join(here, 'src', 'application.ts'), 'utf8'))[1];

/** The examples, each a sample of this repository's and the name its application is made under. */
const examples = [{ sample: 'RulealizeStudio.Sample.Countdown', name: 'Countdown' }];

function dotnet(args, cwd) {
    execFileSync('dotnet', args, { cwd, stdio: 'inherit' });
}

function fresh(folder) {
    fs.rmSync(folder, { recursive: true, force: true });
    fs.mkdirSync(folder, { recursive: true });
}

function example() {
    const out = path.join(here, 'example');
    fresh(out);
    const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'rulealize-example-'));
    try {
        dotnet(['new', 'install', templates, '--add-source', path.join(repository, 'feed')], scratch);
    } catch {
        // Installed already, which `dotnet new install` reports as a failure.
    }

    try {
        for (const { sample, name } of examples) {
            const from = path.join(repository, 'sample', sample);
            const to = path.join(out, name);
            fs.mkdirSync(to);

            // The application's own files. Program.cs is the template's, the same in every application.
            for (const file of fs.readdirSync(from, { withFileTypes: true })) {
                if (file.isFile() && file.name !== 'Program.cs' && !file.name.endsWith('.csproj')) {
                    const text = fs.readFileSync(path.join(from, file.name), 'utf8');
                    fs.writeFileSync(path.join(to, file.name), file.name.endsWith('.axaml') ? text.split(sample).join(name) : text);
                }
            }

            // The template's project, with the vocabularies the sample's own project lists in its last ItemGroup.
            dotnet(['new', 'rulealize-avalonia-app', '--name', name, '--output', path.join(scratch, name)], scratch);
            const project = fs.readFileSync(path.join(scratch, name, `${name}.csproj`), 'utf8');
            const listed = fs.readFileSync(path.join(from, `${sample}.csproj`), 'utf8');
            const vocabularies = [...listed.slice(listed.lastIndexOf('<ItemGroup>')).matchAll(/^.*<PackageReference .*$/gm)].map(m => m[0]);
            const empty = project.lastIndexOf('<ItemGroup>');
            const closed = project.indexOf('</ItemGroup>', empty);
            if (vocabularies.length === 0 || empty < 0 || project.slice(empty + '<ItemGroup>'.length, closed).trim() !== '') {
                throw new Error(`${sample}'s vocabularies, or the template's ItemGroup for them, are not where they were.`);
            }

            const eol = project.includes('\r\n') ? '\r\n' : '\n';
            fs.writeFileSync(path.join(to, `${name}.csproj`),
                project.slice(0, empty + '<ItemGroup>'.length) + eol + vocabularies.map(v => v.trimEnd()).join(eol) + eol + '  ' + project.slice(closed));
        }
    } finally {
        fs.rmSync(scratch, { recursive: true, force: true });
    }
}

function server() {
    const out = path.join(here, 'server');
    fresh(out);
    dotnet(['publish', path.join(repository, 'src', 'RulealizeStudio.Server'), '-c', 'Release', '-o', out, '-nologo'], repository);
}

function feed() {
    const out = path.join(here, 'feed');
    fresh(out);
    const newest = new Map();
    for (const file of fs.readdirSync(path.join(repository, 'feed')).filter(f => f.endsWith('.nupkg'))) {
        const [, id, version] = /^(.+?)\.(\d+\.\d+\.\d+.*)\.nupkg$/.exec(file);
        const was = newest.get(id);
        if (!was || compare(version, was.version) > 0) {
            newest.set(id, { version, file });
        }
    }

    for (const { file } of newest.values()) {
        fs.copyFileSync(path.join(repository, 'feed', file), path.join(out, file));
    }
}

function notices() {
    // Each licence once, under every package it covers.
    const texts = new Map();
    const listed = [];
    function add(name, licence, url, text) {
        listed.push(`- ${name} (${licence})${url ? ' ' + url : ''}`);
        const normal = text.replace(/\r\n/g, '\n').trim();
        texts.set(normal, [...(texts.get(normal) ?? []), name]);
    }

    // The server's packages, from the folder NuGet restored them into.
    const cache = /global-packages: (.+)/.exec(execFileSync('dotnet', ['nuget', 'locals', 'global-packages', '--list'], { encoding: 'utf8' }))[1].trim();
    const deps = JSON.parse(fs.readFileSync(path.join(here, 'server', 'RulealizeStudio.Server.deps.json'), 'utf8'));
    for (const [key] of Object.entries(deps.libraries).filter(([, l]) => l.type === 'package')) {
        const [id, version] = key.split('/');
        const folder = path.join(cache, id.toLowerCase(), version.toLowerCase());
        const spec = fs.readFileSync(path.join(folder, fs.readdirSync(folder).find(f => f.endsWith('.nuspec'))), 'utf8');
        const expression = /<license type="expression">([^<]+)</.exec(spec)?.[1];
        const copyright = /<copyright>([^<]+)</.exec(spec)?.[1] ?? /<authors>([^<]+)</.exec(spec)?.[1];
        const url = /<repository[^>]* url="([^"]+)"/.exec(spec)?.[1] ?? /<projectUrl>([^<]+)</.exec(spec)?.[1];
        const files = fs.readdirSync(folder);
        const file = files.find(f => /^licen[cs]e(\.txt|\.md)?$/i.test(f));
        const name = `${id} ${version}`;
        if (file) {
            add(name, expression ?? 'see below', url, fs.readFileSync(path.join(folder, file), 'utf8'));
        } else if (expression === 'MIT') {
            add(name, expression, url, mit(copyright));
        } else if (expression === 'Apache-2.0') {
            add(name, expression, url, `${copyright ? copyright + '\n\n' : ''}Licensed under the Apache License, Version 2.0, the text of which is in LICENSE beside this file.`);
        } else {
            throw new Error(`${name} carries no licence pack.js can read.`);
        }

        // What a package says of what it brings in turn, as SkiaSharp does of Skia.
        const theirs = files.find(f => /^third-party-notices(\.txt|\.md)?$/i.test(f));
        if (theirs) {
            const normal = fs.readFileSync(path.join(folder, theirs), 'utf8').replace(/\r\n/g, '\n').trim();
            texts.set(normal, [...(texts.get(normal) ?? []), `${name}'s own third-party notices`]);
        }
    }

    // The extension's own dependencies, which the .vsix carries in node_modules.
    const seen = new Set();
    (function walk(names) {
        for (const id of names) {
            if (seen.has(id)) {
                continue;
            }

            seen.add(id);
            const folder = path.join(here, 'node_modules', id);
            const manifest = JSON.parse(fs.readFileSync(path.join(folder, 'package.json'), 'utf8'));
            const file = fs.readdirSync(folder).find(f => /^licen[cs]e(\.txt|\.md)?$/i.test(f));
            if (!file) {
                throw new Error(`${id} ${manifest.version} carries no licence pack.js can read.`);
            }

            const url = typeof manifest.repository === 'string' ? manifest.repository : manifest.repository?.url;
            add(`${id} ${manifest.version}`, manifest.license, url, fs.readFileSync(path.join(folder, file), 'utf8'));
            walk(Object.keys(manifest.dependencies ?? {}));
        }
    })(Object.keys(JSON.parse(fs.readFileSync(path.join(here, 'package.json'), 'utf8')).dependencies ?? {}));

    const rule = '-'.repeat(80);
    const body = [
        'RulealizeStudio.Avalonia brings the following software with it, each under its own licence.',
        '',
        ...listed,
        '',
        ...[...texts].flatMap(([text, names]) => [rule, names.join(', '), rule, '', text, '']),
    ];
    fs.writeFileSync(path.join(here, 'THIRD-PARTY-NOTICES.txt'), body.join('\n'));
}

function mit(copyright) {
    return `MIT License

${copyright}

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.`;
}

function compare(a, b) {
    const x = a.split(/[.-]/).map(Number);
    const y = b.split(/[.-]/).map(Number);
    for (let i = 0; i < Math.max(x.length, y.length); i++) {
        if ((x[i] ?? 0) !== (y[i] ?? 0)) {
            return (x[i] ?? 0) - (y[i] ?? 0);
        }
    }

    return 0;
}

example();
if (process.argv[2] !== 'example') {
    server();
    feed();
    fs.copyFileSync(path.join(repository, 'LICENSE'), path.join(here, 'LICENSE'));
    notices();
    execFileSync(process.execPath, [require.resolve('@vscode/vsce/vsce'), 'package', '--allow-missing-repository'], { cwd: here, stdio: 'inherit' });
}
