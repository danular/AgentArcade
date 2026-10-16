// Golden screens: what the status dots, needs-you alerts and the phone read from each agent's terminal.
// tests/status/fixtures/<agent>-<state>.txt is synthetic pane text, hand-written to mirror each CLI's layout (README,
// "Tested with"); none of it is captured from a real session. When a vendor changes its screen, change the fixture
// to the new layout and see what breaks here, before users see a wrong dot.
// Run: node --test tests/status/status.test.mjs (also part of AgentArcade.exe --self-check when node.exe is on PATH).
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const Remote = createRequire(import.meta.url)('../../web/remote.js');
const FIXTURES = join(here, 'fixtures');

const AGENTS = ['claude', 'codex', 'copilot', 'cursor', 'agy'];
const STATES = ['approval', 'menu', 'tasks', 'working', 'idle'];

// A pane's text down to its last drawn row, as the page passes it to Remote.parts.
const lines = name => readFileSync(join(FIXTURES, `${name}.txt`), 'utf8').replace(/\r?\n$/, '').split(/\r?\n/);

const plan = (done, current, items) => ({ done, total: items.length, current, items: items.map(([text, isDone]) => ({ text, done: isDone })) });
const RETRY_PLAN = plan(2, 'Write the tests',
  [['Read the config loader', true], ['Add the retry option', true], ['Write the tests', false], ['Update the changelog', false]]);
const ANSWER = 'Added a retry option to the loader. It tries 3 times with a 500 ms pause.';

// What each screen must give. Fields left out aren't checked; preview only matters while nothing asks.
const base = { asking: false, asks: false, menu: false, question: null, choices: [], activity: null, plan: null };
const EXPECT = {
  // Claude Code in a Windows console without Unicode: > cursor, task marks √ done, ■ in progress, □ open.
  'claude-approval': {
    ...base, asking: true, asks: true, question: 'Do you want to proceed?',
    choices: [['1', 'Yes'], ['2', "Yes, and don't ask again for npm test commands in this project"], ['3', 'No, and tell Claude what to do differently']],
  },
  'claude-menu': { ...base, asking: true, menu: true },
  'claude-tasks': { ...base, activity: '* Writing the tests… (42s · ↓ 1.8k tokens · esc to interrupt)', plan: RETRY_PLAN },
  'claude-working': { ...base, activity: '* Pondering… (12s · ↑ 640 tokens · esc to interrupt)' },
  'claude-idle': { ...base, preview: ['Added a retry option to the loader: it tries 3 times with a 500 ms pause.', 'The tests pass.'] },

  'codex-approval': {
    ...base, asking: true, asks: true, question: 'Would you like to run the following command?',
    choices: [['1', 'Yes, proceed'], ['2', "Yes, and don't ask again for commands that start with `npm test`"], ['3', 'No, and tell Codex what to do differently']],
  },
  // Codex numbers its pickers, so they are choices rather than an arrow-key menu.
  'codex-menu': {
    ...base, asking: true,
    choices: [
      ['1', 'Read Only Codex can read files. Edits and commands need your OK.'],
      ['2', 'Auto (current) Codex can read and edit files and run commands in the workspace.'],
      ['3', 'Full Access Codex can edit files outside the workspace and use the network.'],
    ],
  },
  'codex-tasks': { ...base, activity: '• Working (1m 05s • esc to interrupt)', plan: RETRY_PLAN },
  'codex-working': { ...base, activity: '• Working (38s • esc to interrupt)' },
  'codex-idle': { ...base, preview: ['Added a retry option to the loader. It tries 3 times with a 500 ms pause, and the tests pass.'] },

  'copilot-approval': {
    ...base, asking: true, asks: true, question: 'Do you want to run this command?',
    choices: [['1', 'Yes'], ['2', 'Yes, and approve `npm` for the rest of the running session'], ['3', 'No, and tell Copilot what to do differently']],
  },
  'copilot-menu': { ...base, asking: true, menu: true },
  // "Esc to cancel" without a timer also ends dialogs, so it isn't an activity line; the dot goes orange from output.
  'copilot-tasks': { ...base, plan: RETRY_PLAN },
  'copilot-working': { ...base },
  'copilot-idle': { ...base, preview: [ANSWER] },

  // Cursor (layout from cursor-agent 2026.09.28's own decision surface): its approval marks the option with → and ends
  // every option in its key, without numbers or a footer, so it is a menu answered with keys. Its "ctrl+c to stop"
  // isn't an activity line.
  'cursor-approval': { ...base, asking: true, asks: true, menu: true, question: 'Run this command?' },
  'cursor-menu': { ...base, asking: true, menu: true },
  'cursor-tasks': { ...base, plan: RETRY_PLAN },
  'cursor-working': { ...base },
  'cursor-idle': { ...base, preview: [ANSWER] },

  // Antigravity CLI: the approval offers "Yes, and run in sandbox" (antigravity.google/docs/cli/features); the trust
  // dialog is the one in Google's codelab. The approval's exact wording is unverified (README, "Tested with").
  'agy-approval': { ...base, asking: true, asks: true, menu: true },
  'agy-menu': { ...base, asking: true, menu: true, question: 'Do you trust the contents of this project?' },
  'agy-tasks': { ...base, plan: RETRY_PLAN },
  'agy-working': { ...base },
  'agy-idle': { ...base, preview: [ANSWER] },
};

test('every agent has a fixture for every state, and nothing else is in the folder', () => {
  const expected = AGENTS.flatMap(agent => STATES.map(state => `${agent}-${state}.txt`)).sort();
  assert.deepEqual(readdirSync(FIXTURES).filter(name => !name.startsWith('.')).sort(), expected);
  assert.deepEqual(Object.keys(EXPECT).map(name => `${name}.txt`).sort(), expected);
});

test('fixtures are synthetic: no email addresses, no Windows or home folders', () => {
  for (const name of Object.keys(EXPECT)) {
    const text = lines(name).join('\n');
    assert.doesNotMatch(text, /[\w.+-]+@[\w-]+\.[a-z]{2,}/i, `${name} has an email address`);
    assert.doesNotMatch(text, /\b[A-Za-z]:\\|\\Users\\|\/Users\/|\/home\/|%USERPROFILE%|%LOCALAPPDATA%/i, `${name} has a folder path`);
  }
});

for (const [name, expected] of Object.entries(EXPECT)) {
  test(`${name}: status parts`, () => {
    const parts = Remote.parts(lines(name));
    for (const field of ['asking', 'asks', 'menu', 'question', 'activity', 'plan']) {
      assert.deepEqual(parts[field], expected[field], `${name}: ${field}`);
    }
    assert.deepEqual(parts.choices.map(choice => [choice.key, choice.label]), expected.choices, `${name}: choices`);
    if (expected.preview) assert.deepEqual(parts.preview, expected.preview, `${name}: preview`);
    // The same screen through the helpers the page uses on its own.
    assert.equal(Remote.picking(lines(name)), expected.choices.length > 0 || expected.menu, `${name}: picking`);
    assert.equal(Remote.approval(lines(name).slice(-12)), expected.asks, `${name}: approval`);
    assert.deepEqual(Remote.findChoices(lines(name).slice(-20)).choices.map(choice => choice.key), expected.choices.map(([key]) => key), `${name}: findChoices`);
  });
}

test('Claude Code task marks: √ and ✔ done, ■ and ◼ in progress, □ and ◻ open', () => {
  const windows = ['  ⎿  √ One', '     √ Two', '     ■ Three', '     □ Four', '     □ Five'];
  const unicode = ['  ⎿  ✔ One', '     ✔ Two', '     ◼ Three', '     ◻ Four', '     ◻ Five'];
  for (const rows of [windows, unicode]) {
    const found = Remote.plan(rows);
    assert.equal(found.done, 2);
    assert.equal(found.total, 5);
    assert.equal(found.current, 'Three');
    assert.deepEqual(found.items.map(item => item.done), [true, true, false, false, false]);
  }
  // The item in progress is the current one even when an open item comes first.
  assert.equal(Remote.plan(['□ Write the docs', '■ Fix the build', '√ Read the code']).current, 'Fix the build');
  // Without one in progress, current is the first item not done.
  assert.equal(Remote.plan(['√ Read the code', '□ Fix the build', '□ Write the docs']).current, 'Fix the build');
  assert.equal(Remote.plan(['√ Read the code', '√ Fix the build']).current, null);
});

test("Antigravity CLI's documented approval option is enough to ask, with or without a menu footer", () => {
  const card = lines('agy-approval');
  const bare = card.filter(line => !/navigate/i.test(line));
  const parts = Remote.parts(bare);
  assert.equal(parts.asks, true);
  assert.equal(parts.menu, false);
  assert.equal(Remote.approval(['  Yes, and run without sandbox restrictions']), true);
  assert.equal(Remote.approval(['  Yes, and run in sandbox']), true);
  // Plain prose about sandboxes doesn't ask.
  assert.equal(Remote.approval(['The tests run in a sandbox, and yes, they pass.']), false);
});

test('APPROVAL is identical in web/app.js and web/remote.js', () => {
  const app = readFileSync(join(here, '..', '..', 'web', 'app.js'), 'utf8');
  const match = app.match(/^const APPROVAL = (\/.*\/[a-z]*);$/m);
  assert.ok(match, 'web/app.js defines APPROVAL on one line');
  assert.equal(match[1], String(Remote.APPROVAL));
});

// cursor-approval with its body (the command, the question, the options) swapped for another of Cursor's decisions.
const cursorDecision = (preview, question, reason, options) => {
  const card = lines('cursor-approval');
  const top = card.findIndex(line => /^─+$/.test(line));
  const footer = card.findIndex((line, i) => i > top && !line.trim() && /·/.test(card[i + 1] || ''));
  const body = [preview, '', ' ' + question, ...(reason ? [reason] : []), ...options.map((option, i) => (i ? '    ' : '  → ') + option)];
  return [...card.slice(0, top + 1), ...body, ...card.slice(footer)];
};

test("Cursor's other approvals ask too, and its answers and lists don't", () => {
  const write = cursorDecision(' src/config.js +3 -1', 'Write to this file?', ' in src/config.js',
    ['Proceed (y)', 'Reject & propose changes (esc or n or p)', 'Add Write(src/config.js) to allowlist? (tab)']);
  const remove = cursorDecision(' Delete file: old-config.js', 'Delete this file?', null, ['Delete (y)', 'Keep (n)']);
  const fetch = cursorDecision(' 🌐 Web Fetch: example.com', 'Allow this web fetch?', null,
    ['Fetch (y)', 'Always allow example.com (tab)', 'Skip (esc or n)']);
  for (const [name, screen, question] of [['write', write, 'Write to this file?'], ['delete', remove, 'Delete this file?'], ['fetch', fetch, 'Allow this web fetch?']]) {
    const parts = Remote.parts(screen);
    assert.equal(parts.asking, true, `${name}: asking`);
    assert.equal(parts.asks, true, `${name}: asks`);
    assert.equal(parts.menu, true, `${name}: menu`);
    assert.equal(parts.question, question, `${name}: question`);
    assert.deepEqual(parts.choices, [], `${name}: choices`);
  }

  // Two waiting at once: "Approval 1 of 2" above and "←/→ to switch approval" under the options.
  const card = lines('cursor-approval');
  const two = [...card.slice(0, 5), ' Approval 1 of 2', '', ...card.slice(5, 13), '', ' ←/→ to switch approval', ...card.slice(13)];
  assert.equal(Remote.parts(two).asking, true);
  assert.equal(Remote.parts(two).question, 'Run this command?');

  // Answered: Cursor takes the options away and its input line comes back.
  const answered = [...card.slice(0, 4), '  ⬢ $ npm test 1.2s', '', ' ┌────────┐', ' │ →      │', ' └────────┘', '  Auto · 12%'];
  assert.equal(Remote.parts(answered).asking, false);
  // A list in an answer that ends in keys has no cursor; one option with the cursor isn't a menu either.
  assert.equal(Remote.parts(['  Keys:', '    Run once (y)', '    Skip (esc or n)', '', ' │ → │']).asking, false);
  assert.equal(Remote.parts(['  → Run (once) (y)', '', '  Auto · 12%']).asking, false);
  // The options must be near the bottom: more than 4 rows with text below them means it's gone.
  assert.equal(Remote.parts([...card.slice(0, 13), 'one', 'two', 'three', 'four', 'five']).menu, false);
  // Claude Code's numbered options keep their own rules, even with the cursor on a hinted one.
  const claude = ['│ Do you want to proceed?', '│   1. Yes', "│ ❯ 2. Yes, and don't ask again (shift+tab)", '│   3. No (esc)', '╰────╯'];
  assert.deepEqual(Remote.parts(claude).choices.map(choice => choice.key), ['1', '2', '3']);
  assert.equal(Remote.parts(claude).menu, false);
});

test("Cursor's next waiting approval alerts after the answered one, though its question is the same", () => {
  const first = lines('cursor-approval');
  const next = cursorDecision(' $  npm run build in ~/retry-demo', 'Run this command?', ' Not in allowlist: npm run build',
    ['Run (once) (y)', 'Add Shell(npm) to allowlist? (tab)', 'Run Everything (shift+tab)', 'Skip & tell the agent what to do instead (esc or n)']);
  assert.equal(Remote.dialogSig(Remote.parts(next)), Remote.dialogSig(Remote.parts(first)));
  const tracker = {};
  assert.equal(Remote.dialogAppeared(tracker, Remote.parts(first), 1000, false, first.join('\n')), true);
  Remote.dialogKeys(tracker, 'y', first.join('\n'), 1200);
  assert.equal(Remote.dialogAppeared(tracker, Remote.parts(next), 2000, false, next.join('\n')), false);
  assert.equal(Remote.dialogAppeared(tracker, Remote.parts(next), 3000, false, next.join('\n')), true);
  assert.equal(Remote.dialogAppeared(tracker, Remote.parts(next), 4000, false, next.join('\n')), false);
});
