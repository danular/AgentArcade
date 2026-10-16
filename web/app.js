'use strict';

// The page talks to the C# side (MainForm.cs) through WebView2 messages.
const bridge = window.chrome && window.chrome.webview;
const send = message => bridge && bridge.postMessage(message);
const $ = selector => document.querySelector(selector);

const SAVE_PROMPT =
  "We're about to stop. Overwrite HANDOFF.md in the project root with a short handoff (max 40 lines): " +
  'what you changed this session, what works, what is broken or unfinished, and the exact next steps. ' +
  "Don't change any other files.";
const CONTINUE_PROMPT = 'Read HANDOFF.md, then continue with the next steps listed there. Ask me before large changes.';

const THEME = {
  background: '#0f0f12', foreground: '#e4e4e7', cursor: '#f4f4f5', cursorAccent: '#0f0f12',
  selectionBackground: 'rgba(224, 122, 95, 0.32)',
  black: '#27272a', red: '#f87171', green: '#4ade80', yellow: '#facc15',
  blue: '#60a5fa', magenta: '#c084fc', cyan: '#22d3ee', white: '#d4d4d8',
  brightBlack: '#71717a', brightRed: '#fca5a5', brightGreen: '#86efac', brightYellow: '#fde047',
  brightBlue: '#93c5fd', brightMagenta: '#d8b4fe', brightCyan: '#67e8f9', brightWhite: '#fafafa',
};

const BRAND_COLORS = { claude: '#d97757', codex: '#10a37f', gemini: '#4f8cff', agy: '#3fb6c6', copilot: '#a371f7', 'cursor-agent': '#7c8aa5' };
// Built-in agents show these letters (the same as on the website), never a vendor's logo. Other agents show the
// first two letters of their name.
const MONOGRAMS = { claude: 'CC', codex: 'CX', gemini: 'GM', agy: 'AG', copilot: 'CP', 'cursor-agent': 'CU' };

// Names agents put in the terminal title when it says nothing about the chat itself.
const GENERIC_TITLES = ['claude', 'claude code', 'codex', 'gemini', 'gemini cli', 'antigravity', 'antigravity cli', 'agy', 'copilot', 'github copilot', 'github copilot cli', 'cursor', 'cursor agent', 'cursor-agent', 'agent'];

const ICONS = {
  plus: '<path d="M12 5v14M5 12h14"/>',
  more: '<circle cx="5" cy="12" r="1.5"/><circle cx="12" cy="12" r="1.5"/><circle cx="19" cy="12" r="1.5"/>',
  expand: '<path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7"/>',
  shrink: '<path d="M4 14h6v6M20 10h-6V4M14 10l7-7M3 21l7-7"/>',
  close: '<path d="M18 6 6 18M6 6l12 12"/>',
  refresh: '<path d="M21 12a9 9 0 1 1-2.64-6.36L21 8"/><path d="M21 3v5h-5"/>',
  folder: '<path d="M3 7.5A2.5 2.5 0 0 1 5.5 5H9l2 2h7.5A2.5 2.5 0 0 1 21 9.5v7a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 16.5z"/>',
  'folder-plus': '<path d="M3 7.5A2.5 2.5 0 0 1 5.5 5H9l2 2h7.5A2.5 2.5 0 0 1 21 9.5v7a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 16.5z"/><path d="M12 10.5v5M9.5 13h5"/>',
  cube: '<path d="M12 2.5 3.5 7.25v9.5L12 21.5l8.5-4.75v-9.5z"/><path d="M3.5 7.25 12 12l8.5-4.75M12 12v9.5"/>',
  doc: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M9 13h6M9 17h4"/>',
  chevron: '<path d="m6 9 6 6 6-6"/>',
  terminal: '<path d="m5 17 5-5-5-5M12 19h7"/>',
  external: '<path d="M14 4h6v6M10 14 20 4M20 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1h5"/>',
  login: '<path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4M10 17l5-5-5-5M15 12H3"/>',
  download: '<path d="M12 3v12M7 10l5 5 5-5M5 21h14"/>',
  trash: '<path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/>',
  save: '<path d="M5 3h11l5 5v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"/><path d="M8 3v5h7M8 21v-7h8v7"/>',
  play: '<path d="M7 4v16l13-8z"/>',
  eraser: '<path d="m7 21-4-4L14 6l7 7-8 8zM9 12l6 6"/>',
  history: '<path d="M3 12a9 9 0 1 0 2.64-6.36L3 8"/><path d="M3 3v5h5M12 7v5l3 2"/>',
  pencil: '<path d="M4 20h4L19 9l-4-4L4 16z"/><path d="m13.5 6.5 4 4"/>',
  text: '<path d="M4 7V5h11v2M9.5 5v14M7 19h5M14 12v-1.5h7V12M17.5 10.5V19M16 19h3"/>',
  bell: '<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/>',
  image: '<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="9" cy="10" r="2"/><path d="m21 16-5-5-9 9"/>',
  sidebar: '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M9 4v16"/>',
  panel: '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M15 4v16"/>',
  home: '<rect x="3.5" y="3.5" width="7" height="7" rx="2"/><rect x="13.5" y="3.5" width="7" height="7" rx="2"/><rect x="3.5" y="13.5" width="7" height="7" rx="2"/><rect x="13.5" y="13.5" width="7" height="7" rx="2"/>',
  search: '<circle cx="10.5" cy="10.5" r="6.5"/><path d="m15.5 15.5 5 5"/>',
  phone: '<rect x="6.5" y="2.5" width="11" height="19" rx="2.5"/><path d="M10.5 18h3"/>',
  gear: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
  branch: '<circle cx="6" cy="5" r="2"/><circle cx="6" cy="19" r="2"/><circle cx="18" cy="7" r="2"/><path d="M6 7v10M18 9c0 5-6 4-11.2 8.6"/>',
};

const LOGO = '<img src="icon.png" alt="" draggable="false">';

// The website, the code and the changelog, for Settings → About.
const LINKS = {
  website: 'https://danular.github.io/AgentArcade/',
  github: 'https://github.com/danular/AgentArcade',
  changelog: 'https://danular.github.io/AgentArcade/changelog.html',
};

let state = { workspace: '', workspaces: [], agents: [], accounts: [] };
// What the main area shows: 'home' (every project, also the start screen) or 'project' (the panes of state.workspace).
// Only the PC changes it: a phone's actions never move the view.
let view = 'home';
let history = [];
const sessions = new Map();
// Pane ids start at a random number each run, so a phone screen or alert from before a restart can't reach another
// agent that got the same id. They stay "s" and at most 9 digits, as Remote.cs accepts.
let sessionCount = 100000 + crypto.getRandomValues(new Uint32Array(1))[0] % 799900000;
let maxed = null;
// Columns of panes in the grid now (layoutGrid); the side panel leaves each of them room (setPanelWidth).
let paneColumns = 1;
// The window's shape (web/layout.js, applyWindowShape): a portrait window stacks its panes and docks the side panel
// under them; a narrow one makes the sidebar a drawer, open or not.
let portraitMode = Layout.portrait(innerWidth, innerHeight);
let drawerMode = Layout.drawer(innerWidth);
let drawerOpen = false;
let openMenu = null;
let drag = null;
let historyView = null;
let pendingResume = null;

// ---------- small helpers ----------

function icon(name, size = 16) {
  const filled = name === 'more' || name === 'play';
  return `<svg class="ic" width="${size}" height="${size}" viewBox="0 0 24 24" fill="${filled ? 'currentColor' : 'none'}" ` +
    `stroke="${filled ? 'none' : 'currentColor'}" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[name]}</svg>`;
}

const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const same = (a, b) => (a || '').toLowerCase() === (b || '').toLowerCase();
const accountOf = key => state.accounts.find(account => account.key === key);
const agentOf = account => account && state.agents.find(agent => agent.name === account.agent);
const workspaceOf = path => state.workspaces.find(workspace => same(workspace.path, path));
const sessionsIn = path => [...sessions.values()].filter(session => same(session.workspace, path)).sort((a, b) => a.order - b.order);
const runningFor = key => [...sessions.values()].filter(session => session.key === key && session.running).length;
const shortVersion = version => (String(version).match(/\d+\.\d+(\.\d+)?/) || [version])[0];
const isChat = session => session.mode === 'start' || session.mode === 'resume';
const titleOf = session => isChat(session) ? session.customTitle || session.autoTitle || 'New chat' : session.label;
const chatOf = id => history.find(chat => chat.id === id);
const folderName = path => String(path || '').replace(/[\\/]+$/, '').split(/[\\/]/).pop() || path;

function when(iso) {
  const date = new Date(iso);
  const now = new Date();
  const time = date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const days = Math.round((new Date(now.toDateString()) - new Date(date.toDateString())) / 864e5);
  if (days === 0) return 'Today ' + time;
  if (days === 1) return 'Yesterday ' + time;
  return date.toLocaleDateString([], { day: 'numeric', month: 'short', year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric' }) + ' ' + time;
}

function duration(from, to) {
  const minutes = Math.round((new Date(to) - new Date(from)) / 60000);
  if (minutes < 1) return 'under a minute';
  if (minutes < 60) return minutes + ' min';
  return Math.floor(minutes / 60) + ' h' + (minutes % 60 ? ' ' + (minutes % 60) + ' min' : '');
}

function agentColor(agent) {
  const command = (agent?.command || '').toLowerCase();
  if (BRAND_COLORS[command]) return BRAND_COLORS[command];
  let hash = 0;
  for (const c of agent?.name || '?') hash = (hash * 31 + c.charCodeAt(0)) >>> 0;
  return `hsl(${hash % 360} 55% 58%)`;
}

function avatar(agent, size = '') {
  const command = (agent?.command || '').toLowerCase();
  const letters = Object.hasOwn(MONOGRAMS, command) ? MONOGRAMS[command]
    : (agent?.name || '?').replace(/[^A-Za-z0-9]/g, '').slice(0, 2) || '?';
  return `<span class="avatar ${size}" style="--c:${agentColor(agent)}">${esc(letters)}</span>`;
}

function accountLine(account) {
  if (!account) return '';
  if (account.checking) return 'checking…';
  if (account.signedIn === true) return account.email || 'signed in';
  if (account.signedIn === false) return 'not signed in' + (account.note ? ' · ' + account.note : '');
  return account.note || '';
}

function toast(text, kind = 'info') {
  const element = document.createElement('div');
  element.className = `toast ${kind}`;
  element.textContent = text;
  $('#toasts').append(element);
  setTimeout(() => {
    element.classList.add('out');
    setTimeout(() => element.remove(), 300);
  }, kind === 'error' ? 7000 : 4000);
}

// ---------- menus and dialogs ----------

function menu(anchor, items) {
  closeMenu();
  const element = document.createElement('div');
  element.className = 'menu';
  for (const item of items) {
    if (item === '-') {
      element.insertAdjacentHTML('beforeend', '<div class="menu-sep"></div>');
      continue;
    }
    if (item.title) {
      element.insertAdjacentHTML('beforeend', `<div class="menu-title">${esc(item.title)}</div>`);
      continue;
    }
    const button = document.createElement('button');
    button.className = 'menu-item' + (item.danger ? ' danger' : '');
    button.disabled = !!item.disabled;
    button.innerHTML = (item.avatar || (item.icon ? icon(item.icon, 15) : '')) +
      `<span class="menu-label"><span>${esc(item.label)}</span>${item.sub ? `<span class="menu-sub">${esc(item.sub)}</span>` : ''}</span>`;
    button.addEventListener('click', event => {
      event.stopPropagation();
      closeMenu();
      // A choice in a menu from the sidebar drawer closes the drawer too.
      if (anchor.closest?.('.sidebar')) setDrawer(false);
      item.run();
    });
    element.append(button);
  }
  document.body.append(element);
  const rect = anchor.getBoundingClientRect();
  const width = element.offsetWidth;
  const height = element.offsetHeight;
  const left = Math.max(8, Math.min(rect.right - width, innerWidth - width - 8));
  let top = rect.bottom + 6;
  if (top + height > innerHeight - 8) top = Math.max(8, rect.top - height - 6);
  element.style.left = left + 'px';
  element.style.top = top + 'px';
  openMenu = element;
}

function closeMenu() {
  openMenu?.remove();
  openMenu = null;
}

document.addEventListener('mousedown', event => {
  if (openMenu && !openMenu.contains(event.target)) closeMenu();
}, true);
document.addEventListener('keydown', event => {
  if (event.key === 'Escape') closeMenu();
});
// Outside the terminals and text fields the browser menu has nothing useful, only Reload and the like.
document.addEventListener('contextmenu', event => {
  if (!event.target.closest('.xterm, input, select, textarea')) event.preventDefault();
});

// Resolves with the dialog's field values (or {}) when confirmed, and null when cancelled. opened(root) runs once the
// dialog shows. submit(values) is for an answer that comes later: it returns a promise of a problem (the dialog stays
// open and shows it) or null (the dialog closes).
function modal({ title, body, confirm = 'OK', cancel = 'Cancel', danger = false, validate, opened, submit }) {
  closeMenu();
  return new Promise(resolve => {
    const root = document.createElement('div');
    root.className = 'modal-backdrop';
    root.innerHTML = `<div class="modal" role="dialog" aria-modal="true"><h2>${esc(title)}</h2>` +
      `<div class="modal-body">${body}<div class="modal-error" hidden></div></div>` +
      `<div class="modal-actions">${cancel ? `<button class="ghost-btn" data-answer="no">${esc(cancel)}</button>` : ''}` +
      `<button class="${danger ? 'danger-btn' : 'primary-btn'}" data-answer="yes">${esc(confirm)}</button></div></div>`;
    const values = () => Object.fromEntries([...root.querySelectorAll('[name]')].map(field => [field.name, field.value]));
    const finish = result => {
      document.removeEventListener('keydown', onKey, true);
      root.remove();
      resolve(result);
    };
    const showProblem = problem => {
      const error = root.querySelector('.modal-error');
      error.textContent = problem;
      error.hidden = false;
    };
    let waiting = false;
    const accept = () => {
      if (waiting) return;
      const result = values();
      const problem = validate && validate(result);
      if (problem) return showProblem(problem);
      if (!submit) return finish(result);
      waiting = true;
      const yes = root.querySelector('[data-answer="yes"]');
      yes.disabled = true;
      submit(result).then(later => {
        waiting = false;
        yes.disabled = false;
        if (later) showProblem(later);
        else finish(result);
      });
    };
    const onKey = event => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        finish(null);
      } else if (event.key === 'Enter') {
        // A focused button handles Enter itself, so Cancel stays Cancel.
        if (event.target.closest && event.target.closest('button')) return;
        event.preventDefault();
        event.stopPropagation();
        accept();
      }
    };
    document.addEventListener('keydown', onKey, true);
    root.addEventListener('mousedown', event => {
      if (event.target === root) finish(null);
    });
    root.querySelector('[data-answer="yes"]').addEventListener('click', accept);
    root.querySelector('[data-answer="no"]')?.addEventListener('click', () => finish(null));
    document.body.append(root);
    (root.querySelector('input, select') || root.querySelector('[data-answer="yes"]')).focus();
    opened?.(root);
  });
}

// ---------- rendering ----------

function render() {
  renderWorkspaces();
  renderTopbar();
  renderPanes();
  renderRecent();
  renderHome();
  renderAgentsTab();
  shellStatus(true);
}

// ---------- sidebar: projects ----------

// Each project with a status dot per running agent (the shapes of DOTS); a dot shows its pane when clicked.
function renderWorkspaces() {
  $('#workspaces').innerHTML = state.workspaces.map(workspace => {
    const active = view === 'project' && same(workspace.path, state.workspace);
    const running = sessionsIn(workspace.path).filter(session => session.running);
    return `<div class="ws-row${active ? ' active' : ''}">` +
      `<button class="ws-main" type="button" data-path="${esc(workspace.path)}" title="${esc(workspace.path)}"${active ? ' aria-current="page"' : ''}>` +
      `<span class="ws-icon">${icon(workspace.unity ? 'cube' : 'folder', 15)}</span>` +
      `<span class="ws-name ellipsis">${esc(workspace.name)}</span>` +
      (workspace.exists ? '' : '<span class="tag-warn">missing</span>') + '</button>' +
      dotsHtml(running) +
      `<button class="row-more" type="button" data-workspace-menu="${esc(workspace.path)}" title="More" aria-label="More for ${esc(workspace.name)}">${icon('more', 15)}</button></div>`;
  }).join('');
}

// At most this many dots per project in the sidebar; the rest is a number.
const MAX_DOTS = 6;

function dotsHtml(list) {
  if (!list.length) return '';
  const more = list.length - MAX_DOTS;
  return `<span class="dots">${list.slice(0, MAX_DOTS).map(sessionDot).join('')}` +
    (more > 0 ? `<span class="dots-more" title="${more} more">+${more}</span>` : '') + '</span>';
}

// A pane's status dot outside the pane; refreshDots keeps it current through data-dot.
function sessionDot(session) {
  const status = session.dotStatus || (session.running ? 'idle' : 'ended');
  const name = `${agentName(session)} · ${titleOf(session)}`;
  const [cls, label] = DOTS[status];
  return `<span class="${cls}" role="img" data-dot="${session.id}" data-name="${esc(name)}" title="${esc(name)}: ${label}" aria-label="${esc(name)}: ${label}"></span>`;
}

// ---------- top bar ----------

function renderTopbar() {
  const workspace = workspaceOf(state.workspace);
  const home = view === 'home';
  const name = $('#ws-name');
  name.textContent = home ? 'Home' : workspace ? workspace.name : 'No project';
  name.title = home ? '' : state.workspace || '';
  $('#project-menu').hidden = home;
  $('#nav-home').setAttribute('aria-current', home ? 'page' : 'false');
  renderGitChip();
  // The bar's buttons changed, so their room did too.
  fitTopbar();
}

function renderPanes() {
  const visible = sessionsIn(state.workspace);
  const home = view === 'home';
  if (maxed && !visible.some(session => session.id === maxed)) maxed = null;
  for (const session of sessions.values()) {
    session.el.hidden = !same(session.workspace, state.workspace) || (maxed && session.id !== maxed);
    session.el.style.order = session.order;
    session.el.classList.toggle('movable', !maxed && visible.length > 1);
    const maxButton = session.el.querySelector('[data-act="max"]');
    maxButton.innerHTML = icon(session.id === maxed ? 'shrink' : 'expand', 14);
    maxButton.title = session.id === maxed ? 'Restore' : 'Maximize';
    maxButton.setAttribute('aria-label', maxButton.title);
    updatePaneSub(session);
  }
  const count = maxed ? 1 : visible.length;
  const grid = $('#grid');
  // Home hides the grid without taking the terminals out of the page; they refit when it shows again.
  grid.hidden = home || count === 0;
  $('#empty').hidden = home || count !== 0;
  $('#home').hidden = !home;
  if (!home && count === 0) renderEmpty();
  layoutGrid(count);
  if (!home) for (const session of visible) scheduleFit(session);
}

// The grid's columns and rows for count panes (Layout.paneGrid). Landscape always gets the same grid; a portrait
// stage may fit another number of columns after the window or the docked side panel changes size, so those run it too.
let gridCount = 0;

function layoutGrid(count = gridCount) {
  gridCount = count;
  const stage = $('.stage');
  const { cols, rows } = Layout.paneGrid(count, stage.clientWidth, stage.clientHeight, portraitMode);
  const grid = $('#grid');
  grid.style.gridTemplateColumns = `repeat(${cols}, minmax(0, 1fr))`;
  grid.style.gridTemplateRows = `repeat(${rows}, minmax(${rows > 2 ? (portraitMode ? 160 : 260) : 0}px, 1fr))`;
  if (cols !== paneColumns) {
    paneColumns = cols;
    fitPanel();
  }
}

// A project without panes: its agents as cards, its recent chats and the handoff tip.
function renderEmpty() {
  const workspace = workspaceOf(state.workspace);
  const ready = state.accounts.filter(account => agentOf(account)?.installed);
  const cards = ready.map(account => {
    const agent = agentOf(account);
    return `<button class="card" data-account="${esc(account.key)}">${avatar(agent, 'lg')}` +
      `<span class="card-text"><span class="card-title">${esc(agent.name)}</span>` +
      `<span class="card-sub ellipsis">${esc(account.name)} · ${esc(accountLine(account))}</span></span></button>`;
  }).join('');
  const checking = state.agents.length === 0 || state.agents.some(agent => agent.checking);
  const recent = history.filter(chat => same(chat.workspace, state.workspace)).slice(0, 4);
  $('#empty').innerHTML =
    `<div class="hero"><div class="logo">${LOGO}</div><h1>What are we building today?</h1>` +
    `<p>Start an agent in <b>${esc(workspace ? workspace.name : 'your project')}</b>. Each one gets its own pane, and you can run several side by side.</p></div>` +
    `<div class="cards">${cards || `<p class="tip">${checking ? 'Looking for your agents…'
      : 'No agents installed yet. <button class="text-btn link" data-settings-tab="agents">Install one in Settings</button>.'}</p>`}</div>` +
    (recent.length
      ? `<div class="recent"><div class="recent-head"><span>Recent chats</span><button class="text-btn" data-history>See all</button></div>` +
        recent.map(chatRow).join('') + '</div>'
      : '') +
    '<p class="tip">Switching agents? Open <b>⋯</b> on a pane and paste the <b>save progress</b> prompt first, then paste <b>continue</b> in the next agent.</p>';
}

function updatePaneSub(session) {
  const account = accountOf(session.key);
  const sub = session.el.querySelector('.pane-sub');
  if (session.mode === 'install') sub.textContent = '';
  else if (session.mode === 'login') sub.textContent = account ? account.name : '';
  else {
    const agent = agentOf(account);
    sub.textContent = account ? `${agent ? agent.name + ' · ' : ''}${account.name} · ${accountLine(account)}` : '';
  }
}

// ---------- views ----------

// Opens a project's panes (from the sidebar, a Home card or a pane's own dot). A project that is already the
// selected one just shows again.
function openProject(path) {
  if (!same(path, state.workspace)) return selectWorkspace(path);
  if (view === 'project') return;
  view = 'project';
  render();
}

function showHome() {
  if (view === 'home') return;
  view = 'home';
  closeMenu();
  render();
}

// ---------- sidebar: Recent ----------

// The history in the sidebar, newest first across projects (also ones no longer in the sidebar). A click continues the
// chat exactly like History's Resume; an open chat shows its pane. The search looks in all of it.
const RECENT_PAGE = 60;
let recentQuery = '';
let recentLimit = RECENT_PAGE;

function renderRecent() {
  const box = $('#recent');
  const found = history.filter(chat => HomeData.matches(chat, recentQuery));
  if (!found.length) {
    box.innerHTML = `<p class="side-empty">${recentQuery ? 'No chats match.' : 'No chats yet. Start an agent and it shows up here.'}</p>`;
    return;
  }
  const shown = found.slice(0, recentLimit);
  box.innerHTML = HomeData.groupRecent(shown).map(group =>
    `<div class="recent-group" role="group" aria-label="${group.label}"><div class="recent-label" aria-hidden="true">${group.label}</div>` +
    group.chats.map(recentRow).join('') + '</div>').join('') +
    (found.length > shown.length
      ? `<button class="text-btn recent-more" type="button" data-recent-more>Show older chats (${found.length - shown.length})</button>`
      : '');
}

function recentRow(chat) {
  const agent = chatAgent(chat);
  const project = workspaceOf(chat.workspace)?.name || folderName(chat.workspace);
  const problem = chat.open ? '' : resumeProblem(chat);
  const title = chat.title || 'Untitled chat';
  const tip = chat.open ? `Show it in ${project}` : problem || `Resume in ${project}`;
  return `<div class="chat-row${problem ? ' cant' : ''}">` +
    `<button class="chat-main" type="button" data-recent="${esc(chat.id)}" title="${esc(`${title} · ${project}\n${tip}`)}">` +
    avatar(agent, 'sm') +
    `<span class="chat-title ellipsis${chat.title ? '' : ' placeholder'}">${esc(title)}</span>` +
    (chat.open ? '<span class="chat-when open">open</span>' : `<span class="chat-when">${esc(HomeData.shortTime(HomeData.activeOf(chat)))}</span>`) +
    '</button>' +
    `<button class="row-more" type="button" data-chat-menu="${esc(chat.id)}" title="More" aria-label="More for ${esc(title)}">${icon('more', 14)}</button></div>`;
}

function setRecentSearch(open) {
  const box = $('.recent-search');
  const input = $('#recent-search');
  box.hidden = !open;
  $('#search-recent').setAttribute('aria-expanded', String(open));
  if (open) return input.focus();
  input.value = '';
  if (recentQuery) {
    recentQuery = '';
    recentLimit = RECENT_PAGE;
    renderRecent();
  }
}

// ---------- Home ----------

// Every project as a card: what runs there and its status, a 10-day activity line from the history's chat times, its
// last chats, and one-click starts. Whatever needs you is on top. Live parts (dots, counts, the needs-you strip)
// change in place, so a status change doesn't redraw a card under the pointer.
function renderHome() {
  if (view !== 'home') return;
  const box = $('#home');
  const scroll = box.scrollTop;
  const recent = history.slice(0, 4);
  box.innerHTML = '<div class="home-inner">' +
    `<header class="hello"><h2>What are we building today?</h2><p id="home-counts">${esc(homeCounts(statusCounts()))}</p></header>` +
    `<div class="needs-list" id="home-needs">${needsHtml()}</div>` +
    `<div class="project-grid">${state.workspaces.map(projectCard).join('')}` +
    `<button class="project-card add" type="button" data-add-project aria-haspopup="menu">${icon('plus', 22)}<span>Add a folder or make a new project</span></button></div>` +
    (recent.length
      ? `<section class="pick-up" aria-labelledby="pick-up-title"><h3 id="pick-up-title">Pick up where you left off</h3><div class="pick-strip">${recent.map(pickUpCard).join('')}</div></section>`
      : '') +
    '</div>';
  box.scrollTop = scroll;
  for (const workspace of state.workspaces) askGit(workspace.path);
}

function homeCounts({ needs, working }) {
  const projects = state.workspaces.length;
  return `${projects} project${projects === 1 ? '' : 's'} · ${working} agent${working === 1 ? '' : 's'} working · ${needs} waiting for you`;
}

// One row per pane that needs you, with its question; Open shows the pane.
function needsHtml() {
  return [...sessions.values()].filter(session => session.dotStatus === 'needsYou').sort((a, b) => a.order - b.order).map(session => {
    let question = '';
    try {
      const screen = screenParts(session);
      question = screen.asking && screen.question ? screen.question : session.attentionMessage || '';
    } catch { /* the pane is being closed */ }
    const project = workspaceOf(session.workspace)?.name || folderName(session.workspace);
    return '<div class="needs-row">' +
      `<span class="dot needs" role="img" aria-label="Needs you" title="Needs you"></span>` +
      `<b class="ellipsis">${esc(agentName(session))} needs you</b>` +
      `<span class="needs-text ellipsis">in ${esc(project)}${question ? ` · "${esc(Remote.cut(question, 140))}"` : ''}</span>` +
      `<button class="ghost-btn small" type="button" data-show-session="${session.id}">Open</button></div>`;
  }).join('');
}

// The user's first three installed agents (their main account) start in a card's project with one click.
function quickStarts() {
  return state.agents.filter(agent => agent.installed).slice(0, 3)
    .map(agent => state.accounts.find(account => account.agent === agent.name && account.main)).filter(Boolean);
}

function projectCard(workspace) {
  const path = workspace.path;
  const running = sessionsIn(path).filter(session => session.running);
  const chats = history.filter(chat => same(chat.workspace, path));
  const agents = running.length
    ? running.slice(0, 8).map(session => `<span class="card-agent">${avatar(agentOf(accountOf(session.key)), 'sm')}${sessionDot(session)}</span>`).join('') +
      (running.length > 8 ? `<span class="muted">+${running.length - 8}</span>` : '')
    : '<span class="muted">No agents running</span>';
  // A project without chats says so once, in its chat list; its footer then has only the quick starts.
  const active = running.length ? 'Active now' : chats.length ? `Active ${HomeData.ago(HomeData.activeOf(chats[0]))}` : '';
  const starts = workspace.exists ? quickStarts().map(account => {
    const agent = agentOf(account);
    const tip = `Start ${agent.name} in ${workspace.name}`;
    return `<button class="quick-start" type="button" data-quick="${esc(account.key)}" data-path="${esc(path)}" title="${esc(tip)}" aria-label="${esc(tip)}">${avatar(agent, 'sm')}</button>`;
  }).join('') : '';
  const counts = HomeData.activity(history, path);
  return `<article class="project-card${same(path, state.workspace) ? ' current' : ''}">` +
    `<h3 class="card-head"><button class="card-open" type="button" data-open-project="${esc(path)}" title="Open ${esc(workspace.name)}">` +
    `<span class="ellipsis">${esc(workspace.name)}</span></button>` +
    (workspace.exists ? '' : '<span class="tag-warn">missing</span>') +
    `<span class="git-chip" data-git="${esc(path.toLowerCase())}"${gitInfo.get(path.toLowerCase()) ? '' : ' hidden'}>${gitChipInner(gitInfo.get(path.toLowerCase()))}</span></h3>` +
    `<div class="card-path ellipsis" title="${esc(path)}">${esc(HomeData.shortPath(path))}</div>` +
    `<div class="card-agents">${agents}</div>` +
    sparkline(counts) +
    `<div class="card-chats">${chats.slice(0, 2).map(chat => `<div class="ellipsis">${esc(chat.title || 'Untitled chat')}</div>`).join('') || '<div class="muted">No chats yet</div>'}</div>` +
    `<div class="card-foot">${active ? `<span class="muted ellipsis">${esc(active)}</span>` : ''}<span class="quick-starts">${starts}</span></div></article>`;
}

// No chats in those 10 days: a faint baseline keeps the card's shape without a flat orange line.
function sparkline(counts) {
  const total = counts.reduce((sum, count) => sum + count, 0);
  if (!total) {
    return '<svg class="spark" viewBox="0 0 100 26" preserveAspectRatio="none" role="img" aria-label="No chats started in the last 10 days">' +
      '<title>No chats started in the last 10 days</title><line class="spark-base" x1="0" y1="23" x2="100" y2="23" vector-effect="non-scaling-stroke"/></svg>';
  }
  const points = HomeData.sparkPoints(counts).map(point => point.join(',')).join(' ');
  return `<svg class="spark" viewBox="0 0 100 26" preserveAspectRatio="none" role="img" ` +
    `aria-label="${total} chat${total === 1 ? '' : 's'} started in the last 10 days"><title>Chats started per day, last 10 days</title>` +
    `<polygon class="spark-fill" points="0,26 ${points} 100,26"/><polyline class="spark-line" points="${points}" vector-effect="non-scaling-stroke"/></svg>`;
}

function pickUpCard(chat) {
  const agent = chatAgent(chat);
  const project = workspaceOf(chat.workspace)?.name || folderName(chat.workspace);
  const problem = chat.open ? '' : resumeProblem(chat);
  const title = chat.title || 'Untitled chat';
  return `<button class="pick-chat" type="button" data-recent="${esc(chat.id)}" title="${esc(chat.open ? `Show it in ${project}` : problem || `Resume in ${project}`)}">` +
    `${avatar(agent, 'sm')}<span class="pick-text"><span class="ellipsis">${esc(title)}</span>` +
    `<span class="pick-sub ellipsis">${esc(project)} · ${esc(chat.open ? 'open' : HomeData.shortTime(HomeData.activeOf(chat)))}</span></span></button>`;
}

// ---------- git branch and changes: the top bar's chip and the Home cards ----------

// The Changes tab's own git status (gitChanges), asked for one project at a time. These requests are numbered below
// zero, so the Changes tab never takes their answer for its own; its answers update the chip too. A project that isn't
// a repository, or whose git config wasn't trusted (the Changes tab asks), gets no chip.
const gitInfo = new Map(); // lower-case path -> { branch, changed } or null
const gitAsked = new Map(); // lower-case path -> when it was last asked
const gitQueue = [];
let gitSeq = 0;
let gitWaiting = null; // { key, at }
const GIT_AGAIN_MS = 30000;

function askGit(path, force = false) {
  if (!path) return;
  const key = path.toLowerCase();
  if (!force && Date.now() - (gitAsked.get(key) || 0) < GIT_AGAIN_MS) return;
  gitAsked.set(key, Date.now());
  if (!gitQueue.some(item => same(item, path))) gitQueue.push(path);
  pumpGit();
}

function pumpGit() {
  if (gitWaiting && Date.now() - gitWaiting.at < LOST_MS) return;
  gitWaiting = null;
  const path = gitQueue.shift();
  if (!path) return;
  gitWaiting = { key: path.toLowerCase(), at: Date.now() };
  send({ type: 'gitChanges', workspace: path, seq: -(++gitSeq) });
}

function gitArrived(message) {
  if (typeof message.workspace !== 'string' || !message.workspace) return;
  const key = message.workspace.toLowerCase();
  const files = Array.isArray(message.files) ? message.files : [];
  gitInfo.set(key, message.error || message.trust ? null
    : { branch: String(message.branch || ''), changed: Math.max(Number(message.total) || 0, files.length) });
  if (message.seq < 0 && gitWaiting?.key === key) {
    gitWaiting = null;
    pumpGit();
  }
  for (const chip of document.querySelectorAll(`.git-chip[data-git="${CSS.escape(key)}"]`)) {
    const info = gitInfo.get(key);
    chip.hidden = !info;
    chip.innerHTML = gitChipInner(info);
  }
  if (same(message.workspace, state.workspace)) renderGitChip();
}

function gitChipInner(info) {
  if (!info) return '';
  const changed = info.changed ? ` · ${info.changed} changed` : '';
  return `${icon('branch', 12)}<span class="ellipsis">${esc(info.branch || 'git')}</span>${changed ? `<span class="git-changed">${changed}</span>` : ''}`;
}

function renderGitChip() {
  const chip = $('#git-chip');
  const info = view === 'project' ? gitInfo.get(String(state.workspace).toLowerCase()) : null;
  chip.hidden = !info;
  if (!info) return;
  chip.innerHTML = gitChipInner(info);
  const label = `Branch ${info.branch || 'unknown'}, ${info.changed ? info.changed + ' changed file' + (info.changed === 1 ? '' : 's') : 'no changes'}. Show the changes`;
  chip.title = label;
  chip.setAttribute('aria-label', label);
}

// The selected project looks again every 30 seconds while it shows; the Changes tab's 5 s refresh updates it too.
setInterval(() => {
  if (view === 'project' && document.visibilityState === 'visible') askGit(state.workspace);
}, GIT_AGAIN_MS);

// ---------- status line, mascot ----------

// Panes that need you and panes that work, everywhere. The sidebar's footer, the mascot's antenna and Home follow it.
function statusCounts() {
  let needs = 0;
  let working = 0;
  for (const session of sessions.values()) {
    if (session.dotStatus === 'needsYou') needs++;
    else if (session.dotStatus === 'working') working++;
  }
  return { needs, working };
}

let shellKey = '';
let mascotKey = '';

function shellStatus(force = false) {
  const counts = statusCounts();
  const needing = [...sessions.values()].filter(session => session.dotStatus === 'needsYou').map(session => session.id).join(',');
  const key = `${counts.working}|${needing}`;
  if (key === shellKey && !force) return;
  const needsChanged = key.split('|')[1] !== shellKey.split('|')[1];
  shellKey = key;
  if (`${counts.working}|${counts.needs}` !== mascotKey) tellMascot();
  const parts = [];
  if (counts.needs) parts.push(`<span class="dot needs" aria-hidden="true"></span><span>${counts.needs} needs you</span>`);
  if (counts.working) parts.push(`<span class="dot working" aria-hidden="true"></span><span>${counts.working} working</span>`);
  $('#foot-state').innerHTML = parts.join('<span class="sep" aria-hidden="true">·</span>') || '<span>All quiet</span>';
  $('#foot-state').title = HomeData.statusLine(counts);
  if (view === 'home') {
    const line = $('#home-counts');
    if (line) line.textContent = homeCounts(counts);
    const strip = $('#home-needs');
    if (strip && (needsChanged || force)) strip.innerHTML = needsHtml();
  }
}

// The mascot's antenna shows the status line; it moves only while its animations are on (Settings → General).
function tellMascot() {
  const { working, needs } = statusCounts();
  mascotKey = `${working}|${needs}`;
  try {
    window.Mascot?.setState({ working, needs });
  } catch { /* the mascot is decoration; the status line says the same */ }
}

// ---------- chat titles ----------

function showTitle(session) {
  const label = session.el.querySelector('.pane-title');
  if (!label) return;
  label.textContent = titleOf(session);
  label.classList.toggle('placeholder', isChat(session) && !session.customTitle && !session.autoTitle);
  if (isChat(session)) label.title = titleOf(session) + ' (double-click to rename)';
  renderWorkspaces();
  remoteSoon();
}

// The page knows best which title is current; the history gets it a moment later.
function syncTitle(session) {
  if (!session.chat) return;
  clearTimeout(session.titleTimer);
  session.titleTimer = setTimeout(() => {
    send({ type: 'renameChat', chat: session.chat, title: session.customTitle || session.autoTitle, named: !!session.customTitle });
  }, 600);
}

function setCustomTitle(session, title) {
  session.customTitle = String(title || '').replace(/\s+/g, ' ').trim().slice(0, 120);
  showTitle(session);
  syncTitle(session);
}

function setAutoTitle(session, title) {
  if (session.autoTitle === title) return;
  session.autoTitle = title;
  if (session.customTitle) return;
  showTitle(session);
  syncTitle(session);
}

// Many agents name the chat in the terminal title (Claude Code does). Status words and program names are skipped.
function agentTitle(session, raw) {
  if (!isChat(session)) return;
  const title = String(raw || '').replace(/[\x00-\x1f]/g, '').replace(/^[^\p{L}\p{N}]+/u, '').replace(/\s+/g, ' ').trim().slice(0, 120);
  const lower = title.toLowerCase();
  const agent = agentOf(accountOf(session.key));
  const folder = folderName(session.workspace).toLowerCase();
  if (title.length < 3 || /[\\/]|\.(exe|cmd|bat)\b/i.test(title)) return;
  if (/^(ready|working|thinking|idle|waiting|loading)\b/i.test(title) || (folder && lower.includes(folder))) return;
  if (GENERIC_TITLES.includes(lower) || lower === (agent?.name || '').toLowerCase() || lower === (agent?.command || '').toLowerCase()) return;
  setAutoTitle(session, title);
}

// Keys and tokens, as TextGuard in Backend.cs knows them: a line with one never becomes a title.
const TOKEN_SHAPE = /sk-ant-[\w-]+|(?<![\w-])sk-(?:proj-)?[\w-]{20,}|AIza[\w-]{30,}|\bgh[pousr]_[A-Za-z0-9]{20,}|github_pat_\w{20,}|\bxox[abeprs]-[A-Za-z0-9-]{10,}|\bAKIA[0-9A-Z]{16}\b|eyJ[\w-]{5,}\.eyJ[\w-]{5,}\./;
const AUTO_TITLE_MAX = 60;

// A typed line that may name the chat: not a command or a handoff prompt, not one long word (a key typed or pasted
// where the agent doesn't mark pastes), and nothing shaped like a key.
const titleWorthy = line => line.length >= 3 && !line.startsWith('/') && line !== SAVE_PROMPT && line !== CONTINUE_PROMPT
  && !(line.length >= 20 && !/\s/.test(line)) && !TOKEN_SHAPE.test(line);

function autoTitleOf(line) {
  const chars = [...line];
  return chars.length > AUTO_TITLE_MAX ? chars.slice(0, AUTO_TITLE_MAX - 1).join('').trimEnd() + '…' : line;
}

// Until the agent names the chat, the first thing the user types becomes its title. Pasted text never counts (it may
// be a key pasted at an agent's key prompt): a paste in the pane, or bracketed paste. Text from a phone does count;
// the phone types through a paste.
function trackPrompt(session, data) {
  if (!isChat(session) || session.customTitle || session.autoTitle || session.pasting) return;
  // Shift+Enter: the next line of the same prompt.
  if (session.newline) {
    session.typed += ' ';
    return;
  }
  const text = data
    .replace(/\x1b\[200~([\s\S]*?)(?:\x1b\[201~|$)/g, (_, pasted) => session.phoneTyping ? pasted.replace(/[\r\n]+/g, ' ') : '')
    // Escape sequences: keys, focus and mouse reports (ESC [<35;12;9M; ESC [M and three bytes in the old encoding).
    .replace(/\x1b(\[M[\s\S]{3}|\[[0-?]*[ -/]*[@-~]|O.|.)/g, '');
  for (const c of text) {
    if (c === '\r' || c === '\n') {
      const line = session.typed.trim();
      session.typed = '';
      if (titleWorthy(line)) {
        setAutoTitle(session, autoTitleOf(line));
        return;
      }
    } else if (c === '\x7f' || c === '\b') {
      session.typed = session.typed.slice(0, -1);
    } else if (c === '\x15' || c === '\x03') {
      session.typed = '';
    } else if (c >= ' ') {
      session.typed += c;
    }
  }
  session.typed = session.typed.slice(-500);
}

function editTitle(session) {
  if (!isChat(session)) return;
  const label = session.el.querySelector('.pane-title');
  if (!label) return;
  const input = document.createElement('input');
  input.className = 'title-input';
  input.value = session.customTitle || session.autoTitle;
  input.placeholder = 'Name this chat';
  input.maxLength = 120;
  input.spellcheck = false;
  label.replaceWith(input);
  input.focus();
  input.select();
  let done = false;
  const finish = save => {
    if (done) return;
    done = true;
    input.replaceWith(label);
    if (save) setCustomTitle(session, input.value);
    session.term.focus();
  };
  input.addEventListener('keydown', event => {
    event.stopPropagation();
    if (event.key === 'Enter') finish(true);
    else if (event.key === 'Escape') finish(false);
  });
  input.addEventListener('blur', () => finish(true));
}

// ---------- moving panes ----------

// Dragging a pane by its header onto another pane swaps their places in the grid. Only the CSS
// order changes, so the terminals are never taken out of the page.
function swapPanes(a, b) {
  [a.order, b.order] = [b.order, a.order];
  renderPanes();
  renderWorkspaces();
  for (const session of [a, b]) {
    session.el.classList.remove('flash');
    void session.el.offsetWidth;
    session.el.classList.add('flash');
  }
}

function endDrag(drop) {
  if (!drag) return;
  const { source, target, ghost, active } = drag;
  drag = null;
  ghost?.remove();
  document.body.classList.remove('dragging');
  source.el.classList.remove('drag-source');
  target?.el.classList.remove('drop-target');
  if (drop && active && target && target !== source) swapPanes(source, target);
}

$('#grid').addEventListener('pointerdown', event => {
  if (event.button !== 0 || drag) return;
  const head = event.target.closest('.pane-head');
  if (!head || event.target.closest('button, input')) return;
  const source = sessions.get(head.closest('.pane').dataset.session);
  if (source) drag = { source, head, pointer: event.pointerId, x: event.clientX, y: event.clientY, active: false, target: null, ghost: null };
});

document.addEventListener('pointermove', event => {
  if (!drag || event.pointerId !== drag.pointer) return;
  if (!drag.active) {
    if (Math.hypot(event.clientX - drag.x, event.clientY - drag.y) < 6) return;
    if (maxed || sessionsIn(state.workspace).length < 2) return endDrag(false);
    drag.active = true;
    try { drag.head.setPointerCapture(drag.pointer); } catch { /* the pointer is already gone */ }
    drag.source.el.classList.add('drag-source');
    document.body.classList.add('dragging');
    drag.ghost = document.createElement('div');
    drag.ghost.className = 'drag-ghost';
    drag.ghost.textContent = titleOf(drag.source);
    document.body.append(drag.ghost);
  }
  drag.ghost.style.transform = `translate(${event.clientX + 14}px, ${event.clientY + 12}px)`;
  const pane = document.elementFromPoint(event.clientX, event.clientY)?.closest('.pane');
  const target = pane && pane !== drag.source.el ? sessions.get(pane.dataset.session) : null;
  if (target !== drag.target) {
    drag.target?.el.classList.remove('drop-target');
    target?.el.classList.add('drop-target');
    drag.target = target;
  }
});

document.addEventListener('pointerup', event => {
  if (drag && event.pointerId === drag.pointer) endDrag(true);
});
document.addEventListener('pointercancel', () => endDrag(false));

// ---------- terminal panes ----------

// chat: a history entry to continue; its account and project are used.
// options.workspace: the project to start in instead of the one on screen.
// options.remote: opened from the phone, so the PC keeps showing what it showed: no maximize reset, no focus, no dialogs.
function openSession(key, mode = 'start', chat = null, options = {}) {
  const account = accountOf(key);
  const agent = agentOf(account);
  if (!account || !agent) return options.remote ? null : toast('That account is gone. Use Check accounts again in Settings.', 'error');
  if (mode !== 'install' && !agent.installed) {
    if (options.remote) return null;
    if (agent.checking) return toast(`Still checking ${agent.name}. Try again in a moment.`);
    return askInstall(agent);
  }

  const id = 's' + (++sessionCount);
  const label = mode === 'install' ? `Install ${agent.name}` : mode === 'login' ? `${agent.name} · sign in` : agent.name;
  const session = {
    id, key, mode, label, workspace: chat ? chat.workspace : options.workspace || state.workspace, running: true, order: sessionCount,
    chat: null, resumeOf: chat?.id || '', customTitle: chat?.named ? chat.title : '', autoTitle: chat && !chat.named ? chat.title : '', typed: '',
    // For the phone (remoteSummary); times are epoch ms. workedAt: the last output after Enter that counts as work.
    startedAt: Date.now(), turnStartedAt: null, lastOutputAt: null, endedAt: null, exitCode: null, error: null,
    progress: null, attentionKind: null, attentionMessage: null, seq: 0, typedAt: 0, workedAt: 0, resizedAt: 0,
  };
  session.el = document.createElement('div');
  session.el.className = 'pane';
  session.el.dataset.session = id;
  session.el.innerHTML =
    `<div class="pane-head">${dotHtml('idle')}${avatar(agent, 'sm')}` +
    '<span class="pane-title ellipsis"></span><span class="pane-sub ellipsis"></span>' +
    '<span class="pane-actions">' +
    `<button class="icon-btn small" data-act="menu" title="More" aria-label="More">${icon('more', 15)}</button>` +
    `<button class="icon-btn small" data-act="max" title="Maximize" aria-label="Maximize">${icon('expand', 14)}</button>` +
    `<button class="icon-btn small" data-act="new" title="New pane with this account" aria-label="New pane with this account">${icon('plus', 15)}</button>` +
    `<button class="icon-btn small" data-act="close" title="Close" aria-label="Close">${icon('close', 15)}</button>` +
    '</span></div><div class="pane-body"><div class="term"></div></div>';
  $('#grid').append(session.el);
  sessions.set(id, session);

  const term = session.term = new Terminal({
    fontFamily: '"Cascadia Mono", "Cascadia Code", Consolas, monospace',
    fontSize,
    cursorBlink: true,
    allowProposedApi: true,
    scrollback: 5000,
    theme: THEME,
    // Links that agents print as OSC 8 hyperlinks: localhost pages open in the side panel, the rest in the browser.
    linkHandler: { activate: (event, uri) => openLink(uri) },
  });
  session.fit = new FitAddon.FitAddon();
  term.loadAddon(session.fit);
  try {
    term.loadAddon(new Unicode11Addon.Unicode11Addon());
    term.unicode.activeVersion = '11';
  } catch { /* default widths are fine too */ }
  term.loadAddon(new WebLinksAddon.WebLinksAddon((event, uri) => openLink(uri)));
  term.registerLinkProvider({ provideLinks: (y, callback) => callback(imageLinks(session, y)) });
  term.open(session.el.querySelector('.term'));
  try {
    const webgl = new WebglAddon.WebglAddon();
    webgl.onContextLoss(() => webgl.dispose());
    term.loadAddon(webgl);
  } catch { /* the standard renderer works without a GPU */ }
  term.attachCustomKeyEventHandler(event => handleKey(session, event));
  // A paste in the pane (Ctrl+V, right-click) reaches term.onData during this event. It isn't typed, so it names nothing.
  session.el.addEventListener('paste', () => {
    session.pasting = true;
    setTimeout(() => { session.pasting = false; });
  }, true);
  term.onData(data => {
    if (!session.running) return;
    send({ type: 'input', session: id, data });
    trackPrompt(session, data);
    watchTurn(session, data);
  });
  term.onTitleChange(title => agentTitle(session, title));
  term.onWriteParsed(() => screenChanged(session));
  term.onResize(() => {
    session.resizedAt = Date.now();
    screenChanged(session);
  });
  // Agents that announce themselves: OSC 9 (Codex, Claude Code's iTerm2 channel), OSC 777 and the bell.
  // OSC 9;4 and the like are progress reports, not messages; 9;4 is kept for the phone.
  term.parser.registerOscHandler(9, data => {
    const progress = Remote.progress(data);
    if (progress !== undefined) session.progress = progress;
    if (/^\d+;/.test(data)) return false;
    needsYou(session, data);
    return true;
  });
  term.parser.registerOscHandler(777, data => {
    const [kind, title, body] = data.split(';');
    if (kind !== 'notify') return false;
    needsYou(session, body || title);
    return true;
  });
  term.onBell(() => {
    if (session.armed) needsYou(session);
  });
  session.el.addEventListener('focusin', () => clearAttention(session));
  new ResizeObserver(() => scheduleFit(session)).observe(session.el.querySelector('.pane-body'));

  // Started at the PC: its project shows. A phone's start leaves the PC's view, dialogs and focus as they are.
  if (!options.remote) {
    maxed = null;
    view = 'project';
    closeSettings();
  }
  showTitle(session);
  render();
  // Resolves once the C# side was told to start the agent; typing from the phone waits for it.
  session.begun = new Promise(resolve => {
    let begun = false;
    const begin = () => {
      if (begun) return;
      begun = true;
      // Stopped or closed before it began (the phone can be that quick): nothing to start.
      if (!session.running) return resolve();
      // A pane the PC doesn't show can't be measured; the agent gets a usable size until someone looks at it.
      if (!fitNow(session)) term.resize(120, 36);
      send({ type: 'start', session: id, account: key, mode, workspace: session.workspace, chat: session.resumeOf, cols: term.cols, rows: term.rows });
      term.onResize(size => send({ type: 'resize', session: id, cols: size.cols, rows: size.rows }));
      if (!options.remote) term.focus();
      remoteSoon();
      resolve();
    };
    requestAnimationFrame(begin);
    // Animation frames wait while the window is minimized, and nobody may be at the PC to restore it.
    setTimeout(begin, 250);
  });
  return session;
}

// Shift+Enter adds a line to the prompt instead of sending it. Through ConPTY a line feed reaches Claude Code, Copilot
// and Gemini as a new line, but Codex (which reads the console's key events) only takes Alt+Enter, ESC CR.
const NEWLINE = { codex: '\x1b\r' };
const newlineFor = session =>
  NEWLINE[String(agentOf(accountOf(session.key))?.command || '').split(/[\\/]/).pop().replace(/\.\w+$/, '').toLowerCase()] || '\n';

function handleKey(session, event) {
  // Agent chats only: the console's PowerShell and the sign-in and install panes keep Enter as it is.
  if (event.key === 'Enter' && event.shiftKey && !event.ctrlKey && !event.altKey && !event.metaKey && !event.isComposing &&
      session.id && isChat(session)) {
    // The keypress after this keydown would still send Enter (\r), so it is stopped too.
    if (event.type === 'keydown') {
      event.preventDefault();
      // watchTurn and trackPrompt count it as typing, not as a submit.
      session.newline = true;
      try { session.term.input(newlineFor(session)); } finally { session.newline = false; }
    }
    return false;
  }
  if (event.type !== 'keydown') return true;
  const key = event.key.toLowerCase();
  // Ctrl+C copies when text is selected; otherwise it goes to the agent as usual.
  if (event.ctrlKey && key === 'c' && (event.shiftKey || session.term.hasSelection())) {
    const text = session.term.getSelection();
    if (text) send({ type: 'copy', text });
    session.term.clearSelection();
    return false;
  }
  // Let the browser paste, so Ctrl+V pastes text instead of sending a control key.
  if (event.ctrlKey && key === 'v') return false;
  // Ctrl+, opens Settings from a pane too: the page's own key handler gets it. (Ctrl+N stays the agent's own key.)
  if (event.ctrlKey && !event.altKey && !event.shiftKey && key === ',') return false;
  // Ctrl+= / Ctrl+- / Ctrl+0 change the text size of every pane, like zoom in a browser.
  if (event.ctrlKey && !event.altKey && (key === '=' || key === '+' || (key === '-' && !event.shiftKey) || key === '0')) {
    setFontSize(key === '0' ? DEFAULT_FONT_SIZE : fontSize + (key === '-' ? -1 : 1));
    return false;
  }
  return true;
}

// ---------- text size ----------

const DEFAULT_FONT_SIZE = 13;
const clampFontSize = size => Math.min(24, Math.max(9, Math.round(Number(size)) || DEFAULT_FONT_SIZE));
let fontSize = clampFontSize(localStorage.getItem('fontSize'));

// Every pane uses the same size; refitting tells each agent its new number of rows and columns.
function setFontSize(size, save = true) {
  fontSize = clampFontSize(size);
  if (save) localStorage.setItem('fontSize', String(fontSize));
  for (const session of sessions.values()) {
    session.term.options.fontSize = fontSize;
    scheduleFit(session);
  }
  if (consoleTerm) {
    consoleTerm.options.fontSize = fontSize;
    fitConsole();
  }
}

// Settings → General shows the size; these four are its quick choices.
const FONT_SIZES = [['S', 11], ['M', 13], ['L', 15], ['XL', 18]];

// ---------- version and update notice ----------

// appInfo from the C# side: version, releases (the releases page), updateCheck (on or off), update ({ version, url }
// of a newer release, or null) and mascotAnimations. The C# side asks GitHub at most once a day; the page only shows
// what it found.
let appInfo = null;

function appInfoArrived(message) {
  appInfo = message;
  renderUpdateNote();
  try {
    window.Mascot?.setEnabled(message.mascotAnimations !== false);
  } catch { /* the mascot is decoration */ }
  if (settingsView?.tab === 'general' || settingsView?.tab === 'about') renderSettingsTab(true);
}

// Hidden for a version you closed it for; the next version shows again.
function renderUpdateNote() {
  const note = $('#update-note');
  const update = appInfo?.update;
  note.hidden = !update || localStorage.getItem('updateDismissed') === update.version;
  note.innerHTML = note.hidden ? '' :
    `<button class="update-link" data-update-open title="Open the release page">${icon('download', 14)}` +
    `<span class="ellipsis">Agent Arcade ${esc(update.version)} is available</span></button>` +
    `<button class="icon-btn small" data-update-close title="Hide until the next version" aria-label="Hide until the next version">${icon('close', 13)}</button>`;
}

function scheduleFit(session) {
  if (session.fitFrame) return;
  session.fitFrame = requestAnimationFrame(() => {
    session.fitFrame = 0;
    fitNow(session);
  });
}

// Returns whether the pane could be measured.
function fitNow(session) {
  if (!session.el.isConnected || session.el.hidden) return false;
  const body = session.el.querySelector('.pane-body');
  if (body.clientWidth < 60 || body.clientHeight < 40) return false;
  try {
    session.fit.fit();
    return true;
  } catch {
    return false; // the pane is being removed
  }
}

function sessionEnded(message) {
  const session = sessions.get(message.session);
  if (!session || !session.running) return;
  endSession(session, { code: message.code, error: message.error });
}

// code and error come from the agent's exit; phone: the phone stopped it (the C# side already did).
function endSession(session, { code = null, error = null, phone = false } = {}) {
  session.running = false;
  session.endedAt = Date.now();
  session.exitCode = typeof code === 'number' ? code : null;
  session.error = error || null;
  session.stoppedFromPhone = phone;
  session.progress = null;
  session.term.write(error
    ? `\r\n\x1b[31m${error}\x1b[0m\r\n`
    : phone
      ? '\r\n\x1b[90m— stopped from your phone —\x1b[0m\r\n'
      : `\r\n\x1b[90m— session ended${code ? ` (exit code ${code})` : ''} —\x1b[0m\r\n`);
  session.attention = false;
  session.attentionKind = null;
  session.attentionMessage = null;
  session.armed = 0;
  refreshDots();
  if (isChat(session)) notify(session, error ? `${agentName(session)} stopped with an error.` : endMessage(session));
  const resumable = chatOf(session.chat)?.resume;
  session.el.querySelector('.pane-body').insertAdjacentHTML('beforeend',
    '<div class="pane-ended">Session ended' +
    (resumable ? '<button data-act="resume" title="Continue this chat">Resume</button>' : '') +
    (isChat(session) ? '<button data-act="fresh">New chat</button>' : '<button data-act="restart">Restart</button>') +
    '<button data-act="close">Close</button></div>');
  if (session.mode === 'login' && !error && !phone) toast('Sign-in finished. Settings → Agents and accounts shows the email in a moment.');
  renderWorkspaces();
  renderHome();
  renderAgentsTab();
  remoteAttention(session, 'ended', endMessage(session));
  remoteSoon();
}

const endMessage = session => session.error ||
  (session.stoppedFromPhone ? `${agentName(session)} was stopped from your phone.` : `${agentName(session)} stopped.`);

async function closeSession(session, force = false) {
  if (session.running && !force) {
    const ok = await modal({
      title: `Stop ${titleOf(session)}?`,
      body: '<p>The agent in this pane will be closed. Work it has already saved to your project stays.</p>',
      confirm: 'Stop agent',
      danger: true,
    });
    if (!ok) return;
  }
  if (session.running) send({ type: 'close', session: session.id });
  session.running = false;
  session.el.remove();
  sessions.delete(session.id);
  paneClosed(session.id);
  try {
    session.term.dispose();
  } catch { /* the pane is already gone from the screen */ }
  if (maxed === session.id) maxed = null;
  render();
}

// Only for the PC's own clicks: it shows the project's panes.
function focusSession(id) {
  const session = sessions.get(id);
  if (!session) return;
  if (maxed && maxed !== id) maxed = null;
  if (view !== 'project') {
    view = 'project';
    render();
  } else {
    renderPanes();
  }
  session.term.focus();
  session.el.classList.remove('flash');
  void session.el.offsetWidth;
  session.el.classList.add('flash');
}

function pastePrompt(session, text) {
  if (!session.running) return toast('This session has ended. Restart it first.');
  session.term.paste(text);
  session.term.focus();
  toast('Prompt pasted. Press Enter in the pane to send it.');
}

// ---------- dropping files and images on a pane ----------

// Dropped files are pasted as paths; Claude Code and Codex attach pasted image paths, Gemini reads @paths.
let fileTarget = null;

function setFileTarget(session) {
  if (session === fileTarget) return;
  fileTarget?.el.classList.remove('drop-target');
  session?.el.classList.add('drop-target');
  fileTarget = session;
}

function paneAt(event) {
  const pane = event.target.closest?.('.pane');
  const session = pane && sessions.get(pane.dataset.session);
  return session && session.running ? session : null;
}

function pastePaths(session, paths) {
  if (!session?.running || !paths.length) return;
  const gemini = (agentOf(accountOf(session.key))?.command || '').toLowerCase() === 'gemini';
  paths.forEach((path, i) => {
    if (i) session.term.paste(' ');
    session.term.paste(gemini ? '@' + path.replace(/\\/g, '/').replace(/ /g, '\\ ') : /\s/.test(path) ? `"${path}"` : path);
  });
  session.term.paste(' ');
  session.term.focus();
}

function saveImage(session, file) {
  if (file.size > 30 * 1024 * 1024) return toast(`${file.name || 'That image'} is larger than 30 MB.`, 'error');
  const reader = new FileReader();
  reader.onload = () => send({ type: 'saveImage', session: session.id, name: file.name || 'image.png', data: String(reader.result).split(',')[1] || '' });
  reader.readAsDataURL(file);
}

// The C# side found paths for the files that exist on disk; images without one (from a browser) are saved first.
function filesDropped(message) {
  const session = sessions.get(message.session);
  if (!session) return;
  const files = session.droppedFiles || [];
  session.droppedFiles = null;
  pastePaths(session, message.paths || []);
  const names = (message.paths || []).map(path => path.split(/[\\/]/).pop().toLowerCase());
  for (const file of files) {
    if (names.includes((file.name || '').toLowerCase())) continue;
    if (file.type.startsWith('image/')) saveImage(session, file);
    else toast(`${file.name || 'That item'} has no file on disk to point the agent at.`, 'error');
  }
}

document.addEventListener('dragover', event => {
  // Never let WebView2 open a dropped file in place of the app, which would stop every agent.
  event.preventDefault();
  const session = drag ? null : paneAt(event);
  event.dataTransfer.dropEffect = session ? 'copy' : 'none';
  setFileTarget(session);
});

document.addEventListener('dragleave', event => {
  if (!event.relatedTarget) setFileTarget(null);
});

document.addEventListener('drop', event => {
  event.preventDefault();
  setFileTarget(null);
  const session = paneAt(event);
  if (!session) return;
  const files = [...event.dataTransfer.files];
  if (files.length) {
    if (bridge?.postMessageWithAdditionalObjects) {
      session.droppedFiles = files;
      bridge.postMessageWithAdditionalObjects({ type: 'dropFiles', session: session.id }, event.dataTransfer.files);
    } else {
      files.filter(file => file.type.startsWith('image/')).forEach(file => saveImage(session, file));
    }
    return;
  }
  const imagePath = event.dataTransfer.getData(IMAGE_DRAG);
  if (imagePath) return pastePaths(session, [imagePath]);
  const text = event.dataTransfer.getData('text/plain');
  if (text) {
    session.term.paste(text);
    session.term.focus();
  }
});

// ---------- needing attention ----------

// An agent that worked for a while after you pressed Enter and then went quiet is done or waiting for
// you: agents animate a spinner while they work. Agents that send their own notification are believed
// straight away. Windows notifications only show when you can't see the pane; MainForm checks the window.
const QUIET_MS = 5000;
const MIN_WORK_MS = 4000;
// A resize makes ConPTY repaint the pane; output this soon after one isn't the agent writing.
const REPAINT_MS = 600;
// The same check as APPROVAL in remote.js; keep both identical. "Yes, and run in sandbox" / "... without sandbox
// restrictions" is the option Antigravity CLI's command approval always offers (antigravity.google/docs/cli/features).
// Cursor's approvals end in a Skip or Reject option with "(esc or n)", except "Delete this file?" (Keep is "(n)").
const APPROVAL = /do you want|would you like to|allow (this|command|execution)|apply this change|yes, and run (in|without) sandbox|\(esc or n\b|delete this file\?|\(y\/n\)|\[y\/n\]/i;
// Focus and mouse reports that xterm.js sends for the agent; nobody typed them.
const REPORTS = /^(?:\x1b\[[IO]|\x1b\[<\d+;\d+;\d+[Mm]|\x1b\[M[\s\S]{3})+$/;
let notifyOn = localStorage.getItem('notifications') !== 'off';

// The pane menu and Settings → General switch it, for every pane.
function setNotify(on) {
  notifyOn = on;
  localStorage.setItem('notifications', on ? 'on' : 'off');
}

const agentName = session => agentOf(accountOf(session.key))?.name || 'The agent';

// typedAt: when you started typing after the last Enter; the echo of a draft isn't the agent working.
function watchTurn(session, data) {
  clearAttention(session);
  if (REPORTS.test(data)) return;
  // Keys went to the dialog on screen, so once it leaves the screen it was answered, and after a key that can answer
  // it the same question may come back at once (Remote.dialogKeys); a phone's expect may not reach the dialog that
  // follows (Remote.dialogInput).
  if (session.dialog) Remote.dialogKeys(session.dialog, data, screenText(session), Date.now());
  Remote.dialogInput(session.asked, session.seq);
  // Shift+Enter adds a line to the prompt (newlineFor): typing, also where it is ESC CR.
  if (session.newline) {
    session.typedAt ||= Date.now();
  } else if (data.includes('\r')) {
    session.armed = Date.now();
    session.turnStartedAt = session.armed;
    session.busyStart = 0;
    session.typedAt = 0;
  } else if (picking(session)) {
    // Answering a dialog with a digit, y or an arrow lets the agent go on, so its next stop is noticed again.
    // A turn that is still armed keeps the work it already did.
    if (!session.armed) {
      session.armed = Date.now();
      session.busyStart = 0;
    }
    session.typedAt = 0;
  } else {
    session.typedAt ||= Date.now();
  }
}

function outputArrived(session) {
  const now = Date.now();
  if (now - (session.resizedAt || 0) >= REPAINT_MS) {
    session.lastOutputAt = now;
    if (session.turnStartedAt && !session.typedAt) session.workedAt = now;
  }
  if (!session.armed) return;
  session.lastOutput = now;
  session.busyStart ||= session.lastOutput;
}

setInterval(() => {
  const now = Date.now();
  for (const session of sessions.values()) {
    // A dialog needs you as soon as it shows, however soon after Enter: once per dialog, and again when it is answered
    // and the same question comes back. After a resize the same dialog can wrap differently, which isn't a new one.
    if (session.running && Remote.dialogAppeared(session.dialog ||= {}, screenParts(session), now,
      now - (session.resizedAt || 0) < 2000, screenText(session))) {
      needsYou(session, '', true);
    }
    if (session.running && session.armed && session.busyStart && now - session.lastOutput > QUIET_MS &&
        session.lastOutput - session.busyStart >= MIN_WORK_MS) needsYou(session);
  }
  refreshDots();
}, 1000);

// ---------- status dots ----------

// Each status has its own shape, not only its own colour, so red and green needn't be told apart: working is a
// pulsing orange dot, needs you a red diamond, done (not looked at yet) a green ring, idle a green dot, ended a grey
// dot. The words are the tooltip and what a screen reader says. The phone uses the same statuses (paneStatus).
const DOTS = {
  working: ['dot working', 'Working'],
  needsYou: ['dot needs', 'Needs you'],
  done: ['dot done', 'Done'],
  idle: ['dot on', 'Idle'],
  ended: ['dot off', 'Ended'],
};

function dotHtml(status) {
  const [cls, label] = DOTS[status];
  return `<span class="${cls}" role="img" title="${label}" aria-label="${label}"></span>`;
}

// A dot outside the pane (sessionDot) says whose it is: data-name goes before the status.
function setDot(dot, status) {
  const [cls, label] = DOTS[status];
  const text = dot.dataset.name ? `${dot.dataset.name}: ${label}` : label;
  dot.className = cls;
  dot.title = text;
  dot.setAttribute('aria-label', text);
}

// Sets each pane's dot, and its dots in the sidebar and on Home, from paneStatus; then the status line and the mascot.
// The dots change in place: redrawing the sidebar every time a status changes could swallow a click that is halfway
// through.
function refreshDots() {
  for (const session of sessions.values()) {
    const screen = session.running ? screenParts(session) : null;
    const status = screen ? paneStatus(session, screen) : 'ended';
    noteDialog(session, screen, status);
    if (status === session.dotStatus) continue;
    session.dotStatus = status;
    setDot(session.el.querySelector('.pane-head .dot'), status);
    for (const listed of document.querySelectorAll(`[data-dot="${CSS.escape(session.id)}"]`)) setDot(listed, status);
  }
  shellStatus();
}

// claimed: the 1 s check already counted this dialog as new (Remote.dialogAppeared).
function needsYou(session, text = '', claimed = false) {
  session.armed = 0;
  session.busyStart = 0;
  const own = String(text).replace(/[\x00-\x1f]/g, ' ').trim();
  const screen = screenParts(session);
  // A dialog on screen makes any alert about it a needsYou.
  const asks = screen.asking || (own ? Remote.asksOk(own) : screen.asks || screen.choices.length > 0 || screen.menu);
  const kind = asks ? 'needsYou' : 'done';
  // needsYou goes once per dialog (its question and choices); done at most once per 8 s.
  if (kind === 'needsYou') {
    if (!claimed && !Remote.claimDialog(session.dialog ||= {}, screen, Date.now())) return;
  } else {
    if (Date.now() - (session.notifiedAt || 0) < 8000) return;
    session.notifiedAt = Date.now();
  }
  const message = Remote.cut(own, 200) ||
    (asks ? `${agentName(session)} needs your OK to continue.` : `${agentName(session)} is done and waiting for you.`);
  if (!seenOnPc(session)) {
    session.attention = true;
    session.attentionKind = kind;
    session.attentionMessage = message;
    refreshDots();
    remoteSoon();
  }
  // A menu without approval words still needs you, so the toast says so too.
  notify(session, own.slice(0, 200) || (kind === 'needsYou' ? message : attentionText(session)));
  remoteAttention(session, kind, message, asks ? screen.question : null);
}

// Whether someone is at the PC, from the C# side (presence: every 5 s and on lock or unlock) and this page's own
// keyboard and mouse events, which are newer than the last report. at: when the report came.
const AWAY_MS = 45000;
let presence = { idleMs: 0, locked: false, at: Date.now() };
let pageInputAt = 0;
for (const type of ['keydown', 'pointerdown', 'pointermove', 'wheel']) {
  document.addEventListener(type, () => { pageInputAt = Date.now(); }, { capture: true, passive: true });
}

function presenceChanged(message) {
  const idleMs = Number(message.idleMs);
  presence = { idleMs: Number.isFinite(idleMs) && idleMs >= 0 ? idleMs : 0, locked: message.locked === true, at: Date.now() };
}

// Nobody touched the PC's keyboard or mouse for 45 s, or Windows is locked.
const awayFromPc = () => presence.locked || Math.min(presence.idleMs + Date.now() - presence.at, Date.now() - pageInputAt) >= AWAY_MS;

// Someone is looking at this pane on the PC right now: window and pane focused, input in the last 45 s, unlocked.
const seenOnPc = session => !session.el.hidden && document.hasFocus() && session.el.contains(document.activeElement) && !awayFromPc();

// The bottom of the screen shows whether the agent asks to run or change something.
function attentionText(session) {
  const buffer = session.term.buffer.active;
  const bottom = [];
  for (let y = Math.max(0, buffer.baseY + buffer.cursorY - 12); y < buffer.length; y++) bottom.push(buffer.getLine(y)?.translateToString(true) || '');
  return APPROVAL.test(bottom.join('\n')) ? `${agentName(session)} needs your OK to continue.` : `${agentName(session)} is done and waiting for you.`;
}

function notify(session, body) {
  const title = session.customTitle || session.autoTitle || session.label;
  if (notifyOn) send({ type: 'notify', session: session.id, title, body, visible: view === 'project' && !session.el.hidden });
}

function clearAttention(session) {
  if (!session.attention) return;
  session.attention = false;
  session.attentionKind = null;
  session.attentionMessage = null;
  refreshDots();
  remoteSoon();
}

// A notification, Home's needs-you strip or a status dot: the pane's project shows and the pane gets focus.
function showSession(id) {
  const session = sessions.get(id);
  if (!session) return;
  closeSettings();
  openProject(session.workspace);
  focusSession(id);
}

// ---------- images in a pane ----------

// Image paths an agent prints can be clicked to view them in the pane. Quoted paths may contain spaces.
const IMAGE_PATH = /(["'`])([^"'`\n]+?\.(?:png|jpe?g|gif|webp|bmp))\1|([^\s"'`<>|()[\]{}*?]+\.(?:png|jpe?g|gif|webp|bmp))(?![\w-]|\.\w)/gi;

// A path can wrap onto the next rows, so the whole wrapped line is searched.
function imageLinks(session, y) {
  const buffer = session.term.buffer.active;
  const cols = session.term.cols;
  let first = y - 1;
  while (first > 0 && y - 1 - first < 20 && buffer.getLine(first)?.isWrapped) first--;
  let last = y - 1;
  while (last - first < 40 && buffer.getLine(last + 1)?.isWrapped) last++;
  let text = '';
  for (let row = first; row <= last; row++) text += buffer.getLine(row)?.translateToString(row === last) || '';
  const at = index => ({ x: index % cols + 1, y: first + Math.floor(index / cols) + 1 });
  const links = [];
  for (const match of text.matchAll(IMAGE_PATH)) {
    const path = match[2] || match[3];
    if (path.includes('://')) continue;
    const start = match.index + (match[2] ? 1 : 0);
    const range = { start: at(start), end: at(start + path.length - 1) };
    if (range.start.y > y || range.end.y < y) continue;
    links.push({
      range, text: path,
      decorations: { underline: true, pointerCursor: true },
      activate: () => send({ type: 'openImage', session: session.id, workspace: session.workspace, path }),
    });
  }
  return links.length ? links : undefined;
}

// open: the user clicked a path, so it is shown in the pane. Otherwise an image just appeared in a project folder or
// an agent's image folder: the Images tab lists it if it shows.
function imageArrived(message) {
  if (!message.open) return loadImagesSoon();
  const session = sessions.get(message.session);
  if (!session) return;
  const images = session.images ||= [];
  const known = images.findIndex(image => image.path.toLowerCase() === message.path.toLowerCase());
  if (known >= 0) images.splice(known, 1);
  images.push({ url: message.url, path: message.path, name: message.name, version: Date.now() });
  if (images.length > 24) images.shift();
  viewImage(session, images.length - 1);
}

const imageSrc = image => `${image.url}?v=${image.version}`;

// ---------- Images tab ----------

// The side panel's Images tab: the images in the current project and in its agents' own image folders. Drag one
// onto a pane to paste its path into that agent's prompt. The Images button in the top bar opens the tab, or
// hides the panel when the tab already shows.
const IMAGE_DRAG = 'application/x-agent-arcade-image';
// Each request is numbered; a scan that finishes late doesn't replace the answer to a newer one.
let imagesAsked = 0;
let imagesShown = 0;
let imagesKey = '';
let imagesTimer = 0;

function toggleImages() {
  if (panelShows('images')) return setPanel(false);
  panelTab = 'images';
  setPanel(true);
}

function loadImages() {
  if (!panelShows('images')) return;
  if (!state.workspace) return showImageList(null, []);
  send({ type: 'listImages', workspace: state.workspace, seq: ++imagesAsked });
}

// New images come in bursts (a test run saving screenshots): one look once they stop.
function loadImagesSoon() {
  clearTimeout(imagesTimer);
  imagesTimer = setTimeout(loadImages, 800);
}

// total: how many images there are; the C# side sends only the newest ones.
function showImageList(workspace, items, total = items.length) {
  if (!panelShows('images') || (workspace && !same(workspace, state.workspace))) return;
  // The time is in the key and the image URL: an image written again under the same name shows anew.
  const key = JSON.stringify([total, items.map(item => [item.path, item.time])]);
  if (key === imagesKey) return;
  imagesKey = key;
  const count = $('#images-count');
  count.textContent = !items.length ? '' : total > items.length ? `${items.length} of ${total}` : String(items.length);
  count.title = total > items.length ? `The ${items.length} newest of ${total} images` : '';
  const grid = $('#images-grid');
  const scroll = grid.scrollTop;
  // Lazy: only the tiles scrolled into view load their (full-size) image.
  grid.innerHTML = items.length
    ? items.map(item => `<figure class="image-tile" draggable="true" data-path="${esc(item.path)}" title="${esc(item.path)}">` +
        `<img src="${esc(`${item.url}?v=${item.time || 0}`)}" alt="" draggable="false" loading="lazy" decoding="async"><figcaption>${esc(item.name)}</figcaption></figure>`).join('')
    : `<p class="panel-empty">${state.workspace ? 'No images in this project yet. Images that appear here show up on their own.' : 'Pick a project in the sidebar first.'}</p>`;
  grid.scrollTop = scroll;
}

function imageListArrived(message) {
  if ((message.seq || 0) < imagesShown) return;
  imagesShown = message.seq || 0;
  const items = message.items || [];
  showImageList(message.workspace, items, Math.max(message.total || 0, items.length));
}

// ---------- hiding the sidebar ----------

// The button left of the project name hides the sidebar, so the panes get the whole width. Survives restarts.
// The panes refit through their ResizeObserver; there is no animation, so each agent gets one resize, not dozens.
// In a narrow window (drawerMode) the sidebar is a drawer over the page instead: the button opens it, and picking
// something in it, Esc or a click beside it closes it. That never resizes the panes, and the saved choice stays for
// wider windows.
function setSidebar(hidden) {
  localStorage.setItem('sidebar', hidden ? 'hidden' : 'shown');
  showSidebar();
}

function setDrawer(open) {
  if (drawerOpen === (drawerMode && open)) return;
  drawerOpen = drawerMode && open;
  showSidebar();
}

function showSidebar() {
  const app = $('.app');
  const hidden = drawerMode ? !drawerOpen : localStorage.getItem('sidebar') === 'hidden';
  app.classList.toggle('side-hidden', !drawerMode && hidden);
  app.classList.toggle('drawer-open', drawerMode && drawerOpen);
  // A closed drawer is out of reach for Tab and screen readers too.
  $('.sidebar').inert = drawerMode && !drawerOpen;
  $('#drawer-backdrop').hidden = !(drawerMode && drawerOpen);
  const button = $('#toggle-sidebar');
  button.title = hidden ? 'Show the sidebar' : 'Hide the sidebar';
  button.setAttribute('aria-label', button.title);
  button.setAttribute('aria-pressed', String(hidden));
  fitTopbar();
}

// ---------- window shape ----------

// Found again whenever the window changes size: a monitor turned on its side, a window snapped to half a screen.
// app.css lays out .portrait and .drawer; this sets the classes and what depends on sizes (the panel, the grid).
function applyWindowShape() {
  const portrait = Layout.portrait(innerWidth, innerHeight, portraitMode);
  const drawer = Layout.drawer(innerWidth);
  const app = $('.app');
  app.classList.toggle('portrait', portrait);
  app.classList.toggle('drawer', drawer);
  if (drawer !== drawerMode) {
    drawerMode = drawer;
    drawerOpen = false;
    showSidebar();
  }
  portraitMode = portrait;
  fitPanel();
  layoutGrid();
}

let shapeFrame = 0;
window.addEventListener('resize', () => {
  if (!shapeFrame) shapeFrame = requestAnimationFrame(() => {
    shapeFrame = 0;
    applyWindowShape();
  });
});

// The top bar's buttons drop their labels when they would leave the project name too little room (side panel
// open, narrow window). Runs on every resize of the bar, and right after the panel or sidebar is shown or hidden.
function fitTopbar() {
  const topbar = $('.topbar');
  topbar.classList.remove('compact');
  if (topbar.querySelector('.top-actions').offsetWidth + 220 > topbar.clientWidth) topbar.classList.add('compact');
}

// ---------- side panel: Preview, Images, Changes, Console ----------

// A panel on the right, like the ones in Claude and Codex: preview a page an agent works on, drag its images onto
// panes, see what changed in the project, or use a console in it. The button at the right of the top bar hides it; its tab and width
// survive restarts. The phone never sees it.
const panel = $('#side-panel');
const PANEL_TABS = ['preview', 'images', 'changes', 'console'];
let panelTab = PANEL_TABS.includes(localStorage.getItem('panelTab')) ? localStorage.getItem('panelTab') : 'preview';
const panelShows = tab => !panel.hidden && panelTab === tab;

function setPanel(open) {
  panel.hidden = !open;
  $('.app').classList.toggle('panel-open', open);
  const button = $('#toggle-panel');
  button.title = open ? 'Hide the side panel' : 'Show the side panel';
  button.setAttribute('aria-label', button.title);
  button.setAttribute('aria-pressed', String(open));
  localStorage.setItem('panel', open ? 'open' : 'closed');
  fitTopbar();
  // Docked under the panes (portrait), the panel takes height from the grid.
  layoutGrid();
  if (open) showPanelTab(panelTab);
}

function showPanelTab(tab) {
  panelTab = PANEL_TABS.includes(tab) ? tab : 'preview';
  localStorage.setItem('panelTab', panelTab);
  for (const button of panel.querySelectorAll('.panel-tab')) {
    button.classList.toggle('active', button.dataset.tab === panelTab);
    button.setAttribute('aria-selected', String(button.dataset.tab === panelTab));
  }
  for (const view of panel.querySelectorAll('.panel-view')) view.hidden = view.dataset.view !== panelTab;
  if (panelTab === 'preview' && state.workspace) send({ type: 'listDocs', workspace: state.workspace });
  if (panelTab === 'images') loadImages();
  if (panelTab === 'changes') loadChanges();
  if (panelTab === 'console') openConsole();
}

// Another project was picked: a project file in the preview and the picked change belonged to the old one.
function panelWorkspaceChanged() {
  if (previewFile) resetPreview();
  imagesKey = '';
  changesSelected = null;
  changesKey = '';
  diffShown = null;
  changesFiles = [];
  changesWaiting = 0;
  diffWaiting = 0;
  $('#changes-list').innerHTML = '<p class="panel-empty">Looking…</p>';
  $('#changes-diff').innerHTML = '';
  $('#changes-branch').textContent = '';
  $('#changes-count').textContent = '';
  if (!panel.hidden) showPanelTab(panelTab);
}

// Dragged by its left edge. As a column it leaves every column of panes at least MIN_COLUMN px, so three panes side by
// side need three times that; when the window is too narrow for that, it floats over the panes instead
// (panel-overlay), so they keep their width.
const MIN_COLUMN = 360;
const MIN_PANEL = 280;
// The sidebar's width in app.css (.app's first column).
const SIDEBAR_WIDTH = 252;

function setPanelWidth(width, save = true) {
  const app = $('.app');
  // The columns of panes with the grid's gaps (8) between them, and the grid's padding (16).
  const panes = MIN_COLUMN * paneColumns + 8 * (paneColumns - 1) + 16;
  // The sidebar (SIDEBAR_WIDTH) when it takes a column: not hidden, and not a drawer over the page.
  const room = window.innerWidth - (drawerMode || app.classList.contains('side-hidden') ? 0 : SIDEBAR_WIDTH) - panes;
  const overlay = room < MIN_PANEL;
  const wanted = Math.max(MIN_PANEL, Math.round(Number(width) || 480));
  const shown = Math.min(wanted, overlay ? window.innerWidth - 20 : room);
  app.classList.toggle('panel-overlay', overlay);
  app.style.setProperty('--panel-width', shown + 'px');
  if (save) localStorage.setItem('panelWidth', String(shown));
  fitTopbar();
}

// Portrait: the panel docks under the panes and is dragged by its top edge, between MIN_PANEL_HEIGHT and what leaves
// the panes MIN_STAGE px under the top bar (52 px). It starts at 40% of the window.
const MIN_PANEL_HEIGHT = 180;
const MIN_STAGE = 240;

function setPanelHeight(height, save = true) {
  const app = $('.app');
  app.classList.remove('panel-overlay');
  const most = Math.max(MIN_PANEL_HEIGHT, window.innerHeight - 52 - MIN_STAGE);
  const shown = Math.min(most, Math.max(MIN_PANEL_HEIGHT, Math.round(Number(height) || window.innerHeight * 0.4)));
  app.style.setProperty('--panel-height', shown + 'px');
  if (save) localStorage.setItem('panelHeight', String(shown));
  fitTopbar();
}

// The panel's size for the window's shape: a width beside the panes, or a height under them.
function fitPanel() {
  if (portraitMode) setPanelHeight(localStorage.getItem('panelHeight'), false);
  else setPanelWidth(localStorage.getItem('panelWidth'), false);
}

$('#panel-resize').addEventListener('pointerdown', event => {
  event.preventDefault();
  const handle = event.currentTarget;
  handle.setPointerCapture(event.pointerId);
  const rows = portraitMode;
  document.body.classList.add('resizing-panel');
  document.body.classList.toggle('resizing-rows', rows);
  const box = panel.getBoundingClientRect();
  const move = moveEvent => rows ? setPanelHeight(box.bottom - moveEvent.clientY, false) : setPanelWidth(box.right - moveEvent.clientX, false);
  handle.addEventListener('pointermove', move);
  handle.addEventListener('lostpointercapture', () => {
    handle.removeEventListener('pointermove', move);
    document.body.classList.remove('resizing-panel', 'resizing-rows');
    const now = panel.getBoundingClientRect();
    if (rows) setPanelHeight(now.height);
    else setPanelWidth(now.width);
    layoutGrid();
  }, { once: true });
});

// ---------- Preview ----------

// Pages on this PC (localhost) and the project's own files open in the panel; other sites go to the normal browser.
const LOOPBACK = /^(localhost|127\.0\.0\.1|\[::1\]|[a-z0-9-]+\.localhost)$/i;
let previewUrl = '';
// The project file shown, relative to the project; '' for a localhost page.
let previewFile = '';

function localUrl(text) {
  const value = String(text || '').trim();
  const full = /^https?:\/\//i.test(value) ? value
    : /^(localhost|127\.0\.0\.1|\[::1\])(:\d+)?([/?#]|$)/i.test(value) ? 'http://' + value : '';
  if (!full) return null;
  try {
    const url = new URL(full);
    return LOOPBACK.test(url.hostname) ? url.href : null;
  } catch {
    return null;
  }
}

function openPreview(text) {
  const value = String(text || '').trim();
  if (!value) return;
  const url = localUrl(value);
  if (url) return showPreview(url, '');
  // A web address that isn't on this PC (C:\… is a path, not a scheme).
  if (/^[a-z][a-z0-9+.-]*:/i.test(value) && !/^[a-z]:[\\/]/i.test(value)) {
    return toast('Only pages on this PC (localhost) and files in the project open here. Other sites open in your browser.');
  }
  if (!state.workspace) return toast('Pick a project in the sidebar first.');
  send({ type: 'previewFile', workspace: state.workspace, path: value });
}

// file: the project file behind url, so a reload asks for it fresh.
function showPreview(url, file) {
  previewUrl = url;
  previewFile = file;
  $('#preview-address').value = file || url;
  $('#preview-external').hidden = !!file;
  $('#preview-empty').hidden = true;
  const frame = $('#preview-frame');
  frame.hidden = false;
  frame.src = file ? `${url}?v=${Date.now()}` : url;
  // Without the query: a link from a terminal can carry a token (Jupyter's ?token=…), which mustn't be kept.
  localStorage.setItem('previewAddress', file || url.split(/[?#]/)[0]);
}

function resetPreview() {
  previewUrl = '';
  previewFile = '';
  const frame = $('#preview-frame');
  frame.removeAttribute('src');
  frame.hidden = true;
  $('#preview-empty').hidden = false;
  $('#preview-external').hidden = false;
}

function previewUrlArrived(message) {
  if (!same(message.workspace, state.workspace)) return;
  showPreview(message.url, message.path);
}

function docListArrived(message) {
  if (!same(message.workspace, state.workspace)) return;
  $('#preview-docs').innerHTML = (message.files || []).map(file => `<option value="${esc(file)}"></option>`).join('');
}

// Links in a pane or the console: pages on this PC open in the panel, the rest in the normal browser.
function openLink(uri) {
  const url = localUrl(uri);
  if (!url) return send({ type: 'openUrl', url: uri });
  panelTab = 'preview';
  setPanel(true);
  showPreview(url, '');
}

// ---------- Changes ----------

const STATUS_LETTER = { modified: 'M', new: 'U', added: 'A', deleted: 'D', renamed: 'R', copied: 'C', conflict: '!' };
let changesFiles = [];
// The file whose diff shows, and the last list and diff drawn, so a refresh that changes nothing keeps the scroll.
let changesSelected = null;
let changesKey = '';
let diffShown = null;
// The config hash the Changes tab asks "Run git here anyway" for; gitTrust sends it back.
let changesTrust = '';
// One git status and one git diff at a time: on a big repository they take seconds, and the 5 s refresh must not
// pile them up. Status requests are numbered and only the newest answer counts. A request without an answer
// after 30 s counts as lost.
let changesAsked = 0;
let changesWaiting = 0;
let diffWaiting = 0;
const LOST_MS = 30000;

// again: the 5 s refresh, which waits for the previous answer.
function loadChanges(again = false) {
  if (!state.workspace) return changesArrived({ workspace: '', error: 'Pick a project in the sidebar first.' });
  if (again && changesWaiting && Date.now() - changesWaiting < LOST_MS) return;
  changesWaiting = Date.now();
  send({ type: 'gitChanges', workspace: state.workspace, seq: ++changesAsked });
}

function changesArrived(message) {
  if (message.seq) {
    if (message.seq !== changesAsked) return;
    changesWaiting = 0;
  }
  if (!panelShows('changes') || !same(message.workspace, state.workspace)) return;
  const files = message.files || [];
  const key = JSON.stringify([message.error, message.branch, message.total, files, message.trust]);
  if (key !== changesKey) {
    changesKey = key;
    changesFiles = files;
    changesTrust = message.trust?.config || '';
    $('#changes-branch').textContent = message.error ? '' : message.branch || '';
    $('#changes-count').textContent = message.error || !files.length ? ''
      : message.total > files.length ? `${files.length} of ${message.total} files` : `${files.length} file${files.length === 1 ? '' : 's'}`;
    const list = $('#changes-list');
    if (message.trust) {
      // The repository's config makes git read other files or another folder (MainForm.TrustGit stores the answer).
      const uses = (message.trust.uses || []).map(esc).join('<br>') + (message.trust.more ? `<br>… and ${message.trust.more} more` : '');
      list.innerHTML = `<div class="panel-empty"><p>${esc(message.error)}</p><p class="git-uses">${uses}</p>` +
        '<button class="ghost-btn" data-act="git-trust">Run git here anyway</button></div>';
      changesSelected = null;
      diffShown = null;
      $('#changes-diff').innerHTML = '';
      return;
    }
    if (message.error || !files.length) {
      list.innerHTML = `<p class="panel-empty">${esc(message.error || 'No changes: the project matches its last commit.')}</p>`;
      changesSelected = null;
      diffShown = null;
      $('#changes-diff').innerHTML = '';
      return;
    }
    changesSelected = changesSelected && files.find(file => file.path === changesSelected.path) || null;
    const scroll = list.scrollTop;
    list.innerHTML = files.map((file, i) => {
      const slash = file.path.lastIndexOf('/');
      return `<button class="change-row${file === changesSelected ? ' active' : ''}" data-index="${i}" ` +
        `title="${esc(file.from ? `${file.from} → ${file.path}` : file.path)} (${esc(file.status)})">` +
        `<span class="change-status ${esc(file.status)}">${STATUS_LETTER[file.status] || 'M'}</span>` +
        `<span class="change-name ellipsis">${esc(file.path.slice(slash + 1))}</span>` +
        `<span class="change-dir ellipsis">${esc(slash > 0 ? file.path.slice(0, slash) : '')}</span></button>`;
    }).join('');
    list.scrollTop = scroll;
    if (!changesSelected) {
      diffShown = null;
      $('#changes-diff').innerHTML = '<p class="panel-empty">Pick a file to see what changed.</p>';
    }
  }
  // The file may have changed again without its status changing.
  if (changesSelected) requestDiff(changesSelected, true);
}

// again: a refresh, which waits for the previous diff.
function requestDiff(file, again = false) {
  if (again && diffWaiting && Date.now() - diffWaiting < LOST_MS) return;
  diffWaiting = Date.now();
  send({ type: 'gitDiff', workspace: state.workspace, path: file.path, from: file.from || '', untracked: file.status === 'new' });
}

const diffClass = line =>
  /^(diff |index |--- |\+\+\+ |new file|deleted file|similarity |rename |old mode|new mode|Binary files)/.test(line) ? 'meta'
    : line.startsWith('@@') ? 'hunk' : line.startsWith('+') ? 'add' : line.startsWith('-') ? 'del' : '';

function diffArrived(message) {
  diffWaiting = 0;
  if (!panelShows('changes') || !same(message.workspace, state.workspace) || message.path !== changesSelected?.path) return;
  if (message.error) {
    diffShown = null;
    $('#changes-diff').innerHTML = `<p class="panel-empty">${esc(message.error)}</p>`;
    return;
  }
  const key = message.path + '\0' + message.text;
  if (key === diffShown) return;
  diffShown = key;
  const box = $('#changes-diff');
  const scroll = box.scrollTop;
  const lines = String(message.text || '').split('\n');
  if (lines.at(-1) === '') lines.pop();
  if (!lines.length) {
    box.innerHTML = '<p class="panel-empty">No differences to show (only the file mode or line endings changed).</p>';
    return;
  }
  if (message.cut) lines.push('… the rest is cut off: this diff is too long to show here.');
  box.innerHTML = '<div class="diff-lines">' +
    lines.map(line => `<div class="diff-line ${diffClass(line)}">${esc(line) || ' '}</div>`).join('') + '</div>';
  box.scrollTop = scroll;
}

$('#changes-list').addEventListener('click', event => {
  if (event.target.closest('[data-act="git-trust"]')) {
    if (!changesTrust) return;
    send({ type: 'gitTrust', workspace: state.workspace, config: changesTrust });
    $('#changes-list').innerHTML = '<p class="panel-empty">Looking…</p>';
    changesKey = '';
    return loadChanges();
  }
  const row = event.target.closest('.change-row');
  if (!row) return;
  changesSelected = changesFiles[Number(row.dataset.index)] || null;
  for (const item of $('#changes-list').querySelectorAll('.change-row')) item.classList.toggle('active', item === row);
  diffShown = null;
  $('#changes-diff').scrollTop = 0;
  if (changesSelected) requestDiff(changesSelected);
});

// Agents change files all the time: while the tab shows, it looks again every 5 seconds.
setInterval(() => {
  if (panelShows('changes') && document.visibilityState === 'visible') loadChanges(true);
}, 5000);

// ---------- Console ----------

// A PowerShell in the project, started the first time the tab opens. It keeps running when you switch projects;
// the + button starts a new one in the current project.
let consoleTerm = null;
let consoleFit = null;
let consoleRunning = false;

function openConsole() {
  if (!consoleTerm) {
    consoleTerm = new Terminal({
      fontFamily: '"Cascadia Mono", "Cascadia Code", Consolas, monospace',
      fontSize,
      cursorBlink: true,
      allowProposedApi: true,
      scrollback: 5000,
      theme: THEME,
      linkHandler: { activate: (event, uri) => openLink(uri) },
    });
    consoleFit = new FitAddon.FitAddon();
    consoleTerm.loadAddon(consoleFit);
    try {
      consoleTerm.loadAddon(new Unicode11Addon.Unicode11Addon());
      consoleTerm.unicode.activeVersion = '11';
    } catch { /* default widths are fine too */ }
    consoleTerm.loadAddon(new WebLinksAddon.WebLinksAddon((event, uri) => openLink(uri)));
    consoleTerm.open($('#console-term'));
    // Copy, paste and text size work as in the panes.
    const keys = { term: consoleTerm };
    consoleTerm.attachCustomKeyEventHandler(event => handleKey(keys, event));
    consoleTerm.onData(data => {
      if (consoleRunning) send({ type: 'consoleInput', data });
    });
    consoleTerm.onResize(size => {
      if (consoleRunning) send({ type: 'consoleResize', cols: size.cols, rows: size.rows });
    });
    new ResizeObserver(() => fitConsole()).observe($('#console-term'));
  }
  fitConsole();
  // Not before the first state: the page's ready message stops everything that runs, the console too.
  if (!consoleRunning && state.workspace) startConsole();
  consoleTerm.focus();
}

function fitConsole() {
  if (!consoleTerm || !panelShows('console')) return;
  const box = $('#console-term');
  if (box.clientWidth < 60 || box.clientHeight < 40) return;
  try {
    consoleFit.fit();
  } catch { /* not measurable yet */ }
}

// Each console gets a number, which comes back with its output: what a replaced console still sent is dropped.
let consoleId = 0;

function startConsole() {
  consoleRunning = true;
  $('#console-folder').textContent = state.workspace;
  $('#console-folder').title = state.workspace;
  send({ type: 'consoleStart', workspace: state.workspace, cols: consoleTerm.cols, rows: consoleTerm.rows, id: ++consoleId });
}

const consoleNow = message => message.id === consoleId;

function consoleExited(message) {
  if (!consoleNow(message)) return;
  consoleRunning = false;
  consoleTerm?.write(message.error
    ? `\r\n\x1b[31m${message.error}\x1b[0m\r\n`
    : '\r\n\x1b[90m— console ended; + starts a new one —\x1b[0m\r\n');
}

$('#console-restart').addEventListener('click', () => {
  if (consoleRunning) send({ type: 'consoleStop' });
  consoleRunning = false;
  consoleTerm?.reset();
  openConsole();
});

function viewImage(session, index) {
  const image = session.images?.[index];
  if (!image) return;
  session.viewing = index;
  let view = session.el.querySelector('.image-view');
  if (!view) {
    session.el.querySelector('.pane-body').insertAdjacentHTML('beforeend',
      '<div class="image-view" tabindex="-1"><div class="image-bar">' +
      `${icon('image', 15)}<span class="image-name ellipsis"></span><span class="image-count"></span>` +
      `<button class="icon-btn small" data-act="image-paste" title="Paste the path into the pane, so the agent can look at it" aria-label="Paste the path into the pane">${icon('terminal', 15)}</button>` +
      `<button class="icon-btn small" data-act="image-reveal" title="Show in folder" aria-label="Show in folder">${icon('folder', 15)}</button>` +
      `<button class="icon-btn small" data-act="image-close" title="Close (Esc)" aria-label="Close the image">${icon('close', 15)}</button>` +
      '</div><div class="image-stage"><img alt="" draggable="false" title="Click to switch between fit and actual size"></div></div>');
    view = session.el.querySelector('.image-view');
    view.addEventListener('keydown', event => imageKey(session, event));
    view.querySelector('img').addEventListener('click', event => event.currentTarget.closest('.image-stage').classList.toggle('actual'));
    view.querySelector('img').addEventListener('error', () => toast(`Can't show ${session.images?.[session.viewing]?.name || 'that image'}.`, 'error'));
  }
  view.querySelector('.image-name').textContent = image.name;
  view.querySelector('.image-name').title = image.path;
  const count = session.images.length;
  view.querySelector('.image-count').textContent = count > 1 ? `${index + 1} / ${count}` : '';
  view.querySelector('.image-stage').classList.remove('actual');
  view.querySelector('img').src = imageSrc(image);
  view.focus();
}

function closeImage(session) {
  session.el.querySelector('.image-view')?.remove();
  session.viewing = null;
  session.term.focus();
}

function imageKey(session, event) {
  const step = event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowRight' ? 1 : 0;
  if (event.key === 'Escape') closeImage(session);
  else if (step) viewImage(session, (session.viewing + step + session.images.length) % session.images.length);
  else return;
  event.preventDefault();
  event.stopPropagation();
}

function imageAction(session, button) {
  const image = session.images?.[session.viewing];
  switch (button.dataset.act) {
    case 'image': return viewImage(session, Number(button.dataset.index));
    case 'image-close': return closeImage(session);
    case 'image-reveal': return image && send({ type: 'revealImage', url: image.url });
    case 'image-paste':
      if (!image) return;
      if (!session.running) return toast('This session has ended. Restart it first.');
      closeImage(session);
      return pastePaths(session, [image.path]);
  }
}

function paneMenu(session, anchor) {
  const items = [];
  if (isChat(session)) {
    items.push(
      { label: 'Rename chat…', sub: 'Or double-click the title', icon: 'pencil', run: () => editTitle(session) },
      { label: 'Chat history', icon: 'history', run: () => openHistory() },
      '-',
      { label: 'Paste "save progress" prompt', sub: 'Writes HANDOFF.md for the next agent', icon: 'save', run: () => pastePrompt(session, SAVE_PROMPT) },
      { label: 'Paste "continue" prompt', sub: 'Picks up from HANDOFF.md', icon: 'play', run: () => pastePrompt(session, CONTINUE_PROMPT) },
      '-');
  }
  items.push(
    {
      label: 'Clear screen', icon: 'eraser',
      run: () => {
        session.term.clear();
        screenChanged(session);
      },
    },
    { label: 'Text size…', sub: `${fontSize} px · Ctrl+= / Ctrl+-`, icon: 'text', run: () => openSettings('general') },
    {
      label: notifyOn ? 'Turn off Windows notifications' : 'Turn on Windows notifications',
      sub: 'When an agent is done or needs you, for every pane', icon: 'bell',
      run: () => {
        setNotify(!notifyOn);
        toast(notifyOn ? 'Windows tells you when an agent is done or needs you, unless you are looking at it.' : 'No more Windows notifications. Panes still turn red when an agent needs you.');
      },
    },
    { label: session.running ? 'Restart' : 'Start again', sub: session.mode === 'resume' ? 'Continues the same chat' : isChat(session) ? 'Starts a new chat' : null, icon: 'refresh', run: () => restart(session) });
  if (isChat(session)) {
    items.push({ label: 'Open in Windows Terminal', icon: 'external', run: () => send({ type: 'external', account: session.key }) });
  }
  menu(anchor, items);
}

// fresh: start a new chat even when the pane continued an earlier one.
async function restart(session, fresh = false) {
  if (session.running) {
    const ok = await modal({ title: `Restart ${titleOf(session)}?`, body: '<p>The agent stops and starts again in a new pane.</p>', confirm: 'Restart' });
    if (!ok) return;
  }
  const { key, workspace, order } = session;
  const mode = fresh && isChat(session) ? 'start' : session.mode;
  const chat = mode === 'resume' ? chatOf(session.chat || session.resumeOf) : null;
  await closeSession(session, true);
  if (!same(workspace, state.workspace)) return;
  // The new pane takes the old one's place in the grid.
  const reopened = openSession(key, mode === 'resume' && !chat ? 'start' : mode, chat);
  if (reopened) {
    reopened.order = order;
    renderPanes();
  }
}

// Ends the pane and continues its chat in a new one at the same place.
async function resumeHere(session) {
  const chat = chatOf(session.chat);
  if (!chat) return;
  const order = session.order;
  await closeSession(session, true);
  const fresh = resumeChat(chat);
  if (fresh) {
    fresh.order = order;
    renderPanes();
  }
}

function toggleMax(session) {
  maxed = maxed === session.id ? null : session.id;
  renderPanes();
  session.term.focus();
}

// ---------- account and workspace actions ----------

async function confirmSignIn(account) {
  const agent = agentOf(account);
  const ok = await modal({
    title: `Sign in to ${agent.name} (${account.name})`,
    body: `<p>${esc(agent.loginHint || 'Follow the steps in the pane.')}</p>` +
      '<p class="muted">If your browser is still signed in to another account, switch to the right one there before you approve. ' +
      `The sign-in runs inside ${esc(agent.name)} itself; Agent Arcade never sees your password or login tokens.</p>`,
    confirm: 'Open sign-in',
  });
  if (ok) openSession(account.key, 'login');
}

// What an agent's Install command needs first (Node.js, PowerShell 7, WinGet). C# looks (Prerequisites in Backend.cs)
// and answers installCheck with { agent, needs: [{ text, url }] }. null when no answer came in time: the install pane
// checks again before it runs anything.
const installChecks = new Map();

function installNeeds(agent) {
  return new Promise(resolve => {
    installChecks.get(agent.name)?.(null);
    let timer = 0;
    const done = needs => {
      clearTimeout(timer);
      if (installChecks.get(agent.name) === done) installChecks.delete(agent.name);
      resolve(needs);
    };
    timer = setTimeout(() => done(null), 15000);
    installChecks.set(agent.name, done);
    send({ type: 'installCheck', agent: agent.name });
  });
}

// Nothing is installed: what is missing, each with a button to the page that tells how to get it.
async function showInstallNeeds(agent, needs) {
  const site = url => {
    try { return new URL(url).hostname.replace(/^www\./, ''); } catch { return url; }
  };
  const several = needs.length > 1;
  const ok = await modal({
    title: `${agent.name} needs something first`,
    body: needs.map((need, i) => `<p>${esc(need.text)}</p>` +
        (several ? `<p><button class="tag-btn" data-need="${i}">Open ${esc(site(need.url))}</button></p>` : '')).join('') +
      '<p class="muted">Nothing was installed.</p>',
    confirm: several ? 'Close' : `Open ${site(needs[0].url)}`,
    cancel: several ? null : 'Close',
    opened: root => root.querySelectorAll('[data-need]').forEach(button =>
      button.addEventListener('click', () => send({ type: 'openUrl', url: needs[Number(button.dataset.need)].url }))),
  });
  if (ok && !several) send({ type: 'openUrl', url: needs[0].url });
}

async function askInstall(agent) {
  if (!agent) return;
  if (!agent.install) return toast(`No install command is set for ${agent.name}. Use Edit agent list.`, 'error');
  const needs = await installNeeds(agent);
  if (needs?.length) return showInstallNeeds(agent, needs);
  const ok = await modal({
    title: `Install or update ${agent.name}?`,
    body: `<p>This runs in a new pane:</p><pre class="cmd">${esc(agent.install)}</pre><p class="muted">When it's done, Settings → Agents and accounts updates by itself.</p>`,
    confirm: 'Install',
  });
  if (!ok) return;
  const main = state.accounts.find(account => account.agent === agent.name && account.main);
  if (main) openSession(main.key, 'install');
}

// New project: C# checks the name and makes the folder next to the current workspace (NewProject in Remote.cs). The
// page only shows where it goes and what C# says, while you type and after Create.
let newProjectChecked = null;
let newProjectAnswer = null;

async function newProject() {
  if (!state.projectsFolder) {
    await modal({
      title: 'New project',
      body: '<p>A new project goes next to your current one, but there is no folder for it yet. Add a project folder with + first.</p>',
      cancel: null,
    });
    return;
  }
  const base = state.projectsFolder.replace(/[\\/]+$/, '') + '\\';
  let timer = 0;
  await modal({
    title: 'New project',
    body: '<p>Agent Arcade makes an empty folder and adds it to the workspaces.</p>' +
      '<input name="name" placeholder="Project name" maxlength="80" spellcheck="false" autocomplete="off" aria-label="Project name">' +
      '<div class="new-project-path"></div>',
    confirm: 'Create',
    opened: root => {
      const input = root.querySelector('[name="name"]');
      const path = root.querySelector('.new-project-path');
      const error = root.querySelector('.modal-error');
      const showPath = () => { path.textContent = base + (input.value.trim() || '…'); };
      // An empty field isn't a problem until you press Create.
      newProjectChecked = (name, problem) => {
        if (name !== input.value) return;
        error.textContent = problem || '';
        error.hidden = !problem || !name.trim();
      };
      input.addEventListener('input', () => {
        showPath();
        clearTimeout(timer);
        timer = setTimeout(() => send({ type: 'newProjectCheck', name: input.value }), 120);
      });
      showPath();
    },
    submit: values => new Promise(resolve => {
      newProjectAnswer = resolve;
      send({ type: 'newProject', name: values.name });
    }),
  });
  clearTimeout(timer);
  newProjectChecked = newProjectAnswer = null;
}

async function addAccount(agentName) {
  const choices = state.agents.filter(agent => agent.multi && agent.installed);
  if (!choices.length) return toast('Install Claude Code, Codex or Gemini first.', 'error');
  const result = await modal({
    title: 'Add account',
    body: `<label>Agent<select name="agent">${choices.map(agent =>
        `<option${agent.name === agentName ? ' selected' : ''}>${esc(agent.name)}</option>`).join('')}</select></label>` +
      '<label>Short name<input name="name" value="second" maxlength="31" spellcheck="false" autocomplete="off"></label>' +
      '<p class="muted">The account gets its own folder and signs in with its own login. You can run it next to your other accounts.</p>',
    confirm: 'Add and sign in',
    validate: values => {
      const name = values.name.trim();
      if (!/^[A-Za-z0-9][A-Za-z0-9._-]{0,30}$/.test(name)) return 'Use letters, digits, dot, _ or - (no spaces).';
      if (name.toLowerCase() === 'main' || state.accounts.some(account => account.agent === values.agent && account.name.toLowerCase() === name.toLowerCase()))
        return 'That name is already used for this agent.';
      return null;
    },
  });
  if (result) send({ type: 'addAccount', agent: result.agent, name: result.name.trim() });
}

function accountMenu(account, anchor) {
  const agent = agentOf(account);
  if (!account || !agent) return;
  menu(anchor, [
    { label: 'New session', icon: 'terminal', disabled: !agent.installed, run: () => openSession(account.key) },
    { label: 'Sign in', sub: account.main ? 'Or switch the account it uses' : null, icon: 'login', disabled: !agent.installed, run: () => confirmSignIn(account) },
    { label: 'Open in Windows Terminal', icon: 'external', disabled: !agent.installed, run: () => send({ type: 'external', account: account.key }) },
    { label: 'Install / update', icon: 'download', run: () => askInstall(agent) },
    '-',
    {
      label: 'Remove account', icon: 'trash', danger: true, disabled: account.main,
      sub: account.main ? 'The main account always stays' : null,
      run: async () => {
        const ok = await modal({
          title: `Remove ${agent.name} (${account.name})?`,
          body: "<p>Its folder is deleted, including that account's login and chat history on this PC. Your project files stay.</p>",
          confirm: 'Remove',
          danger: true,
        });
        if (ok) send({ type: 'removeAccount', account: account.key });
      },
    },
  ]);
}

function workspaceMenu(path, anchor) {
  const running = sessionsIn(path).filter(session => session.running).length;
  const chats = history.filter(chat => same(chat.workspace, path)).length;
  menu(anchor, [
    { label: 'Chat history', sub: chats ? `${chats} chat${chats === 1 ? '' : 's'}` : 'No chats yet', icon: 'history', run: () => openHistory('project', path) },
    '-',
    {
      label: 'Remove from list', icon: 'trash', danger: true, disabled: state.workspaces.length <= 1,
      sub: state.workspaces.length <= 1 ? 'Add another workspace first' : 'The folder itself stays',
      run: async () => {
        const ok = await modal({
          title: `Remove ${workspaceOf(path)?.name || path}?`,
          body: '<p>It disappears from the sidebar. The folder and its files stay on your PC, and its chats stay in the history.</p>' +
            (running ? `<p>${running} running agent${running === 1 ? '' : 's'} in it will be stopped.</p>` : ''),
          confirm: 'Remove',
          danger: true,
        });
        if (!ok) return;
        for (const session of sessionsIn(path)) closeSession(session, true);
        send({ type: 'removeWorkspace', path });
      },
    },
  ]);
}

function newAgentMenu(anchor) {
  const items = [{ title: `Start in ${workspaceOf(state.workspace)?.name || 'this workspace'}` }];
  for (const account of state.accounts) {
    const agent = agentOf(account);
    if (!agent?.installed) continue;
    items.push({ label: `${agent.name} · ${account.name}`, sub: accountLine(account), avatar: avatar(agent, 'sm'), run: () => openSession(account.key) });
  }
  if (items.length === 1) items.push({ label: 'No agents installed yet', disabled: true, run: () => {} });
  items.push('-', { label: 'Add account…', icon: 'plus', run: () => addAccount() });
  menu(anchor, items);
}

// The top bar's ⋯: the project's folder, images, shared instructions, Windows Terminal, Unity and chat history.
function projectMenu(anchor) {
  const workspace = workspaceOf(state.workspace);
  const name = workspace?.name || 'this project';
  const chats = history.filter(chat => same(chat.workspace, state.workspace)).length;
  const items = [
    { label: 'Open folder', sub: 'In File Explorer', icon: 'folder', run: () => send({ type: 'openFolder' }) },
    { label: 'Images', sub: 'In the side panel, to drag onto a pane', icon: 'image', run: toggleImages },
    { label: 'Shared instructions…', sub: 'The same instructions for every agent', icon: 'doc', run: () => openSettings('instructions') },
    { label: 'Open in Windows Terminal…', sub: 'An agent outside Agent Arcade', icon: 'external', run: () => terminalMenu(anchor) },
  ];
  if (workspace?.unity) items.push({ label: 'Open in Unity', sub: 'The editor this project uses', icon: 'cube', run: () => send({ type: 'openUnity' }) });
  items.push('-', {
    label: 'Chat history', sub: chats ? `${chats} chat${chats === 1 ? '' : 's'} in ${name}` : `No chats in ${name} yet`, icon: 'history',
    run: () => openHistory('project', state.workspace),
  });
  menu(anchor, items);
}

// An agent in Windows Terminal, in the selected project, with the account's own folder.
function terminalMenu(anchor) {
  const items = [{ title: `Windows Terminal in ${workspaceOf(state.workspace)?.name || 'this project'}` }];
  for (const account of state.accounts) {
    const agent = agentOf(account);
    if (!agent?.installed) continue;
    items.push({ label: `${agent.name} · ${account.name}`, sub: accountLine(account), avatar: avatar(agent, 'sm'), run: () => send({ type: 'external', account: account.key }) });
  }
  if (items.length === 1) items.push({ label: 'No agents installed yet', disabled: true, run: () => {} });
  menu(anchor, items);
}

// The sidebar's + next to Projects and Home's last card: a folder you have, or a new empty one.
function addProjectMenu(anchor) {
  menu(anchor, [
    { label: 'Add a project folder…', sub: 'A folder you already have', icon: 'folder', run: () => send({ type: 'addWorkspace' }) },
    {
      label: 'New project…', sub: state.projectsFolder ? `An empty folder in ${state.projectsFolder}` : 'An empty folder for a new project',
      icon: 'folder-plus', run: () => newProject(),
    },
  ]);
}

// Ctrl+N and the sidebar's New agent: the same menu as the top bar's + New agent.
function openNewAgentMenu() {
  const side = $('#side-new-agent');
  newAgentMenu(side.offsetParent ? side : $('#new-agent'));
}

// ---------- chat history ----------

function chatAgent(chat) {
  return state.agents.find(agent => agent.name === chat.agent) || { name: chat.agent, command: '' };
}

// Why a chat can't be continued, or '' when it can.
function resumeProblem(chat) {
  const agent = state.agents.find(item => item.name === chat.agent);
  if (!agent) return `${chat.agent} is no longer in the agent list.`;
  if (!accountOf(chat.account)) return `The ${chat.accountName} account is gone.`;
  if (!chat.resume) return `${agent.name} can't continue earlier chats.`;
  if (!agent.installed && !agent.checking) return `${agent.name} isn't installed.`;
  return '';
}

function chatRow(chat) {
  const agent = chatAgent(chat);
  const problem = resumeProblem(chat);
  const tip = chat.resume === 'agent' ? `Opens ${agent.name}'s own resume, which may ask which chat` : 'Continue this chat';
  const action = chat.open
    ? `<button class="ghost-btn small" data-show-chat="${esc(chat.id)}">Show</button>`
    : `<button class="ghost-btn small" data-resume="${esc(chat.id)}" title="${esc(problem || tip)}"${problem ? ' disabled' : ''}>${icon('play', 12)}<span>Resume</span></button>`;
  const length = chat.ended ? ' · ' + duration(chat.started, chat.ended) : '';
  return `<div class="hist-row" data-chat="${esc(chat.id)}">` +
    `<span class="dot ${chat.open ? 'on' : 'off'}"></span>${avatar(agent, 'sm')}` +
    `<span class="hist-text"><span class="hist-title ellipsis${chat.title ? '' : ' placeholder'}">${esc(chat.title || 'Untitled chat')}</span>` +
    `<span class="hist-sub ellipsis">${esc(agent.name)} · ${esc(chat.accountName)} · ${esc(when(chat.started))}${esc(length)}</span></span>` +
    `${action}<button class="icon-btn small" data-chat-menu="${esc(chat.id)}" title="More" aria-label="More">${icon('more', 15)}</button></div>`;
}

// Continues a chat from the history in its own project. Returns the new pane's session.
function resumeChat(chat) {
  const open = [...sessions.values()].find(session => session.chat === chat.id);
  if (open) {
    closeHistory();
    if (!same(open.workspace, state.workspace)) selectWorkspace(open.workspace);
    return focusSession(open.id);
  }
  const problem = resumeProblem(chat);
  if (problem) return toast(problem, 'error');
  closeHistory();
  if (!workspaceOf(chat.workspace)) {
    // Removed from the sidebar since; it comes back first and the chat opens when it is there.
    pendingResume = chat.id;
    send({ type: 'restoreWorkspace', path: chat.workspace });
    return null;
  }
  if (!same(chat.workspace, state.workspace)) selectWorkspace(chat.workspace);
  return openSession(chat.account, 'resume', chat);
}

// Picks a project at the PC and shows its panes.
function selectWorkspace(path) {
  state.workspace = path;
  view = 'project';
  render();
  panelWorkspaceChanged();
  send({ type: 'selectWorkspace', path });
}

function renameChat(chat) {
  const session = [...sessions.values()].find(item => item.chat === chat.id);
  if (session) {
    closeHistory();
    if (!same(session.workspace, state.workspace)) selectWorkspace(session.workspace);
    focusSession(session.id);
    return editTitle(session);
  }
  modal({
    title: 'Rename chat',
    body: `<label>Title<input name="title" value="${esc(chat.title)}" maxlength="120" spellcheck="false" autocomplete="off"></label>`,
    confirm: 'Save',
  }).then(result => {
    if (result) send({ type: 'renameChat', chat: chat.id, title: result.title, named: result.title.trim().length > 0 });
  });
}

function chatMenu(chat, anchor) {
  menu(anchor, [
    { label: 'Rename…', icon: 'pencil', run: () => renameChat(chat) },
    { label: 'Copy title', icon: 'doc', disabled: !chat.title, run: () => send({ type: 'copy', text: chat.title }) },
    '-',
    {
      label: 'Remove from history', icon: 'trash', danger: true, disabled: chat.open,
      sub: chat.open ? 'Close its pane first' : `The chat itself stays in ${chat.agent}`,
      run: () => send({ type: 'removeChat', chat: chat.id }),
    },
  ]);
}

// scope: 'project' (one folder) or 'all' (every project, also ones no longer in the sidebar).
function openHistory(scope = 'project', path = state.workspace) {
  closeMenu();
  closeHistory();
  // One of the two big dialogs at a time, so Escape always knows which one to close.
  closeSettings();
  const root = document.createElement('div');
  root.className = 'modal-backdrop';
  root.innerHTML =
    '<div class="modal history" role="dialog" aria-modal="true" aria-label="Chat history">' +
    '<div class="hist-head"><h2>Chat history</h2><div class="seg">' +
    '<button data-scope="project"></button><button data-scope="all">All projects</button></div>' +
    `<button class="icon-btn" data-close title="Close" aria-label="Close">${icon('close', 15)}</button></div>` +
    '<input class="hist-search" placeholder="Search titles, agents and projects" spellcheck="false" autocomplete="off" aria-label="Search chats">' +
    '<div class="hist-list"></div></div>';
  historyView = { root, scope, path, query: '' };
  root.addEventListener('mousedown', event => {
    if (event.target === root) closeHistory();
  });
  root.addEventListener('input', event => {
    historyView.query = event.target.value.trim().toLowerCase();
    renderHistory();
  });
  root.addEventListener('click', event => {
    const target = event.target;
    const find = name => target.closest(`[${name}]`)?.getAttribute(name);
    if (target.closest('[data-close]')) return closeHistory();
    const scopeButton = find('data-scope');
    if (scopeButton) {
      historyView.scope = scopeButton;
      return renderHistory();
    }
    const resume = find('data-resume');
    if (resume) return chatOf(resume) && resumeChat(chatOf(resume));
    const show = find('data-show-chat');
    if (show) return chatOf(show) && resumeChat(chatOf(show));
    const more = target.closest('[data-chat-menu]');
    if (more) return chatOf(more.dataset.chatMenu) && chatMenu(chatOf(more.dataset.chatMenu), more);
    const restore = find('data-restore');
    if (restore) {
      closeHistory();
      return send({ type: 'restoreWorkspace', path: restore });
    }
    const open = find('data-open-workspace');
    if (open) {
      closeHistory();
      return selectWorkspace(open);
    }
  });
  document.body.append(root);
  renderHistory();
  root.querySelector('.hist-search').focus();
}

function closeHistory() {
  historyView?.root.remove();
  historyView = null;
}

function renderHistory() {
  if (!historyView) return;
  const { root, scope, path, query } = historyView;
  root.querySelector('[data-scope="project"]').textContent = workspaceOf(path)?.name || folderName(path) || 'This project';
  for (const button of root.querySelectorAll('[data-scope]')) button.classList.toggle('on', button.dataset.scope === scope);
  const matches = history.filter(chat =>
    (scope === 'all' || same(chat.workspace, path)) &&
    (!query || [chat.title, chat.agent, chat.accountName, chat.workspace].some(text => String(text).toLowerCase().includes(query))));
  // Projects in order of their latest chat; the history arrives newest first.
  const groups = new Map();
  for (const chat of matches) {
    const key = chat.workspace.toLowerCase();
    if (!groups.has(key)) groups.set(key, { path: chat.workspace, chats: [] });
    groups.get(key).chats.push(chat);
  }
  const list = root.querySelector('.hist-list');
  if (!matches.length) {
    list.innerHTML = `<p class="tip">${query ? 'No chats match.' : scope === 'all' ? 'No chats yet. Start an agent and it shows up here.' : 'No chats in this project yet.'}</p>`;
    return;
  }
  list.innerHTML = [...groups.values()].map(group => {
    const workspace = workspaceOf(group.path);
    const current = same(group.path, state.workspace);
    const action = !workspace
      ? `<button class="text-btn" data-restore="${esc(group.path)}" title="Put this project back in the sidebar">Add to sidebar</button>`
      : current ? '<span class="hist-current">current</span>'
        : `<button class="text-btn" data-open-workspace="${esc(group.path)}">Open project</button>`;
    const head = scope === 'all'
      ? `<div class="hist-project"><span class="ws-icon">${icon(workspace?.unity ? 'cube' : 'folder', 15)}</span>` +
        `<span class="hist-project-name">${esc(workspace?.name || folderName(group.path))}</span>` +
        `<span class="hist-project-path ellipsis" title="${esc(group.path)}">${esc(group.path)}</span>` +
        `<span class="count">${group.chats.length}</span>${action}</div>`
      : '';
    return `<div class="hist-group">${head}${group.chats.map(chatRow).join('')}</div>`;
  }).join('');
}

// Escape closes an open menu or dialog first, and the history only after that.
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && historyView && !openMenu && !document.querySelector('.modal:not(.history)')) closeHistory();
}, true);

// ---------- phone remote: what the phone sees ----------

// The phone app talks to Remote.cs, which asks this page for everything that comes from the panes
// (docs/phone-remote/REMOTE_API.md). Requests carry a requestId and always get a remoteReply.
const PALETTE = Remote.palette(THEME);
const NOT_OPEN = "That agent isn't open on the PC any more.";
const HAS_ENDED = 'That agent has ended. Resume it or start a new chat.';
const BUSY = 'Agent Arcade is busy, try again.';
// REMOTE_API.md's sentences for dialogChanged and dialogOpen (Remote.cs has the same ones for the fixtures).
const DIALOG_CHANGED = 'The question on your PC changed, so nothing was sent. Check it and answer again.';
const DIALOG_OPEN = 'The agent is asking a question on your PC, so nothing was typed. Answer it, or press Esc first.';
const DIALOG_OPEN_TYPED = "The agent started asking a question on your PC, so Enter wasn't pressed.";
// The C# side answers 503 after 10 s, counted from a little before the page gets the request; typing that
// can't finish well before then could be typed twice when the phone tries again.
const JOB_WAIT_MS = 8000;
// A paste from the phone can't carry escape sequences; special keys go through remoteKeys.
const CONTROL = /[\x00-\x08\x0b\x0c\x0e-\x1f\x7f-\x9f]/g;

const remote = {
  phones: 0,
  sent: new Map(), // pane id -> the summary JSON the C# side last got
  known: new Set(), // panes the C# side has a summary of, so it hears when they close
  closed: new Set(),
  soon: 0,
  watches: new Map(), // pane id -> { lines, seq, at, timer, force }
  jobs: new Set(), // typing waiting or running: { device, at, cancelled }
  removed: new Set(), // phones removed on the PC: their requests that still arrive are refused
  pairWaiting: false, // a phone waits for Allow or Deny in the Phone dialog
  info: null, // the newest remoteInfo: the Phone tab's facts and keep-awake
};

const panes = () => [...sessions.values()].sort((a, b) => a.order - b.order);
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// The last `count` lines of the pane down to its last drawn row, and the cursor's index in them.
function screenLines(session, count) {
  const buffer = session.term.buffer.active;
  const cursor = buffer.baseY + buffer.cursorY;
  const text = y => buffer.getLine(y)?.translateToString(true) || '';
  let end = buffer.length;
  while (end > Math.max(buffer.baseY, cursor + 1) && !text(end - 1).trim()) end--;
  const start = Math.max(0, end - count);
  const lines = [];
  for (let y = start; y < end; y++) lines.push(text(y));
  return { lines, cursor: cursor - start };
}

function screenParts(session) {
  const { lines, cursor } = screenLines(session, 120);
  // The approval check looks where attentionText looks: from 12 rows above the cursor.
  return Remote.parts(lines, cursor - 12);
}

// The pane shows choices or a menu at its bottom.
const picking = session => Remote.picking(screenLines(session, 20).lines);

// The bottom of the pane as text, to see whether it changed after a key (Remote.dialogKeys).
const screenText = session => screenLines(session, 40).lines.join('\n');

function paneStatus(session, screen) {
  if (!session.running) return 'ended';
  if (screen.asking) return 'needsYou';
  if (session.attention) return session.attentionKind === 'needsYou' ? 'needsYou' : 'done';
  const busy = session.progress?.state === 'busy' || session.progress?.state === 'percent';
  const recent = session.workedAt && Date.now() - session.workedAt < QUIET_MS;
  // The agent worked long enough for the done check, which fires after QUIET_MS: stay working until then.
  // workedAt leaves out typing echo and resize repaints, so those can't make a quiet pane look busy.
  const settling = session.armed && session.busyStart && session.workedAt - session.busyStart >= MIN_WORK_MS &&
    Date.now() - session.workedAt < QUIET_MS + 1500;
  return screen.activity || busy || recent || settling ? 'working' : 'idle';
}

function statusMessage(session, status) {
  if (status === 'ended') return endMessage(session);
  if (status === 'needsYou') return (session.attentionKind === 'needsYou' && session.attentionMessage) || `${agentName(session)} needs your OK to continue.`;
  if (status === 'done') return session.attentionMessage || `${agentName(session)} is done and waiting for you.`;
  return null;
}

// The Session object of REMOTE_API.md.
function remoteSummary(session) {
  const account = accountOf(session.key);
  const [keyAgent, keyAccount] = session.key.split('|');
  // An ended pane can outlive its account; the agent named in the key still knows the command.
  const agent = agentOf(account) || state.agents.find(item => item.name === keyAgent);
  const screen = screenParts(session);
  const status = paneStatus(session, screen);
  const asking = status === 'needsYou';
  noteDialog(session, screen, status);
  return {
    id: session.id,
    chat: session.chat || null,
    mode: session.mode,
    agent: agent?.name || keyAgent || null,
    command: agent?.command || '',
    account: session.key,
    accountName: account?.name || keyAccount || null,
    workspace: session.workspace,
    title: titleOf(session),
    status,
    message: statusMessage(session, status),
    question: asking ? screen.question : null,
    choices: asking ? screen.choices : [],
    activity: session.running ? screen.activity : null,
    plan: screen.plan,
    progress: session.running ? session.progress : null,
    preview: asking ? [] : screen.preview,
    startedAt: session.startedAt,
    turnStartedAt: session.turnStartedAt,
    lastOutputAt: session.lastOutputAt,
    endedAt: session.endedAt,
    exitCode: session.exitCode,
    error: session.error,
    seenOnPc: seenOnPc(session),
    cols: session.term.cols,
    rows: session.term.rows,
  };
}

// The dialog the pane shows and the screen seq at which the PC first saw it (Remote.trackDialog), for expect. Noted
// before every Session or Screen the phones get, so a phone whose screen showed a dialog never gets a false dialogChanged.
function noteDialog(session, screen, status) {
  session.asked = Remote.trackDialog(session.asked, status === 'needsYou' ? Remote.dialogKey(screen) : '', session.seq);
}

function lookAtDialog(session) {
  const screen = screenParts(session);
  const status = paneStatus(session, screen);
  noteDialog(session, screen, status);
  return { screen, status };
}

function safeSummary(session) {
  try {
    return remoteSummary(session);
  } catch {
    return null; // the pane is being closed
  }
}

// While a phone is connected: the summaries that changed since the last send, and the panes that closed.
function pushSessions() {
  if (!remote.phones) return;
  const changed = [];
  for (const session of panes()) {
    const summary = safeSummary(session);
    if (!summary) continue;
    const json = JSON.stringify(summary);
    if (remote.sent.get(session.id) === json) continue;
    remote.sent.set(session.id, json);
    remote.known.add(session.id);
    changed.push(summary);
  }
  const closed = [...remote.closed];
  remote.closed.clear();
  if (changed.length || closed.length) send({ type: 'remoteSessions', sessions: changed, closed });
}
setInterval(pushSessions, 500);

// A pane started, ended, closed, got a title or turned amber: the phone hears it now, not at the next tick.
function remoteSoon() {
  if (!remote.phones || remote.soon) return;
  remote.soon = setTimeout(() => {
    remote.soon = 0;
    pushSessions();
  });
}

function paneClosed(id) {
  clearTimeout(remote.watches.get(id)?.timer);
  remote.watches.delete(id);
  remote.sent.delete(id);
  if (remote.known.delete(id)) remote.closed.add(id);
  remoteSoon();
}

// Sent whether or not Windows notifications are on; the C# side drops it when no phone is connected.
function remoteAttention(session, status, message, question = null) {
  send({
    type: 'remoteAttention',
    attention: { id: session.id, status, title: titleOf(session), message, question, seenOnPc: seenOnPc(session), at: Date.now() },
  });
}

function phonesChanged(message) {
  const phones = Math.max(0, Math.floor(Number(message.phones)) || 0);
  // The first phone after none: every summary goes again.
  if (!remote.phones && phones) remote.sent.clear();
  remote.phones = phones;
  showPhones();
  if (message.requestId) remoteRequest(message, () => remoteOk({ sessions: panes().map(safeSummary).filter(Boolean) }));
  pushSessions();
}

// ---------- phone remote: screens ----------

const clampLines = lines => Math.min(2000, Math.max(1, Math.floor(Number(lines)) || 200));

function screenOf(session, lines) {
  return { id: session.id, seq: session.seq, ...Remote.serializeScreen(session.term, lines, PALETTE) };
}

function screenChanged(session) {
  session.seq++;
  if (remote.watches.has(session.id)) queueScreen(session.id);
}

// watches: every pane a phone watches, with the most lines any phone wants; refresh: send these now.
function watchChanged(message) {
  const wanted = new Map();
  for (const watch of Array.isArray(message.watches) ? message.watches : []) {
    if (typeof watch?.id === 'string') wanted.set(watch.id, clampLines(watch.lines));
  }
  for (const [id, watch] of remote.watches) {
    if (wanted.has(id)) continue;
    clearTimeout(watch.timer);
    remote.watches.delete(id);
  }
  const now = new Set((Array.isArray(message.refresh) ? message.refresh : []).filter(id => typeof id === 'string'));
  for (const [id, lines] of wanted) {
    const watch = remote.watches.get(id);
    if (!watch || lines > watch.lines) now.add(id);
    if (watch) watch.lines = lines;
    else remote.watches.set(id, { lines, seq: -1, at: 0, timer: 0, force: false });
  }
  for (const id of now) queueScreen(id, true);
}

// At most 4 screens a second per pane, and only when it changed (or the C# side asked for it). A long
// screen costs tens of milliseconds to build, so one of more than 400 lines goes at most once a second.
function queueScreen(id, force = false) {
  const watch = remote.watches.get(id);
  if (!watch || !sessions.has(id)) return;
  watch.force ||= force;
  if (watch.timer) return;
  const delay = watch.at + (watch.lines > 400 ? 1000 : 250) - Date.now();
  if (delay <= 0) sendScreen(id);
  else watch.timer = setTimeout(() => sendScreen(id), delay);
}

function sendScreen(id) {
  const watch = remote.watches.get(id);
  const session = sessions.get(id);
  if (!watch || !session) return;
  watch.timer = 0;
  if (!watch.force && watch.seq === session.seq) return;
  watch.force = false;
  watch.seq = session.seq;
  watch.at = Date.now();
  try {
    lookAtDialog(session);
    send({ type: 'remoteScreen', screen: screenOf(session, watch.lines) });
  } catch { /* the pane is being closed */ }
}

// ---------- phone remote: requests ----------

const remoteOk = (body = { ok: true }, status = 200) => ({ status, body });
// code: from REMOTE_API.md's error table (Errors); the C# side turns an error without a known code into 500 internal.
const remoteRefuse = (status, error, code, body) => ({ status, error, code, body });

async function remoteRequest(message, handler) {
  let result;
  try {
    result = typeof message.device === 'string' && remote.removed.has(message.device)
      ? remoteRefuse(401, "This phone isn't paired any more. Pair it again on your PC.", 'notPaired')
      : await handler(message);
  } catch {
    result = remoteRefuse(500, 'Something went wrong on the PC.', 'internal');
  }
  const reply = { type: 'remoteReply', requestId: message.requestId, status: result.status };
  if (result.body) reply.body = result.body;
  if (result.error) reply.error = result.error;
  if (result.code) reply.code = result.code;
  send(reply);
}

// For messages without a requestId: a failure must not reach the page's message handler.
function quietly(run) {
  try {
    run();
  } catch { /* the next message of the same kind sets it right */ }
}

// Text and keys for one pane go one request at a time, in the order they came, after the agent was started.
// A job is never typed once its phone was removed or Phone remote went off (remoteCancel), or when it waited
// so long that the C# side already answered 503: the phone may have sent it again.
// duration: how long the job types (keys 40 ms apart, Enter 150 ms after a paste); it must end in time too.
function inOrder(session, message, job, duration = 0) {
  const ticket = { device: typeof message.device === 'string' ? message.device : null, at: Date.now(), cancelled: false };
  remote.jobs.add(ticket);
  const run = (session.remoteQueue || session.begun).then(() => {
    if (ticket.cancelled || Date.now() - ticket.at + duration > JOB_WAIT_MS) return remoteRefuse(503, BUSY, 'busy');
    if (sessions.get(session.id) !== session) return remoteRefuse(404, NOT_OPEN, 'sessionNotFound');
    if (!session.running) return remoteRefuse(409, HAS_ENDED, 'sessionEnded');
    return job(ticket);
  }).finally(() => remote.jobs.delete(ticket));
  session.remoteQueue = run.catch(() => {});
  return run;
}

// device: the phone that was removed, or null when Phone remote stopped. No reply: the C# side answered already.
function cancelJobs(message) {
  const device = typeof message.device === 'string' ? message.device : null;
  if (device) remote.removed.add(device);
  for (const job of remote.jobs) {
    if (device === null || job.device === device) job.cancelled = true;
  }
}

// Why an agent can't start right now, or ''.
function notReady(agent) {
  if (!agent) return "That agent isn't in the agent list on the PC any more.";
  if (agent.installed) return '';
  return agent.checking ? `Agent Arcade is still checking ${agent.name}. Try again in a moment.` : `${agent.name} isn't installed on the PC.`;
}

function remoteGetScreen(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN, 'sessionNotFound');
  lookAtDialog(session);
  return remoteOk(screenOf(session, clampLines(message.lines)));
}

// expect (REMOTE_API.md): the phone acted on what it saw. Checked when the request's turn comes, right before the first
// key or character; a refusal when the pane shows something else now, else null. 1.0 phones send none.
function expectProblem(session, expect) {
  if (!expect || typeof expect !== 'object') return null;
  const { screen, status } = lookAtDialog(session);
  const asking = status === 'needsYou';
  const pane = { asking, question: asking ? screen.question : null, dialogSeq: session.asked?.seq, seq: session.seq };
  return Remote.expectRefused(expect, pane) ? remoteRefuse(409, DIALOG_CHANGED, 'dialogChanged') : null;
}

// The pane's Session status would be needsYou now.
const asksNow = session => lookAtDialog(session).status === 'needsYou';

// Typed like a paste at the PC, so titles, amber and "done" keep working through term.onData.
function remoteInput(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN, 'sessionNotFound');
  if (!session.running) return remoteRefuse(409, HAS_ENDED, 'sessionEnded');
  const text = typeof message.text === 'string' ? message.text.replace(CONTROL, '') : '';
  return inOrder(session, message, async ticket => {
    const changed = expectProblem(session, message.expect);
    if (changed) return changed;
    // Never Enter on a dialog, for any phone: a message to an asking pane waits until it stops asking (the phone
    // presses Esc first). Text for a dialog's own field comes with submit false, then keys ["enter"].
    if (message.submit === true && asksNow(session)) return remoteRefuse(409, DIALOG_OPEN, 'dialogOpen');
    if (text) {
      // Typed on the phone, so it may name the chat (trackPrompt) although it arrives as a paste.
      session.phoneTyping = true;
      try { session.term.paste(text); } finally { session.phoneTyping = false; }
    }
    if (message.submit !== true) return remoteOk();
    await wait(150);
    if (ticket.cancelled) return remoteRefuse(503, BUSY, 'busy');
    if (!session.running) return remoteRefuse(409, HAS_ENDED, 'sessionEnded');
    // It started asking in those 150 ms: the text is there, but Enter would answer the dialog.
    if (asksNow(session)) return remoteRefuse(409, DIALOG_OPEN_TYPED, 'dialogOpen', { typed: true });
    session.term.input('\r');
    return remoteOk();
  }, message.submit === true ? 150 : 0);
}

function remoteKeys(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN, 'sessionNotFound');
  if (!session.running) return remoteRefuse(409, HAS_ENDED, 'sessionEnded');
  const keys = Array.isArray(message.keys) ? message.keys : [];
  if (!keys.length || keys.length > 32) return remoteRefuse(400, 'Send between 1 and 32 keys.', 'badRequest');
  const unknown = keys.findIndex(name => Remote.keySequence(name, false) === null);
  if (unknown >= 0) return remoteRefuse(400, `Unknown key: ${Remote.cut(String(keys[unknown]).replace(CONTROL, ''), 40)}`, 'unknownKey');
  return inOrder(session, message, async ticket => {
    // Only the first key is checked: the rest are the user's own sequence, such as down then enter.
    const changed = expectProblem(session, message.expect);
    if (changed) return changed;
    for (const [i, name] of keys.entries()) {
      if (i) await wait(40);
      if (ticket.cancelled) return remoteRefuse(503, BUSY, 'busy');
      if (!session.running) return remoteRefuse(409, HAS_ENDED, 'sessionEnded');
      session.term.input(Remote.keySequence(name, session.term.modes.applicationCursorKeysMode));
    }
    return remoteOk();
  }, (keys.length - 1) * 40);
}

function remoteStart(message) {
  const account = accountOf(message.account);
  const agent = agentOf(account);
  if (!account || !agent) return remoteRefuse(404, "That account isn't on the PC any more.", 'accountNotFound');
  const workspace = workspaceOf(message.workspace);
  if (!workspace) return remoteRefuse(404, "That project isn't in the sidebar on the PC any more.", 'workspaceNotFound');
  if (!workspace.exists) return remoteRefuse(409, `The ${workspace.name} folder is missing on the PC.`, 'folderMissing');
  const problem = notReady(agent);
  if (problem) return remoteRefuse(409, problem, 'agentNotReady');
  const session = openSession(account.key, 'start', null, { workspace: workspace.path, remote: true });
  return session ? remoteOk({ id: session.id }, 201) : remoteRefuse(409, `${agent.name} couldn't start on the PC.`, 'startFailed');
}

function remoteResume(message) {
  const chat = chatOf(message.chat);
  if (!chat) return remoteRefuse(404, 'That chat is no longer in the history.', 'chatNotFound');
  const holders = [...sessions.values()].filter(session => session.chat === chat.id || (session.running && session.resumeOf === chat.id));
  const running = holders.find(session => session.running);
  if (running) return remoteRefuse(409, 'That chat is already open.', 'chatOpen', { id: running.id });
  const open = holders[0];
  const problem = resumeProblem(chat);
  // An agent that isn't installed is agentNotReady, like at start; the rest is about this chat.
  if (problem) return remoteRefuse(409, problem, problem.endsWith(" isn't installed.") ? 'agentNotReady' : 'cantResume');
  if (!workspaceOf(chat.workspace)) return remoteRefuse(409, `Add ${folderName(chat.workspace)} back to the sidebar on the PC first.`, 'cantResume');
  const agent = state.agents.find(item => item.name === chat.agent);
  const waiting = notReady(agent);
  if (waiting) return remoteRefuse(409, waiting, 'agentNotReady');
  // A pane where this chat ended makes way for it, like its Resume button on the PC. If the PC had that pane
  // maximized, the new one is.
  const order = open?.order;
  const wasMaxed = !!open && maxed === open.id;
  if (open) closeSession(open, true);
  const session = openSession(chat.account, 'resume', chat, { remote: true });
  if (!session) return remoteRefuse(409, `${agent.name} couldn't start on the PC.`, 'startFailed');
  if (order) session.order = order;
  if (wasMaxed) maxed = session.id;
  if (order || wasMaxed) renderPanes();
  return remoteOk({ id: session.id }, 201);
}

function remoteStop(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN, 'sessionNotFound');
  if (!session.running) return remoteRefuse(409, HAS_ENDED, 'sessionEnded');
  // The C# side stops the agent and sends no exit for it.
  send({ type: 'close', session: session.id });
  endSession(session, { phone: true });
  return remoteOk();
}

async function remoteClose(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN, 'sessionNotFound');
  if (session.running) return remoteRefuse(409, 'Stop the agent first.', 'sessionRunning');
  await closeSession(session, true);
  return remoteOk();
}

function remoteRename(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN, 'sessionNotFound');
  if (!isChat(session)) return remoteRefuse(400, 'Only chats can be renamed.', 'badRequest');
  setCustomTitle(session, typeof message.title === 'string' ? message.title.replace(CONTROL, ' ') : '');
  return remoteOk();
}

// ---------- phone remote: Settings → Phone ----------

// The Phone tab of the open Settings window is what REMOTE_API.md calls the Phone dialog: a pairing code lives only
// while it shows, and a phone's pairing question waits there for Allow or Deny. phoneView is set while it shows.
let phoneView = null;

// The sidebar's phone button: a green badge while a phone is connected, a red diamond while one waits for Allow.
function showPhones() {
  const button = $('#phone');
  const text = remote.pairWaiting ? 'Phone remote: a phone wants to pair with this PC'
    : remote.phones ? `Phone remote: ${remote.phones} phone${remote.phones === 1 ? '' : 's'} connected` : 'Phone remote: use Agent Arcade from your phone';
  button.innerHTML = icon('phone', 16) + (remote.pairWaiting ? '<span class="badge needs"></span>' : remote.phones ? '<span class="badge on"></span>' : '');
  button.title = text;
  button.setAttribute('aria-label', text);
}

// The Phone tab shows in panel. Opening it makes no pairing code: Pair a phone does.
function openPhoneTab(panel) {
  closePhone();
  phoneView = { root: panel, info: remote.info, asked: null, timer: setInterval(showValidity, 10000) };
  renderPhone();
  send({ type: 'remoteDialog' });
}

// Leaving the tab or closing Settings ends the pairing code it showed. A phone that waits for Allow keeps waiting; its
// question is there when the tab opens again.
function closePhone() {
  if (!phoneView) return;
  clearInterval(phoneView.timer);
  phoneView = null;
  send({ type: 'remoteDialogClosed' });
}

function phoneChange(event) {
  if (event.target.name === 'remote') send({ type: 'remoteEnable', on: event.target.checked });
}

function phoneClick(event) {
  const target = event.target;
  if (target.closest('[data-pair]')) return send({ type: 'remotePair' });
  if (target.closest('[data-new-code]')) return send({ type: 'remoteNewCode' });
  if (target.closest('[data-app-link]') && phoneView?.info?.app?.url) return send({ type: 'openUrl', url: phoneView.info.app.url });
  // Answers the question the tab shows; the C# side ignores an answer for any other.
  const answer = target.closest('[data-pair-answer]');
  const pairing = phoneView?.info?.pairing;
  if (answer && !answer.disabled && pairing) return send({ type: 'remotePairAnswer', id: pairing.id, allow: answer.dataset.pairAnswer === 'allow' });
  const copy = target.closest('[data-link]');
  if (copy && copy.dataset.link) {
    // Kept out of Windows clipboard history and the cloud clipboard (MainForm.SecretData).
    send({ type: 'copySecret', text: copy.dataset.link });
    return toast('Pairing link copied. It works once, for 10 minutes, and stays out of clipboard history.');
  }
  const remove = target.closest('[data-remove-device]');
  const device = remove && phoneView?.info?.devices?.find(item => item.id === remove.dataset.removeDevice);
  if (device) removePhone(device);
}

// remoteInfo: the Phone tab's facts, and keep-awake for General. Kept, so a tab that opens shows them at once.
function phoneInfo(message) {
  remote.info = message;
  // The phone button shows a waiting pairing also while the tab is closed.
  const waiting = !!message.pairing;
  if (waiting !== remote.pairWaiting) {
    remote.pairWaiting = waiting;
    showPhones();
  }
  if (settingsView?.tab === 'general') renderAwake();
  if (!phoneView) return;
  phoneView.info = message;
  renderPhone();
}

// A phone waits for Allow. The PC never opens Settings for it (a phone doesn't move the PC's view): a notice, the
// phone button's badge, and the question in the Phone tab.
function pairAsked(message) {
  if (phoneView) return;
  toast(`${String(message.name || 'A phone')} wants to pair with this PC. Open Settings → Phone (the phone at the bottom of the sidebar) to allow or deny it.`);
}

function codeState(info) {
  if (info.code === 'used') return `The last code was used by ${info.pairedName || 'a phone'}.`;
  if (info.code === 'expired') return 'The last code has expired.';
  if (info.code === 'cancelled') return 'Too many wrong codes cancelled the last code.';
  if (info.code !== 'valid') return '';
  const minutes = Math.ceil((Number(info.expiresAt) - Date.now()) / 60000);
  if (!(minutes > 0)) return 'This code has expired.';
  return `Valid for ${minutes} minute${minutes === 1 ? '' : 's'}`;
}

function showValidity() {
  const line = phoneView?.root.querySelector('.phone-valid');
  if (line) line.textContent = codeState(phoneView.info);
}

function seenText(device) {
  if (device.connected) return 'connected';
  const time = Number(device.lastSeen) || Number(device.paired);
  return time ? 'last seen ' + when(time).replace(/^(Today|Yesterday)/, day => day.toLowerCase()) : 'not seen yet';
}

// Everything in info comes from the C# side, and device names from the phones themselves: all of it is escaped.
function renderPhone() {
  if (!phoneView) return;
  const info = phoneView.info;
  const body = phoneView.root.querySelector('.phone-body');
  if (!info) {
    body.innerHTML = '<p class="muted">Checking Phone remote…</p>';
    return;
  }
  const devices = Array.isArray(info.devices) ? info.devices : [];
  const addresses = Array.isArray(info.addresses) ? info.addresses : [];
  let html =
    `<label class="switch"><input type="checkbox" name="remote" data-key="remote"${info.enabled ? ' checked' : ''}>` +
    '<span class="switch-track"></span><span>Let my phone watch and drive these agents</span></label>';
  if (info.warning) html += `<p class="phone-warning">${esc(info.warning)}</p>`;
  if (info.error) html += `<p class="phone-error">${esc(info.error)}</p>`;
  html += appSection(info.app);
  if (info.enabled && info.running) {
    html += `<div class="phone-section"><span class="phone-label">2. Pair your phone</span>${pairSection(info)}</div>`;
    if (addresses.length) {
      const tailscale = addresses.some(isTailscale);
      html += '<div class="phone-section"><span class="phone-label">Listening on</span>' +
        `<span class="phone-addresses">${addresses.map(address =>
          esc(address) + `<span class="muted"> · ${isTailscale(address) ? 'Tailscale, also away from home' : 'this network'}</span>`).join('<br>')}</span>` +
        (tailscale ? '' : '<span class="muted">Away from home: install Tailscale on this PC and your phone (free for personal use), ' +
          'sign in to both with the same account, then pair again.</span>') +
        '<span class="muted">New address or just installed Tailscale? Pair the phone again.</span></div>';
    }
  } else if (info.enabled && !info.error) {
    html += '<p class="muted">Starting…</p>';
  }
  html += awakeNote(info.awake);
  if (devices.length) {
    html += '<div class="phone-section"><span class="phone-label">Paired phones</span>' + devices.map(device =>
      `<div class="device-row"><span class="dot ${device.connected ? 'on' : 'off'}"></span>` +
      `<span class="hist-text"><span class="device-name ellipsis">${esc(device.name)}</span>` +
      `<span class="hist-sub ellipsis">${esc(seenText(device) + (device.client ? ' · ' + device.client : ''))}</span></span>` +
      `<button class="ghost-btn small" data-remove-device="${esc(device.id)}" data-key="remove-${esc(device.id)}" aria-label="Remove ${esc(device.name)}">Remove</button></div>`).join('') + '</div>';
  }
  keepFocus(body, () => { body.innerHTML = html; });
}

function pngQr(value) {
  return /^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(value || '') ? value : '';
}

// Step 1: Agent Arcade Remote from the website's download page, by QR code or link.
function appSection(app) {
  const qr = pngQr(app?.qr);
  if (!qr || typeof app.url !== 'string') return '';
  return '<div class="phone-section"><span class="phone-label">1. Get Agent Arcade Remote (Android 8+)</span><div class="phone-pair">' +
    `<div class="qr-card app-qr"><img src="${esc(qr)}" alt="Download page of Agent Arcade Remote" draggable="false"></div>` +
    '<div class="phone-facts"><span class="muted">Scan this with your phone\'s camera to open the download page, and install the app from there.</span>' +
    `<button class="text-btn link app-link" data-app-link data-key="app-link">${esc(app.url.replace(/^https:\/\//, '').replace(/#.*$/, ''))}</button></div></div></div>`;
}

// Step 2: Pair a phone makes a code and shows its QR code; a phone that used it waits for Allow or Deny.
function pairSection(info) {
  if (info.pairing && Number.isSafeInteger(info.pairing.id)) return pairQuestion(info.pairing);
  const qr = pngQr(info.qr);
  if (info.code === 'valid' && Number(info.expiresAt) > Date.now() && qr && info.link) {
    return '<div class="phone-pair">' +
      `<div class="qr-card"><img src="${esc(qr)}" alt="Pairing code" draggable="false"></div>` +
      '<div class="phone-facts"><span class="phone-label">PC fingerprint</span>' +
      `<span class="fingerprint">${esc(info.fingerprint || '')}</span>` +
      `<span class="phone-valid">${esc(codeState(info))}</span>` +
      '<span class="muted">Scan the code with Agent Arcade Remote on your phone, and check that it shows the same fingerprint. ' +
      'This PC then asks you to allow the phone.</span>' +
      '<div class="phone-actions"><button class="ghost-btn small" data-new-code data-key="new-code">New code</button>' +
      `<button class="ghost-btn small" data-link="${esc(info.link)}" data-key="copy-link">Copy link</button></div></div></div>`;
  }
  const last = codeState(info);
  return '<div class="phone-facts">' + (last ? `<span class="muted">${esc(last)}</span>` : '') +
    '<span class="muted">Shows a QR code to scan with Agent Arcade Remote. It works once, for 10 minutes, and this PC asks you before it pairs the phone.</span>' +
    '<div class="phone-actions"><button class="ghost-btn small" data-pair data-key="pair">Pair a phone</button></div></div>';
}

// Allow works only a moment after the question shows, so a click meant for the button that was there can't pair a
// phone. Nothing is focused or preselected: Enter alone never pairs one.
const PAIR_ARM_MS = 800;

function pairQuestion(pairing) {
  if (phoneView.asked?.id !== pairing.id) {
    phoneView.asked = { id: pairing.id, at: Date.now() };
    setTimeout(renderPhone, PAIR_ARM_MS + 50);
  }
  const armed = Date.now() - phoneView.asked.at >= PAIR_ARM_MS;
  return '<div class="pair-ask"><span class="phone-label">A phone wants to pair with this PC</span>' +
    `<span class="device-name">${esc(pairing.name)}</span>` +
    (pairing.client ? `<span class="muted">${esc(pairing.client)}</span>` : '') +
    '<span class="muted">Allow it only if it is your own phone and you just scanned the code. It waits up to a minute.</span>' +
    '<div class="phone-actions"><button class="ghost-btn small" data-pair-answer="deny" data-key="pair-deny">Deny</button>' +
    `<button class="primary-btn small" data-pair-answer="allow" data-key="pair-allow"${armed ? '' : ' disabled'}>Allow</button></div></div>`;
}

// Tailscale hands out 100.64.0.0/10 addresses.
function isTailscale(host) {
  const [a, b] = String(host).split(':')[0].split('.').map(Number);
  return a === 100 && b >= 64 && b <= 127;
}

// Whether Windows may sleep while the agents work, or while the phone wants to reach the PC (Settings → General;
// the Phone tab says what it is now). [value, short label, today's full wording]
const AWAKE_MODES = [
  ['agents', 'While agents run', 'While agents run'],
  ['remote', 'Also for Phone', 'While agents run or Phone remote is on'],
  ['off', 'Off', 'Never, Windows decides'],
];

function awakeState(awake) {
  return awake.on
    ? "Windows won't go to sleep on its own now. The screen still turns off."
    : awake.mode === 'off' ? 'Windows sleeps as its power settings say, also while agents work.'
      : 'Windows may go to sleep now: no agent is running.';
}

function awakeWarnings(awake) {
  let html = '';
  if (awake.mode !== 'off' && awake.onBattery) {
    html += '<span class="phone-warning">On battery, Windows still goes to sleep a few minutes after its sleep time. Plug the PC in.</span>';
  }
  if (awake.mode !== 'off' && awake.lidSleeps) {
    html += '<span class="muted">Closing the lid still puts it to sleep. To keep it running with the lid closed, set ' +
      '"When I close the lid" to "Do nothing" for Plugged in, under Settings > System > Power & battery > Lid & power button controls.</span>';
  }
  return html;
}

// The Phone tab's note: what keeps the PC awake now, and where to change it.
function awakeNote(awake) {
  if (!awake) return '';
  const mode = AWAKE_MODES.find(([value]) => value === awake.mode) || AWAKE_MODES[0];
  return '<div class="phone-section"><span class="phone-label">Keep this PC awake</span>' +
    `<span class="muted">${esc(mode[2])}. ${esc(awakeState(awake))} ` +
    '<button class="text-btn link" type="button" data-settings-tab="general" data-key="awake-general">Change it in General</button></span>' +
    awakeWarnings(awake) + '</div>';
}

// Settings → General: the three choices and what they mean now.
function renderAwake() {
  const box = settingsView?.root.querySelector('#awake-field');
  if (!box) return;
  const awake = remote.info?.awake;
  keepFocus(box, () => {
    box.querySelector('.field-help').innerHTML = awake ? esc(awakeState(awake)) + awakeWarnings(awake) : 'Checking…';
    box.querySelector('.field-control').innerHTML = `<div class="seg" role="group" aria-label="Keep this PC awake">${AWAKE_MODES.map(([value, label, full]) =>
      `<button type="button" data-awake="${value}" data-key="awake-${value}" title="${esc(full)}" aria-pressed="${awake?.mode === value}"${awake ? '' : ' disabled'}>${esc(label)}</button>`).join('')}</div>`;
  });
}

async function removePhone(device) {
  const ok = await modal({
    title: `Remove ${device.name}?`,
    body: '<p>It is disconnected at once and has to pair again.</p>',
    confirm: 'Remove',
    danger: true,
  });
  if (ok) send({ type: 'remoteRemove', device: device.id });
}

// ---------- Settings ----------

// One window for what isn't a project: General, Agents and accounts, Phone, Shared instructions, MCP servers and About.
// A dialog inside the page: Esc closes it, Tab stays inside it, and focus goes back where it was. Only the PC opens
// it; a phone's actions never do.
const SETTINGS_TABS = [
  ['general', 'General'], ['agents', 'Agents and accounts'], ['phone', 'Phone'],
  ['instructions', 'Shared instructions'], ['mcp', 'MCP servers'], ['about', 'About'],
];
let settingsView = null; // { root, tab, opener }

function openSettings(tab = 'general') {
  closeMenu();
  closeHistory();
  if (!SETTINGS_TABS.some(([id]) => id === tab)) tab = 'general';
  if (settingsView) return showSettingsTab(tab, true);
  const root = document.createElement('div');
  root.className = 'modal-backdrop settings-backdrop';
  root.innerHTML =
    '<div class="settings-window" role="dialog" aria-modal="true" aria-labelledby="settings-title">' +
    '<div class="settings-side"><h2 id="settings-title">Settings</h2>' +
    '<div class="settings-tabs" role="tablist" aria-orientation="vertical" aria-labelledby="settings-title">' +
    SETTINGS_TABS.map(([id, label]) =>
      `<button class="settings-tab" type="button" role="tab" id="settings-tab-${id}" data-settings-tab="${id}" aria-controls="settings-panel" aria-selected="false" tabindex="-1">${esc(label)}</button>`).join('') +
    '</div></div>' +
    '<div class="settings-panel" id="settings-panel" role="tabpanel" tabindex="-1"></div>' +
    `<button class="icon-btn settings-close" type="button" data-settings-close title="Close (Esc)" aria-label="Close settings">${icon('close', 16)}</button>` +
    '</div>';
  settingsView = { root, tab: null, opener: document.activeElement };
  root.addEventListener('mousedown', event => {
    if (event.target === root) closeSettings();
  });
  root.addEventListener('click', settingsClick);
  root.addEventListener('change', settingsChange);
  root.addEventListener('input', settingsInput);
  root.addEventListener('keydown', settingsTabKeys);
  document.body.append(root);
  // The newest Phone remote and keep-awake facts (remoteInfo). The Phone tab asks for them itself.
  if (tab !== 'phone') send({ type: 'remoteDialog' });
  showSettingsTab(tab, true);
}

function closeSettings() {
  if (!settingsView) return;
  closePhone();
  const { root, opener } = settingsView;
  settingsView = null;
  root.remove();
  // Focus goes back where it was, when that is still on screen.
  if (opener?.isConnected && opener !== document.body && opener.getClientRects().length) opener.focus();
}

// focus: the tab itself gets focus (opened, or picked with the keyboard).
function showSettingsTab(tab, focus = false) {
  if (!settingsView) return;
  if (settingsView.tab === 'phone' && tab !== 'phone') closePhone();
  const changed = settingsView.tab !== tab;
  settingsView.tab = tab;
  for (const button of settingsView.root.querySelectorAll('.settings-tab')) {
    const on = button.dataset.settingsTab === tab;
    button.setAttribute('aria-selected', String(on));
    button.tabIndex = on ? 0 : -1;
  }
  const panel = settingsView.root.querySelector('.settings-panel');
  panel.setAttribute('aria-labelledby', `settings-tab-${tab}`);
  if (changed) {
    renderSettingsTab();
    panel.scrollTop = 0;
  }
  if (focus) settingsView.root.querySelector(`#settings-tab-${tab}`).focus();
}

// update: the facts behind the open tab changed; it is drawn again where it is, and the focused control keeps focus.
function renderSettingsTab(update = false) {
  if (!settingsView) return;
  const panel = settingsView.root.querySelector('.settings-panel');
  const tab = settingsView.tab;
  if (tab === 'phone') {
    if (!update) {
      panel.innerHTML = phoneTabHtml();
      openPhoneTab(panel);
    }
    return;
  }
  keepFocus(panel, () => {
    if (tab === 'general') panel.innerHTML = generalHtml();
    else if (tab === 'agents') panel.innerHTML = agentsHtml();
    else if (tab === 'instructions') panel.innerHTML = instructionsHtml();
    else if (tab === 'mcp') panel.innerHTML = mcpTabHtml();
    else if (tab === 'about') panel.innerHTML = aboutHtml();
  });
  if (tab === 'general') renderAwake();
  if (tab === 'about') mountMascot(panel.querySelector('.about-mascot'), 120);
  if (tab === 'mcp') {
    if (update) renderMcp();
    else refreshMcp();
  }
}

// The state changed (agents, accounts, sign-ins, running panes, the selected project): the open tab shows it.
function renderAgentsTab() {
  if (['agents', 'instructions', 'general'].includes(settingsView?.tab)) renderSettingsTab(true);
}

// Redraws part of a dialog without losing the keyboard's place: the control with the same data-key gets focus back.
// When it is gone, the dialog's panel takes focus, so it never falls out of the dialog (and never onto a new button).
function keepFocus(container, draw) {
  const active = document.activeElement;
  const key = container.contains(active) && active !== container ? active.dataset?.key || '' : null;
  draw();
  if (key === null) return;
  // Still inside: the focused control was kept, or the drawing focused one itself (McpView's Check again).
  const now = document.activeElement;
  if (now !== container && container.contains(now)) return;
  const again = key && container.querySelector(`[data-key="${CSS.escape(key)}"]`);
  if (again && !again.disabled) again.focus();
  else (container.closest('.settings-panel') || container).focus?.();
}

const field = (label, help, control, extra = '') =>
  `<div class="field"${extra}><div class="field-text"><span class="field-label">${label}</span><span class="field-help">${help}</span></div>` +
  `<div class="field-control">${control}</div></div>`;

const toggle = (name, on, label) =>
  `<button class="toggle" type="button" role="switch" data-toggle="${name}" data-key="toggle-${name}" aria-checked="${!!on}" aria-label="${esc(label)}"></button>`;

function generalHtml() {
  const folder = state.projectsFolder;
  return '<h3 class="settings-title">General</h3><p class="lead">How Agent Arcade looks and behaves on this PC.</p>' +
    field('Text size', `Every pane, the same size: <b class="size-value">${fontSize} px</b>. Ctrl+= and Ctrl+- in a pane change it too; Ctrl+0 goes back to 13.`,
      `<div class="seg" role="group" aria-label="Text size">${FONT_SIZES.map(([label, size]) =>
        `<button type="button" data-size="${size}" data-key="size-${label}" title="${size} px" aria-pressed="${fontSize === size}">${label}</button>`).join('')}</div>` +
      `<input type="range" class="size-range" min="9" max="24" step="1" value="${fontSize}" data-key="size-range" aria-label="Text size in pixels">`) +
    field('Notifications', 'A Windows notification when an agent is done or needs you, unless you are looking at it.',
      toggle('notify', notifyOn, 'Windows notifications')) +
    field('Keep this PC awake', '', '', ' id="awake-field"') +
    field('New projects go to', folder
      ? `<span class="path-text" title="${esc(folder)}">${esc(HomeData.shortPath(folder))}</span>` +
        (state.projectsFolderChosen ? 'You picked this folder.' : 'Next to your projects, until you pick a folder.')
      : 'Nowhere yet: add a project folder first, or pick a folder.',
      '<button class="ghost-btn small" type="button" data-projects-folder data-key="projects-folder">Change…</button>' +
      (state.projectsFolderChosen ? '<button class="ghost-btn small" type="button" data-projects-auto data-key="projects-auto" title="New projects go next to your projects again">Automatic</button>' : '')) +
    field('Check for updates', "Once a day, GitHub's releases API is asked for the newest version number. No identifier is sent and nothing is downloaded. " +
      '<button class="text-btn link" type="button" data-releases data-key="releases">All releases</button>',
      toggle('updates', appInfo?.updateCheck, 'Tell me when a new version is out')) +
    field('Mascot animations', 'The robot blinks, looks around and reacts to clicks. Windows "reduce motion" turns it off too.',
      toggle('mascot', appInfo?.mascotAnimations !== false, 'Mascot animations'));
}

// Today's agent list with versions, every account with its email, and the ways to add, install and sign in.
function agentsHtml() {
  const rows = state.agents.map(agent => {
    const version = agent.checking
      ? '<span class="muted">checking…</span>'
      : agent.installed
        ? `<span class="ver" title="${esc(agent.version)}">${esc(shortVersion(agent.version))}</span>`
        : `<button class="tag-btn" type="button" data-install="${esc(agent.name)}" data-key="install-${esc(agent.name)}">Install</button>`;
    const accounts = state.accounts.filter(account => account.agent === agent.name).map(account => {
      const running = runningFor(account.key);
      const [dot, said] = account.checking ? ['wait', 'Checking'] : account.signedIn === true ? ['on', 'Signed in']
        : account.signedIn === false ? ['off', 'Not signed in'] : ['idle', 'Check inside the agent'];
      return `<div class="acc-row${agent.installed ? '' : ' disabled'}"><span class="dot ${dot}" role="img" title="${said}" aria-label="${said}"></span>` +
        `<span class="acc-text"><span class="acc-top"><span class="acc-name">${esc(account.name)}</span>` +
        (account.plan ? `<span class="plan">${esc(account.plan)}</span>` : '') +
        (running ? `<span class="count live" title="Running">${running} running</span>` : '') +
        `</span><span class="acc-sub ellipsis">${esc(accountLine(account))}</span></span>` +
        `<button class="icon-btn small" type="button" data-account-menu="${esc(account.key)}" data-key="account-${esc(account.key)}" ` +
        `title="New session, sign in, install, Windows Terminal, remove" aria-label="More for ${esc(agent.name)} (${esc(account.name)})" aria-haspopup="menu">${icon('more', 15)}</button></div>`;
    }).join('');
    const add = agent.multi && agent.installed
      ? `<button class="ghost-btn small" type="button" data-add="${esc(agent.name)}" data-key="add-${esc(agent.name)}" title="Add another ${esc(agent.name)} account">${icon('plus', 13)}<span>Account</span></button>`
      : '';
    return `<tr><td><span class="agent-cell">${avatar(agent)}<span class="agent-name">${esc(agent.name)}</span></span></td>` +
      `<td>${version}</td><td class="acc-cell">${accounts}</td><td class="add-cell">${add}</td></tr>`;
  }).join('');
  return '<h3 class="settings-title">Agents and accounts</h3>' +
    '<p class="lead">Each agent signs in with its own official login. Agent Arcade never reads your tokens.</p>' +
    (rows
      ? '<table class="agents-table"><thead><tr><th scope="col">Agent</th><th scope="col">Version</th><th scope="col">Accounts</th>' +
        `<th scope="col"><span class="sr-only">Add an account</span></th></tr></thead><tbody>${rows}</tbody></table>`
      : '<p class="muted">Looking for your agents…</p>') +
    '<div class="settings-actions">' +
    '<button class="ghost-btn" type="button" data-edit-agents data-key="edit-agents">Edit agent list</button>' +
    '<button class="ghost-btn" type="button" data-add-account data-key="add-account">Add account…</button>' +
    `<button class="ghost-btn" type="button" data-refresh data-key="refresh">${icon('refresh', 14)}<span>Check accounts again</span></button></div>` +
    '<p class="note">An account\'s ⋯ starts a session, signs in (or switches the account it uses), opens it in Windows Terminal, installs or updates the agent, or removes the account.</p>';
}

function phoneTabHtml() {
  return '<h3 class="settings-title">Phone</h3><p class="lead">Watch and drive your agents from Agent Arcade Remote on Android.</p>' +
    '<div class="phone-body"></div>' +
    '<p class="phone-note">A paired phone can read your panes and type into your agents, like you can at this PC. ' +
    'Only pair your own phone, and allow it only right after you scanned the code yourself. If Windows asks, allow Agent Arcade on private networks. ' +
    'Set your Wi-Fi to Private only if it is your own home network; on shared or public Wi-Fi keep it Public and use Tailscale.</p>';
}

// The sync dialog's content: AGENTS.md and the files that point to it, for the selected project.
function instructionsHtml() {
  const workspace = workspaceOf(state.workspace);
  return '<h3 class="settings-title">Shared instructions</h3><p class="lead">Give every agent in a project the same instructions.</p>' +
    (workspace
      ? `<div class="settings-card"><p class="card-for">For <b>${esc(workspace.name)}</b> <span class="path-text">${esc(HomeData.shortPath(workspace.path))}</span></p>` +
        '<p>AGENTS.md (or CLAUDE.md, if that is all you have) stays the main file. Missing AGENTS.md, CLAUDE.md and GEMINI.md files are created and only point to it.</p>' +
        '<p class="muted">Existing files are never changed.</p>' +
        `<div class="settings-actions"><button class="primary-btn" type="button" data-sync data-key="sync"${workspace.exists ? '' : ' disabled'}>Create missing files</button></div></div>` +
        '<p class="note">For another project, pick it in the sidebar first.</p>'
      : '<p class="muted">Pick a project in the sidebar first.</p>');
}

// ---------- Settings → MCP servers ----------

// web/mcp.js draws the list (McpView) from the C# side's answer to mcpList: only each server's name, scope and
// status. Without mcp.js the tab says where the list will be; without an answer it says so and offers to ask again.
const MCP_WAIT_MS = 45000;
const mcp = { data: null, waiting: false, failed: false, timer: 0 };

function mcpTabHtml() {
  return '<h3 class="settings-title">MCP servers</h3>' +
    '<p class="lead">The MCP servers each agent has, and whether they answer right now, from each agent\'s own <span class="mono">mcp list</span> command.</p>' +
    '<div class="mcp-box" id="mcp-box"></div>';
}

function refreshMcp() {
  if (!window.McpView?.render) return renderMcp();
  clearTimeout(mcp.timer);
  mcp.waiting = true;
  mcp.failed = false;
  mcp.timer = setTimeout(() => {
    mcp.waiting = false;
    mcp.failed = true;
    renderMcp();
  }, MCP_WAIT_MS);
  send({ type: 'mcpList' });
  renderMcp();
}

function mcpArrived(message) {
  clearTimeout(mcp.timer);
  mcp.waiting = false;
  mcp.failed = false;
  mcp.data = message;
  renderMcp();
}

function renderMcp() {
  const box = settingsView?.tab === 'mcp' ? settingsView.root.querySelector('#mcp-box') : null;
  if (!box) return;
  keepFocus(box, () => {
    if (!window.McpView?.render) {
      box.innerHTML = '<div class="settings-card placeholder"><p>MCP servers will show here.</p>' +
        '<p class="muted">Only names and status are shown. Agent Arcade never reads a server\'s command line, keys or environment.</p></div>';
      return;
    }
    if (mcp.failed) {
      box.innerHTML = '<p class="muted">No answer came about the MCP servers.</p><button class="ghost-btn small" type="button" data-mcp-again data-key="mcp-again">Check again</button>';
      return;
    }
    // While a check runs, McpView shows it (Check again reads Checking…); the answer comes with mcpArrived.
    try {
      window.McpView.render(box, mcp.waiting ? null : mcp.data, refreshMcp);
    } catch {
      box.innerHTML = '<p class="phone-error">The MCP servers couldn\'t be shown.</p>' +
        '<button class="ghost-btn small" type="button" data-mcp-again data-key="mcp-again">Check again</button>';
    }
  });
}

// ---------- Settings → About ----------

function aboutHtml() {
  const version = appInfo?.version || '';
  const update = appInfo?.update;
  const links = [['website', 'Website'], ['github', 'GitHub'], ['changelog', 'Changelog']];
  return '<div class="about"><div class="about-mascot"></div>' +
    `<h3 class="settings-title">Agent Arcade${version ? ' ' + esc(version) : ''}</h3>` +
    '<p class="lead">Free for personal use · PolyForm Noncommercial · offered by 3Dmatters</p>' +
    (update ? `<p><button class="text-btn link" type="button" data-update-open data-key="update">Agent Arcade ${esc(update.version)} is available</button></p>` : '') +
    `<div class="about-links">${links.map(([key, label]) =>
      `<button class="ghost-btn small" type="button" data-link-to="${key}" data-key="link-${key}">${icon('external', 13)}<span>${label}</span></button>`).join('')}` +
    '<button class="ghost-btn small" type="button" data-releases data-key="releases">All releases</button></div>' +
    (window.Mascot ? '<p class="note">Click the robot. Then click it five times, fast.</p>' : '') + '</div>';
}

// The mascot (web/mascot.js) in slot; the app icon when that file is missing or fails. The button takes focus only
// when it is clicked, never through Tab, so it can't take the keyboard from a pane.
function mountMascot(slot, size) {
  if (!slot) return;
  if (window.Mascot?.mount) {
    try {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'mascot';
      button.tabIndex = -1;
      button.title = 'Agent Arcade';
      button.setAttribute('aria-label', 'Agent Arcade mascot');
      button.style.width = button.style.height = size + 'px';
      slot.replaceChildren(button);
      window.Mascot.mount(button, { size });
      if (appInfo) window.Mascot.setEnabled(appInfo.mascotAnimations !== false);
      tellMascot();
      return;
    } catch { /* the icon below instead */ }
  }
  slot.innerHTML = `<span class="mascot static" style="width:${size}px;height:${size}px">${LOGO}</span>`;
}

// ---------- Settings: clicks, switches and keys ----------

function settingsClick(event) {
  if (!settingsView) return;
  const target = event.target;
  const tab = target.closest('[data-settings-tab]');
  if (tab) return showSettingsTab(tab.dataset.settingsTab, true);
  if (target.closest('[data-settings-close]')) return closeSettings();
  if (target.closest('[data-releases]')) return appInfo?.releases && send({ type: 'openUrl', url: appInfo.releases });
  if (target.closest('[data-update-open]')) return appInfo?.update && send({ type: 'openUrl', url: appInfo.update.url });
  switch (settingsView.tab) {
    case 'general': return generalClick(target);
    case 'agents': return agentsClick(target);
    case 'phone': return phoneClick(event);
    case 'instructions':
      if (target.closest('[data-sync]')) send({ type: 'syncInstructions' });
      return;
    case 'mcp':
      if (target.closest('[data-mcp-again]')) refreshMcp();
      return;
    case 'about': {
      const link = target.closest('[data-link-to]');
      if (link && LINKS[link.dataset.linkTo]) send({ type: 'openUrl', url: LINKS[link.dataset.linkTo] });
    }
  }
}

function generalClick(target) {
  const size = target.closest('[data-size]');
  if (size) return changeFontSize(size.dataset.size, true);
  const awake = target.closest('[data-awake]');
  if (awake) return send({ type: 'keepAwake', mode: awake.dataset.awake });
  if (target.closest('[data-projects-folder]')) return send({ type: 'projectsFolder', reset: false });
  if (target.closest('[data-projects-auto]')) return send({ type: 'projectsFolder', reset: true });
  const switched = target.closest('[data-toggle]');
  if (!switched) return;
  const on = switched.getAttribute('aria-checked') !== 'true';
  switched.setAttribute('aria-checked', String(on));
  if (switched.dataset.toggle === 'notify') setNotify(on);
  if (switched.dataset.toggle === 'updates') send({ type: 'updateCheck', on });
  if (switched.dataset.toggle === 'mascot') {
    send({ type: 'mascotAnimations', on });
    try {
      window.Mascot?.setEnabled(on);
    } catch { /* the mascot is decoration */ }
  }
}

// The S/M/L/XL buttons and the slider: every pane at once, saved when the choice is made.
function changeFontSize(size, save) {
  setFontSize(size, save);
  const panel = settingsView?.root.querySelector('.settings-panel');
  if (!panel || settingsView.tab !== 'general') return;
  panel.querySelector('.size-value').textContent = `${fontSize} px`;
  const range = panel.querySelector('.size-range');
  if (Number(range.value) !== fontSize) range.value = fontSize;
  for (const button of panel.querySelectorAll('[data-size]')) button.setAttribute('aria-pressed', String(Number(button.dataset.size) === fontSize));
}

function agentsClick(target) {
  const more = target.closest('[data-account-menu]');
  if (more) return accountMenu(accountOf(more.dataset.accountMenu), more);
  const install = target.closest('[data-install]');
  if (install) return askInstall(state.agents.find(agent => agent.name === install.dataset.install));
  const add = target.closest('[data-add]');
  if (add) return addAccount(add.dataset.add);
  if (target.closest('[data-add-account]')) return addAccount();
  if (target.closest('[data-edit-agents]')) return send({ type: 'editAgents' });
  if (target.closest('[data-refresh]')) send({ type: 'refresh' });
}

function settingsChange(event) {
  if (settingsView?.tab === 'phone') return phoneChange(event);
  if (event.target.classList.contains('size-range')) changeFontSize(event.target.value, true);
}

function settingsInput(event) {
  if (event.target.classList.contains('size-range')) changeFontSize(event.target.value, false);
}

// The tabs are a vertical tab list: arrow keys, Home and End move between them.
function settingsTabKeys(event) {
  const tab = event.target.closest?.('.settings-tab');
  if (!tab) return;
  const ids = SETTINGS_TABS.map(([id]) => id);
  const index = ids.indexOf(tab.dataset.settingsTab);
  const next = { ArrowDown: index + 1, ArrowRight: index + 1, ArrowUp: index - 1, ArrowLeft: index - 1, Home: 0, End: ids.length - 1 }[event.key];
  if (next === undefined) return;
  event.preventDefault();
  showSettingsTab(ids[(next + ids.length) % ids.length], true);
}

const focusables = root => [...root.querySelectorAll('button, [href], input, select, textarea, [tabindex]')]
  .filter(item => item.tabIndex >= 0 && !item.disabled && item.getClientRects().length);

// While Settings is open, Tab stays inside it, Esc closes it (a menu or a dialog on top of it closes first), and
// focus that lands outside it (a click on the dimmed page) goes back into it.
document.addEventListener('keydown', event => {
  if (!settingsView || openMenu || document.querySelector('.modal')) return;
  if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation();
    return closeSettings();
  }
  if (event.key !== 'Tab') return;
  const items = focusables(settingsView.root.querySelector('.settings-window'));
  if (!items.length) return;
  const active = document.activeElement;
  const inside = settingsView.root.contains(active);
  if (!inside || (event.shiftKey ? active === items[0] : active === items.at(-1))) {
    event.preventDefault();
    (event.shiftKey ? items.at(-1) : items[0]).focus();
  }
}, true);

document.addEventListener('focusin', event => {
  if (!settingsView || settingsView.root.contains(event.target) || event.target.closest?.('.modal, .menu')) return;
  settingsView.root.querySelector('.settings-panel').focus();
});

// Ctrl+, opens Settings (also from a pane), Ctrl+N the new-agent menu (not from inside a pane, where it is the agent's
// own key). Neither while another dialog is open.
document.addEventListener('keydown', event => {
  if (!event.ctrlKey || event.altKey || event.metaKey || event.shiftKey || document.querySelector('.modal')) return;
  const key = event.key.toLowerCase();
  if (key === ',') {
    event.preventDefault();
    if (!settingsView) openSettings();
  } else if (key === 'n' && !settingsView && !event.target.closest?.('.xterm')) {
    event.preventDefault();
    openNewAgentMenu();
  }
});


// ---------- wiring ----------

$('#workspaces').addEventListener('click', event => {
  const more = event.target.closest('[data-workspace-menu]');
  if (more) return workspaceMenu(more.dataset.workspaceMenu, more);
  // A pane's dot shows that pane.
  const dot = event.target.closest('[data-dot]');
  if (dot && sessions.has(dot.dataset.dot)) return showSession(dot.dataset.dot);
  const row = event.target.closest('[data-path]');
  if (row) openProject(row.dataset.path);
});

// A chat in Recent or Home's "Pick up where you left off": continued like History's Resume, or its pane shown.
function recentClick(event) {
  const more = event.target.closest('[data-chat-menu]');
  if (more) return chatOf(more.dataset.chatMenu) && chatMenu(chatOf(more.dataset.chatMenu), more);
  const row = event.target.closest('[data-recent]');
  if (row && chatOf(row.dataset.recent)) return resumeChat(chatOf(row.dataset.recent));
  if (event.target.closest('[data-recent-more]')) {
    recentLimit += RECENT_PAGE * 2;
    renderRecent();
  }
}

$('#recent').addEventListener('click', recentClick);
$('#search-recent').addEventListener('click', () => setRecentSearch($('.recent-search').hidden));
$('#recent-search').addEventListener('input', event => {
  recentQuery = event.target.value.trim();
  recentLimit = RECENT_PAGE;
  renderRecent();
});
$('#recent-search').addEventListener('keydown', event => {
  if (event.key !== 'Escape') return;
  event.stopPropagation();
  setRecentSearch(false);
  $('#search-recent').focus();
});

$('#home').addEventListener('click', event => {
  const quick = event.target.closest('[data-quick]');
  if (quick) {
    openProject(quick.dataset.path);
    return openSession(quick.dataset.quick);
  }
  const shown = event.target.closest('[data-show-session]');
  if (shown) return showSession(shown.dataset.showSession);
  const dot = event.target.closest('[data-dot]');
  if (dot && sessions.has(dot.dataset.dot)) return showSession(dot.dataset.dot);
  const add = event.target.closest('[data-add-project]');
  if (add) return addProjectMenu(add);
  const open = event.target.closest('[data-open-project]');
  if (open) return openProject(open.dataset.openProject);
  recentClick(event);
});

$('#empty').addEventListener('click', event => {
  const card = event.target.closest('[data-account]');
  if (card) return openSession(card.dataset.account);
  const settings = event.target.closest('[data-settings-tab]');
  if (settings) return openSettings(settings.dataset.settingsTab);
  if (event.target.closest('[data-history]')) return openHistory();
  const resume = event.target.closest('[data-resume], [data-show-chat]');
  if (resume) return chatOf(resume.dataset.resume || resume.dataset.showChat) && resumeChat(chatOf(resume.dataset.resume || resume.dataset.showChat));
  const more = event.target.closest('[data-chat-menu]');
  if (more && chatOf(more.dataset.chatMenu)) chatMenu(chatOf(more.dataset.chatMenu), more);
});

$('#grid').addEventListener('click', event => {
  const button = event.target.closest('[data-act]');
  if (!button) return;
  const session = sessions.get(button.closest('.pane').dataset.session);
  if (!session) return;
  switch (button.dataset.act) {
    case 'menu': return paneMenu(session, button);
    case 'max': return toggleMax(session);
    case 'new': return openSession(session.key, 'start');
    case 'close': return closeSession(session);
    case 'restart': return restart(session);
    case 'resume': return resumeHere(session);
    case 'fresh': return restart(session, true);
    default: return imageAction(session, button);
  }
});

$('#grid').addEventListener('dblclick', event => {
  const label = event.target.closest('.pane-title');
  if (label) editTitle(sessions.get(label.closest('.pane').dataset.session));
});

mountMascot($('#mascot-slot'), 30);
try {
  if (window.Mascot) window.Mascot.onToast = text => toast(String(text));
} catch { /* the mascot is decoration */ }
$('#side-new-agent').innerHTML = icon('plus', 15) + '<span>New agent</span><kbd>Ctrl N</kbd>';
$('#nav-home').innerHTML = icon('home', 16) + '<span>Home</span>';
$('#add-project').innerHTML = icon('plus', 15);
$('#search-recent').innerHTML = icon('search', 14);
$('#settings').innerHTML = icon('gear', 17);
$('#new-agent').innerHTML = icon('plus', 15) + '<span>New agent</span>' + icon('chevron', 14);
$('#project-menu').innerHTML = icon('more', 16);
$('#git-chip').addEventListener('click', () => {
  if (panelShows('changes')) return setPanel(false);
  panelTab = 'changes';
  setPanel(true);
});
$('#images-refresh').innerHTML = icon('refresh', 14);

$('#side-new-agent').addEventListener('click', event => newAgentMenu(event.currentTarget));
$('#nav-home').addEventListener('click', showHome);
$('#add-project').addEventListener('click', event => addProjectMenu(event.currentTarget));
$('#phone').addEventListener('click', () => openSettings('phone'));
$('#settings').addEventListener('click', () => openSettings('general'));
showPhones();
$('#update-note').addEventListener('click', event => {
  const update = appInfo?.update;
  if (!update) return;
  if (event.target.closest('[data-update-open]')) send({ type: 'openUrl', url: update.url });
  if (event.target.closest('[data-update-close]')) {
    localStorage.setItem('updateDismissed', update.version);
    renderUpdateNote();
  }
});
$('#new-agent').addEventListener('click', event => newAgentMenu(event.currentTarget));
$('#project-menu').addEventListener('click', event => projectMenu(event.currentTarget));
$('#images-refresh').addEventListener('click', () => {
  imagesKey = '';
  loadImages();
});
$('#toggle-sidebar').innerHTML = icon('sidebar', 16);
$('#toggle-sidebar').addEventListener('click', () => {
  if (drawerMode) return setDrawer(!drawerOpen);
  setSidebar(!$('.app').classList.contains('side-hidden'));
  fitPanel();
});
applyWindowShape();
setSidebar(localStorage.getItem('sidebar') === 'hidden');

// The drawer closes on a click beside it, on Esc, and when something in it is picked. Buttons that open a menu keep
// it open until the menu's choice (menu()); so do the mascot, the chat search and "Show more".
$('#drawer-backdrop').addEventListener('click', () => setDrawer(false));
$('.sidebar').addEventListener('click', event => {
  if (!drawerOpen || !event.target.closest('button')) return;
  if (event.target.closest('[aria-haspopup="menu"], [data-workspace-menu], [data-chat-menu], [data-recent-more], #search-recent, .mascot-slot')) return;
  setDrawer(false);
});
// In the capture phase, so a menu open over the drawer or the chat search's own Esc comes first.
document.addEventListener('keydown', event => {
  if (event.key !== 'Escape' || !drawerOpen || openMenu || settingsView || document.querySelector('.modal')) return;
  if (event.target.closest?.('.recent-search, .xterm')) return;
  setDrawer(false);
  $('#toggle-sidebar').focus();
}, true);

$('#toggle-panel').innerHTML = icon('panel', 16);
$('#panel-close').innerHTML = icon('close', 14);
$('#preview-reload').innerHTML = icon('refresh', 14);
$('#preview-external').innerHTML = icon('external', 14);
$('#changes-refresh').innerHTML = icon('refresh', 14);
$('#console-restart').innerHTML = icon('plus', 14);
$('#toggle-panel').addEventListener('click', () => setPanel(panel.hidden));
$('#panel-close').addEventListener('click', () => setPanel(false));
panel.querySelector('.panel-tabs').addEventListener('click', event => {
  const tab = event.target.closest('.panel-tab');
  if (tab) showPanelTab(tab.dataset.tab);
});
$('#preview-form').addEventListener('submit', event => {
  event.preventDefault();
  openPreview($('#preview-address').value);
});
$('#preview-reload').addEventListener('click', () => {
  if (previewUrl) showPreview(previewUrl, previewFile);
  else openPreview($('#preview-address').value);
});
$('#preview-external').addEventListener('click', () => {
  const url = previewUrl || localUrl($('#preview-address').value);
  if (url) send({ type: 'openUrl', url });
});
$('#changes-refresh').addEventListener('click', () => {
  changesKey = '';
  diffShown = null;
  loadChanges();
});
new ResizeObserver(fitTopbar).observe($('.topbar'));
$('#preview-address').value = localStorage.getItem('previewAddress') || '';
setPanelWidth(localStorage.getItem('panelWidth'), false);
setPanel(localStorage.getItem('panel') === 'open');
$('#images-grid').addEventListener('dragstart', event => {
  const tile = event.target.closest('.image-tile');
  if (!tile) return;
  // Only the path travels; the pane's drop handler pastes it like a dropped file.
  event.dataTransfer.setData(IMAGE_DRAG, tile.dataset.path);
  event.dataTransfer.effectAllowed = 'copy';
});
render();

if (bridge) {
  bridge.addEventListener('message', event => {
    const message = event.data;
    switch (message.type) {
      case 'state': {
        const before = state.workspace;
        const listed = state.workspaces;
        state = message;
        // A folder just added at the PC (Add a project folder, New project, put back from History) opens. A project
        // a phone made is added at the end and the PC's view stays.
        if (!same(before, state.workspace) && !listed.some(workspace => same(workspace.path, state.workspace)) && before) view = 'project';
        render();
        renderHistory();
        if (!same(before, state.workspace)) panelWorkspaceChanged();
        const waiting = pendingResume && chatOf(pendingResume);
        if (waiting && workspaceOf(waiting.workspace)) {
          pendingResume = null;
          if (!same(waiting.workspace, state.workspace)) selectWorkspace(waiting.workspace);
          openSession(waiting.account, 'resume', waiting);
        }
        break;
      }
      case 'installCheck':
        installChecks.get(message.agent)?.(Array.isArray(message.needs) ? message.needs : []);
        break;
      case 'newProjectProblem':
        newProjectChecked?.(message.name, message.problem);
        break;
      case 'newProjectResult':
        newProjectAnswer?.(message.problem || null);
        newProjectAnswer = null;
        break;
      case 'history':
        history = message.chats || [];
        renderHistory();
        renderRecent();
        renderHome();
        if (!$('#empty').hidden) renderEmpty();
        break;
      case 'started': {
        // The pane's chat now has a place in the history; a title it already got goes there too.
        const session = sessions.get(message.session);
        if (!session) break;
        session.chat = message.chat;
        if (session.customTitle || session.autoTitle) syncTitle(session);
        remoteSoon();
        break;
      }
      case 'output': {
        const session = sessions.get(message.session);
        if (!session) break;
        session.term.write(message.data);
        outputArrived(session);
        break;
      }
      case 'focusSession':
        showSession(message.session);
        break;
      case 'presence':
        presenceChanged(message);
        break;
      case 'dropped':
        filesDropped(message);
        break;
      case 'image':
        imageArrived(message);
        break;
      case 'imageList':
        imageListArrived(message);
        break;
      // The side panel.
      case 'docList':
        docListArrived(message);
        break;
      case 'previewUrl':
        previewUrlArrived(message);
        break;
      case 'changes':
        gitArrived(message);
        changesArrived(message);
        break;
      case 'diff':
        diffArrived(message);
        break;
      case 'consoleStarted':
        if (!consoleNow(message)) break;
        $('#console-folder').textContent = message.workspace;
        $('#console-folder').title = message.workspace;
        break;
      case 'consoleOutput':
        if (consoleNow(message)) consoleTerm?.write(message.data);
        break;
      case 'consoleExit':
        consoleExited(message);
        break;
      case 'exit':
        sessionEnded(message);
        break;
      case 'toast':
        toast(message.text, message.kind);
        if (message.kind === 'error') pendingResume = null;
        break;
      case 'appInfo':
        appInfoArrived(message);
        break;
      // Settings → MCP servers (Mcp.cs): server names and status per agent.
      case 'mcpList':
        mcpArrived(message);
        break;
      case 'accountAdded': {
        const account = accountOf(message.account);
        const agent = agentOf(account);
        if (!account || !agent) break;
        toast(agent.loginHint || 'Follow the steps in the new pane to sign in.');
        openSession(account.key, 'login');
        break;
      }
      // Phone remote (Remote.cs). Nothing here may throw: requests answer with a remoteReply instead.
      case 'remoteStatus':
        quietly(() => phonesChanged(message));
        break;
      case 'remoteWatch':
        quietly(() => watchChanged(message));
        break;
      case 'remoteInfo':
        quietly(() => phoneInfo(message));
        break;
      case 'remotePairAsk':
        quietly(() => pairAsked(message));
        break;
      case 'remoteCancel':
        quietly(() => cancelJobs(message));
        break;
      case 'remoteGetScreen':
        remoteRequest(message, remoteGetScreen);
        break;
      case 'remoteInput':
        remoteRequest(message, remoteInput);
        break;
      case 'remoteKeys':
        remoteRequest(message, remoteKeys);
        break;
      case 'remoteStart':
        remoteRequest(message, remoteStart);
        break;
      case 'remoteResume':
        remoteRequest(message, remoteResume);
        break;
      case 'remoteStop':
        remoteRequest(message, remoteStop);
        break;
      case 'remoteClose':
        remoteRequest(message, remoteClose);
        break;
      case 'remoteRename':
        remoteRequest(message, remoteRename);
        break;
    }
  });
  send({ type: 'ready' });
} else {
  renderEmpty();
  toast('Open this page from Agent Arcade.', 'error');
}
