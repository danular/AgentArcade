'use strict';

// Home and the sidebar's Recent list: pure helpers over the chat history the page gets (history.json's titles, folders
// and times, never chat contents). No DOM and no globals except HomeData, so Node can test them (tests/home).
const HomeData = (() => {
  const DAY = 864e5;
  const startOfDay = time => {
    const date = new Date(time);
    return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
  };
  // Whole calendar days between two times (0 = the same day), so a chat at 23:59 yesterday is 1 day ago.
  const daysBetween = (then, now) => Math.round((startOfDay(now) - startOfDay(then)) / DAY);
  const same = (a, b) => String(a || '').toLowerCase() === String(b || '').toLowerCase();
  const timeOf = chat => new Date(chat?.started).getTime();
  // When a chat was last used: started, continued or ended. The history arrives sorted by it.
  const activeOf = chat => chat?.lastActive || chat?.ended || chat?.started;

  // How many chats started in the project on each of the last `days` days, oldest first; today is the last.
  function activity(chats, path, now = Date.now(), days = 10) {
    const counts = new Array(days).fill(0);
    for (const chat of chats || []) {
      if (!same(chat.workspace, path)) continue;
      const time = timeOf(chat);
      if (!Number.isFinite(time)) continue;
      const ago = daysBetween(time, now);
      if (ago >= 0 && ago < days) counts[days - 1 - ago]++;
    }
    return counts;
  }

  // The points of a sparkline in a width x height box: the highest day touches the top margin, an empty day the bottom.
  function sparkPoints(counts, width = 100, height = 26, margin = 3) {
    const max = Math.max(1, ...counts);
    const step = counts.length > 1 ? width / (counts.length - 1) : 0;
    return counts.map((count, i) => [+(i * step).toFixed(1), +(height - margin - count / max * (height - 2 * margin)).toFixed(1)]);
  }

  // Newest first, in Today, Yesterday and Earlier by when each chat was last used. The history arrives newest first
  // already; this keeps that order.
  function groupRecent(chats, now = Date.now()) {
    const groups = [{ label: 'Today', chats: [] }, { label: 'Yesterday', chats: [] }, { label: 'Earlier', chats: [] }];
    for (const chat of chats || []) {
      const ago = daysBetween(new Date(activeOf(chat)).getTime(), now);
      groups[ago <= 0 ? 0 : ago === 1 ? 1 : 2].chats.push(chat);
    }
    return groups.filter(group => group.chats.length);
  }

  // The short time on a Recent row: 14:02 today, the weekday for the last week, else 12 Oct (and the year when it isn't
  // this one), in the PC's own date and time format unless a locale is given.
  function shortTime(iso, now = Date.now(), locale = undefined) {
    const date = new Date(iso);
    if (!Number.isFinite(date.getTime())) return '';
    const ago = daysBetween(date.getTime(), now);
    if (ago <= 0) return date.toLocaleTimeString(locale, { hour: 'numeric', minute: '2-digit' });
    if (ago < 7) return date.toLocaleDateString(locale, { weekday: 'short' });
    const year = new Date(now).getFullYear() === date.getFullYear() ? undefined : 'numeric';
    return date.toLocaleDateString(locale, { day: 'numeric', month: 'short', year });
  }

  // "Active 5 min ago" on a project card.
  function ago(time, now = Date.now(), locale = undefined) {
    const then = new Date(time).getTime();
    if (!Number.isFinite(then)) return '';
    const minutes = Math.floor((now - then) / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return minutes + ' min ago';
    const days = daysBetween(then, now);
    if (days === 0) return Math.floor(minutes / 60) + ' h ago';
    if (days === 1) return 'yesterday';
    if (days < 7) return days + ' days ago';
    if (days < 14) return 'last week';
    return 'on ' + shortTime(then, now, locale);
  }

  // The sidebar footer's line: "1 needs you · 2 working", or "All quiet".
  function statusLine({ needs = 0, working = 0 } = {}) {
    const parts = [];
    if (needs) parts.push(needs + ' needs you');
    if (working) parts.push(working + ' working');
    return parts.join(' · ') || 'All quiet';
  }

  // Home's project cards: the sidebar's workspaces, and Chats (Start without a project) first even before its first use,
  // while C# hasn't made and listed it yet. Once listed it is one of them (C# lists it first).
  function projectCards(workspaces, chatsFolder) {
    const list = Array.isArray(workspaces) ? workspaces : [];
    if (!chatsFolder || list.some(workspace => same(workspace.path, chatsFolder))) return list;
    return [{ path: chatsFolder, name: 'Chats', exists: true, unity: false }, ...list];
  }

  // A path under the user folder starts with ~, as in a terminal.
  const shortPath = path => String(path || '').replace(/^[A-Za-z]:\\Users\\[^\\]+(?=\\|$)/i, '~');

  // A search in Recent: every word somewhere in the title, the agent, the account or the project folder.
  function matches(chat, query) {
    const words = String(query || '').toLowerCase().split(/\s+/).filter(Boolean);
    if (!words.length) return true;
    const text = [chat.title, chat.agent, chat.accountName, chat.workspace].map(part => String(part || '').toLowerCase()).join('\n');
    return words.every(word => text.includes(word));
  }

  return { activity, sparkPoints, groupRecent, shortTime, ago, statusLine, projectCards, shortPath, matches, daysBetween, activeOf };
})();

if (typeof module !== 'undefined') module.exports = HomeData;
