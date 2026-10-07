// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';
import { saysRuleSet } from './ruleSet';

/** What an application's first specification is called in its folder. It comes before the rules, so it is named after none. */
export const specificationFile = 'specification.json';

/** What a specification made beside the first is called after: `checkout.specification.json`. */
export const specificationSuffix = '.specification.json';

/** What separates a rule set's name from the rule in a binding, as the server reads one: `signup#/inputs/book`. */
export const separator = '#';

/** One element of a specification, as the server read it: what it says, how it stands to the others, and the rules it is bound to. */
export interface Element {
    id: string;
    kind: 'state' | 'transition' | 'note';
    name: string;
    says: string;
    guard: string;
    from: string | null;
    to: string | null;
    on: string | null;
    final: boolean;
    rules: { name: string; known: boolean }[];
}

/** What a specification has, as the server read it, with the rules of the rule sets beside it — by rule set where there are several — and which of them nothing in it asks for. */
export interface Machine {
    schema: string;
    initial: string | null;
    elements: Element[];
    rules: string[];
    unasked: string[];
    faults: string[];
}

/** Whether a text says it is a specification in the format the Studio knows: its `$schema`, as the server reads it. */
export function saysSpecification(text: string): boolean {
    return /"\$schema"\s*:\s*"rulealize-studio\/state-machine\//.test(text);
}

/** The JSON files of a folder whose text says something, in the order of their names. */
function jsonIn(folder: string, says: (text: string) => boolean): string[] {
    return fs.existsSync(folder)
        ? fs.readdirSync(folder).filter(name => name.endsWith('.json')).sort().map(name => path.join(folder, name))
            .filter(name => says(fs.readFileSync(name, 'utf8')))
        : [];
}

/** Every specification of the application in a folder: any JSON in it that says it is one. */
export function specificationsIn(folder: string): string[] {
    return jsonIn(folder, saysSpecification);
}

/** Every rule set of the application in a folder: any JSON in it that says it is one. */
export function ruleSetsIn(folder: string): string[] {
    return jsonIn(folder, saysRuleSet);
}

/** Every rule set beside a specification, where there are any yet. */
export function ruleSetsBeside(specification: vscode.Uri): vscode.Uri[] {
    return ruleSetsIn(path.dirname(specification.fsPath)).map(file => vscode.Uri.file(file));
}

/**
 * The rule set a binding is to, and the rule in it: the one it names, `signup#/inputs/book`, or
 * where it names none, the one beside the specification. Nothing where there is no such rule set.
 */
export function ruleOf(specification: vscode.Uri, binding: string): { rules: vscode.Uri; rule: string } | undefined {
    const beside = ruleSetsBeside(specification);
    const at = binding.indexOf(separator);
    if (at <= 0) {
        return beside.length > 0 ? { rules: beside[0], rule: binding } : undefined;
    }

    const named = binding.slice(0, at);
    const rules = beside.find(uri => path.basename(uri.fsPath, '.json') === named);
    return rules && { rules, rule: binding.slice(at + 1) };
}

/**
 * A rule's name, as somebody reads it: `/inputs/setParty/params/size` is `inputs › setParty › params › size`,
 * and `signup#/inputs/book` is `signup: inputs › book`.
 */
export function display(rule: string): string {
    const at = rule.indexOf(separator);
    const named = at > 0 ? rule.slice(0, at) + ': ' : '';
    return named + rule.slice(at > 0 ? at + 1 : 0).split('/').slice(1).map(s => s.replace(/~1/g, '/').replace(/~0/g, '~')).join(' › ');
}

/** What a specification has, against every rule set beside it; nothing where it is not one, or not JSON just now. */
export function machineOf(client: LanguageClient, specification: vscode.Uri): Promise<Machine | null> {
    return client.sendRequest<Machine | null>('rulealize/specification', {
        uri: specification.toString(),
        ruleSets: ruleSetsBeside(specification).map(uri => uri.toString()),
    });
}
