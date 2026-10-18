'use strict';

// Settings → Agents and accounts: pure helpers over the page's account list (each account's agent, name and the email
// the tool itself reported; never a token). No DOM and no globals except AccountsData, so Node can test them
// (tests/accounts.test.js).
const AccountsData = (() => {
  // Main is the first account of an agent; Add account suggests the first of these the agent doesn't use yet.
  const ORDINALS = ['second', 'third', 'fourth', 'fifth', 'sixth', 'seventh', 'eighth', 'ninth', 'tenth'];
  const lower = text => String(text || '').trim().toLowerCase();

  // Whether the agent already has an account by this name. Main is always there, and case doesn't count: the name is
  // part of a folder name on Windows (AccountStore.Add checks the same).
  function taken(accounts, agent, name) {
    const wanted = lower(name);
    return wanted === 'main' || (accounts || []).some(account => account.agent === agent && lower(account.name) === wanted);
  }

  // The name Add account suggests for the agent: second, third, … tenth, then account11, account12, …
  function nextName(accounts, agent) {
    const free = ORDINALS.find(name => !taken(accounts, agent, name));
    if (free) return free;
    let number = ORDINALS.length + 2;
    while (taken(accounts, agent, 'account' + number)) number++;
    return 'account' + number;
  }

  // Accounts of one agent that report the same email are one login in two folders: one subscription, not two. Each such
  // account's key gets the names of the others, in list order. Different agents may share an email; that's no clash.
  function sameLogins(accounts) {
    const list = accounts || [];
    const emailOf = account => account.signedIn === true ? lower(account.email) : '';
    const result = {};
    for (const account of list) {
      const email = emailOf(account);
      if (!email) continue;
      const others = list.filter(other => other !== account && other.agent === account.agent && emailOf(other) === email);
      if (others.length) result[account.key] = others.map(other => other.name);
    }
    return result;
  }

  // "Same login as main", "Same login as main and second", "Same login as main, second and third".
  function sameLoginText(names) {
    const list = (names || []).map(String);
    if (!list.length) return '';
    const joined = list.length === 1 ? list[0] : list.slice(0, -1).join(', ') + ' and ' + list[list.length - 1];
    return 'Same login as ' + joined;
  }

  return { taken, nextName, sameLogins, sameLoginText, ORDINALS };
})();

if (typeof module !== 'undefined') module.exports = AccountsData;
