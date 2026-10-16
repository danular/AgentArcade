'use strict';

// The page's pure helpers in web/remote.js. Run: node --test tests/remote.test.js
const test = require('node:test');
const assert = require('node:assert/strict');
const Remote = require('../web/remote.js');

// A Claude Code permission dialog at the bottom of a pane.
const dialog = (file = 'a.txt') => [
  '● Update(a.txt)',
  '╭──────────────────────────────────────────────────────────╮',
  '│ Edit file                                                │',
  `│ Do you want to make this edit to ${file}?`.padEnd(59) + '│',
  '│ ❯ 1. Yes                                                 │',
  "│   2. Yes, and don't ask again this session (shift+tab)   │",
  '│   3. No, and tell Claude what to do differently (esc)    │',
  '╰──────────────────────────────────────────────────────────╯',
];
const working = ['● Update(a.txt)', '✻ Brewing… (3s · esc to interrupt)', '╭────╮', '│ >  │', '╰────╯'];
const screen = lines => Remote.parts(lines);

test('a dialog is its question and choices', () => {
  const asking = screen(dialog());
  assert.equal(asking.asking, true);
  assert.equal(asking.question, 'Do you want to make this edit to a.txt?');
  assert.equal(Remote.dialogSig(asking), Remote.dialogSig(screen(dialog())));
  assert.notEqual(Remote.dialogSig(asking), Remote.dialogSig(screen(dialog('b.txt'))));
  assert.equal(Remote.dialogSig(screen(working)), '');
});

test('a prompt that shows right after Enter alerts once', () => {
  const tracker = {};
  assert.equal(Remote.dialogAppeared(tracker, screen(working), 1000), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog()), 2000), true);
  // Still on screen, the quiet timer and the agent's own notification about it: nothing new.
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog()), 3000), false);
  assert.equal(Remote.claimDialog(tracker, screen(dialog()), 3500), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog()), 60000), false);
});

test("the agent's own notification before its dialog is drawn counts for that dialog", () => {
  const tracker = {};
  assert.equal(Remote.claimDialog(tracker, screen(working), 1000), true);
  assert.equal(Remote.claimDialog(tracker, screen(working), 1500), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog()), 2000), false);
  assert.equal(Remote.claimDialog(tracker, screen(dialog()), 2500), false);
});

test('an answered dialog that comes back is new; a redraw gap is not', () => {
  const tracker = {};
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog()), 1000), true);
  // Gone for a moment without input (a redraw): the same dialog again is no new alert.
  assert.equal(Remote.dialogAppeared(tracker, screen(working), 2000), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog()), 3000), false);
  // Answered: keys went to the pane, then it left the screen.
  tracker.input = true;
  assert.equal(Remote.dialogAppeared(tracker, screen(working), 4000), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog()), 5000), true);
  // Dismissed without input and gone for 3 s: new as well.
  assert.equal(Remote.dialogAppeared(tracker, screen(working), 6000), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(working), 9000), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog()), 10000), true);
});

test('the same question asked again right after an answer alerts again, once', () => {
  const tracker = {};
  const first = dialog();
  assert.equal(Remote.dialogAppeared(tracker, screen(first), 1000, false, first.join('\n')), true);
  // Enter answers it, and in the same frame the agent asks the same question for its next queued edit: the 1 s check
  // never sees the screen without it.
  Remote.dialogKeys(tracker, '\r', first.join('\n'), 1500);
  const again = ['● Update(a.txt)', '  ⎿  Updated a.txt with 1 addition', ...dialog()];
  // Too soon after the key to tell a new dialog from the answered one going away.
  assert.equal(Remote.dialogAppeared(tracker, screen(again), 2000, false, again.join('\n')), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(again), 3000, false, again.join('\n')), true);
  // Still on screen, the quiet timer and the agent's own notification about it: nothing new.
  assert.equal(Remote.dialogAppeared(tracker, screen(again), 4000, false, again.join('\n')), false);
  assert.equal(Remote.claimDialog(tracker, screen(again), 4500), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(again), 60000, false, again.join('\n')), false);

  // Answered with 1, gone for less than a second between two checks, then asked again.
  Remote.dialogKeys(tracker, '1', again.join('\n'), 61000);
  const third = [...again, '● Update(a.txt)', '  ⎿  Updated a.txt with 2 additions', ...dialog()];
  assert.equal(Remote.dialogAppeared(tracker, screen(third), 62200, false, third.join('\n')), true);
  assert.equal(Remote.dialogAppeared(tracker, screen(third), 63200, false, third.join('\n')), false);
});

test('one question alerts once: keys that answer nothing, arrow keys, typing and a resize', () => {
  const tracker = {};
  const lines = dialog();
  const text = lines.join('\n');
  assert.equal(Remote.dialogAppeared(tracker, screen(lines), 1000, false, text), true);
  // A key the agent ignored: the screen stayed as it was.
  Remote.dialogKeys(tracker, '7', text, 1500);
  assert.equal(Remote.dialogAppeared(tracker, screen(lines), 3000, false, text), false);
  // That key is forgotten: a change much later (here only the cursor moving) isn't an answer.
  const moved = lines.map(line => line.replace('❯ 1.', '  1.').replace('  2.', '❯ 2.'));
  assert.equal(Remote.dialogAppeared(tracker, screen(moved), 4000, false, moved.join('\n')), false);
  // Arrow keys move the cursor: the screen changes, but no key answered.
  Remote.dialogKeys(tracker, '\x1b[B', text, 5000);
  assert.equal(Remote.dialogAppeared(tracker, screen(moved), 7000, false, moved.join('\n')), false);
  // y and then typing in the dialog's own field: the last key didn't answer.
  Remote.dialogKeys(tracker, 'y', moved.join('\n'), 8000);
  Remote.dialogKeys(tracker, 'use pnpm', moved.join('\n'), 8100);
  const typed = [...moved.slice(0, -1), '│      use pnpm'.padEnd(59) + '│', moved.at(-1)];
  assert.equal(Remote.dialogAppeared(tracker, screen(typed), 10000, false, typed.join('\n')), false);
  // Enter, then a resize wraps the same dialog again before the next check.
  Remote.dialogKeys(tracker, '\r', typed.join('\n'), 11000);
  const wrapped = typed.map(line => line.replace(/ +│$/, ' │'));
  assert.notEqual(wrapped.join('\n'), typed.join('\n'));
  assert.equal(Remote.dialogAppeared(tracker, screen(wrapped), 12500, true, wrapped.join('\n')), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(wrapped), 15000, false, wrapped.join('\n')), false);
  // Keys while no dialog was alerted answer nothing.
  const fresh = {};
  Remote.dialogKeys(fresh, '\r', '', 1000);
  assert.equal(fresh.answeredAt, 0);
});

test('expect: a phone answers only the dialog its screen showed', () => {
  const asking = screen(dialog());
  const key = Remote.dialogKey(asking);
  // First seen at seq 10; later looks keep that.
  const tracker = Remote.trackDialog(Remote.trackDialog(null, key, 10), key, 12);
  assert.equal(tracker.seq, 10);
  const pane = { asking: true, question: asking.question, dialogSeq: tracker.seq, seq: 12 };
  assert.equal(Remote.expectRefused({ question: asking.question, seq: 12 }, pane), false);
  assert.equal(Remote.expectRefused({ question: asking.question, seq: 10 }, pane), false);
  // Another question, a screen from before this dialog, a seq the pane hasn't reached, no question seen.
  assert.equal(Remote.expectRefused({ question: 'Do you want to make this edit to b.txt?', seq: 12 }, pane), true);
  assert.equal(Remote.expectRefused({ question: asking.question, seq: 9 }, pane), true);
  assert.equal(Remote.expectRefused({ question: asking.question, seq: 13 }, pane), true);
  assert.equal(Remote.expectRefused({ question: null, seq: 12 }, pane), true);
  assert.equal(Remote.expectRefused({ seq: 12 }, pane), true);
  // The pane stopped asking: the old answer doesn't go. Not asking and no question seen: a message may go.
  const idle = { asking: false, question: null, dialogSeq: undefined, seq: 12 };
  assert.equal(Remote.expectRefused({ question: asking.question, seq: 12 }, idle), true);
  assert.equal(Remote.expectRefused({ question: null, seq: 12 }, idle), false);
  assert.equal(Remote.expectRefused({ seq: 3 }, idle), false);
  // An asking pane always has a first seq; without one nothing is answered.
  assert.equal(Remote.expectRefused({ question: asking.question, seq: 12 }, { ...pane, dialogSeq: undefined }), true);
});

test('expect: a dialog answered and asked again, or gone at a look, is new', () => {
  const key = Remote.dialogKey(screen(dialog()));
  let tracker = Remote.trackDialog(null, key, 10);
  // Keys went in at seq 12. Until the screen changes it is the same dialog.
  Remote.dialogInput(tracker, 12);
  assert.equal(Remote.trackDialog(tracker, key, 12).seq, 10);
  // The screen changed after the keys and shows the same question again: a new dialog, first seen now.
  tracker = Remote.trackDialog(tracker, key, 15);
  assert.equal(tracker.seq, 15);
  assert.equal(tracker.input, false);
  // Gone at a look, then back: new. Another question: new.
  assert.equal(Remote.trackDialog(tracker, '', 16), null);
  assert.equal(Remote.trackDialog(null, key, 20).seq, 20);
  assert.equal(Remote.trackDialog(tracker, Remote.dialogKey(screen(dialog('b.txt'))), 16).seq, 16);
  // A pane that asks only through the agent's notification still has a dialog.
  assert.notEqual(Remote.dialogKey(screen(working)), '');
});

test('arrow keys in a dialog and a resize keep it one dialog; another dialog is new', () => {
  const tracker = {};
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog()), 1000), true);
  tracker.input = true;
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog()), 2000), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog('b.txt')), 3000), true);
  // Rewrapped after a resize: a changed signature is still the same dialog.
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog('a much longer name.txt')), 4000, true), false);
  assert.equal(Remote.dialogAppeared(tracker, screen(dialog('c.txt')), 5000), true);
});
