// Settings → Agents and accounts → New sessions start with: the page refuses the same model names as AgentSetup in
// Backend.cs, which checks them again before they reach a command line (self-check agent-setup).
// Run: node --test tests/agent-setup.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const app = readFileSync(join(root, 'web', 'app.js'), 'utf8');
const backend = readFileSync(join(root, 'Backend.cs'), 'utf8');

function pagePattern() {
  const match = app.match(/^const MODEL_NAME = \/(.*)\/;$/m);
  assert.ok(match, 'web/app.js defines MODEL_NAME on one line, without flags');
  return match[1];
}

function backendPattern() {
  const match = backend.match(/ModelName = new\(@"(.*)", RegexOptions/);
  assert.ok(match, 'Backend.cs defines AgentSetup.ModelName');
  return match[1];
}

test('the page and Backend.cs allow the same model names', () => {
  // .NET ends the pattern with \z (no line break after the name); a JavaScript $ without the m flag means the same.
  assert.ok(pagePattern().endsWith('$') && backendPattern().endsWith('\\z'));
  assert.equal(pagePattern().slice(0, -1), backendPattern().slice(0, -2));
});

test('a model name has letters, digits and . _ : / -, starts with a letter or digit, and is at most 100 long', () => {
  const name = new RegExp(pagePattern());
  for (const ok of ['opus', 'gpt-5.6-sol', 'gemini-2.5-pro', 'auto', 'org/model:v1_2', 'a'.repeat(100)]) assert.ok(name.test(ok), ok);
  for (const bad of ['', '--dangerously-skip-permissions', '-m', '.opus', 'a b', 'opus&calc', 'opus|calc', 'opus"', 'opus^', 'op%PATH%',
    'op(s)', 'op<s>', 'opus[1m]', 'ópus', 'opus\n', 'a'.repeat(101)]) assert.ok(!name.test(bad), JSON.stringify(bad));
});
