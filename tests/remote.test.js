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
