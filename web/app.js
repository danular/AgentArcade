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
};

const LOGO = '<img src="icon.png" alt="" draggable="false">';

// Each built-in agent's own mark, by command (paths from Simple Icons, CC0; the marks belong to their makers).
const BRAND_LOGOS = {
  claude: 'm4.7144 15.9555 4.7174-2.6471.079-.2307-.079-.1275h-.2307l-.7893-.0486-2.6956-.0729-2.3375-.0971-2.2646-.1214-.5707-.1215-.5343-.7042.0546-.3522.4797-.3218.686.0608 1.5179.1032 2.2767.1578 1.6514.0972 2.4468.255h.3886l.0546-.1579-.1336-.0971-.1032-.0972L6.973 9.8356l-2.55-1.6879-1.3356-.9714-.7225-.4918-.3643-.4614-.1578-1.0078.6557-.7225.8803.0607.2246.0607.8925.686 1.9064 1.4754 2.4893 1.8336.3643.3035.1457-.1032.0182-.0728-.164-.2733-1.3539-2.4467-1.445-2.4893-.6435-1.032-.17-.6194c-.0607-.255-.1032-.4674-.1032-.7285L6.287.1335 6.6997 0l.9957.1336.419.3642.6192 1.4147 1.0018 2.2282 1.5543 3.0296.4553.8985.2429.8318.091.255h.1579v-.1457l.1275-1.706.2368-2.0947.2307-2.6957.0789-.7589.3764-.9107.7468-.4918.5828.2793.4797.686-.0668.4433-.2853 1.8517-.5586 2.9021-.3643 1.9429h.2125l.2429-.2429.9835-1.3053 1.6514-2.0643.7286-.8196.85-.9046.5464-.4311h1.0321l.759 1.1293-.34 1.1657-1.0625 1.3478-.8804 1.1414-1.2628 1.7-.7893 1.36.0729.1093.1882-.0183 2.8535-.607 1.5421-.2794 1.8396-.3157.8318.3886.091.3946-.3278.8075-1.967.4857-2.3072.4614-3.4364.8136-.0425.0304.0486.0607 1.5482.1457.6618.0364h1.621l3.0175.2247.7892.522.4736.6376-.079.4857-1.2142.6193-1.6393-.3886-3.825-.9107-1.3113-.3279h-.1822v.1093l1.0929 1.0686 2.0035 1.8092 2.5075 2.3314.1275.5768-.3218.4554-.34-.0486-2.2039-1.6575-.85-.7468-1.9246-1.621h-.1275v.17l.4432.6496 2.3436 3.5214.1214 1.0807-.17.3521-.6071.2125-.6679-.1214-1.3721-1.9246L14.38 17.959l-1.1414-1.9428-.1397.079-.674 7.2552-.3156.3703-.7286.2793-.6071-.4614-.3218-.7468.3218-1.4753.3886-1.9246.3157-1.53.2853-1.9004.17-.6314-.0121-.0425-.1397.0182-1.4328 1.9672-2.1796 2.9446-1.7243 1.8456-.4128.164-.7164-.3704.0667-.6618.4008-.5889 2.386-3.0357 1.4389-1.882.929-1.0868-.0062-.1579h-.0546l-6.3385 4.1164-1.1293.1457-.4857-.4554.0608-.7467.2307-.2429 1.9064-1.3114Z',
  codex: 'M22.2819 9.8211a5.9847 5.9847 0 0 0-.5157-4.9108 6.0462 6.0462 0 0 0-6.5098-2.9A6.0651 6.0651 0 0 0 4.9807 4.1818a5.9847 5.9847 0 0 0-3.9977 2.9 6.0462 6.0462 0 0 0 .7427 7.0966 5.98 5.98 0 0 0 .511 4.9107 6.051 6.051 0 0 0 6.5146 2.9001A5.9847 5.9847 0 0 0 13.2599 24a6.0557 6.0557 0 0 0 5.7718-4.2058 5.9894 5.9894 0 0 0 3.9977-2.9001 6.0557 6.0557 0 0 0-.7475-7.0729zm-9.022 12.6081a4.4755 4.4755 0 0 1-2.8764-1.0408l.1419-.0804 4.7783-2.7582a.7948.7948 0 0 0 .3927-.6813v-6.7369l2.02 1.1686a.071.071 0 0 1 .038.052v5.5826a4.504 4.504 0 0 1-4.4945 4.4944zm-9.6607-4.1254a4.4708 4.4708 0 0 1-.5346-3.0137l.142.0852 4.783 2.7582a.7712.7712 0 0 0 .7806 0l5.8428-3.3685v2.3324a.0804.0804 0 0 1-.0332.0615L9.74 19.9502a4.4992 4.4992 0 0 1-6.1408-1.6464zM2.3408 7.8956a4.485 4.485 0 0 1 2.3655-1.9728V11.6a.7664.7664 0 0 0 .3879.6765l5.8144 3.3543-2.0201 1.1685a.0757.0757 0 0 1-.071 0l-4.8303-2.7865A4.504 4.504 0 0 1 2.3408 7.872zm16.5963 3.8558L13.1038 8.364 15.1192 7.2a.0757.0757 0 0 1 .071 0l4.8303 2.7913a4.4944 4.4944 0 0 1-.6765 8.1042v-5.6772a.79.79 0 0 0-.407-.667zm2.0107-3.0231l-.142-.0852-4.7735-2.7818a.7759.7759 0 0 0-.7854 0L9.409 9.2297V6.8974a.0662.0662 0 0 1 .0284-.0615l4.8303-2.7866a4.4992 4.4992 0 0 1 6.6802 4.66zM8.3065 12.863l-2.02-1.1638a.0804.0804 0 0 1-.038-.0567V6.0742a4.4992 4.4992 0 0 1 7.3757-3.4537l-.142.0805L8.704 5.459a.7948.7948 0 0 0-.3927.6813zm1.0976-2.3654l2.602-1.4998 2.6069 1.4998v2.9994l-2.5974 1.4997-2.6067-1.4997Z',
  gemini: 'M11.04 19.32Q12 21.51 12 24q0-2.49.93-4.68.96-2.19 2.58-3.81t3.81-2.55Q21.51 12 24 12q-2.49 0-4.68-.93a12.3 12.3 0 0 1-3.81-2.58 12.3 12.3 0 0 1-2.58-3.81Q12 2.49 12 0q0 2.49-.96 4.68-.93 2.19-2.55 3.81a12.3 12.3 0 0 1-3.81 2.58Q2.49 12 0 12q2.49 0 4.68.96 2.19.93 3.81 2.55t2.55 3.81',
  copilot: 'M23.922 16.997C23.061 18.492 18.063 22.02 12 22.02 5.937 22.02.939 18.492.078 16.997A.641.641 0 0 1 0 16.741v-2.869a.883.883 0 0 1 .053-.22c.372-.935 1.347-2.292 2.605-2.656.167-.429.414-1.055.644-1.517a10.098 10.098 0 0 1-.052-1.086c0-1.331.282-2.499 1.132-3.368.397-.406.89-.717 1.474-.952C7.255 2.937 9.248 1.98 11.978 1.98c2.731 0 4.767.957 6.166 2.093.584.235 1.077.546 1.474.952.85.869 1.132 2.037 1.132 3.368 0 .368-.014.733-.052 1.086.23.462.477 1.088.644 1.517 1.258.364 2.233 1.721 2.605 2.656a.841.841 0 0 1 .053.22v2.869a.641.641 0 0 1-.078.256Zm-11.75-5.992h-.344a4.359 4.359 0 0 1-.355.508c-.77.947-1.918 1.492-3.508 1.492-1.725 0-2.989-.359-3.782-1.259a2.137 2.137 0 0 1-.085-.104L4 11.746v6.585c1.435.779 4.514 2.179 8 2.179 3.486 0 6.565-1.4 8-2.179v-6.585l-.098-.104s-.033.045-.085.104c-.793.9-2.057 1.259-3.782 1.259-1.59 0-2.738-.545-3.508-1.492a4.359 4.359 0 0 1-.355-.508Zm2.328 3.25c.549 0 1 .451 1 1v2c0 .549-.451 1-1 1-.549 0-1-.451-1-1v-2c0-.549.451-1 1-1Zm-5 0c.549 0 1 .451 1 1v2c0 .549-.451 1-1 1-.549 0-1-.451-1-1v-2c0-.549.451-1 1-1Zm3.313-6.185c.136 1.057.403 1.913.878 2.497.442.544 1.134.938 2.344.938 1.573 0 2.292-.337 2.657-.751.384-.435.558-1.15.558-2.361 0-1.14-.243-1.847-.705-2.319-.477-.488-1.319-.862-2.824-1.025-1.487-.161-2.192.138-2.533.529-.269.307-.437.808-.438 1.578v.021c0 .265.021.562.063.893Zm-1.626 0c.042-.331.063-.628.063-.894v-.02c-.001-.77-.169-1.271-.438-1.578-.341-.391-1.046-.69-2.533-.529-1.505.163-2.347.537-2.824 1.025-.462.472-.705 1.179-.705 2.319 0 1.211.175 1.926.558 2.361.365.414 1.084.751 2.657.751 1.21 0 1.902-.394 2.344-.938.475-.584.742-1.44.878-2.497Z',
  'cursor-agent': 'M11.503.131 1.891 5.678a.84.84 0 0 0-.42.726v11.188c0 .3.162.575.42.724l9.609 5.55a1 1 0 0 0 .998 0l9.61-5.55a.84.84 0 0 0 .42-.724V6.404a.84.84 0 0 0-.42-.726L12.497.131a1.01 1.01 0 0 0-.996 0M2.657 6.338h18.55c.263 0 .43.287.297.515L12.23 22.918c-.062.107-.229.064-.229-.06V12.335a.59.59 0 0 0-.295-.51l-9.11-5.257c-.109-.063-.064-.23.061-.23',
};

// Folded sidebar parts survive restarts: 'agents' for the agent list, 'ws:<path>' for a workspace's panes.
const folded = new Set(JSON.parse(localStorage.getItem('folded') || '[]'));
const isFolded = key => folded.has(key.toLowerCase());
function toggleFold(key) {
  key = key.toLowerCase();
  if (!folded.delete(key)) folded.add(key);
  localStorage.setItem('folded', JSON.stringify([...folded]));
}

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
  const mark = BRAND_LOGOS[(agent?.command || '').toLowerCase()];
  if (mark) {
    return `<span class="avatar mark ${size}" style="--c:${agentColor(agent)}">` +
      `<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="${mark}"/></svg></span>`;
  }
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
  renderAgents();
  renderTopbar();
  renderPanes();
}

function renderWorkspaces() {
  $('#workspaces').innerHTML = state.workspaces.map(workspace => {
    const active = same(workspace.path, state.workspace);
    const list = sessionsIn(workspace.path);
    const shut = isFolded('ws:' + workspace.path);
    const children = list.length && !shut
      ? `<div class="ws-sessions">${list.map(session =>
          `<button class="ws-session" data-session="${session.id}"><span class="${DOT_CLASS[session.dotStatus] || (session.running ? 'dot on' : 'dot off')}"></span>` +
          `<span class="ellipsis">${esc(titleOf(session))}</span></button>`).join('')}</div>`
      : '';
    const fold = list.length
      ? `<span class="fold ${shut ? 'shut' : ''}" data-fold-workspace="${esc(workspace.path)}" title="${shut ? 'Show' : 'Hide'} its agents">${icon('chevron', 13)}</span>`
      : '<span class="fold-space"></span>';
    return `<div class="ws ${active ? 'active' : ''}">` +
      `<button class="ws-row" data-path="${esc(workspace.path)}" title="${esc(workspace.path)}">${fold}` +
      `<span class="ws-icon">${icon(workspace.unity ? 'cube' : 'folder', 15)}</span>` +
      `<span class="ellipsis">${esc(workspace.name)}</span>` +
      (workspace.exists ? '' : '<span class="tag-warn">missing</span>') +
      (list.length ? `<span class="count">${list.length}</span>` : '') +
      `<span class="row-more" data-workspace-menu="${esc(workspace.path)}" title="More">${icon('more', 15)}</span>` +
      `</button>${children}</div>`;
  }).join('');
}

function renderAgents() {
  const shut = isFolded('agents');
  const running = [...sessions.values()].filter(session => session.running).length;
  const toggle = $('#toggle-agents');
  toggle.classList.toggle('shut', shut);
  toggle.title = shut ? 'Show agents' : 'Hide agents';
  toggle.innerHTML = `<span class="fold">${icon('chevron', 13)}</span><span>Agents</span>` +
    (shut && running ? `<span class="count live" title="Running">${running}</span>` : '');
  $('#agents').hidden = shut;
  $('#agents').innerHTML =state.agents.map(agent => {
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
  // The bar's buttons changed, so their room did too.
  fitTopbar();
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
// options.workspace: the project to start in instead of the one on screen.
// options.remote: opened from the phone, so the PC keeps showing what it showed: no maximize reset, no focus, no dialogs.
function openSession(key, mode = 'start', chat = null, options = {}) {
  const account = accountOf(key);
  const agent = agentOf(account);
  if (!account || !agent) return options.remote ? null : toast('That account is gone. Click refresh.', 'error');
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

  if (!options.remote) maxed = null;
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

async function textSizeDialog() {
  const before = fontSize;
  const answer = modal({
    title: 'Text size',
    body: `<label>Terminal text <span class="size-value">${fontSize} px</span>` +
      `<input type="range" name="size" min="9" max="24" step="1" value="${fontSize}"></label>` +
      '<p class="muted">Applies to every pane. Ctrl+= and Ctrl+- change it too; Ctrl+0 goes back to 13.</p>',
    confirm: 'Done',
  });
  const slider = document.querySelector('.modal input[name="size"]');
  slider.addEventListener('input', () => {
    setFontSize(slider.value, false);
    slider.closest('label').querySelector('.size-value').textContent = `${fontSize} px`;
  });
  const result = await answer;
  setFontSize(result ? result.size : before);
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
  if (session.mode === 'login' && !error && !phone) toast('Sign-in finished. The sidebar shows the email in a moment.');
  renderWorkspaces();
  renderAgents();
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
const APPROVAL = /do you want|would you like to|allow (this|command|execution)|apply this change|\(y\/n\)|\[y\/n\]/i;
// Focus and mouse reports that xterm.js sends for the agent; nobody typed them.
const REPORTS = /^(?:\x1b\[[IO]|\x1b\[<\d+;\d+;\d+[Mm]|\x1b\[M[\s\S]{3})+$/;
let notifyOn = localStorage.getItem('notifications') !== 'off';

const agentName = session => agentOf(accountOf(session.key))?.name || 'The agent';

// typedAt: when you started typing after the last Enter; the echo of a draft isn't the agent working.
function watchTurn(session, data) {
  clearAttention(session);
  if (REPORTS.test(data)) return;
  if (data.includes('\r')) {
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
    if (session.running && session.armed && session.busyStart && now - session.lastOutput > QUIET_MS &&
        session.lastOutput - session.busyStart >= MIN_WORK_MS) needsYou(session);
  }
  refreshDots();
}, 1000);

// ---------- status dots ----------

// Orange: working. Red: needs you. Green: idle or done. Grey: ended. The phone uses the same statuses (paneStatus).
const DOT_CLASS = { working: 'dot working', needsYou: 'dot needs', done: 'dot on', idle: 'dot on', ended: 'dot off' };

// Sets each pane's dot, and its dot in the sidebar, from paneStatus. The sidebar dot changes in place: redrawing the
// sidebar every time a status changes could swallow a click that is halfway through.
function refreshDots() {
  for (const session of sessions.values()) {
    const status = session.running ? paneStatus(session, screenParts(session)) : 'ended';
    if (status === session.dotStatus) continue;
    session.dotStatus = status;
    session.el.querySelector('.pane-head .dot').className = DOT_CLASS[status];
    const listed = document.querySelector(`#workspaces .ws-session[data-session="${CSS.escape(session.id)}"] .dot`);
    if (listed) listed.className = DOT_CLASS[status];
  }
}

function needsYou(session, text = '') {
  session.armed = 0;
  session.busyStart = 0;
  if (Date.now() - (session.notifiedAt || 0) < 8000) return;
  session.notifiedAt = Date.now();
  const own = String(text).replace(/[\x00-\x1f]/g, ' ').trim();
  const screen = screenParts(session);
  const asks = own ? Remote.asksOk(own) : screen.asks || screen.choices.length > 0 || screen.menu;
  const kind = asks ? 'needsYou' : 'done';
  const message = Remote.cut(own, 200) ||
    (asks ? `${agentName(session)} needs your OK to continue.` : `${agentName(session)} is done and waiting for you.`);
  if (!seenOnPc(session)) {
    session.attention = true;
    session.attentionKind = kind;
    session.attentionMessage = message;
    refreshDots();
    remoteSoon();
  }
  notify(session, own.slice(0, 200) || attentionText(session));
  remoteAttention(session, kind, message, asks ? screen.question : null);
}

// Someone is looking at this pane on the PC right now.
const seenOnPc = session => !session.el.hidden && document.hasFocus() && session.el.contains(document.activeElement);

// The bottom of the screen shows whether the agent asks to run or change something.
function attentionText(session) {
  const buffer = session.term.buffer.active;
  const bottom = [];
  for (let y = Math.max(0, buffer.baseY + buffer.cursorY - 12); y < buffer.length; y++) bottom.push(buffer.getLine(y)?.translateToString(true) || '');
  return APPROVAL.test(bottom.join('\n')) ? `${agentName(session)} needs your OK to continue.` : `${agentName(session)} is done and waiting for you.`;
}

function notify(session, body) {
  const title = session.customTitle || session.autoTitle || session.label;
  if (notifyOn) send({ type: 'notify', session: session.id, title, body, visible: !session.el.hidden });
}

function clearAttention(session) {
  if (!session.attention) return;
  session.attention = false;
  session.attentionKind = null;
  session.attentionMessage = null;
  refreshDots();
  remoteSoon();
}

function showSession(id) {
  const session = sessions.get(id);
  if (!session) return;
  if (!same(session.workspace, state.workspace)) selectWorkspace(session.workspace);
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
function setSidebar(hidden) {
  $('.app').classList.toggle('side-hidden', hidden);
  const button = $('#toggle-sidebar');
  button.title = hidden ? 'Show the sidebar' : 'Hide the sidebar';
  button.setAttribute('aria-pressed', String(hidden));
  localStorage.setItem('sidebar', hidden ? 'hidden' : 'shown');
  fitTopbar();
}

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
  button.setAttribute('aria-pressed', String(open));
  localStorage.setItem('panel', open ? 'open' : 'closed');
  fitTopbar();
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

// Dragged by its left edge. As a column it leaves the panes at least MIN_PANES px; when the window is too narrow for
// that, it floats over the panes instead (panel-overlay), so they keep their width.
const MIN_PANES = 360;
const MIN_PANEL = 280;

function setPanelWidth(width, save = true) {
  const app = $('.app');
  // The app's padding (20), the sidebar with its gap (282) and the panel's gap (10).
  const room = window.innerWidth - 20 - (app.classList.contains('side-hidden') ? 0 : 282) - 10 - MIN_PANES;
  const overlay = room < MIN_PANEL;
  const wanted = Math.max(MIN_PANEL, Math.round(Number(width) || 480));
  const shown = Math.min(wanted, overlay ? window.innerWidth - 20 : room);
  app.classList.toggle('panel-overlay', overlay);
  app.style.setProperty('--panel-width', shown + 'px');
  if (save) localStorage.setItem('panelWidth', String(shown));
  fitTopbar();
}

$('#panel-resize').addEventListener('pointerdown', event => {
  event.preventDefault();
  const handle = event.currentTarget;
  handle.setPointerCapture(event.pointerId);
  document.body.classList.add('resizing-panel');
  const right = panel.getBoundingClientRect().right;
  const move = moveEvent => setPanelWidth(right - moveEvent.clientX, false);
  handle.addEventListener('pointermove', move);
  handle.addEventListener('lostpointercapture', () => {
    handle.removeEventListener('pointermove', move);
    document.body.classList.remove('resizing-panel');
    setPanelWidth(panel.getBoundingClientRect().width);
  }, { once: true });
});
window.addEventListener('resize', () => setPanelWidth(localStorage.getItem('panelWidth'), false));

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
  const key = JSON.stringify([message.error, message.branch, message.total, files]);
  if (key !== changesKey) {
    changesKey = key;
    changesFiles = files;
    $('#changes-branch').textContent = message.error ? '' : message.branch || '';
    $('#changes-count').textContent = message.error || !files.length ? ''
      : message.total > files.length ? `${files.length} of ${message.total} files` : `${files.length} file${files.length === 1 ? '' : 's'}`;
    const list = $('#changes-list');
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
      `<button class="icon-btn small" data-act="image-paste" title="Paste the path into the pane, so the agent can look at it">${icon('terminal', 15)}</button>` +
      `<button class="icon-btn small" data-act="image-reveal" title="Show in folder">${icon('folder', 15)}</button>` +
      `<button class="icon-btn small" data-act="image-close" title="Close (Esc)">${icon('close', 15)}</button>` +
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
    { label: 'Text size…', sub: `${fontSize} px · Ctrl+= / Ctrl+-`, icon: 'text', run: textSizeDialog },
    {
      label: notifyOn ? 'Turn off Windows notifications' : 'Turn on Windows notifications',
      sub: 'When an agent is done or needs you, for every pane', icon: 'bell',
      run: () => {
        notifyOn = !notifyOn;
        localStorage.setItem('notifications', notifyOn ? 'on' : 'off');
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
  closePhone();
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

// ---------- phone remote: what the phone sees ----------

// The phone app talks to Remote.cs, which asks this page for everything that comes from the panes
// (docs/phone-remote/REMOTE_API.md). Requests carry a requestId and always get a remoteReply.
const PALETTE = Remote.palette(THEME);
const NOT_OPEN = "That agent isn't open on the PC any more.";
const HAS_ENDED = 'That agent has ended. Resume it or start a new chat.';
const BUSY = 'Agent Arcade is busy, try again.';
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
    send({ type: 'remoteScreen', screen: screenOf(session, watch.lines) });
  } catch { /* the pane is being closed */ }
}

// ---------- phone remote: requests ----------

const remoteOk = (body = { ok: true }, status = 200) => ({ status, body });
const remoteRefuse = (status, error, body) => ({ status, error, body });

async function remoteRequest(message, handler) {
  let result;
  try {
    result = typeof message.device === 'string' && remote.removed.has(message.device)
      ? remoteRefuse(401, "This phone isn't paired any more. Pair it again on your PC.")
      : await handler(message);
  } catch {
    result = remoteRefuse(500, 'Something went wrong on the PC.');
  }
  const reply = { type: 'remoteReply', requestId: message.requestId, status: result.status };
  if (result.body) reply.body = result.body;
  if (result.error) reply.error = result.error;
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
    if (ticket.cancelled || Date.now() - ticket.at + duration > JOB_WAIT_MS) return remoteRefuse(503, BUSY);
    if (sessions.get(session.id) !== session) return remoteRefuse(404, NOT_OPEN);
    if (!session.running) return remoteRefuse(409, HAS_ENDED);
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
  if (!session) return remoteRefuse(404, NOT_OPEN);
  return remoteOk(screenOf(session, clampLines(message.lines)));
}

// Typed like a paste at the PC, so titles, amber and "done" keep working through term.onData.
function remoteInput(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN);
  if (!session.running) return remoteRefuse(409, HAS_ENDED);
  const text = typeof message.text === 'string' ? message.text.replace(CONTROL, '') : '';
  return inOrder(session, message, async ticket => {
    if (text) session.term.paste(text);
    if (message.submit !== true) return remoteOk();
    await wait(150);
    if (ticket.cancelled) return remoteRefuse(503, BUSY);
    if (!session.running) return remoteRefuse(409, HAS_ENDED);
    session.term.input('\r');
    return remoteOk();
  }, message.submit === true ? 150 : 0);
}

function remoteKeys(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN);
  if (!session.running) return remoteRefuse(409, HAS_ENDED);
  const keys = Array.isArray(message.keys) ? message.keys : [];
  if (!keys.length || keys.length > 32) return remoteRefuse(400, 'Send between 1 and 32 keys.');
  const unknown = keys.findIndex(name => Remote.keySequence(name, false) === null);
  if (unknown >= 0) return remoteRefuse(400, `Unknown key: ${Remote.cut(String(keys[unknown]).replace(CONTROL, ''), 40)}`);
  return inOrder(session, message, async ticket => {
    for (const [i, name] of keys.entries()) {
      if (i) await wait(40);
      if (ticket.cancelled) return remoteRefuse(503, BUSY);
      if (!session.running) return remoteRefuse(409, HAS_ENDED);
      session.term.input(Remote.keySequence(name, session.term.modes.applicationCursorKeysMode));
    }
    return remoteOk();
  }, (keys.length - 1) * 40);
}

function remoteStart(message) {
  const account = accountOf(message.account);
  const agent = agentOf(account);
  if (!account || !agent) return remoteRefuse(404, "That account isn't on the PC any more.");
  const workspace = workspaceOf(message.workspace);
  if (!workspace) return remoteRefuse(404, "That project isn't in the sidebar on the PC any more.");
  if (!workspace.exists) return remoteRefuse(409, `The ${workspace.name} folder is missing on the PC.`);
  const problem = notReady(agent);
  if (problem) return remoteRefuse(409, problem);
  const session = openSession(account.key, 'start', null, { workspace: workspace.path, remote: true });
  return session ? remoteOk({ id: session.id }, 201) : remoteRefuse(409, `${agent.name} couldn't start on the PC.`);
}

function remoteResume(message) {
  const chat = chatOf(message.chat);
  if (!chat) return remoteRefuse(404, 'That chat is no longer in the history.');
  const holders = [...sessions.values()].filter(session => session.chat === chat.id || (session.running && session.resumeOf === chat.id));
  const running = holders.find(session => session.running);
  if (running) return remoteRefuse(409, 'That chat is already open.', { id: running.id });
  const open = holders[0];
  const problem = resumeProblem(chat);
  if (problem) return remoteRefuse(409, problem);
  if (!workspaceOf(chat.workspace)) return remoteRefuse(409, `Add ${folderName(chat.workspace)} back to the sidebar on the PC first.`);
  const agent = state.agents.find(item => item.name === chat.agent);
  const waiting = notReady(agent);
  if (waiting) return remoteRefuse(409, waiting);
  // A pane where this chat ended makes way for it, like its Resume button on the PC. If the PC had that pane
  // maximized, the new one is.
  const order = open?.order;
  const wasMaxed = !!open && maxed === open.id;
  if (open) closeSession(open, true);
  const session = openSession(chat.account, 'resume', chat, { remote: true });
  if (!session) return remoteRefuse(409, `${agent.name} couldn't start on the PC.`);
  if (order) session.order = order;
  if (wasMaxed) maxed = session.id;
  if (order || wasMaxed) renderPanes();
  return remoteOk({ id: session.id }, 201);
}

function remoteStop(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN);
  if (!session.running) return remoteRefuse(409, HAS_ENDED);
  // The C# side stops the agent and sends no exit for it.
  send({ type: 'close', session: session.id });
  endSession(session, { phone: true });
  return remoteOk();
}

async function remoteClose(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN);
  if (session.running) return remoteRefuse(409, 'Stop the agent first.');
  await closeSession(session, true);
  return remoteOk();
}

function remoteRename(message) {
  const session = sessions.get(message.id);
  if (!session) return remoteRefuse(404, NOT_OPEN);
  if (!isChat(session)) return remoteRefuse(400, 'Only chats can be renamed.');
  setCustomTitle(session, typeof message.title === 'string' ? message.title.replace(CONTROL, ' ') : '');
  return remoteOk();
}

// ---------- phone remote: the Phone dialog ----------

let phoneView = null;

function showPhones() {
  const button = $('#phone');
  button.innerHTML = (remote.phones ? '<span class="dot on"></span>' : '') + '<span>Phone</span>';
  button.title = remote.phones ? `${remote.phones} phone${remote.phones === 1 ? '' : 's'} connected` : 'Use Agent Arcade from your phone';
}

function openPhone() {
  closeMenu();
  closePhone();
  closeHistory();
  const root = document.createElement('div');
  root.className = 'modal-backdrop';
  root.innerHTML =
    '<div class="modal phone" role="dialog" aria-modal="true">' +
    `<div class="hist-head"><h2>Phone remote</h2><button class="icon-btn" data-close title="Close">${icon('close', 15)}</button></div>` +
    '<div class="phone-body"></div>' +
    '<p class="phone-note">A paired phone can read your panes and type into your agents, like you can at this PC. ' +
    'Only pair your own phone. If Windows asks, allow Agent Arcade on private networks, and make sure your Wi-Fi is set to Private.</p></div>';
  phoneView = { root, info: null, timer: setInterval(showValidity, 10000) };
  root.addEventListener('mousedown', event => {
    if (event.target === root) closePhone();
  });
  root.addEventListener('change', event => {
    if (event.target.name === 'remote') send({ type: 'remoteEnable', on: event.target.checked });
    if (event.target.name === 'awake') send({ type: 'keepAwake', mode: event.target.value });
  });
  root.addEventListener('click', event => {
    const target = event.target;
    if (target.closest('[data-close]')) return closePhone();
    if (target.closest('[data-new-code]')) return send({ type: 'remoteNewCode' });
    const copy = target.closest('[data-link]');
    if (copy && copy.dataset.link) {
      send({ type: 'copy', text: copy.dataset.link });
      return toast('Pairing link copied. It works once, for 10 minutes.');
    }
    const remove = target.closest('[data-remove-device]');
    const device = remove && phoneView?.info?.devices?.find(item => item.id === remove.dataset.removeDevice);
    if (device) removePhone(device);
  });
  document.body.append(root);
  renderPhone();
  root.querySelector('[data-close]').focus();
  send({ type: 'remoteDialog' });
}

function closePhone() {
  if (!phoneView) return;
  clearInterval(phoneView.timer);
  phoneView.root.remove();
  phoneView = null;
}

function phoneInfo(message) {
  if (!phoneView) return;
  phoneView.info = message;
  renderPhone();
}

function codeState(info) {
  if (info.code === 'used') return `This code was used by ${info.pairedName || 'a phone'}.`;
  if (info.code === 'expired') return 'This code has expired.';
  if (info.code === 'cancelled') return 'Too many wrong codes. Make a new one.';
  if (info.code !== 'valid') return 'Press New code to pair a phone.';
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
  const focused = ['remote', 'awake'].includes(document.activeElement?.name) ? document.activeElement.name : null;
  const valid = info.code === 'valid' && Number(info.expiresAt) > Date.now();
  const qr = /^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(info.qr || '') ? info.qr : '';
  const devices = Array.isArray(info.devices) ? info.devices : [];
  const addresses = Array.isArray(info.addresses) ? info.addresses : [];
  let html =
    `<label class="switch"><input type="checkbox" name="remote"${info.enabled ? ' checked' : ''}>` +
    '<span class="switch-track"></span><span>Let my phone watch and drive these agents</span></label>';
  if (info.warning) html += `<p class="phone-warning">${esc(info.warning)}</p>`;
  if (info.error) html += `<p class="phone-error">${esc(info.error)}</p>`;
  if (info.enabled && info.running) {
    html += '<div class="phone-pair">' +
      (qr ? `<div class="qr-card${valid ? '' : ' stale'}"><img src="${esc(qr)}" alt="Pairing code" draggable="false"></div>` : '') +
      '<div class="phone-facts"><span class="phone-label">PC fingerprint</span>' +
      `<span class="fingerprint">${esc(info.fingerprint || '')}</span>` +
      `<span class="phone-valid">${esc(codeState(info))}</span>` +
      '<span class="muted">Scan the code with Agent Arcade Remote on your phone, and check that it shows the same fingerprint.</span>' +
      '<div class="phone-actions"><button class="ghost-btn small" data-new-code>New code</button>' +
      `<button class="ghost-btn small" data-link="${esc(info.link || '')}"${valid && info.link ? '' : ' disabled'}>Copy link</button></div></div></div>`;
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
  html += awakeSection(info.awake);
  if (devices.length) {
    html += '<div class="phone-section"><span class="phone-label">Paired phones</span>' + devices.map(device =>
      `<div class="device-row"><span class="dot ${device.connected ? 'on' : 'off'}"></span>` +
      `<span class="hist-text"><span class="device-name ellipsis">${esc(device.name)}</span>` +
      `<span class="hist-sub ellipsis">${esc(seenText(device))}</span></span>` +
      `<button class="ghost-btn small" data-remove-device="${esc(device.id)}">Remove</button></div>`).join('') + '</div>';
  }
  body.innerHTML = html;
  if (focused) body.querySelector(`[name="${focused}"]`)?.focus();
}

// Tailscale hands out 100.64.0.0/10 addresses.
function isTailscale(host) {
  const [a, b] = String(host).split(':')[0].split('.').map(Number);
  return a === 100 && b >= 64 && b <= 127;
}

// Whether Windows may sleep while the agents work, or while the phone wants to reach the PC.
function awakeSection(awake) {
  if (!awake) return '';
  const modes = [
    ['agents', 'While agents run'],
    ['remote', 'While agents run or Phone remote is on'],
    ['off', 'Never, Windows decides'],
  ];
  let html = '<div class="phone-section"><span class="phone-label">Keep this PC awake</span>' +
    `<select name="awake">${modes.map(([value, label]) =>
      `<option value="${value}"${awake.mode === value ? ' selected' : ''}>${label}</option>`).join('')}</select>` +
    `<span class="muted">${awake.on
      ? 'Windows won\'t go to sleep on its own now. The screen still turns off.'
      : awake.mode === 'off' ? 'Windows sleeps as its power settings say, also while agents work.'
      : 'Windows may go to sleep now: no agent is running.'}</span>`;
  if (awake.mode !== 'off' && awake.onBattery) {
    html += '<span class="phone-warning">On battery, Windows still goes to sleep a few minutes after its sleep time. Plug the PC in.</span>';
  }
  if (awake.mode !== 'off' && awake.lidSleeps) {
    html += '<span class="muted">Closing the lid still puts it to sleep. To keep it running with the lid closed, set ' +
      '"When I close the lid" to "Do nothing" for Plugged in, under Settings > System > Power & battery > Lid & power button controls.</span>';
  }
  return html + '</div>';
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

// Like the history: a menu or dialog on top closes first.
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && phoneView && !openMenu && !document.querySelector('.modal:not(.phone)')) closePhone();
}, true);

// ---------- wiring ----------

$('#workspaces').addEventListener('click', event => {
  const more = event.target.closest('[data-workspace-menu]');
  if (more) return workspaceMenu(more.dataset.workspaceMenu, more);
  const fold = event.target.closest('[data-fold-workspace]');
  if (fold) {
    toggleFold('ws:' + fold.dataset.foldWorkspace);
    return renderWorkspaces();
  }
  const child = event.target.closest('[data-session]');
  if (child) {
    const session = sessions.get(child.dataset.session);
    if (session && !same(session.workspace, state.workspace)) selectWorkspace(session.workspace);
    return focusSession(child.dataset.session);
  }
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
    default: return imageAction(session, button);
  }
});

$('#grid').addEventListener('dblclick', event => {
  const label = event.target.closest('.pane-title');
  if (label) editTitle(sessions.get(label.closest('.pane').dataset.session));
});

$('#logo').innerHTML = LOGO;
$('#add-workspace').innerHTML = icon('plus', 15);
$('#new-project').innerHTML = icon('folder-plus', 15);
$('#refresh').innerHTML = icon('refresh', 15);
$('#add-account').innerHTML = icon('plus', 15);
$('#open-unity').innerHTML = icon('cube', 15) + '<span>Open Unity</span>';
$('#open-folder').innerHTML = icon('folder', 15) + '<span>Folder</span>';
$('#sync').innerHTML = icon('doc', 15) + '<span>Shared instructions</span>';
$('#new-agent').innerHTML = icon('plus', 15) + '<span>New agent</span>' + icon('chevron', 14);
$('#history').innerHTML = icon('history', 15) + '<span>History</span>';
$('#images').innerHTML = icon('image', 15) + '<span>Images</span>';
$('#images-refresh').innerHTML = icon('refresh', 14);
renderAgents();

$('#add-workspace').addEventListener('click', () => send({ type: 'addWorkspace' }));
$('#new-project').addEventListener('click', () => newProject());
$('#refresh').addEventListener('click', () => send({ type: 'refresh' }));
$('#add-account').addEventListener('click', () => addAccount());
$('#toggle-agents').addEventListener('click', () => {
  toggleFold('agents');
  renderAgents();
});
$('#edit-agents').addEventListener('click', () => send({ type: 'editAgents' }));
$('#text-size').addEventListener('click', textSizeDialog);
$('#phone').addEventListener('click', openPhone);
showPhones();
$('#open-unity').addEventListener('click', () => send({ type: 'openUnity' }));
$('#open-folder').addEventListener('click', () => send({ type: 'openFolder' }));
$('#new-agent').addEventListener('click', event => newAgentMenu(event.currentTarget));
$('#history').addEventListener('click', () => openHistory());
$('#images').addEventListener('click', toggleImages);
$('#images-refresh').addEventListener('click', () => {
  imagesKey = '';
  loadImages();
});
$('#toggle-sidebar').innerHTML = icon('sidebar', 16);
$('#toggle-sidebar').addEventListener('click', () => {
  setSidebar(!$('.app').classList.contains('side-hidden'));
  setPanelWidth(localStorage.getItem('panelWidth'), false);
});
setSidebar(localStorage.getItem('sidebar') === 'hidden');

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
        const before = state.workspace;
        state = message;
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
