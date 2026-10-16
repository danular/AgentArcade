// The mascot (web/mascot.js) against a tiny DOM stub: the status mapping, the five-click window, the Konami code
// (never while typing), reduced motion and setEnabled(false), the confetti canvas, and that web/mascot.svg is the same
// drawing. Run: node --test tests/mascot.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const M = createRequire(import.meta.url)('../web/mascot.js');

// ---------- the stub ----------

class Classes {
  constructor() { this.set = new Set(); }
  add(name) { this.set.add(name); }
  remove(name) { this.set.delete(name); }
  contains(name) { return this.set.has(name); }
  toggle(name, force) {
    const on = force === undefined ? !this.set.has(name) : !!force;
    if (on) this.set.add(name); else this.set.delete(name);
    return on;
  }
}

class Element {
  constructor(tag, page) {
    this.page = page;
    this.tagName = tag.toUpperCase();
    this.nodeType = 1;
    this.attributes = {};
    this.classList = new Classes();
    this.style = {};
    this.listeners = {};
    this.parentNode = this.parentElement = null;
    this.isConnected = false;
    this.isContentEditable = false;
    this.parts = new Map();
    this.drawn = 0;
    this.animations = [];
    this.rect = { left: 0, top: 0, width: 34, height: 34 };
  }
  setAttribute(name, value) { this.attributes[name] = String(value); if (name === 'id') this.id = String(value); }
  getAttribute(name) { return name in this.attributes ? this.attributes[name] : null; }
  addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); }
  fire(type, event = {}) { for (const fn of this.listeners[type] || []) fn({ target: this, currentTarget: this, ...event }); }
  set innerHTML(html) {
    // The old content leaves the page.
    for (const part of this.parts.values()) {
      part.parentNode = part.parentElement = null;
      part.isConnected = false;
    }
    this.html = html;
    this.drawn++;
    this.parts.clear();
  }
  get innerHTML() { return this.html || ''; }
  // Every selector finds one stand-in child, made on first use, so the module can keep references to its parts.
  querySelector(selector) {
    if (!this.parts.has(selector)) {
      const part = new Element(selector.startsWith('svg') ? 'svg' : 'g', this.page);
      part.parentNode = part.parentElement = this;
      part.isConnected = this.isConnected;
      this.parts.set(selector, part);
    }
    return this.parts.get(selector);
  }
  appendChild(child) { child.parentNode = child.parentElement = this; child.isConnected = true; this.page.added.push(child); return child; }
  getBoundingClientRect() { const r = this.rect; return { ...r, right: r.left + r.width, bottom: r.top + r.height }; }
  animate(keyframes, timing) {
    const animation = { keyframes, timing, cancelled: false, cancel() { this.cancelled = true; if (this.oncancel) this.oncancel(); } };
    this.animations.push(animation);
    return animation;
  }
  getContext() { return this.page.context; }
}

function makePage({ reduced = false } = {}) {
  const page = { time: 0, timers: [], frames: [], added: [], canvases: 0, toasts: [], drawnPieces: 0 };
  page.context = {
    setTransform() {}, clearRect() {}, save() {}, restore() {}, translate() {}, rotate() {},
    fillRect() { page.drawnPieces++; },
  };
  const doc = {
    hidden: false,
    listeners: {},
    addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); },
    createElement(tag) { if (tag === 'canvas') page.canvases++; return new Element(tag, page); },
    getElementById(id) { return page.added.find(el => el.id === id) || null; },
    querySelectorAll() { return page.dots || []; },
  };
  doc.head = new Element('head', page);
  doc.body = new Element('body', page);
  doc.documentElement = new Element('html', page);
  for (const el of [doc.head, doc.body, doc.documentElement]) el.isConnected = true;
  doc.activeElement = doc.body;
  const query = { matches: reduced, listeners: [], addEventListener(type, fn) { this.listeners.push(fn); } };
  const win = {
    document: doc,
    innerWidth: 1280, innerHeight: 800, devicePixelRatio: 1,
    listeners: {},
    addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); },
    matchMedia: () => query,
    getComputedStyle: () => ({ getPropertyValue: () => '' }),
  };
  let ids = 0;
  const options = {
    now: () => page.time,
    random: () => .5,
    setTimeout: (fn, ms) => { const id = ++ids; page.timers.push({ id, at: page.time + ms, fn }); return id; },
    clearTimeout: id => { page.timers = page.timers.filter(t => t.id !== id); },
    requestAnimationFrame: fn => { page.frames.push(fn); return page.frames.length; },
  };
  page.win = win;
  page.doc = doc;
  page.query = query;
  page.mascot = M.createMascot(win, options);
  page.mascot.onToast = text => page.toasts.push(text);
  // Moves the clock, running timers that come due.
  page.advance = ms => {
    const end = page.time + ms;
    for (;;) {
      const due = page.timers.filter(t => t.at <= end).sort((a, b) => a.at - b.at)[0];
      if (!due) break;
      page.timers = page.timers.filter(t => t !== due);
      page.time = due.at;
      due.fn();
    }
    page.time = end;
  };
  // Runs the animation frames that were asked for, at the current time; returns how many ran.
  page.frame = () => {
    const frames = page.frames;
    page.frames = [];
    for (const fn of frames) fn(page.time);
    return frames.length;
  };
  page.mount = (size = 34) => {
    const el = new Element('button', page);
    el.isConnected = true;
    page.mascot.mount(el, { size });
    return el;
  };
  page.key = (key, target = doc.body, extra = {}) => {
    for (const fn of win.listeners.keydown || []) fn({ key, target, ...extra });
  };
  return page;
}

const KEYS = ['ArrowUp', 'ArrowUp', 'ArrowDown', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'ArrowLeft', 'ArrowRight', 'b', 'a'];

// ---------- the status on the antenna ----------

test('the antenna shows needs over working over idle', () => {
  assert.equal(M.stateOf(0, 0), 'idle');
  assert.equal(M.stateOf(2, 0), 'working');
  assert.equal(M.stateOf(2, 1), 'needs');
  assert.equal(M.stateOf(0, 1), 'needs');
  assert.equal(M.stateOf(undefined, null), 'idle');
  assert.equal(M.stateOf('3', 'x'), 'working');
  assert.equal(M.stateOf(-1, 0), 'idle');

  const page = makePage();
  const el = page.mount();
  const svg = el.querySelector('svg.aa-mascot');
  assert.equal(svg.getAttribute('data-state'), 'idle');
  page.mascot.setState({ working: 2, needs: 0 });
  assert.equal(svg.getAttribute('data-state'), 'working');
  page.mascot.setState({ working: 2, needs: 1 });
  assert.equal(svg.getAttribute('data-state'), 'needs');
  page.mascot.setState(null);
  assert.equal(svg.getAttribute('data-state'), 'idle');
  // A mascot mounted later (the About tab) starts in the current state.
  page.mascot.setState({ working: 1, needs: 0 });
  assert.equal(page.mount(120).querySelector('svg.aa-mascot').getAttribute('data-state'), 'working');
});

test('the stylesheet: pulsing orange while working, red with wide eyes when needed, nothing moves when still', () => {
  const css = M.CSS.replace(/\s+/g, ' ');
  assert.match(css, /\[data-state="working"\] \{ color: var\(--working, var\(--orange, #f08a3c\)\)/);
  assert.match(css, /\[data-state="needs"\] \{ color: var\(--needs, var\(--red, #f26b6b\)\)/);
  assert.match(css, /\[data-state="working"\] \.m-ant \{ animation: aam-pulse/);
  assert.match(css, /\[data-state="needs"\] \.m-happy \{ display: none; \}/);
  assert.match(css, /\[data-state="needs"\] \.m-wide \{ display: inline; \}/);
  assert.doesNotMatch(css, /\[data-state="needs"\] \.m-ant \{ animation/);
  assert.match(css, /\.aa-mascot\.still, \.aa-mascot\.still \* \{ animation: none !important; \}/);
  assert.match(css, /@media \(prefers-reduced-motion: reduce\)/);
  // The live drawing hides its state parts until the stylesheet shows them.
  const live = M.markup('t', 34);
  for (const part of ['m-wide', 'm-tint', 'm-halo']) assert.match(live, new RegExp(`class="${part}" display="none"`));
});

// ---------- five quick clicks ----------

test('five clicks within 1.6 s insert a coin', () => {
  const combo = M.comboCounter();
  assert.deepEqual([0, 300, 600, 900].map(combo), [false, false, false, false]);
  assert.equal(combo(1200), true);
  // The window starts again after a coin.
  assert.equal(combo(1300), false);

  const slow = M.comboCounter();
  // Exactly 1.6 s from the first to the fifth click is too slow.
  assert.deepEqual([0, 400, 800, 1200, 1600].map(slow), [false, false, false, false, false]);
  assert.equal(slow(1700), true);
});

test('a click hops with 16 pieces; the fifth quick click shows the credit toast with 70 pieces and a dot wave', () => {
  const page = makePage();
  const el = page.mount();
  const svg = el.querySelector('svg.aa-mascot');
  const hop = svg.querySelector('.m-hop');
  const dot = new Element('i', page);
  dot.rect = { left: 200, top: 40, width: 8, height: 8 };
  const hiddenDot = new Element('i', page);
  hiddenDot.rect = { left: 0, top: 0, width: 0, height: 0 };
  page.dots = [dot, hiddenDot];

  el.fire('click');
  assert.equal(hop.animations.length, 1);
  assert.equal(page.canvases, 1);
  assert.equal(page.frame(), 1);
  assert.equal(page.drawnPieces, 16);
  assert.deepEqual(page.toasts, []);

  // Let the confetti land, then five quick clicks.
  for (let i = 0; i < 60 && page.frames.length; i++) { page.advance(50); page.frame(); }
  assert.equal(page.frames.length, 0);
  page.advance(2000);
  for (let i = 0; i < 5; i++) { el.fire('click'); page.advance(100); }
  assert.deepEqual(page.toasts, ['INSERT COIN · Credit 1']);
  assert.equal(hop.animations.length, 6);
  page.drawnPieces = 0;
  page.frame();
  assert.equal(page.drawnPieces, 4 * 16 + 70);
  assert.equal(dot.animations.length, 1, 'the visible dot waves');
  assert.equal(hiddenDot.animations.length, 0, 'a hidden dot does not');
  assert.equal(page.canvases, 1, 'one canvas, reused');

  page.advance(2000);
  for (let i = 0; i < 5; i++) { el.fire('click'); page.advance(50); }
  assert.deepEqual(page.toasts, ['INSERT COIN · Credit 1', 'INSERT COIN · Credit 2']);
});

test('the confetti canvas never takes focus or clicks, and frees itself when the pieces land', () => {
  const page = makePage();
  const el = page.mount();
  el.fire('click');
  const canvas = page.added.find(node => node.tagName === 'CANVAS');
  assert.ok(canvas);
  assert.equal(canvas.getAttribute('tabindex'), '-1');
  assert.equal(canvas.getAttribute('aria-hidden'), 'true');
  assert.match(canvas.style.cssText, /position:fixed/);
  assert.match(canvas.style.cssText, /pointer-events:none/);
  assert.equal(canvas.width, 1280);
  let frames = 0;
  while (page.frames.length && frames < 200) { page.advance(16); page.frame(); frames++; }
  assert.ok(frames > 50 && frames < 200, `${frames} frames`);
  assert.equal(page.frames.length, 0, 'no more animation frames once nothing flies');
  assert.equal(canvas.width, 0);
  assert.equal(canvas.hidden, true);
  // Idle: time passes, no frames are asked for.
  page.advance(10000);
  assert.equal(page.frames.length, 0);
});

// ---------- the Konami code ----------

test('the Konami code, also after extra presses of the first key', () => {
  const konami = M.konamiCounter();
  assert.deepEqual(KEYS.map(konami), [...Array(9).fill(false), true]);
  const again = M.konamiCounter();
  assert.equal(['ArrowUp', ...KEYS.slice(0, 9), 'A'].map(again).at(-1), true, '↑↑↑ then the rest, A with Shift');
  const wrong = M.konamiCounter();
  assert.equal([...KEYS.slice(0, 5), 'x', ...KEYS.slice(5)].map(wrong).some(Boolean), false);
  const forgotten = M.konamiCounter();
  KEYS.slice(0, 6).forEach(forgotten);
  forgotten(null);
  assert.equal(KEYS.slice(6).map(forgotten).some(Boolean), false);
});

test('Konami spins the mascot with a toast, never while typing in a terminal, input or textarea', () => {
  const page = makePage();
  const el = page.mount();
  const spin = el.querySelector('svg.aa-mascot').querySelector('.m-spin');

  const xtermText = new Element('textarea', page);
  xtermText.classList.add('xterm-helper-textarea');
  const input = new Element('input', page);
  const editable = new Element('div', page);
  editable.isContentEditable = true;
  const terminal = new Element('div', page);
  terminal.classList.add('xterm');
  const insideTerminal = new Element('span', page);
  insideTerminal.parentElement = terminal;

  for (const target of [xtermText, input, editable, insideTerminal]) {
    for (const key of KEYS) page.key(key, target);
    assert.equal(page.toasts.length, 0, target.tagName);
  }
  // Typing in the middle of the code breaks it.
  for (const key of KEYS.slice(0, 5)) page.key(key);
  page.key('x', input);
  for (const key of KEYS.slice(5)) page.key(key);
  assert.equal(page.toasts.length, 0);
  // Shortcuts and held keys don't count; Shift alone is skipped.
  for (const key of KEYS.slice(0, 8)) page.key(key);
  page.key('b', page.doc.body, { ctrlKey: true });
  assert.equal(page.toasts.length, 0);
  for (const key of KEYS) page.key(key);
  assert.deepEqual(page.toasts, ['↑↑↓↓←→←→ B A · 30 lives']);
  assert.equal(spin.animations.length, 1);
  assert.equal(page.frames.length, 1, 'a burst');
  for (const key of KEYS.slice(0, 8)) page.key(key);
  page.key('Shift');
  page.key('B');
  page.key('A', page.doc.body, { repeat: false });
  assert.equal(page.toasts.length, 2);

  assert.equal(M.isTypingTarget(xtermText), true);
  assert.equal(M.isTypingTarget(insideTerminal), true);
  assert.equal(M.isTypingTarget(new Element('button', page)), false);
  assert.equal(M.isTypingTarget(null), false);
});

// ---------- reduced motion and the switch ----------

test('reduced motion: no movement or particles, the colours and toasts stay', () => {
  const page = makePage({ reduced: true });
  const el = page.mount();
  const svg = el.querySelector('svg.aa-mascot');
  assert.equal(svg.classList.contains('still'), true);
  assert.equal(page.timers.length, 0, 'no blink timer');
  page.mascot.setState({ working: 1, needs: 1 });
  assert.equal(svg.getAttribute('data-state'), 'needs');
  for (let i = 0; i < 5; i++) el.fire('click');
  for (const key of KEYS) page.key(key);
  assert.deepEqual(page.toasts, ['INSERT COIN · Credit 1', '↑↑↓↓←→←→ B A · 30 lives']);
  assert.equal(page.canvases, 0, 'no canvas');
  assert.equal(page.frames.length, 0, 'no animation frames');
  assert.equal(svg.querySelector('.m-hop').animations.length + svg.querySelector('.m-spin').animations.length, 0);
  for (const fn of page.doc.listeners.pointermove) fn({ clientX: 0, clientY: 400 });
  page.advance(100);
  assert.ok(!svg.querySelector('.m-look').style.transform, 'the eyes stay put');

  // The system setting changes back: motion returns.
  for (const fn of page.query.listeners) fn({ matches: false });
  assert.equal(svg.classList.contains('still'), false);
  assert.equal(page.timers.length, 1, 'blinking again');
  el.fire('click');
  assert.equal(page.canvases, 1);
  assert.equal(page.frames.length, 1);
});

test('setEnabled(false) stops the blink, the look, the hop and the confetti at once', () => {
  const page = makePage();
  const el = page.mount();
  const svg = el.querySelector('svg.aa-mascot');
  const eyes = svg.querySelector('.m-eyes');
  const look = svg.querySelector('.m-look');

  // Blinks every 4-6 s.
  assert.equal(page.timers.length, 1);
  assert.equal(page.timers[0].at, 5000);
  page.advance(5000);
  assert.equal(eyes.animations.length, 1);
  assert.equal(eyes.animations[0].timing.duration, 180);
  assert.equal(page.timers.length, 1);

  // The eyes follow the pointer, by at most 2.4.
  for (const fn of page.doc.listeners.pointermove) fn({ clientX: -2000, clientY: 17 });
  page.advance(50);
  assert.equal(look.style.transform, 'translate(-2.40px, 0.00px)');
  for (const fn of page.doc.listeners.pointermove) fn({ clientX: 17 + 3, clientY: 17 - 4 });
  page.advance(50);
  const [x, y] = look.style.transform.match(/-?[\d.]+/g).map(Number);
  assert.ok(Math.hypot(x, y) < .2, 'small when the pointer is on the mascot');

  el.fire('click');
  const hop = svg.querySelector('.m-hop').animations[0];
  assert.equal(page.frames.length, 1);
  page.mascot.setEnabled(false);
  assert.equal(svg.classList.contains('still'), true);
  assert.equal(hop.cancelled, true, 'the running hop stops');
  assert.equal(look.style.transform, '', 'the eyes look ahead');
  assert.equal(page.timers.length, 0, 'no blink timer');
  page.drawnPieces = 0;
  page.frame();
  assert.equal(page.drawnPieces, 0, 'the confetti is gone');
  assert.equal(page.frames.length, 0, 'and so are the frames');
  el.fire('click');
  assert.equal(page.frames.length, 0);
  assert.equal(svg.querySelector('.m-hop').animations.length, 1);

  page.mascot.setEnabled(true);
  assert.equal(svg.classList.contains('still'), false);
  assert.equal(page.timers.length, 1);
});

// ---------- mounting ----------

test('mount is idempotent and redraws only when the page replaced the drawing', () => {
  const page = makePage();
  const el = page.mount(34);
  assert.equal(el.drawn, 1);
  assert.match(el.innerHTML, /^<svg class="aa-mascot" data-state="idle" viewBox="0 0 128 128" width="34" height="34" aria-hidden="true"/);
  assert.equal(el.getAttribute('aria-label'), 'Agent Arcade mascot');
  page.mascot.mount(el, { size: 40 });
  assert.equal(el.drawn, 1);
  assert.equal(el.querySelector('svg.aa-mascot').getAttribute('width'), '40');
  assert.equal(el.listeners.click.length, 1);
  assert.equal(page.added.filter(node => node.id === 'aa-mascot-style').length, 1, 'one stylesheet');
  el.innerHTML = '';
  page.mascot.mount(el);
  assert.equal(el.drawn, 3);
  assert.match(el.innerHTML, /width="40" height="40"/);
  assert.equal(el.listeners.click.length, 1);
  // Each copy gets its own gradient ids.
  const other = page.mount(120);
  const ids = html => [...html.matchAll(/ id="([^"]+)"/g)].map(m => m[1]);
  assert.equal(ids(el.innerHTML).filter(id => ids(other.innerHTML).includes(id)).length, 0);
});

test('web/mascot.svg is the same drawing, static', () => {
  // Git may check the file out with Windows line ends.
  const file = readFileSync(join(here, '..', 'web', 'mascot.svg'), 'utf8').replace(/\r\n/g, '\n');
  assert.ok(file === M.standaloneSvg(), 'web/mascot.svg is out of date; write it again (see standaloneSvg in web/mascot.js)');
  assert.doesNotMatch(file, /m-wide|m-tint|m-halo|class=/);
  // The same body, visor and eyes as the live drawing.
  const live = M.markup('x', 34);
  for (const d of file.match(/ d="[^"]+"/g)) assert.ok(live.includes(d), d);
  assert.match(file, /^<svg xmlns="http:\/\/www.w3.org\/2000\/svg" viewBox="0 0 128 128"/);
});
