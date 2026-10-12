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

const BRAND_COLORS = { claude: '#d97757', codex: '#10a37f', gemini: '#4f8cff', copilot: '#a371f7', 'cursor-agent': '#7c8aa5' };

// Names agents put in the terminal title when it says nothing about the chat itself.
const GENERIC_TITLES = ['claude', 'claude code', 'codex', 'gemini', 'gemini cli', 'copilot', 'github copilot', 'github copilot cli', 'cursor', 'cursor agent', 'cursor-agent', 'agent'];

const ICONS = {
  plus: '<path d="M12 5v14M5 12h14"/>',
  more: '<circle cx="5" cy="12" r="1.5"/><circle cx="12" cy="12" r="1.5"/><circle cx="19" cy="12" r="1.5"/>',
  expand: '<path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7"/>',
  shrink: '<path d="M4 14h6v6M20 10h-6V4M14 10l7-7M3 21l7-7"/>',
  close: '<path d="M18 6 6 18M6 6l12 12"/>',
  refresh: '<path d="M21 12a9 9 0 1 1-2.64-6.36L21 8"/><path d="M21 3v5h-5"/>',
  folder: '<path d="M3 7.5A2.5 2.5 0 0 1 5.5 5H9l2 2h7.5A2.5 2.5 0 0 1 21 9.5v7a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 16.5z"/>',
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
};

const LOGO = '<svg viewBox="0 0 24 24" width="60%" height="60%" aria-hidden="true"><defs><linearGradient id="logo-gradient" x1="0" y1="0" x2="1" y2="1">' +
  '<stop offset="0" stop-color="#ff9e74"/><stop offset="1" stop-color="#8f7dff"/></linearGradient></defs>' +
  '<path fill="url(#logo-gradient)" d="M12 1.5c.7 5.6 3.4 8.3 9 9-5.6.7-8.3 3.4-9 9-.7-5.6-3.4-8.3-9-9 5.6-.7 8.3-3.4 9-9z"/></svg>';

let state = { workspace: '', workspaces: [], agents: [], accounts: [] };
let history = [];
const sessions = new Map();
let sessionCount = 0;
let maxed = null;
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
  const letters = (agent?.name || '?').replace(/[^A-Za-z0-9]/g, '').slice(0, 2) || '?';
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

// Resolves with the dialog's field values (or {}) when confirmed, and null when cancelled.
function modal({ title, body, confirm = 'OK', cancel = 'Cancel', danger = false, validate }) {
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
    const accept = () => {
      const result = values();
      const problem = validate && validate(result);
      if (problem) {
        const error = root.querySelector('.modal-error');
        error.textContent = problem;
        error.hidden = false;
        return;
      }
      finish(result);
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
  });
}

// ---------- rendering ----------

function render() {
  renderWorkspaces();
  renderAgents();
  renderTopbar();
  renderPanes();
}

function renderWorkspaces() {
  $('#workspaces').innerHTML = state.workspaces.map(workspace => {
    const active = same(workspace.path, state.workspace);
    const list = sessionsIn(workspace.path);
    const children = active && list.length
      ? `<div class="ws-sessions">${list.map(session =>
          `<button class="ws-session" data-session="${session.id}"><span class="dot ${session.running ? 'on' : 'off'}"></span>` +
          `<span class="ellipsis">${esc(titleOf(session))}</span></button>`).join('')}</div>`
      : '';
    return `<div class="ws ${active ? 'active' : ''}">` +
      `<button class="ws-row" data-path="${esc(workspace.path)}" title="${esc(workspace.path)}">` +
      `<span class="ws-icon">${icon(workspace.unity ? 'cube' : 'folder', 15)}</span>` +
      `<span class="ellipsis">${esc(workspace.name)}</span>` +
      (workspace.exists ? '' : '<span class="tag-warn">missing</span>') +
      (list.length ? `<span class="count">${list.length}</span>` : '') +
      `<span class="row-more" data-workspace-menu="${esc(workspace.path)}" title="More">${icon('more', 15)}</span>` +
      `</button>${children}</div>`;
  }).join('');
}

function renderAgents() {
  $('#agents').innerHTML = state.agents.map(agent => {
    const status = agent.checking
      ? '<span class="ver">checking…</span>'
      : agent.installed
        ? `<span class="ver" title="${esc(agent.version)}">${esc(shortVersion(agent.version))}</span>`
        : `<button class="tag-btn" data-install="${esc(agent.name)}">Install</button>`;
    const add = agent.multi && agent.installed
      ? `<button class="icon-btn small" data-add="${esc(agent.name)}" title="Add another ${esc(agent.name)} account">${icon('plus', 14)}</button>`
      : '';
    const rows = state.accounts.filter(account => account.agent === agent.name).map(account => {
      const running = runningFor(account.key);
      const dot = account.checking ? 'wait' : account.signedIn === true ? 'on' : account.signedIn === false ? 'off' : 'idle';
      return `<button class="account ${agent.installed ? '' : 'disabled'}" data-account="${esc(account.key)}" title="Start ${esc(agent.name)} (${esc(account.name)})">` +
        `<span class="dot ${dot}"></span>` +
        `<span class="acc-text"><span class="acc-top"><span class="acc-name">${esc(account.name)}</span>` +
        (account.plan ? `<span class="plan">${esc(account.plan)}</span>` : '') +
        (running ? `<span class="count live" title="Running">${running}</span>` : '') +
        `</span><span class="acc-sub ellipsis">${esc(accountLine(account))}</span></span>` +
        `<span class="row-more" data-account-menu="${esc(account.key)}" title="More">${icon('more', 15)}</span></button>`;
    }).join('');
    return `<div class="agent"><div class="agent-head">${avatar(agent)}<span class="agent-name ellipsis">${esc(agent.name)}</span>${status}${add}</div>${rows}</div>`;
  }).join('');
}

function renderTopbar() {
  const workspace = workspaceOf(state.workspace);
  $('#ws-name').textContent = workspace ? workspace.name : 'No workspace';
  $('#ws-path').textContent = state.workspace || '';
  $('#open-unity').hidden = !workspace?.unity;
}

function renderPanes() {
  const visible = sessionsIn(state.workspace);
  if (maxed && !visible.some(session => session.id === maxed)) maxed = null;
  for (const session of sessions.values()) {
    session.el.hidden = !same(session.workspace, state.workspace) || (maxed && session.id !== maxed);
    session.el.style.order = session.order;
    session.el.classList.toggle('movable', !maxed && visible.length > 1);
    const maxButton = session.el.querySelector('[data-act="max"]');
    maxButton.innerHTML = icon(session.id === maxed ? 'shrink' : 'expand', 14);
    maxButton.title = session.id === maxed ? 'Restore' : 'Maximize';
    updatePaneSub(session);
  }
  const count = maxed ? 1 : visible.length;
  const cols = count <= 1 ? 1 : count === 2 || count === 4 ? 2 : 3;
  const rows = Math.max(1, Math.ceil(count / cols));
  const grid = $('#grid');
  grid.style.gridTemplateColumns = `repeat(${cols}, minmax(0, 1fr))`;
  grid.style.gridTemplateRows = `repeat(${rows}, minmax(${rows > 2 ? 260 : 0}px, 1fr))`;
  grid.hidden = count === 0;
  $('#empty').hidden = count !== 0;
  if (count === 0) renderEmpty();
  for (const session of visible) scheduleFit(session);
}

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
    `<div class="cards">${cards || `<p class="tip">${checking ? 'Looking for your agents…' : 'No agents installed yet. Use Install in the sidebar.'}</p>`}</div>` +
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

// ---------- chat titles ----------

function showTitle(session) {
  const label = session.el.querySelector('.pane-title');
  if (!label) return;
  label.textContent = titleOf(session);
  label.classList.toggle('placeholder', isChat(session) && !session.customTitle && !session.autoTitle);
  if (isChat(session)) label.title = titleOf(session) + ' (double-click to rename)';
  renderWorkspaces();
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

// Until the agent names the chat, the first thing the user asks becomes its title.
function trackPrompt(session, data) {
  if (!isChat(session) || session.customTitle || session.autoTitle) return;
  const text = data
    .replace(/\x1b\[200~([\s\S]*?)\x1b\[201~/g, (_, pasted) => pasted.replace(/[\r\n]+/g, ' '))
    .replace(/\x1b(\[[0-9;?]*[ -/]*[@-~]|O.|.)/g, '');
  for (const c of text) {
    if (c === '\r' || c === '\n') {
      const line = session.typed.trim();
      session.typed = '';
      if (line.length >= 3 && !line.startsWith('/') && line !== SAVE_PROMPT && line !== CONTINUE_PROMPT) {
        setAutoTitle(session, line.slice(0, 120));
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
function openSession(key, mode = 'start', chat = null) {
  const account = accountOf(key);
  const agent = agentOf(account);
  if (!account || !agent) return toast('That account is gone. Click refresh.', 'error');
  if (mode !== 'install' && !agent.installed) {
    if (agent.checking) return toast(`Still checking ${agent.name}. Try again in a moment.`);
    return askInstall(agent);
  }

  const id = 's' + (++sessionCount);
  const label = mode === 'install' ? `Install ${agent.name}` : mode === 'login' ? `${agent.name} · sign in` : agent.name;
  const session = {
    id, key, mode, label, workspace: chat ? chat.workspace : state.workspace, running: true, order: sessionCount,
    chat: null, resumeOf: chat?.id || '', customTitle: chat?.named ? chat.title : '', autoTitle: chat && !chat.named ? chat.title : '', typed: '',
  };
  session.el = document.createElement('div');
  session.el.className = 'pane';
  session.el.dataset.session = id;
  session.el.innerHTML =
    `<div class="pane-head"><span class="dot on"></span>${avatar(agent, 'sm')}` +
    '<span class="pane-title ellipsis"></span><span class="pane-sub ellipsis"></span>' +
    '<span class="pane-actions">' +
    `<button class="icon-btn small" data-act="menu" title="More">${icon('more', 15)}</button>` +
    `<button class="icon-btn small" data-act="max" title="Maximize">${icon('expand', 14)}</button>` +
    `<button class="icon-btn small" data-act="new" title="New pane with this account">${icon('plus', 15)}</button>` +
    `<button class="icon-btn small" data-act="close" title="Close">${icon('close', 15)}</button>` +
    '</span></div><div class="pane-body"><div class="term"></div></div>';
  $('#grid').append(session.el);
  sessions.set(id, session);

  const term = session.term = new Terminal({
    fontFamily: '"Cascadia Mono", "Cascadia Code", Consolas, monospace',
    fontSize: 13,
    cursorBlink: true,
    allowProposedApi: true,
    scrollback: 5000,
    theme: THEME,
    // Links that agents print as OSC 8 hyperlinks open in the normal browser.
    linkHandler: { activate: (event, uri) => send({ type: 'openUrl', url: uri }) },
  });
  session.fit = new FitAddon.FitAddon();
  term.loadAddon(session.fit);
  try {
    term.loadAddon(new Unicode11Addon.Unicode11Addon());
    term.unicode.activeVersion = '11';
  } catch { /* default widths are fine too */ }
  term.loadAddon(new WebLinksAddon.WebLinksAddon((event, uri) => send({ type: 'openUrl', url: uri })));
  term.open(session.el.querySelector('.term'));
  try {
    const webgl = new WebglAddon.WebglAddon();
    webgl.onContextLoss(() => webgl.dispose());
    term.loadAddon(webgl);
  } catch { /* the standard renderer works without a GPU */ }
  term.attachCustomKeyEventHandler(event => handleKey(session, event));
  term.onData(data => {
    if (!session.running) return;
    send({ type: 'input', session: id, data });
    trackPrompt(session, data);
  });
  term.onTitleChange(title => agentTitle(session, title));
  new ResizeObserver(() => scheduleFit(session)).observe(session.el.querySelector('.pane-body'));

  maxed = null;
  showTitle(session);
  render();
  requestAnimationFrame(() => {
    fitNow(session);
    send({ type: 'start', session: id, account: key, mode, workspace: session.workspace, chat: session.resumeOf, cols: term.cols, rows: term.rows });
    term.onResize(size => send({ type: 'resize', session: id, cols: size.cols, rows: size.rows }));
    term.focus();
  });
  return session;
}

function handleKey(session, event) {
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
  return true;
}

function scheduleFit(session) {
  if (session.fitFrame) return;
  session.fitFrame = requestAnimationFrame(() => {
    session.fitFrame = 0;
    fitNow(session);
  });
}

function fitNow(session) {
  if (!session.el.isConnected || session.el.hidden) return;
  const body = session.el.querySelector('.pane-body');
  if (body.clientWidth < 60 || body.clientHeight < 40) return;
  try {
    session.fit.fit();
  } catch { /* the pane is being removed */ }
}

function sessionEnded(message) {
  const session = sessions.get(message.session);
  if (!session || !session.running) return;
  session.running = false;
  session.term.write(message.error
    ? `\r\n\x1b[31m${message.error}\x1b[0m\r\n`
    : `\r\n\x1b[90m— session ended${message.code ? ` (exit code ${message.code})` : ''} —\x1b[0m\r\n`);
  session.el.querySelector('.pane-head .dot').className = 'dot off';
  const resumable = chatOf(session.chat)?.resume;
  session.el.querySelector('.pane-body').insertAdjacentHTML('beforeend',
    '<div class="pane-ended">Session ended' +
    (resumable ? '<button data-act="resume" title="Continue this chat">Resume</button>' : '') +
    (isChat(session) ? '<button data-act="fresh">New chat</button>' : '<button data-act="restart">Restart</button>') +
    '<button data-act="close">Close</button></div>');
  if (session.mode === 'login' && !message.error) toast('Sign-in finished. The sidebar shows the email in a moment.');
  renderWorkspaces();
  renderAgents();
}

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
  try {
    session.term.dispose();
  } catch { /* the pane is already gone from the screen */ }
  if (maxed === session.id) maxed = null;
  render();
}

function focusSession(id) {
  const session = sessions.get(id);
  if (!session) return;
  if (maxed && maxed !== id) maxed = null;
  renderPanes();
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
    { label: 'Clear screen', icon: 'eraser', run: () => session.term.clear() },
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

async function askInstall(agent) {
  if (!agent) return;
  if (!agent.install) return toast(`No install command is set for ${agent.name}. Use Edit agent list.`, 'error');
  const ok = await modal({
    title: `Install or update ${agent.name}?`,
    body: `<p>This runs in a new pane:</p><pre class="cmd">${esc(agent.install)}</pre><p class="muted">When it's done, the sidebar updates by itself.</p>`,
    confirm: 'Install',
  });
  if (!ok) return;
  const main = state.accounts.find(account => account.agent === agent.name && account.main);
  if (main) openSession(main.key, 'install');
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
    `${action}<button class="icon-btn small" data-chat-menu="${esc(chat.id)}" title="More">${icon('more', 15)}</button></div>`;
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

function selectWorkspace(path) {
  state.workspace = path;
  render();
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
  const root = document.createElement('div');
  root.className = 'modal-backdrop';
  root.innerHTML =
    '<div class="modal history" role="dialog" aria-modal="true">' +
    '<div class="hist-head"><h2>Chat history</h2><div class="seg">' +
    '<button data-scope="project"></button><button data-scope="all">All projects</button></div>' +
    `<button class="icon-btn" data-close title="Close">${icon('close', 15)}</button></div>` +
    '<input class="hist-search" placeholder="Search titles, agents and projects" spellcheck="false" autocomplete="off">' +
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

// ---------- wiring ----------

$('#workspaces').addEventListener('click', event => {
  const more = event.target.closest('[data-workspace-menu]');
  if (more) return workspaceMenu(more.dataset.workspaceMenu, more);
  const child = event.target.closest('[data-session]');
  if (child) return focusSession(child.dataset.session);
  const row = event.target.closest('.ws-row');
  if (row && !same(row.dataset.path, state.workspace)) selectWorkspace(row.dataset.path);
});

$('#agents').addEventListener('click', event => {
  const more = event.target.closest('[data-account-menu]');
  if (more) return accountMenu(accountOf(more.dataset.accountMenu), more);
  const install = event.target.closest('[data-install]');
  if (install) return askInstall(state.agents.find(agent => agent.name === install.dataset.install));
  const add = event.target.closest('[data-add]');
  if (add) return addAccount(add.dataset.add);
  const row = event.target.closest('[data-account]');
  if (row) openSession(row.dataset.account);
});

$('#empty').addEventListener('click', event => {
  const card = event.target.closest('[data-account]');
  if (card) return openSession(card.dataset.account);
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
  }
});

$('#grid').addEventListener('dblclick', event => {
  const label = event.target.closest('.pane-title');
  if (label) editTitle(sessions.get(label.closest('.pane').dataset.session));
});

$('#logo').innerHTML = LOGO;
$('#add-workspace').innerHTML = icon('plus', 15);
$('#refresh').innerHTML = icon('refresh', 15);
$('#add-account').innerHTML = icon('plus', 15);
$('#open-unity').innerHTML = icon('cube', 15) + '<span>Open Unity</span>';
$('#open-folder').innerHTML = icon('folder', 15) + '<span>Folder</span>';
$('#sync').innerHTML = icon('doc', 15) + '<span>Shared instructions</span>';
$('#new-agent').innerHTML = icon('plus', 15) + '<span>New agent</span>' + icon('chevron', 14);
$('#history').innerHTML = icon('history', 15) + '<span>History</span>';

$('#add-workspace').addEventListener('click', () => send({ type: 'addWorkspace' }));
$('#refresh').addEventListener('click', () => send({ type: 'refresh' }));
$('#add-account').addEventListener('click', () => addAccount());
$('#edit-agents').addEventListener('click', () => send({ type: 'editAgents' }));
$('#open-unity').addEventListener('click', () => send({ type: 'openUnity' }));
$('#open-folder').addEventListener('click', () => send({ type: 'openFolder' }));
$('#new-agent').addEventListener('click', event => newAgentMenu(event.currentTarget));
$('#history').addEventListener('click', () => openHistory());
$('#sync').addEventListener('click', async () => {
  const ok = await modal({
    title: 'Give every agent the same instructions?',
    body: '<p>AGENTS.md (or CLAUDE.md, if that is all you have) stays the main file. Missing AGENTS.md, CLAUDE.md and GEMINI.md files are created and only point to it.</p>' +
      '<p class="muted">Existing files are never changed.</p>',
    confirm: 'Create missing files',
  });
  if (ok) send({ type: 'syncInstructions' });
});

if (bridge) {
  bridge.addEventListener('message', event => {
    const message = event.data;
    switch (message.type) {
      case 'state': {
        state = message;
        render();
        renderHistory();
        const waiting = pendingResume && chatOf(pendingResume);
        if (waiting && workspaceOf(waiting.workspace)) {
          pendingResume = null;
          if (!same(waiting.workspace, state.workspace)) selectWorkspace(waiting.workspace);
          openSession(waiting.account, 'resume', waiting);
        }
        break;
      }
      case 'history':
        history = message.chats || [];
        renderHistory();
        if (!$('#empty').hidden) renderEmpty();
        break;
      case 'started': {
        // The pane's chat now has a place in the history; a title it already got goes there too.
        const session = sessions.get(message.session);
        if (!session) break;
        session.chat = message.chat;
        if (session.customTitle || session.autoTitle) syncTitle(session);
        break;
      }
      case 'output':
        sessions.get(message.session)?.term.write(message.data);
        break;
      case 'exit':
        sessionEnded(message);
        break;
      case 'toast':
        toast(message.text, message.kind);
        if (message.kind === 'error') pendingResume = null;
        break;
      case 'accountAdded': {
        const account = accountOf(message.account);
        const agent = agentOf(account);
        if (!account || !agent) break;
        toast(agent.loginHint || 'Follow the steps in the new pane to sign in.');
        openSession(account.key, 'login');
        break;
      }
    }
  });
  send({ type: 'ready' });
} else {
  renderEmpty();
  toast('Open this page from Agent Arcade.', 'error');
}
