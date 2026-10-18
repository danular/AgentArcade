'use strict';

// Settings → Agents and accounts (web/accounts.js): Add account's suggested name and the "Same login as …" line.
// Run: node --test tests/accounts.test.js
const test = require('node:test');
const assert = require('node:assert/strict');
const AccountsData = require('../web/accounts.js');

// The page's account list as MainForm.PostState sends it, with made-up example.com emails.
const account = (agent, name, email = '', signedIn = email ? true : null) =>
  ({ key: agent + '|' + name, agent, name, main: name === 'main', signedIn, email });
const CLAUDE = 'Claude Code';
const CODEX = 'Codex';

test('Add account suggests the next free name instead of always "second"', () => {
  assert.equal(AccountsData.nextName([account(CLAUDE, 'main')], CLAUDE), 'second');
  assert.equal(AccountsData.nextName([account(CLAUDE, 'main'), account(CLAUDE, 'second')], CLAUDE), 'third');
  // A gap is filled first; case doesn't count, as for the folder names.
  assert.equal(AccountsData.nextName([account(CLAUDE, 'main'), account(CLAUDE, 'Third')], CLAUDE), 'second');
  assert.equal(AccountsData.nextName([account(CLAUDE, 'main'), account(CLAUDE, 'SECOND'), account(CLAUDE, 'third')], CLAUDE), 'fourth');
  // Other agents' names don't count.
  assert.equal(AccountsData.nextName([account(CODEX, 'second'), account(CLAUDE, 'main')], CLAUDE), 'second');
  // No list yet: main is always there, so the next one is second.
  assert.equal(AccountsData.nextName(null, CLAUDE), 'second');
  // After tenth: account11, account12, …
  const ten = ['main', ...AccountsData.ORDINALS].map(name => account(CLAUDE, name));
  assert.equal(AccountsData.nextName(ten, CLAUDE), 'account11');
  assert.equal(AccountsData.nextName([...ten, account(CLAUDE, 'account11')], CLAUDE), 'account12');
  // Every suggestion passes the name rule the page and AccountStore.ValidName use.
  for (const name of [...AccountsData.ORDINALS, 'account11']) assert.match(name, /^[A-Za-z0-9][A-Za-z0-9._-]{0,30}$/);
});

test('a name is taken by main and by the same agent only, without case', () => {
  const list = [account(CLAUDE, 'main'), account(CLAUDE, 'work')];
  assert.equal(AccountsData.taken(list, CLAUDE, 'Main'), true);
  assert.equal(AccountsData.taken(list, CLAUDE, ' WORK '), true);
  assert.equal(AccountsData.taken(list, CODEX, 'work'), false);
  assert.equal(AccountsData.taken(list, CLAUDE, 'second'), false);
});

test('two accounts of one agent with the same email both say so', () => {
  const list = [
    account(CLAUDE, 'main', 'daan@example.com'),
    account(CLAUDE, 'second', 'Daan@Example.com '),
    account(CLAUDE, 'third', 'other@example.com'),
    // The same email in another agent is a different subscription.
    account(CODEX, 'main', 'daan@example.com'),
  ];
  assert.deepEqual(AccountsData.sameLogins(list), { 'Claude Code|main': ['second'], 'Claude Code|second': ['main'] });
  assert.equal(AccountsData.sameLoginText(['second']), 'Same login as second');
});

test('three accounts with one login each name the other two', () => {
  const list = ['main', 'second', 'third'].map(name => account(CLAUDE, name, 'daan@example.com'));
  const same = AccountsData.sameLogins(list);
  assert.deepEqual(same['Claude Code|main'], ['second', 'third']);
  assert.deepEqual(same['Claude Code|third'], ['main', 'second']);
  assert.equal(AccountsData.sameLoginText(same['Claude Code|main']), 'Same login as second and third');
  assert.equal(AccountsData.sameLoginText(['main', 'second', 'third']), 'Same login as main, second and third');
  assert.equal(AccountsData.sameLoginText([]), '');
});

test('no email, not signed in or still checking is no match', () => {
  const list = [
    // Codex with an API key: signed in, no email.
    account(CODEX, 'main', '', true),
    account(CODEX, 'second', '', true),
    // Signed out, or not known yet, with an email left over: not compared.
    account(CLAUDE, 'main', 'daan@example.com'),
    account(CLAUDE, 'second', 'daan@example.com', false),
    account(CLAUDE, 'third', 'daan@example.com', null),
  ];
  assert.deepEqual(AccountsData.sameLogins(list), {});
  assert.deepEqual(AccountsData.sameLogins(null), {});
});
