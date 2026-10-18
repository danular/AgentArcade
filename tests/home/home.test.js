'use strict';

// Home and Recent helpers in web/home.js. Run: node --test tests/home/home.test.js
const test = require('node:test');
const assert = require('node:assert/strict');
const HomeData = require('../../web/home.js');

// A fixed "now": Friday 16 Oct 2026, 15:00 local time.
const now = new Date(2026, 9, 16, 15, 0).getTime();
const at = (days, hour = 12, minute = 0) => new Date(2026, 9, 16 - days, hour, minute).toISOString();
const chat = (workspace, days, title = 'A chat', hour = 12) => ({ workspace, started: at(days, hour), title, agent: 'Claude Code', accountName: 'main' });

test('activity counts chats per day for one project, oldest day first', () => {
  const chats = [
    chat('C:\\Repos\\River', 0), chat('C:\\Repos\\River', 0), chat('c:\\repos\\river', 1, 'case differs'),
    chat('C:\\Repos\\River', 9), chat('C:\\Repos\\River', 10, 'too old'), chat('C:\\Repos\\Stone', 0, 'other project'),
    { workspace: 'C:\\Repos\\River', started: 'not a date' },
  ];
  assert.deepEqual(HomeData.activity(chats, 'C:\\Repos\\River', now), [1, 0, 0, 0, 0, 0, 0, 0, 1, 2]);
  assert.deepEqual(HomeData.activity([], 'C:\\Repos\\River', now, 3), [0, 0, 0]);
  // 23:59 yesterday is yesterday, not today.
  assert.deepEqual(HomeData.activity([chat('X', 1, 'late', 23)], 'X', now, 2), [1, 0]);
});

test('sparkline points span the box; an empty project is a flat line at the bottom', () => {
  assert.deepEqual(HomeData.sparkPoints([0, 2, 1], 100, 26, 3), [[0, 23], [50, 3], [100, 13]]);
  assert.deepEqual(HomeData.sparkPoints([0, 0], 100, 26, 3), [[0, 23], [100, 23]]);
});

test('recent chats group into Today, Yesterday and Earlier and keep their order', () => {
  const chats = [chat('A', 0, 'today 1'), chat('B', 0, 'today 2'), chat('A', 1, 'yesterday'), chat('C', 5, 'earlier'), chat('A', 40, 'long ago')];
  const groups = HomeData.groupRecent(chats, now);
  assert.deepEqual(groups.map(group => [group.label, group.chats.map(item => item.title)]), [
    ['Today', ['today 1', 'today 2']], ['Yesterday', ['yesterday']], ['Earlier', ['earlier', 'long ago']],
  ]);
  assert.deepEqual(HomeData.groupRecent([chat('A', 3)], now).map(group => group.label), ['Earlier']);
  assert.deepEqual(HomeData.groupRecent([], now), []);
  // A chat started long ago and continued today counts as today.
  const continued = { ...chat('A', 30, 'continued'), lastActive: at(0, 9) };
  assert.equal(HomeData.activeOf(continued), at(0, 9));
  assert.deepEqual(HomeData.groupRecent([continued], now).map(group => group.label), ['Today']);
  assert.equal(HomeData.activeOf({ started: at(2), ended: at(1) }), at(1));
});

test('short times: the clock today, the weekday this week, the date before that', () => {
  assert.equal(HomeData.shortTime(at(0, 14, 2), now, 'en-GB'), '14:02');
  assert.equal(HomeData.shortTime(at(1), now, 'en-GB'), 'Thu');
  assert.equal(HomeData.shortTime(at(4), now, 'en-GB'), 'Mon');
  assert.equal(HomeData.shortTime(at(7), now, 'en-GB'), '9 Oct');
  assert.equal(HomeData.shortTime(new Date(2025, 11, 30).toISOString(), now, 'en-GB'), '30 Dec 2025');
  assert.equal(HomeData.shortTime('nonsense', now), '');
});

test('time ago on a project card', () => {
  assert.equal(HomeData.ago(now - 20e3, now), 'just now');
  assert.equal(HomeData.ago(now - 18 * 60e3, now), '18 min ago');
  assert.equal(HomeData.ago(now - 3 * 3600e3, now), '3 h ago');
  assert.equal(HomeData.ago(at(1), now), 'yesterday');
  assert.equal(HomeData.ago(at(3), now), '3 days ago');
  assert.equal(HomeData.ago(at(9), now), 'last week');
  assert.match(HomeData.ago(at(30), now, 'en-GB'), /^on 16 Sept?$/);
  assert.equal(HomeData.ago(undefined, now), '');
});

test('the footer status line', () => {
  assert.equal(HomeData.statusLine({ needs: 1, working: 2 }), '1 needs you · 2 working');
  assert.equal(HomeData.statusLine({ needs: 0, working: 3 }), '3 working');
  assert.equal(HomeData.statusLine({ needs: 2 }), '2 needs you');
  assert.equal(HomeData.statusLine({}), 'All quiet');
});

test('Home shows Chats (Start without a project) first, also before it is listed, and only once', () => {
  const chats = 'C:\\Users\\ana\\AgentArcade\\Chats';
  const river = { path: 'C:\\Repos\\River', name: 'River', exists: true, unity: false };
  assert.deepEqual(HomeData.projectCards([river], chats), [{ path: chats, name: 'Chats', exists: true, unity: false }, river]);
  const listed = [{ path: 'c:\\users\\ana\\agentarcade\\chats', name: 'Chats', exists: false, unity: false }, river];
  assert.equal(HomeData.projectCards(listed, chats), listed);
  // Before C#'s first state the page has no chatsFolder.
  assert.deepEqual(HomeData.projectCards([river], undefined), [river]);
  assert.deepEqual(HomeData.projectCards(undefined, chats).map(card => card.name), ['Chats']);
});

test('short paths start with ~ under the user folder', () => {
  assert.equal(HomeData.shortPath('C:\\Users\\ana\\source\\repos\\River'), '~\\source\\repos\\River');
  assert.equal(HomeData.shortPath('c:\\users\\ana'), '~');
  assert.equal(HomeData.shortPath('D:\\Games\\Stone'), 'D:\\Games\\Stone');
  assert.equal(HomeData.shortPath('C:\\Users2\\x'), 'C:\\Users2\\x');
});

test('search matches every word in title, agent, account or project', () => {
  const item = { title: 'Fix the foam near the bridge', agent: 'Codex', accountName: 'second', workspace: 'C:\\Repos\\RiverRun' };
  assert.equal(HomeData.matches(item, ''), true);
  assert.equal(HomeData.matches(item, 'FOAM river'), true);
  assert.equal(HomeData.matches(item, 'codex second'), true);
  assert.equal(HomeData.matches(item, 'foam stone'), false);
  assert.equal(HomeData.matches({ title: null }, 'x'), false);
});
