// The menus' letter keys (menu() in web/app.js): every item of the chat, pane, project and account menus has one, a
// single lower-case letter, and no two items of one menu share it, also items that only show sometimes (Open in Unity,
// a chat pane's own items). Reads app.js as text. Run: node --test tests/menus.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const app = readFileSync(join(dirname(fileURLToPath(import.meta.url)), '..', 'web', 'app.js'), 'utf8').replace(/\r\n/g, '\n');

// The body of a top-level function, up to its closing brace at the start of a line.
function body(name) {
  const match = app.match(new RegExp(`\\nfunction ${name}\\([^)]*\\) \\{\\n[\\s\\S]*?\\n\\}\\n`));
  assert.ok(match, `function ${name} not found in web/app.js`);
  return match[0];
}

const MENUS = ['chatMenu', 'paneMenu', 'projectMenu', 'workspaceMenu', 'addProjectMenu', 'accountMenu'];

for (const name of MENUS) {
  test(`${name}: each item has its own letter`, () => {
    const code = body(name);
    const labels = [...code.matchAll(/\blabel: /g)].length;
    const keys = [...code.matchAll(/\bkey: '([^']*)'/g)].map(match => match[1]);
    assert.ok(labels > 0, `${name} has no items`);
    assert.equal(keys.length, labels, `${name}: ${labels} items but ${keys.length} keys`);
    for (const key of keys) assert.match(key, /^[a-z]$/, `${name}: "${key}" isn't one lower-case letter`);
    const twice = keys.filter((key, i) => keys.indexOf(key) !== i);
    assert.deepEqual(twice, [], `${name}: ${twice.join(', ')} used twice`);
  });
}

test('the chat menu matches Claude: R renames, C copies the title, D removes', () => {
  const keys = [...body('chatMenu').matchAll(/label: '([^']*)'.*?key: '(.)'/g)].map(match => [match[1], match[2]]);
  assert.deepEqual(keys, [['Rename…', 'r'], ['Copy title', 'c'], ['Remove from history', 'd']]);
});

test('menu keys only work while focus is in the menu', () => {
  // The handler sits on the menu element itself, so a terminal or a text field never loses a key to a menu.
  assert.match(body('menu'), /element\.addEventListener\('keydown', event => menuKey\(element, event\)\)/);
  assert.doesNotMatch(app, /document\.addEventListener\('keydown', [^\n]*menuKey/);
});
