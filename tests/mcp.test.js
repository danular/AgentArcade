'use strict';

// Settings → MCP servers (web/mcp.js): what the table shows for Mcp.cs's mcpList message. Run: node --test tests/mcp.test.js
const test = require('node:test');
const assert = require('node:assert/strict');
const McpView = require('../web/mcp.js');

const message = {
  type: 'mcpList',
  at: '2026-10-16T14:02:00+02:00',
  agents: [
    {
      agent: 'Claude Code', command: 'claude', account: 'main', ok: true, note: null, servers: [
        { name: 'github', scope: null, status: 'connected' },
        { name: 'figma', scope: null, status: 'needsAuth' },
        { name: 'unity', scope: 'project', status: 'unknown' },
      ],
    },
    { agent: 'Claude Code', command: 'claude', account: 'work', ok: true, note: null, servers: [{ name: 'sentry', scope: 'user', status: 'disabled' }] },
    { agent: 'Codex', command: 'codex', account: 'main', ok: true, note: 'Codex lists its servers without connecting to them', servers: [{ name: 'docs', scope: null, status: 'unknown' }] },
    { agent: 'Gemini CLI', command: 'gemini', account: 'main', ok: true, note: null, servers: [] },
    { agent: 'Cursor', command: 'cursor-agent', account: 'main', ok: false, note: 'no answer within 20 s', servers: [] },
    { agent: 'Antigravity CLI', command: 'agy', account: 'main', ok: false, note: 'not available', servers: [] },
  ],
};

test('every server is a row with its agent, scope and status word', () => {
  const view = McpView.model(message);
  assert.equal(view.checking, false);
  assert.deepEqual(view.rows.map(row => [row.name, row.agent.agent, row.scope, row.word]), [
    ['github', 'Claude Code', '', 'connected'],
    ['figma', 'Claude Code', '', 'needs sign-in'],
    ['unity', 'Claude Code', 'project', 'not checked'],
    ['sentry', 'Claude Code', 'user', 'off'],
    ['docs', 'Codex', '', 'not checked'],
  ]);
});

test('agents without servers, with a note or without an answer get a line of their own', () => {
  const view = McpView.model(message);
  assert.deepEqual(view.others.map(other => [other.agent.agent, other.text, other.problem]), [
    ['Codex', 'Codex lists its servers without connecting to them', false],
    ['Gemini CLI', 'no MCP servers', false],
    ['Cursor', 'no answer within 20 s', true],
    ['Antigravity CLI', 'not available', true],
  ]);
});

test('the table has the monograms, the other account, the status dots, Check again, the time and the note', () => {
  const html = McpView.html(message);
  for (const part of ['>CC<', '>CX<', '>GM<', '>CU<', '>AG<', 'Claude Code (work)', 'mcp-dot needsAuth', 'mcp-dot connected', 'Check again', 'Checked at ', McpView.NOTE.replace(/'/g, '&#39;')])
    assert.ok(html.includes(part), part);
  assert.ok(!html.includes('aria-disabled="true"'));
});

test('names are text, never markup, and an unknown status or scope shows as not checked', () => {
  const html = McpView.html({ at: '', agents: [{ agent: '<b>x</b>', command: 'x', account: 'main', ok: true, servers: [
    { name: '<img src=x onerror=alert(1)>', scope: '<script>', status: 'pwned" onclick="x' },
  ] }] });
  assert.ok(!html.includes('<img'));
  assert.ok(!html.includes('<script'));
  assert.ok(!html.includes('<b>x'));
  assert.ok(html.includes('&lt;img src=x onerror=alert(1)&gt;'));
  assert.ok(html.includes('mcp-dot unknown'));
  assert.ok(html.includes('not checked'));
  assert.ok(!html.includes('Checked at'));
});

test('before the first answer and while checking again, it says so and Check again waits', () => {
  assert.equal(McpView.model(null).checking, true);
  const first = McpView.html(null);
  assert.ok(first.includes("Checking each agent&#39;s MCP servers…"));
  assert.ok(first.includes('aria-disabled="true"'));
  const again = McpView.html(message, true);
  assert.ok(again.includes('github') && again.includes('aria-disabled="true"') && again.includes('Checking…'));
});

test('no installed agent, or no servers anywhere, reads as such', () => {
  assert.ok(McpView.html({ at: message.at, agents: [] }).includes('None of the agents with an MCP list command is installed.'));
  assert.ok(McpView.html({ at: message.at, agents: [message.agents[3]] }).includes('No MCP servers yet.'));
});
