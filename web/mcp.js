'use strict';

// Settings → MCP servers: the table of each agent's MCP servers (Mcp.cs reads them from each agent's own list command).
// McpView.render(container, data, onRefresh) draws data, the page's mcpList message, into container; null (or no
// message yet) shows that the list is being checked. "Check again" calls onRefresh, which sends {type:'mcpList'}.
// Only names, scopes and statuses ever arrive here. No storage, no network, no globals except McpView.
const McpView = (() => {
  // The same letters and colours as MONOGRAMS and BRAND_COLORS in app.js.
  const MONOGRAMS = { claude: 'CC', codex: 'CX', gemini: 'GM', agy: 'AG', copilot: 'CP', 'cursor-agent': 'CU' };
  const COLORS = { claude: '#d97757', codex: '#10a37f', gemini: '#4f8cff', agy: '#3fb6c6', copilot: '#a371f7', 'cursor-agent': '#7c8aa5' };
  // Mcp.cs statuses, as the dot's shape and its word.
  const STATUS = {
    connected: 'connected',
    failed: 'failed',
    needsAuth: 'needs sign-in',
    disabled: 'off',
    unknown: 'not checked',
  };
  const SCOPES = { user: 'user', project: 'project', local: 'local' };
  const NOTE = "Only names and status are shown. Agent Arcade never reads a server's command line, keys or environment.";

  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  function letters(agent) {
    const command = String(agent.command || '').toLowerCase();
    return Object.hasOwn(MONOGRAMS, command) ? MONOGRAMS[command] : (String(agent.agent || '?').replace(/[^A-Za-z0-9]/g, '').slice(0, 2) || '?');
  }

  function color(agent) {
    const command = String(agent.command || '').toLowerCase();
    if (Object.hasOwn(COLORS, command)) return COLORS[command];
    let hash = 0;
    for (const c of String(agent.agent || '?')) hash = (hash * 31 + c.charCodeAt(0)) >>> 0;
    return `hsl(${hash % 360} 55% 58%)`;
  }

  // "Claude Code" for the main account, "Claude Code (work)" for another one.
  const label = agent => String(agent.agent || '') + (agent.account && agent.account !== 'main' ? ' (' + agent.account + ')' : '');

  function mono(agent) {
    const name = label(agent);
    return `<span class="mcp-mono" style="--c:${esc(color(agent))}" role="img" aria-label="${esc(name)}" title="${esc(name)}">${esc(letters(agent))}</span>`;
  }

  function when(at) {
    const date = new Date(at);
    if (!at || Number.isNaN(date.getTime())) return '';
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  // What the view shows for data: the server rows, a line for each agent without rows or with a note, and the time.
  function model(data) {
    if (!data || !Array.isArray(data.agents)) return { checking: true, rows: [], others: [], time: '' };
    const rows = [];
    const others = [];
    for (const agent of data.agents) {
      if (!agent || typeof agent !== 'object') continue;
      const servers = agent.ok && Array.isArray(agent.servers) ? agent.servers.filter(server => server && server.name) : [];
      for (const server of servers) {
        const status = Object.hasOwn(STATUS, server.status) ? server.status : 'unknown';
        rows.push({ agent, name: String(server.name), scope: Object.hasOwn(SCOPES, server.scope) ? SCOPES[server.scope] : '', status, word: STATUS[status] });
      }
      if (!agent.ok) others.push({ agent, text: agent.note || "couldn't read the list", problem: true });
      else if (servers.length === 0) others.push({ agent, text: 'no MCP servers' + (agent.note ? ' · ' + agent.note : ''), problem: false });
      else if (agent.note) others.push({ agent, text: agent.note, problem: false });
    }
    return { checking: false, rows, others, time: when(data.at), agents: data.agents.length };
  }

  function html(data, checking = false) {
    const view = model(data);
    const busy = checking || view.checking;
    const body = view.rows.length
      ? view.rows.map(row =>
        '<tr>' +
        `<td class="mcp-name">${esc(row.name)}</td>` +
        `<td><span class="mcp-agent">${mono(row.agent)}${row.agent.account && row.agent.account !== 'main' ? `<span class="mcp-account">${esc(row.agent.account)}</span>` : ''}</span></td>` +
        `<td class="mcp-scope">${row.scope ? esc(row.scope) : '<span class="mcp-faint" aria-hidden="true">–</span><span class="mcp-sr">not given</span>'}</td>` +
        `<td><span class="mcp-status"><i class="mcp-dot ${row.status}" aria-hidden="true"></i>${esc(row.word)}</span></td>` +
        '</tr>').join('')
      : `<tr class="mcp-empty"><td colspan="4">${esc(view.checking ? "Checking each agent's MCP servers…"
        : view.agents ? 'No MCP servers yet.' : 'None of the agents with an MCP list command is installed.')}</td></tr>`;
    const others = view.others.length
      ? '<ul class="mcp-others">' + view.others.map(other =>
        `<li${other.problem ? ' class="mcp-problem"' : ''}>${mono(other.agent)}<span><b>${esc(label(other.agent))}</b> · ${esc(other.text)}</span></li>`).join('') + '</ul>'
      : '';
    const status = busy ? 'Checking… this takes up to 20 seconds.'
      : (view.time ? 'Checked at ' + view.time + ' · ' : '') + "from each agent's own mcp list command, in this project.";
    return '<div class="mcp-scroll"><table class="mcp-table">' +
      '<caption class="mcp-sr">MCP servers per agent</caption>' +
      '<thead><tr><th scope="col">Server</th><th scope="col">Agent</th><th scope="col">Scope</th><th scope="col">Status</th></tr></thead>' +
      `<tbody>${body}</tbody></table></div>` + others +
      // aria-disabled rather than disabled, so the button keeps the keyboard focus while it waits.
      `<div class="mcp-actions"><button type="button" class="mcp-refresh" aria-disabled="${busy}">${busy ? 'Checking…' : 'Check again'}</button>` +
      `<span class="mcp-when" role="status" aria-live="polite">${esc(status)}</span></div>` +
      `<p class="mcp-note">${esc(NOTE)}</p>`;
  }

  const CSS = `
.mcp-view { display: grid; gap: 14px; min-width: 0; }
.mcp-scroll { overflow-x: auto; min-width: 0; }
.mcp-table { width: 100%; border-collapse: collapse; font-size: 13px; }
.mcp-table th, .mcp-table td { padding: 9px 10px; text-align: left; vertical-align: middle; border-bottom: 1px solid var(--border, #26262e); }
.mcp-table th { color: var(--faint, #6c6c79); font-size: 11px; font-weight: 600; letter-spacing: .06em; text-transform: uppercase; }
.mcp-name { color: var(--text, #ececf1); font: 12px/1.45 var(--mono, Consolas, monospace); overflow-wrap: anywhere; user-select: text; }
.mcp-scope { color: var(--muted, #9d9daa); }
.mcp-faint { color: var(--faint, #6c6c79); }
.mcp-empty td { color: var(--muted, #9d9daa); }
.mcp-agent { display: inline-flex; align-items: center; gap: 7px; }
.mcp-account { color: var(--muted, #9d9daa); font-size: 12px; }
.mcp-mono { --c: #888; display: inline-grid; flex: none; place-items: center; width: 20px; height: 20px; border-radius: 6px;
  color: #fff; font-size: 10px; font-weight: 700; line-height: 1; background: color-mix(in srgb, var(--c) 82%, #000);
  box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .12); }
.mcp-status { display: inline-flex; align-items: center; gap: 8px; font-size: 12.5px; white-space: nowrap; }
.mcp-dot { flex: none; width: 8px; height: 8px; border-radius: 50%; background: var(--faint, #6c6c79); }
.mcp-dot.connected { background: var(--green, #3ecf7f); box-shadow: 0 0 0 3px rgba(62, 207, 127, .15); }
.mcp-dot.failed { background: var(--red, #f26b6b); box-shadow: 0 0 0 3px rgba(242, 107, 107, .15); }
.mcp-dot.needsAuth { width: 7px; height: 7px; border-radius: 1px; background: var(--red, #f26b6b); transform: rotate(45deg); box-shadow: 0 0 0 2px rgba(242, 107, 107, .3); }
.mcp-dot.disabled { background: #4a4a54; }
.mcp-dot.unknown { background: transparent; box-shadow: inset 0 0 0 1.5px var(--faint, #6c6c79); }
.mcp-others { display: grid; gap: 6px; margin: 0; padding: 0; list-style: none; color: var(--muted, #9d9daa); font-size: 12.5px; }
.mcp-others li { display: flex; align-items: center; gap: 8px; min-width: 0; }
.mcp-others b { color: var(--text, #ececf1); font-weight: 500; }
.mcp-problem { color: var(--muted, #9d9daa); }
.mcp-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
.mcp-refresh { padding: 7px 12px; color: var(--muted, #9d9daa); font-size: 13px; font-weight: 500; background: var(--panel, #141418);
  border: 1px solid var(--border-2, #34343e); border-radius: 9px; cursor: pointer; }
.mcp-refresh:hover:not([aria-disabled="true"]) { color: var(--text, #ececf1); background: var(--hover, #22222a); }
.mcp-refresh[aria-disabled="true"] { cursor: default; opacity: .55; }
.mcp-refresh:focus-visible { outline: 2px solid var(--accent-2, #f0a080); outline-offset: 2px; }
.mcp-when { color: var(--faint, #6c6c79); font-size: 12px; }
.mcp-note { margin: 0; color: var(--faint, #6c6c79); font-size: 12px; }
.mcp-sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
`;

  function style() {
    if (typeof document === 'undefined' || document.getElementById('mcp-view-style')) return;
    const element = document.createElement('style');
    element.id = 'mcp-view-style';
    element.textContent = CSS;
    document.head.appendChild(element);
  }

  function render(container, data, onRefresh) {
    if (!container) return;
    style();
    container.classList.add('mcp-view');
    draw(container, data, false, onRefresh);
  }

  function draw(container, data, checking, onRefresh) {
    // A focused Check again keeps the focus when the view is drawn again (Settings traps the focus inside it).
    const active = document.activeElement;
    const refocus = !!active && active.classList.contains('mcp-refresh') && container.contains(active);
    container.innerHTML = html(data, checking);
    const button = container.querySelector('.mcp-refresh');
    if (typeof onRefresh !== 'function') {
      button.hidden = true;
      return;
    }
    if (refocus) button.focus();
    button.addEventListener('click', () => {
      if (button.getAttribute('aria-disabled') === 'true') return;
      // Shows the check at once; the next render brings the answer.
      draw(container, data, true, onRefresh);
      onRefresh();
    });
  }

  return { render, html, model, STATUS, NOTE };
})();

if (typeof window !== 'undefined') window.McpView = McpView;
if (typeof module !== 'undefined') module.exports = McpView;
