// The window-shape rules (web/layout.js): when a window counts as portrait (with a margin around square), when the
// sidebar becomes a drawer, and how many columns of panes each shape gets. Run: node --test tests/layout.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const Layout = createRequire(import.meta.url)('../web/layout.js');

test('portrait: taller than wide, and square-ish windows keep what they had', () => {
  assert.equal(Layout.portrait(720, 1250), true);
  assert.equal(Layout.portrait(1920, 1080), false);
  assert.equal(Layout.portrait(1000, 1000), false);
  // Near square: no flip either way.
  assert.equal(Layout.portrait(1000, 1050, false), false);
  assert.equal(Layout.portrait(1000, 1080, false), true);
  assert.equal(Layout.portrait(1000, 960, true), true);
  assert.equal(Layout.portrait(1000, 940, true), false);
  // No size yet (a hidden window): keep the answer.
  assert.equal(Layout.portrait(0, 0, true), true);
  assert.equal(Layout.portrait(0, 0, null), false);
});

test('the sidebar is a drawer below 900 px', () => {
  assert.equal(Layout.drawer(720), true);
  assert.equal(Layout.drawer(899), true);
  assert.equal(Layout.drawer(900), false);
  assert.equal(Layout.drawer(1920), false);
  assert.equal(Layout.drawer(0), false);
});

test('landscape keeps the grid people know, whatever the size', () => {
  const grid = n => Layout.paneGrid(n, 1650, 1100, false);
  assert.deepEqual([1, 2, 3, 4, 5, 6, 7].map(n => grid(n).cols), [1, 2, 3, 2, 3, 3, 3]);
  assert.deepEqual([1, 2, 3, 4, 5, 6, 7].map(n => grid(n).rows), [1, 1, 1, 2, 2, 2, 3]);
  assert.deepEqual(Layout.paneGrid(0, 1650, 1100, false), { cols: 1, rows: 1 });
});

test('portrait stacks panes until they would get too short', () => {
  // A 1080 × 1920 monitor at 150 % turned on its side: 720 × 1280 CSS px, about 704 × 1180 for the panes.
  const tall = n => Layout.paneGrid(n, 704, 1180, true);
  assert.deepEqual(tall(2), { cols: 1, rows: 2 });
  assert.deepEqual(tall(3), { cols: 1, rows: 3 });
  assert.deepEqual(tall(4), { cols: 1, rows: 4 });
  assert.deepEqual(tall(5), { cols: 1, rows: 5 });
  // Six stacked would be 187 px tall each (about 10 rows of text); two columns of 340 px win.
  assert.deepEqual(tall(6), { cols: 2, rows: 3 });
  // A 1080 × 1800 stage: four stack (1064 × 440 each), six go 2 × 3 (528 × 589 beats 1064 × 290).
  assert.deepEqual(Layout.paneGrid(4, 1080, 1800, true), { cols: 1, rows: 4 });
  assert.deepEqual(Layout.paneGrid(6, 1080, 1800, true), { cols: 2, rows: 3 });
  // A short stage, like a portrait window with the side panel docked under it: two stay stacked until too short.
  assert.deepEqual(Layout.paneGrid(2, 704, 700, true), { cols: 1, rows: 2 });
  assert.deepEqual(Layout.paneGrid(3, 704, 640, true), { cols: 2, rows: 2 });
});
