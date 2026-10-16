// Animations that never end must step (steps(...)), not ease: a smooth endless animation makes WebView2 draw the whole
// window at the screen's refresh rate (about 13% of a laptop GPU for one pulsing dot), a stepped one only when its
// value changes. Checks every stylesheet in web/ and the mascot's own CSS. Run: node --test tests/animations.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const web = join(dirname(fileURLToPath(import.meta.url)), '..', 'web');
const sheets = readdirSync(web).filter(name => name.endsWith('.css')).map(name => [name, readFileSync(join(web, name), 'utf8')]);
sheets.push(['mascot.js CSS', createRequire(import.meta.url)('../web/mascot.js').CSS]);

// Each animation shorthand or animation-iteration-count that repeats forever.
function endless(css) {
  const found = [];
  for (const match of css.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/animation(?:-iteration-count)?\s*:\s*([^;}]+)/g)) {
    if (/\binfinite\b/.test(match[1])) found.push(match[1].trim());
  }
  return found;
}

test('endless animations step instead of easing', () => {
  const all = sheets.flatMap(([name, css]) => endless(css).map(value => ({ name, value })));
  assert.ok(all.length >= 4, `expected the status dots and the mascot's pulse, found ${all.length}`);
  for (const { name, value } of all) assert.match(value, /\bsteps\(\s*[1-6]\b/, `${name}: "${value}" eases forever; use steps(3, jump-none)`);
});

test('the finder sees a smooth endless animation', () => {
  assert.deepEqual(endless('.x { animation: pulse 1s ease-in-out infinite; } /* animation: y infinite */ .y { animation: fade .1s; }'), ['pulse 1s ease-in-out infinite']);
});
