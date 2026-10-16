'use strict';

// The mascot: the app icon's robot-A as a vector drawing, for the sidebar and the About tab. It blinks, follows the
// pointer with its eyes and wears the agents' status on its antenna: pulsing orange while one works, red with wide eyes
// when one needs you. A click makes it hop with a little confetti; five quick clicks insert a coin; the Konami code
// makes it spin. No sound, no network and no storage: the only state is an in-memory credit counter.
// Windows "reduce motion" (prefers-reduced-motion) or Mascot.setEnabled(false) stops every movement and particle;
// the status colours stay.
//
// Interface (loaded before app.js):
//   Mascot.mount(element, { size })   draws the robot into element (idempotent); a click on element plays with it
//   Mascot.setState({ working, needs })
//   Mascot.setEnabled(bool)
//   Mascot.onToast = fn(text)         set by app.js; shows the easter-egg toasts
(() => {
  // ---------- the drawing (128 x 128, traced from the app icon) ----------

  const BODY = 'M64 33.66C44.53 33.66 45.03 35.78 30.43 64.87L15.74 94.15C11.36 102.87 10.97 111.89 27.73 111.89' +
    'C40.68 111.89 41.2 110.93 45.76 100.85L48.52 94.76C51.73 87.8 51.36 87.8 59.36 87.8H68.64' +
    'C76.64 87.8 76.27 87.8 79.48 94.76L82.24 100.85C86.8 110.93 87.32 111.89 100.27 111.89' +
    'C117.03 111.89 116.64 102.87 112.26 94.15L97.57 64.87C82.97 35.78 83.47 33.66 64 33.66Z';
  const VISOR = 'M52.79 50.78H75.21C82.71 50.78 89.19 57.77 89.19 67.62C89.19 73.16 84.22 78.09 77.93 78.09' +
    'H50.07C43.78 78.09 38.81 73.16 38.81 67.62C38.81 57.77 45.29 50.78 52.79 50.78Z';
  const HAPPY = 'M46.75 68.3A6.2 6.2 0 0 1 59.15 68.3M68.85 68.3A6.2 6.2 0 0 1 81.25 68.3';
  const BALL = { cx: 64, cy: 21.4, r: 8.8 };
  const EYE = '#4debff';

  const stops = list => list.map(([offset, color, opacity]) =>
    `<stop offset="${offset}" stop-color="${color}"${opacity === undefined ? '' : ` stop-opacity="${opacity}"`}/>`).join('');

  // The drawing without its frame. id makes the gradient ids unique per copy on the page. live adds the parts that
  // only the page shows (state tint, halo, wide eyes), hidden until the stylesheet below shows them.
  function art(id, live) {
    const ball = `cx="${BALL.cx}" cy="${BALL.cy}" r="${BALL.r}"`;
    return '<defs>' +
      `<linearGradient id="${id}b" gradientUnits="userSpaceOnUse" x1="60" y1="33.7" x2="68" y2="112">` + stops([
        [0, '#ffe22f'], [.08, '#fec12b'], [.16, '#fd9a38'], [.24, '#fd7748'], [.33, '#fb5a72'], [.42, '#f8478f'],
        [.52, '#f531bf'], [.58, '#e82ae0'], [.63, '#d024f7'], [.67, '#b522fc'], [.71, '#9a2af6'], [.76, '#7144fc'],
        [.88, '#3d62fd'], [1, '#23a0ff']]) + '</linearGradient>' +
      // A soft light along the left edge, as on the icon.
      `<linearGradient id="${id}s" gradientUnits="userSpaceOnUse" x1="30.4" y1="64.9" x2="39.4" y2="69.4">` +
        stops([[0, '#fff', .3], [1, '#fff', 0]]) + '</linearGradient>' +
      `<linearGradient id="${id}k" gradientUnits="userSpaceOnUse" x1="57" y1="13" x2="70" y2="29">` + stops([
        [0, '#ffe24a'], [.25, '#fecf2c'], [.44, '#fda52e'], [.52, '#fd8f37'], [.64, '#fc6047'], [.74, '#fc4051'],
        [.89, '#fc206d'], [1, '#f81a72']]) + '</linearGradient>' +
      `<linearGradient id="${id}t" x1="0" y1="0" x2="0" y2="1">` + stops([[0, '#fb2f5c'], [1, '#fe9a3c']]) +
        '</linearGradient>' +
      // Indigo on the inner sides of the legs, so the outer feet stay the bright blue of the icon.
      `<radialGradient id="${id}i" gradientUnits="userSpaceOnUse" cx="64" cy="110" r="32">` +
        stops([[0, '#2a17d0', .6], [.6, '#2a17d0', .28], [1, '#2a17d0', 0]]) + '</radialGradient>' +
      `<filter id="${id}g" x="-20%" y="-40%" width="140%" height="180%"><feGaussianBlur stdDeviation="1.6"/></filter>` +
      (live
        ? `<radialGradient id="${id}h">` + stops([[0, 'currentColor', .8], [.5, 'currentColor', .6], [.72, 'currentColor', .22], [1, 'currentColor', 0]]) +
            '</radialGradient>' +
          `<radialGradient id="${id}d" cx=".38" cy=".32" r=".75">` +
            stops([[0, '#fff', .28], [.45, '#fff', 0], [1, '#000', .22]]) + '</radialGradient>'
        : '') +
      '</defs>' +
      '<rect width="128" height="128" rx="28" fill="#0d0239"/>' +
      '<g class="m-hop"><g class="m-spin">' +
      `<rect x="61.85" y="29.6" width="4.3" height="6" fill="url(#${id}t)"/>` +
      (live ? `<circle class="m-halo" display="none" cx="${BALL.cx}" cy="${BALL.cy}" r="17" fill="url(#${id}h)"/>` : '') +
      '<g class="m-ant">' +
      `<circle ${ball} fill="url(#${id}k)"/>` +
      (live ? `<g class="m-tint" display="none"><circle ${ball} fill="currentColor"/><circle ${ball} fill="url(#${id}d)"/></g>` : '') +
      '<ellipse cx="60.2" cy="16.9" rx="2.7" ry="1.7" transform="rotate(-38 60.2 16.9)" fill="#fff8d0" opacity=".75"/>' +
      '</g>' +
      `<path d="${BODY}" fill="url(#${id}b)"/>` +
      `<path d="${BODY}" fill="url(#${id}s)"/>` +
      `<path d="${BODY}" fill="url(#${id}i)"/>` +
      `<path d="${VISOR}" fill="#0e023a"/>` +
      '<g class="m-eyes"><g class="m-look">' +
      `<path class="m-happy" d="${HAPPY}" fill="none" stroke="${EYE}" stroke-opacity=".6" stroke-width="4" stroke-linecap="round" filter="url(#${id}g)"/>` +
      `<path class="m-happy" d="${HAPPY}" fill="none" stroke="${EYE}" stroke-width="3.3" stroke-linecap="round"/>` +
      (live
        ? '<g class="m-wide" display="none">' +
          `<g filter="url(#${id}g)" opacity=".6"><circle cx="52.95" cy="65.4" r="5.2" fill="${EYE}"/><circle cx="75.05" cy="65.4" r="5.2" fill="${EYE}"/></g>` +
          `<circle cx="52.95" cy="65.4" r="4.7" fill="${EYE}"/><circle cx="75.05" cy="65.4" r="4.7" fill="${EYE}"/>` +
          '<circle cx="51.3" cy="63.7" r="1.4" fill="#fff"/><circle cx="73.4" cy="63.7" r="1.4" fill="#fff"/></g>'
        : '') +
      '</g></g></g></g>';
  }

  // The static drawing on its own: web/mascot.svg, for the website. After changing the drawing, write it again with
  // node -e "require('fs').writeFileSync('web/mascot.svg', require('./web/mascot.js').standaloneSvg())"
  const standaloneSvg = () => '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" width="128" height="128" ' +
    'role="img" aria-label="Agent Arcade"><title>Agent Arcade</title>' + art('aa', false).replace(/ class="[^"]*"/g, '') +
    '</svg>\n';

  const markup = (id, size) => '<svg class="aa-mascot" data-state="idle" viewBox="0 0 128 128" ' +
    (size ? `width="${size}" height="${size}"` : 'width="100%" height="100%"') +
    ' aria-hidden="true" focusable="false">' + art(id, true) + '</svg>';

  // The states, and the stillness switch (.still, or the system's reduced motion). The working pulse steps instead of
  // easing, like the status dots in app.css: an endless smooth animation redraws the whole window every frame.
  const CSS = `
.aa-mascot { display: block; overflow: visible; }
.aa-mascot .m-hop { transform-box: view-box; transform-origin: 64px 112px; }
.aa-mascot .m-spin { transform-box: view-box; transform-origin: 64px 68px; }
.aa-mascot .m-eyes { transform-box: view-box; transform-origin: 64px 65.5px; }
.aa-mascot .m-look { transition: transform .25s ease-out; }
.aa-mascot[data-state="working"] { color: var(--working, var(--orange, #f08a3c)); }
.aa-mascot[data-state="needs"] { color: var(--needs, var(--red, #f26b6b)); }
.aa-mascot[data-state="working"] .m-tint, .aa-mascot[data-state="needs"] .m-tint,
.aa-mascot[data-state="working"] .m-halo, .aa-mascot[data-state="needs"] .m-halo { display: inline; }
.aa-mascot[data-state="needs"] .m-happy { display: none; }
.aa-mascot[data-state="needs"] .m-wide { display: inline; }
.aa-mascot[data-state="working"] .m-ant { animation: aam-pulse 1.3s steps(3, jump-none) infinite; }
.aa-mascot[data-state="working"] .m-halo { animation: aam-glow 1.3s steps(3, jump-none) infinite; }
.aa-mascot.still .m-look { transform: none !important; transition: none; }
.aa-mascot.still, .aa-mascot.still * { animation: none !important; }
@keyframes aam-pulse { 50% { opacity: .45; } }
@keyframes aam-glow { 50% { opacity: .25; } }
@media (prefers-reduced-motion: reduce) {
  .aa-mascot * { animation: none !important; transition: none !important; }
}`;

  // ---------- pure rules (also used by tests/mascot.test.mjs) ----------

  // The antenna: red with wide eyes when an agent needs you, else pulsing orange while one works, else the icon's ball.
  function stateOf(working, needs) {
    if ((Number(needs) || 0) > 0) return 'needs';
    if ((Number(working) || 0) > 0) return 'working';
    return 'idle';
  }

  // Five clicks within 1.6 s insert a coin. Returns fn(time in ms) that is true on the click that completes a combo.
  function comboCounter(windowMs = 1600, need = 5) {
    let clicks = [];
    return time => {
      clicks = clicks.filter(t => time - t < windowMs);
      clicks.push(time);
      if (clicks.length < need) return false;
      clicks = [];
      return true;
    };
  }

  const KONAMI = ['ArrowUp', 'ArrowUp', 'ArrowDown', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'ArrowLeft', 'ArrowRight', 'b', 'a'];

  // Returns fn(key) that is true when the last keys spell the Konami code; fn(null) forgets the keys so far.
  function konamiCounter() {
    let keys = [];
    return key => {
      if (key == null) {
        keys = [];
        return false;
      }
      keys.push(key.length === 1 ? key.toLowerCase() : key);
      if (keys.length > KONAMI.length) keys.shift();
      if (keys.length < KONAMI.length || keys.some((k, i) => k !== KONAMI[i])) return false;
      keys = [];
      return true;
    };
  }

  // Typing somewhere: a terminal (xterm's helper textarea or anything inside .xterm), an input, a textarea, a select,
  // editable text or an iframe (the side panel's preview).
  function isTypingTarget(element) {
    for (let node = element; node && node.nodeType === 1; node = node.parentElement || null) {
      if (/^(INPUT|TEXTAREA|SELECT|IFRAME)$/.test(node.tagName || '')) return true;
      if (node.isContentEditable) return true;
      if (node.classList && node.classList.contains('xterm')) return true;
    }
    return false;
  }

  // ---------- the live mascot ----------

  const COLORS = [['--working', '--orange', '#f08a3c'], ['--needs', '--red', '#f26b6b'], ['--done', '--green', '#3ecf7f']];
  const EXTRA_COLORS = ['#4debff', '#ffd43b', '#c03cff'];
  const DOTS = '.dot';
  const HOP = [
    { transform: 'scale(1, 1)' },
    { transform: 'scale(1.12, .86)', offset: .18 },
    { transform: 'translateY(-16px) scale(.94, 1.08)', offset: .45 },
    { transform: 'translateY(0) scale(1.06, .95)', offset: .75 },
    { transform: 'none' },
  ];
  const SPIN = [{ transform: 'rotate(0deg)' }, { transform: 'rotate(360deg)' }];
  const BLINK = [{ transform: 'scaleY(1)' }, { transform: 'scaleY(.12)' }, { transform: 'scaleY(1)' }];
  const WAVE = [{ translate: '0 0', scale: '1' }, { translate: '0 -6px', scale: '1.5', offset: .4 }, { translate: '0 0', scale: '1' }];

  function createMascot(win, options = {}) {
    const doc = win.document;
    const now = options.now || (() => win.performance.now());
    const random = options.random || Math.random;
    const later = options.setTimeout || ((fn, ms) => win.setTimeout(fn, ms));
    const cancelLater = options.clearTimeout || (handle => win.clearTimeout(handle));
    const frame = options.requestAnimationFrame || (fn => win.requestAnimationFrame(fn));

    const query = win.matchMedia ? win.matchMedia('(prefers-reduced-motion: reduce)') : null;
    let reduced = !!(query && query.matches);
    let enabled = true;
    let state = 'idle';
    let mounted = [];
    const byElement = new WeakMap();
    let copies = 0;
    let credits = 0;
    const combo = comboCounter();
    const konami = konamiCounter();
    const running = new Set();
    let blinkTimer = null;
    let lookTimer = null;
    let lookAt = null;
    let lastLook = -Infinity;

    // confetti on one fixed, full-window canvas that is only drawn on while pieces fly
    let canvas = null;
    let context = null;
    let pieces = [];
    let ticking = false;
    let lastTick = 0;

    const motion = () => enabled && !reduced;
    const live = () => (mounted = mounted.filter(m => m.el.isConnected));
    const visible = m => {
      const box = m.el.getBoundingClientRect();
      return box.width > 0 && box.height > 0 ? box : null;
    };

    function play(element, keyframes, timing) {
      if (!motion() || !element || typeof element.animate !== 'function') return;
      const animation = element.animate(keyframes, timing);
      running.add(animation);
      animation.onfinish = animation.oncancel = () => running.delete(animation);
    }

    function toast(text) {
      const show = api.onToast;
      if (typeof show === 'function') {
        try { show(text); } catch { /* the page's toast failed; the mascot carries on */ }
      }
    }

    function applyState() {
      for (const m of live()) m.svg.setAttribute('data-state', state);
    }

    function applyMotion() {
      const still = !motion();
      for (const m of live()) m.svg.classList.toggle('still', still);
      if (still) {
        for (const animation of [...running]) animation.cancel();
        running.clear();
        for (const m of mounted) m.look.style.transform = '';
        pieces = [];
        if (blinkTimer !== null) cancelLater(blinkTimer);
        blinkTimer = null;
      } else scheduleBlink();
    }

    function scheduleBlink() {
      if (blinkTimer !== null || !motion() || !mounted.length) return;
      blinkTimer = later(() => {
        blinkTimer = null;
        if (!motion() || !live().length) return;
        if (!doc.hidden) for (const m of mounted) if (visible(m)) play(m.eyes, BLINK, { duration: 180, easing: 'ease-in-out' });
        scheduleBlink();
      }, 4000 + random() * 2000);
    }

    // ---------- eyes that follow the pointer ----------

    function look() {
      lookTimer = null;
      if (!motion() || !lookAt) return;
      lastLook = now();
      for (const m of live()) {
        const box = visible(m);
        if (!box) continue;
        const dx = lookAt.x - (box.left + box.width / 2);
        const dy = lookAt.y - (box.top + box.height / 2);
        const distance = Math.hypot(dx, dy) || 1;
        const reach = Math.min(1, distance / (box.width * 1.5 + 40));
        m.look.style.transform = `translate(${(dx / distance * 2.4 * reach).toFixed(2)}px, ${(dy / distance * 1.6 * reach).toFixed(2)}px)`;
      }
    }

    function onPointer(event) {
      if (!motion() || !mounted.length) return;
      lookAt = { x: event.clientX, y: event.clientY };
      if (lookTimer !== null) return;
      const wait = 40 - (now() - lastLook);
      if (wait <= 0) look();
      else lookTimer = later(look, wait);
    }

    function lookAhead() {
      lookAt = null;
      for (const m of mounted) m.look.style.transform = '';
    }

    // ---------- confetti ----------

    function palette() {
      let style = null;
      try { style = win.getComputedStyle(doc.documentElement); } catch { /* no styles yet */ }
      const token = name => (style && style.getPropertyValue(name).trim()) || '';
      return COLORS.map(([first, second, fallback]) => token(first) || token(second) || fallback).concat(EXTRA_COLORS);
    }

    function sizeCanvas() {
      const ratio = win.devicePixelRatio || 1;
      canvas.width = Math.round(win.innerWidth * ratio);
      canvas.height = Math.round(win.innerHeight * ratio);
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
    }

    function ensureCanvas() {
      if (!canvas) {
        canvas = doc.createElement('canvas');
        canvas.className = 'aa-mascot-fx';
        canvas.setAttribute('aria-hidden', 'true');
        canvas.setAttribute('tabindex', '-1');
        canvas.style.cssText = 'position:fixed;left:0;top:0;width:100vw;height:100vh;pointer-events:none;z-index:2147483000;';
        context = canvas.getContext('2d');
        win.addEventListener('resize', () => { if (ticking) sizeCanvas(); });
      }
      if (!canvas.isConnected) doc.body.appendChild(canvas);
      return !!context;
    }

    function burst(x, y, count, power) {
      if (!motion() || !ensureCanvas()) return;
      const colors = palette();
      const born = now();
      for (let i = 0; i < count; i++) {
        const angle = -Math.PI / 2 + (random() - .5) * Math.PI * 1.5;
        const speed = power * (240 + random() * 300);
        pieces.push({
          x, y, vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed,
          turn: random() * Math.PI, spin: (random() - .5) * 14, flip: random() * Math.PI * 2, flutter: 7 + random() * 9,
          w: 5 + random() * 4, h: 3 + random() * 3, color: colors[i % colors.length], born, life: 1300 + random() * 500,
        });
      }
      if (pieces.length > 400) pieces.splice(0, pieces.length - 400);
      if (!ticking) {
        ticking = true;
        canvas.hidden = false;
        sizeCanvas();
        lastTick = born;
        frame(tick);
      }
    }

    function tick(time) {
      const step = Math.min(.05, Math.max(0, (time - lastTick) / 1000));
      lastTick = time;
      const ratio = win.devicePixelRatio || 1;
      const width = canvas.width / ratio;
      const height = canvas.height / ratio;
      context.clearRect(0, 0, width, height);
      pieces = pieces.filter(p => time - p.born < p.life && p.y < height + 40);
      const drag = Math.pow(.2, step);
      for (const p of pieces) {
        p.vx *= drag;
        p.vy = p.vy * drag + 1400 * step;
        p.x += p.vx * step;
        p.y += p.vy * step;
        p.turn += p.spin * step;
        p.flip += p.flutter * step;
        const age = (time - p.born) / p.life;
        context.globalAlpha = age < .6 ? 1 : Math.max(0, 1 - (age - .6) / .4);
        context.save();
        context.translate(p.x, p.y);
        context.rotate(p.turn);
        context.fillStyle = p.color;
        context.fillRect(-p.w / 2, -p.h / 2 * Math.abs(Math.cos(p.flip)), p.w, p.h * Math.abs(Math.cos(p.flip)) + .5);
        context.restore();
      }
      context.globalAlpha = 1;
      if (pieces.length) {
        frame(tick);
        return;
      }
      // Nothing left in the air: free the canvas memory until the next burst.
      ticking = false;
      canvas.width = 0;
      canvas.height = 0;
      canvas.hidden = true;
    }

    // The visible status dots do a short wave, from the top-left corner where the mascot sits.
    function wave() {
      if (!motion()) return;
      const width = win.innerWidth;
      const height = win.innerHeight;
      const dots = [...doc.querySelectorAll(DOTS)]
        .map(el => ({ el, box: el.getBoundingClientRect() }))
        .filter(({ box }) => box.width > 0 && box.bottom > 0 && box.right > 0 && box.top < height && box.left < width)
        .sort((a, b) => (a.box.left + a.box.top) - (b.box.left + b.box.top))
        .slice(0, 80);
      const gap = Math.min(40, 1400 / Math.max(1, dots.length));
      dots.forEach(({ el }, i) => play(el, WAVE, { duration: 460, delay: i * gap, easing: 'ease-out' }));
    }

    const centre = box => [box.left + box.width / 2, box.top + box.height / 2];

    function onClick(m) {
      const box = visible(m);
      if (combo(now())) {
        credits++;
        toast(`INSERT COIN · Credit ${credits}`);
        play(m.hop, HOP, { duration: 620, easing: 'cubic-bezier(.3, .7, .3, 1)' });
        if (box) burst(...centre(box), 70, 1.5);
        wave();
        return;
      }
      play(m.hop, HOP, { duration: 620, easing: 'cubic-bezier(.3, .7, .3, 1)' });
      if (box) burst(...centre(box), 16, 1);
    }

    function onKey(event) {
      if (event.ctrlKey || event.altKey || event.metaKey || event.repeat) return;
      if (/^(Shift|Control|Alt|Meta|CapsLock)$/.test(event.key || '')) return;
      if (isTypingTarget(event.target) || isTypingTarget(doc.activeElement)) {
        konami(null);
        return;
      }
      if (!konami(event.key || '')) return;
      for (const m of live()) {
        const box = visible(m);
        if (!box) continue;
        play(m.spin, SPIN, { duration: 900, easing: 'cubic-bezier(.5, .1, .3, 1)' });
        burst(...centre(box), 40, 1.3);
      }
      toast('↑↑↓↓←→←→ B A · 30 lives');
    }

    if (query) {
      const changed = event => {
        reduced = !!event.matches;
        applyMotion();
      };
      if (query.addEventListener) query.addEventListener('change', changed);
      else if (query.addListener) query.addListener(changed);
    }
    doc.addEventListener('pointermove', onPointer, { passive: true });
    if (doc.documentElement) doc.documentElement.addEventListener('mouseleave', lookAhead);
    win.addEventListener('blur', lookAhead);
    win.addEventListener('keydown', onKey, true);

    const api = {
      // Draws the robot into element (again: only the size changes). A click on element hops; the element itself keeps
      // its own focus behaviour (a button takes focus only when clicked).
      mount(element, { size } = {}) {
        if (!element) return;
        if (!doc.getElementById('aa-mascot-style')) {
          const style = doc.createElement('style');
          style.id = 'aa-mascot-style';
          style.textContent = CSS;
          doc.head.appendChild(style);
        }
        let m = byElement.get(element);
        if (!m) {
          m = { el: element, size: null };
          byElement.set(element, m);
          element.addEventListener('click', () => onClick(m));
          if (!element.getAttribute('aria-label')) element.setAttribute('aria-label', 'Agent Arcade mascot');
        }
        if (size) m.size = size;
        if (!m.svg || m.svg.parentNode !== element) {
          // First time, or the page replaced the element's content: draw it (again).
          element.innerHTML = markup(`aam${++copies}`, m.size);
          m.svg = element.querySelector('svg.aa-mascot');
          m.hop = m.svg.querySelector('.m-hop');
          m.spin = m.svg.querySelector('.m-spin');
          m.eyes = m.svg.querySelector('.m-eyes');
          m.look = m.svg.querySelector('.m-look');
        } else if (size) {
          m.svg.setAttribute('width', String(size));
          m.svg.setAttribute('height', String(size));
        }
        if (!mounted.includes(m)) mounted.push(m);
        m.svg.setAttribute('data-state', state);
        m.svg.classList.toggle('still', !motion());
        scheduleBlink();
      },

      setState(counts) {
        state = stateOf(counts && counts.working, counts && counts.needs);
        applyState();
      },

      setEnabled(on) {
        enabled = !!on;
        applyMotion();
      },

      onToast: null,
    };
    return api;
  }

  if (typeof window !== 'undefined' && window.document) window.Mascot = createMascot(window);
  if (typeof module !== 'undefined' && module.exports) {
    module.exports = { createMascot, stateOf, comboCounter, konamiCounter, isTypingTarget, KONAMI, art, markup, standaloneSvg, CSS };
  }
})();
