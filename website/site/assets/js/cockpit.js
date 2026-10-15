/* Agent Arcade cockpit mock, shared by the website hero and the promo film (website/video/promo.html).
 *
 * Everything is a pure function of time: render(state) draws exactly what a state object describes, and
 * heroState(t) describes the website's 12-second loop. Nothing here reads a clock, so the film can step it
 * frame by frame and every render of the same t looks the same.
 *
 * Terminal text is invented sample output. Agents show as neutral monograms, never vendor logos or colours.
 */
(function (global) {
  'use strict';

  var AGENTS = {
    cc: { mono: 'CC', name: 'Claude Code', sub: 'main', lines: [
      '<i class="u">&gt;</i> Narrow the foam band near the bridge',
      '<i class="g">●</i> Read(Assets/Shaders/WaterSurface.shader)',
      '<i class="m">  └ Read 212 lines</i>',
      '<i class="g">●</i> Update(Assets/Shaders/WaterSurface.shader)',
      '<i class="m">  └ 1 addition, 1 removal</i>',
      '<i class="g">●</i> Bash(dotnet test Water.Tests)',
      '<i class="m">  └ </i><i class="g">✓ 12 passed</i>'
    ], busy: function (s) { return '<i class="o">' + spin(s) + ' Brewing… (' + s + 's · esc to interrupt)</i>'; } },
    cx: { mono: 'CX', name: 'Codex', sub: 'main', lines: [
      '<i class="u">›</i> Add LOD groups to the pine prefabs',
      '<i class="b">•</i> Explored PineTree.prefab, LODBuilder.cs',
      '<i class="b">•</i> Edited LODBuilder.cs <i class="g">+24</i> <i class="r">−3</i>',
      '<i class="b">•</i> Ran dotnet build <i class="g">✓ 0 errors</i>',
      '<i class="b">•</i> Edited PineTree.prefab <i class="g">+8</i>',
      '<i class="m">  Plan 3/5 · Bake impostors</i>'
    ], busy: function (s) { return '<i class="o">◦ Working (' + s + 's • esc to interrupt)</i>'; } },
    gm: { mono: 'GM', name: 'Gemini CLI', sub: 'main', lines: [
      '<i class="u">&gt;</i> Write tests for the lap timer',
      '<i class="b">✦</i> I\'ll start with the split times.',
      '<i class="g"> ✔</i> ReadFile LapTimer.cs',
      '<i class="g"> ✔</i> WriteFile LapTimerTests.cs',
      '<i class="g"> ✔</i> Shell npm test <i class="m">(9 passed)</i>',
      '<i class="b">✦</i> Adding an edge case for pauses.'
    ], busy: function (s) { return '<i class="o">' + dots(s) + ' Running tests… (esc to cancel, ' + s + 's)</i>'; } },
    cp: { mono: 'CP', name: 'GitHub Copilot CLI', sub: 'main', lines: [
      '<i class="u">&gt;</i> Fix the failing CI job',
      '<i class="g">●</i> Read .github/workflows/build.yml',
      '<i class="g">●</i> Edit build.yml: cache node_modules',
      '<i class="g">●</i> Run npm test',
      '<i class="g">  ✓ 42 passing</i> <i class="m">(1.8s)</i>',
      '<i class="g">●</i> Done. The job passes locally.'
    ], busy: function (s) { return '<i class="o">● Thinking (' + s + 's · esc to cancel)</i>'; } },
    cu: { mono: 'CU', name: 'Cursor', sub: 'main', lines: [
      '<i class="u">→</i> Refactor the save system',
      '<i class="b">⬢</i> Read SaveGame.cs',
      '<i class="b">⬢</i> Edit SaveGame.cs <i class="g">+31</i> <i class="r">−12</i>',
      '<i class="b">⬢</i> Read SaveSlotView.cs'
    ], busy: function (s) { return '<i class="o">⬢ Generating… ' + s + 's</i>'; } }
  };

  var PROMPT = [
    '<i class="w">Do you want to make this edit to WaterSurface.shader?</i>',
    '<i class="r">› 1. Yes</i>',
    '  2. Yes, and don\'t ask again this session',
    '  3. No, and tell Claude what to do differently'
  ];
  var DONE_LINE = '<i class="g">● Done.</i> Your turn.';
  var APPLIED = '<i class="g">✓ Edit applied</i> <i class="m">· Plan 4/7</i>';
  var STATUS_TEXT = { working: 'working', needs: 'needs you', done: 'done', ended: 'ended' };

  function spin(s) { return '✻✶✳✢'.charAt(s % 4); }
  function dots(s) { return '⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏'.charAt(s % 10); }
  function clamp(v, a, b) { return v < a ? a : v > b ? b : v; }
  function ease(x) { x = clamp(x, 0, 1); return x < .5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2; }
  function easeOut(x) { x = clamp(x, 0, 1); return 1 - Math.pow(1 - x, 3); }
  function lerp(a, b, k) { return a + (b - a) * k; }
  function prog(t, a, b) { return clamp((t - a) / (b - a), 0, 1); }

  // The app's own rule (web/app.js): one pane fills the grid, two or four get two columns, any other count three.
  function layout(count, gap) {
    gap = gap == null ? 0.012 : gap;
    var cols = count <= 1 ? 1 : count === 2 || count === 4 ? 2 : 3;
    var rows = Math.max(1, Math.ceil(count / cols));
    var w = (1 - gap * (cols - 1)) / cols, h = (1 - gap * (rows - 1)) / rows, out = [];
    for (var i = 0; i < count; i++) {
      var c = i % cols, r = Math.floor(i / cols);
      out.push({ x: c * (w + gap), y: r * (h + gap), w: w, h: h });
    }
    return out;
  }
  function lerpRect(a, b, k) { return { x: lerp(a.x, b.x, k), y: lerp(a.y, b.y, k), w: lerp(a.w, b.w, k), h: lerp(a.h, b.h, k) }; }

  function el(tag, cls, html) { var e = document.createElement(tag); if (cls) e.className = cls; if (html != null) e.innerHTML = html; return e; }

  // A cockpit window: title bar, optional sidebar, and an area where panes are placed by fractional rects.
  function create(root, opts) {
    opts = opts || {};
    var win = el('div', 'ck');
    if (opts.sidebar === false) win.classList.add('ck-nosb');
    var bar = el('div', 'ck-bar', '<span class="ck-logo"></span><b>Agent Arcade</b><span class="ck-ws">HimalayanExpedition</span><span class="ck-win"><i></i><i></i><i></i></span>');
    var body = el('div', 'ck-body');
    var side = el('div', 'ck-side');
    var area = el('div', 'ck-area');
    body.appendChild(side); body.appendChild(area);
    win.appendChild(bar); win.appendChild(body);
    root.appendChild(win);
    if (opts.logo) bar.querySelector('.ck-logo').style.backgroundImage = 'url(' + opts.logo + ')';

    var panes = {}, sideRows = {};
    function pane(id) {
      if (panes[id]) return panes[id];
      var a = AGENTS[id];
      var p = el('div', 'ck-pane');
      p.innerHTML = '<div class="ck-ph"><i class="ck-dot"></i><span class="ck-mg">' + a.mono + '</span><span class="ck-tt">' + a.name + '</span><span class="ck-st"></span></div><pre class="ck-tm"></pre>';
      area.appendChild(p);
      var row = el('div', 'ck-row', '<i class="ck-dot"></i><span>' + a.name + '</span>');
      side.appendChild(row);
      panes[id] = { el: p, dot: p.querySelector('.ck-dot'), st: p.querySelector('.ck-st'), tm: p.querySelector('.ck-tm'), row: row, rdot: row.querySelector('.ck-dot'), html: '', status: '' };
      sideRows[id] = row;
      return panes[id];
    }
    side.appendChild(el('div', 'ck-sh', 'Agents'));

    // state.panes: [{ id, rect, opacity, status, shown (lines), secs, prompt, applied, drop, lift }]
    function render(state) {
      var seen = {};
      (state.panes || []).forEach(function (s, order) {
        var p = pane(s.id); seen[s.id] = true;
        var r = s.rect;
        p.el.style.left = (r.x * 100) + '%'; p.el.style.top = (r.y * 100) + '%';
        p.el.style.width = (r.w * 100) + '%'; p.el.style.height = (r.h * 100) + '%';
        p.el.style.opacity = s.opacity == null ? 1 : s.opacity;
        p.el.style.zIndex = s.lift ? 3 : 1;
        p.el.style.transform = s.lift ? 'translateY(-0.4em) scale(1.02)' : '';
        p.el.hidden = false; p.row.hidden = (s.opacity || 1) < 0.5;
        p.row.style.order = order;
        if (p.status !== s.status) {
          p.status = s.status;
          p.el.dataset.status = s.status; p.row.dataset.status = s.status;
          p.st.textContent = STATUS_TEXT[s.status] || '';
        }
        p.el.classList.toggle('ck-drop', !!s.drop);
        var a = AGENTS[s.id], shown = clamp(s.shown == null ? a.lines.length : s.shown, 0, a.lines.length);
        var lines = a.lines.slice(0, Math.floor(shown));
        if (s.applied) lines.push(APPLIED);
        if (s.status === 'needs' && s.prompt) lines = lines.concat(PROMPT);
        else if (s.status === 'working') lines.push(a.busy(Math.max(1, Math.floor(s.secs || 1))));
        else if (s.status === 'done') lines.push(DONE_LINE);
        var max = s.maxLines || 9;
        if (lines.length > max) lines = lines.slice(lines.length - max);
        var html = lines.join('\n');
        if (html !== p.html) { p.html = html; p.tm.innerHTML = '<div>' + html + '</div>'; }
      });
      Object.keys(panes).forEach(function (id) { if (!seen[id]) { panes[id].el.hidden = true; panes[id].row.hidden = true; } });
      win.dataset.needs = (state.panes || []).some(function (s) { return s.status === 'needs'; }) ? '1' : '';
    }
    return { el: win, render: render };
  }

  // The Android remote, drawn from the phone mockups: a chat that asks for an OK and a lock-screen style alert.
  function createPhone(root) {
    var ph = el('div', 'ckp');
    ph.innerHTML =
      '<div class="ckp-scr">' +
        '<div class="ckp-top"><span class="ckp-back">‹</span><i class="ck-dot"></i><span class="ckp-mg">CC</span><div class="ckp-tt"><b>Fix the foam near the bridge</b><span>Claude Code · main · HimalayanExpedition</span></div></div>' +
        '<div class="ckp-st"><i class="ck-dot"></i><span class="ckp-stt"></span><span class="ckp-plan">Plan 3/7</span></div>' +
        '<pre class="ckp-tm"><i class="u">&gt;</i> The foam is too wide near the bridge.\n\n<i class="g">●</i> Update(WaterSurface.shader)\n<i class="r">118 - foam = smoothstep(0, .6, d);</i>\n<i class="g">118 + foam = smoothstep(0, .25, d);</i></pre>' +
        '<div class="ckp-ask"><div class="ckp-q"><i class="ck-dot"></i><b>Claude Code needs your OK</b></div><span class="ckp-sub">Make this edit to WaterSurface.shader?</span>' +
          '<div class="ckp-opt" data-n="1"><b>1</b>Yes</div><div class="ckp-opt"><b>2</b>Yes, and don\'t ask again</div><div class="ckp-opt"><b>3</b>No, tell Claude what to do</div></div>' +
        '<div class="ckp-done"><i class="g">✓</i> Edit applied · <span class="ckp-g">Working</span></div>' +
        '<div class="ckp-keys"><span>Esc</span><span>Tab</span><span>↑</span><span>↓</span><span>Enter</span></div>' +
      '</div>' +
      '<div class="ckp-alert"><i class="ck-dot"></i><div><b>Needs your OK · Claude Code</b><span>Make this edit to WaterSurface.shader?</span></div></div>';
    root.appendChild(ph);
    var ask = ph.querySelector('.ckp-ask'), done = ph.querySelector('.ckp-done'), opt = ph.querySelector('[data-n="1"]'),
      stt = ph.querySelector('.ckp-stt'), plan = ph.querySelector('.ckp-plan'), alert = ph.querySelector('.ckp-alert');
    // state: { status: 'needs'|'working', ask (0..1 rise), pressed, applied, alert (0..1) }
    function render(s) {
      ph.dataset.status = s.status;
      stt.textContent = s.status === 'needs' ? 'Needs your OK · 2m 10s' : 'Working · 2m 14s';
      plan.textContent = s.applied ? 'Plan 4/7' : 'Plan 3/7';
      var k = easeOut(s.ask == null ? 1 : s.ask);
      ask.style.opacity = s.applied ? 0 : k;
      ask.style.transform = 'translateY(' + ((1 - k) * 1.4) + 'em)';
      opt.classList.toggle('on', !!s.pressed);
      done.style.opacity = s.applied ? 1 : 0;
      var a = easeOut(s.alert || 0);
      alert.style.opacity = a; alert.style.transform = 'translateY(' + ((a - 1) * 2) + 'em)';
    }
    return { el: ph, render: render };
  }

  // The website hero: a 12-second loop. Four agents stream; at 4.5 s Claude Code needs you; the phone asks; at 7.5 s
  // "1 Yes" is tapped; Claude Code carries on; at 9 s Cursor joins and the grid reflows to three columns; it leaves
  // again before the loop restarts.
  var HERO_LEN = 12;
  function heroState(t) {
    t = ((t % HERO_LEN) + HERO_LEN) % HERO_LEN;
    var four = layout(4), five = layout(5);
    var join = ease(prog(t, 9, 9.7)) * (1 - ease(prog(t, 11.2, 11.9)));
    function rect(i) { return lerpRect(four[i] || five[i], five[i], join); }
    var ccNeeds = t >= 4.5 && t < 8;
    var panes = [
      { id: 'cx', rect: rect(0), status: 'working', shown: 2 + t * 0.45, secs: 40 + t },
      { id: 'cc', rect: rect(1), status: ccNeeds ? 'needs' : 'working', prompt: true, applied: t >= 8, shown: Math.min(4, 1 + t * 0.7), secs: t < 4.5 ? 30 + t : 2 + (t - 8) },
      { id: 'gm', rect: rect(2), status: 'working', shown: 1 + t * 0.4, secs: 12 + t },
      { id: 'cp', rect: rect(3), status: t < 3 ? 'working' : 'done', shown: Math.min(6, 3 + t), secs: 8 + t }
    ];
    if (join > 0.001) {
      var r = five[4];
      panes.push({ id: 'cu', rect: { x: r.x, y: r.y + (1 - join) * 0.05, w: r.w, h: r.h }, opacity: join, status: 'working', shown: (t - 9) * 1.2, secs: 1 + (t - 9) });
    }
    var phone = {
      status: t < 8 ? 'needs' : 'working',
      ask: prog(t, 5, 5.24),
      alert: t < 5 ? prog(t, 4.6, 4.84) : 1 - prog(t, 5.6, 5.9),
      pressed: t >= 7.5,
      applied: t >= 8
    };
    var phoneShow = prog(t, 4.6, 4.9) * (1 - prog(t, 10.6, 11.2));
    return { t: t, panes: panes, phone: phone, phoneShow: phoneShow };
  }

  global.AACockpit = { AGENTS: AGENTS, create: create, createPhone: createPhone, layout: layout, lerpRect: lerpRect,
    heroState: heroState, HERO_LEN: HERO_LEN, ease: ease, easeOut: easeOut, prog: prog, lerp: lerp, clamp: clamp };
})(window);
