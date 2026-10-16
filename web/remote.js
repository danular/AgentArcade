'use strict';

// Phone remote: pure helpers that read a pane's terminal for the phone (docs/phone-remote/REMOTE_API.md).
// No DOM and no globals except Remote, so Node can test them.
const Remote = (() => {
  // The same check as APPROVAL in app.js; keep both identical.
  const APPROVAL = /do you want|would you like to|allow (this|command|execution)|apply this change|\(y\/n\)|\[y\/n\]/i;
  // An agent's own notification that asks for permission rather than saying it is done.
  const ASKS_OK = /needs your (permission|approval|ok|input)|^approval requested\b|wants to (run|edit|use|execute)|permission to use/i;
  // Working agents say how to stop them; "esc to cancel" also ends dialogs, so there it needs a timer (Gemini CLI's "12s").
  const INTERRUPT = /esc to interrupt/i;
  const CANCEL = /esc to cancel/i;
  const ELAPSED = /\b\d+\s*[smh]\b/;
  // A dialog's key hints. Arrows count only as a pair: Claude Code's working line shows "↓ 1.2k tokens".
  const DIALOG_HINT = /enter to (confirm|select|submit|send)|tab to |↑\s*\/?\s*↓|↓\s*\/?\s*↑|to navigate|to select/i;
  // Lines under a dialog's options that still belong to the dialog.
  const FOOTER = /enter to |esc to |tab to |[↑↓]|to navigate|to select|\(esc\)/i;
  const MENU_FOOTER = /enter to (confirm|select)|↑\s*\/?\s*↓|use (the )?arrow keys|to navigate/i;
  const MENU_CURSOR = /^(\s*[❯›>▶●]\s+)\S/;
  const BOX = /[─-╿]/g;
  const BOX_LINE = /^[─-╿\s]*$/;
  const YES_NO = /\(y\/n\)|\[y\/n\]/i;
  // Checklist markers. Done: ☒ ✔ ✓ √ [x]. In progress: ◼ ■. Open: ☐ ◻ □ ○ [ ]. Claude Code draws ✔ ◼ ◻, and in a
  // Windows console without Unicode its fallbacks √ ■ □.
  const CHECK = /^(\s*(?:[⎿└•]\s*)?)(☒|✔|✓|√|■|◼|\[[xX]\]|☐|□|◻|○|\[ \]) +(\S.*)$/;
  const DONE_MARKS = ['☒', '✔', '✓', '√', '[x]'];
  const ACTIVE_MARKS = ['■', '◼'];
  // Gemini CLI marks the selected option with ●, the others with ❯ › > ▶.
  const CHOICE = /^(\s*([❯›>▶●])?\s*)([1-9])[.)] +(\S.*)$/;
  const KEY_HINT = /\s*\((?:esc|enter|return|tab|space|shift\+[\w-]+|ctrl\+[\w-]+|alt\+[\w-]+|[a-z0-9])\)$/i;
  // Gemini CLI starts its answers with ✦.
  const LEAD = /^[\s●⏺•✻✦⎿▌❯›>▶]+/;
  const BULLET = /^[●⏺•✻✦⎿]\s*/;
  const PROMPT = /^\s*[│┃]?\s*[>❯›](\s|$)/;
  // Lines an agent keeps under its own dialog, e.g. Gemini CLI's "Waiting for user confirmation..." and its footer.
  const AGENT_CHROME = /waiting for user confirmation|context left/i;
  const HINT = /\? for shortcuts|esc to|shift\+tab|ctrl\+|for help|auto-accept|bypass permissions|⏵⏵|context left|⏎|⌃/i;
  // The page's own grey lines, like "— session ended —".
  const OWN_LINE = /^— .* —$/;

  const squash = text => String(text).replace(/\s+/g, ' ').trim();
  const unbox = line => String(line).replace(/^\s*[│┃]/, '').replace(/[│┃]\s*$/, '').replace(/\s+$/, '');

  // Shortens text to at most max UTF-16 units without splitting a surrogate pair.
  function cut(text, max) {
    if (text.length <= max) return text;
    let end = max - 1;
    if (/[\ud800-\udbff]/.test(text[end - 1])) end--;
    return text.slice(0, end) + '…';
  }

  function approval(rows) {
    return APPROVAL.test(rows.join('\n'));
  }

  const asksOk = text => ASKS_OK.test(String(text));

  const isActivity = line => (INTERRUPT.test(line) || (CANCEL.test(line) && ELAPSED.test(line))) && !DIALOG_HINT.test(line);
  const isHint = line => FOOTER.test(line) && !isActivity(line);

  function activity(rows) {
    for (let i = rows.length - 1; i >= 0; i--) {
      if (isActivity(rows[i])) return cut(squash(rows[i].replace(BOX, ' ')), 160) || null;
    }
    return null;
  }

  // The last run of at least two checklist lines. Agents wrap long items themselves, so an indented line
  // right under an item continues it.
  function plan(lines) {
    let best = null;
    let run = [];
    const finish = () => {
      if (run.length >= 2) best = run;
      run = [];
    };
    for (const raw of lines.slice(-120)) {
      const line = unbox(raw);
      const match = line.match(CHECK);
      if (match) {
        const mark = match[2].toLowerCase();
        run.push({ text: squash(match[3]), done: DONE_MARKS.includes(mark), active: ACTIVE_MARKS.includes(mark), column: line.length - match[3].length });
        continue;
      }
      const last = run[run.length - 1];
      if (last && line.trim() && line.search(/\S/) >= last.column) {
        last.text += ' ' + squash(line);
        continue;
      }
      finish();
    }
    finish();
    if (!best) return null;
    // current: the item in progress, else the first one not done.
    const open = best.find(item => item.active) || best.find(item => !item.done);
    return {
      done: best.filter(item => item.done).length,
      total: best.length,
      current: open ? cut(open.text, 200) : null,
      items: best.slice(0, 20).map(item => ({ text: cut(item.text, 200), done: item.done })),
    };
  }

  // Indexes of checklist rows, with the indented rows that continue an item (the same rule as plan).
  function checklistRows(rows) {
    const found = new Set();
    let column = -1;
    rows.forEach((raw, index) => {
      const line = unbox(raw);
      const match = line.match(CHECK);
      if (match) column = line.length - match[3].length;
      else if (column < 0 || !line.trim() || line.search(/\S/) < column) {
        column = -1;
        return;
      }
      found.add(index);
    });
    return found;
  }

  function choiceLabel(text) {
    let label = squash(text);
    while (KEY_HINT.test(label)) label = label.replace(KEY_HINT, '');
    return cut(label, 120);
  }

  // A dialog is still open while its options are the last thing on screen: below the last option at most
  // 4 lines, each a border, a key hint or a description indented deeper than the options. Anything else
  // (the agent's next output, a new input box) means it was answered.
  function atBottom(rows, last, column) {
    const below = rows.slice(last + 1).map(unbox).filter(line => line.trim());
    return below.length <= 4 && below.every(line => BOX_LINE.test(line) || isHint(line) || AGENT_CHROME.test(line) || line.search(/\S/) > column);
  }

  // The row of a (y/n) prompt among the last 3 rows with text (borders and key hints don't count), or -1.
  function yesNoRow(rows) {
    let seen = 0;
    for (let i = rows.length - 1; i >= 0 && seen < 3; i--) {
      const line = unbox(rows[i]);
      if (!line.trim() || BOX_LINE.test(line)) continue;
      if (YES_NO.test(line)) return i;
      // The agent's input box came back below it, so the prompt was answered.
      if (PROMPT.test(rows[i])) return -1;
      if (!isHint(line)) seen++;
    }
    return -1;
  }

  // Numbered options start at 1 and count up; other lines may sit between them. One option must carry the
  // selection cursor, so a numbered list in the agent's answer isn't taken for a question.
  // choices: only while the dialog is at the bottom. first: index of the first option, or for a (y/n) prompt
  // the row after it (that row is the question). options: the rows of the last numbered dialog, even an
  // answered one, so the preview can leave them out.
  function findChoices(rows) {
    let best = null;
    let current = null;
    rows.forEach((raw, index) => {
      const match = unbox(raw).match(CHOICE);
      if (!match) return;
      const number = Number(match[3]);
      if (number === 1) current = { first: index, items: [], options: [], cursor: false };
      else if (!current || number !== current.items.length + 1) {
        current = null;
        return;
      }
      current.items.push({ key: match[3], label: choiceLabel(match[4]) });
      current.options.push(index);
      current.column = match[1].length;
      current.cursor ||= !!match[2];
      if (current.cursor && current.items.length >= 2) best = current;
    });
    const options = best ? best.options : [];
    if (best && atBottom(rows, options[options.length - 1], best.column)) {
      return { choices: best.items.slice(0, 9), first: best.first, options };
    }
    const asked = yesNoRow(rows);
    if (asked >= 0) return { choices: [{ key: 'y', label: 'Yes' }, { key: 'n', label: 'No' }], first: asked + 1, options };
    return { choices: [], first: rows.length, options };
  }

  const choices = rows => findChoices(rows).choices;

  // An arrow-key menu at the bottom: a key hint like "Enter to confirm" among the last 4 rows with text, and
  // right above it at least 2 options at one text column, exactly one of them behind the cursor.
  // Returns { first, options } (row indexes) or null.
  function findMenu(rows) {
    let seen = 0;
    for (let i = rows.length - 1; i >= 0 && seen < 4; i--) {
      const line = unbox(rows[i]);
      if (!line.trim()) continue;
      seen++;
      if (!MENU_FOOTER.test(line)) continue;
      const menu = menuAbove(rows, i);
      if (menu) return menu;
    }
    return null;
  }

  function menuAbove(rows, footer) {
    const options = [];
    let column = -1;
    let cursors = 0;
    for (let i = footer - 1; i >= 0; i--) {
      const line = unbox(rows[i]);
      if (!line.trim() || BOX_LINE.test(line) || (!options.length && (isHint(line) || MENU_FOOTER.test(line)))) continue;
      const cursor = line.match(MENU_CURSOR);
      const at = cursor ? cursor[1].length : line.search(/\S/);
      if (options.length && at !== column) break;
      column = at;
      if (cursor) cursors++;
      options.unshift(i);
    }
    return options.length >= 2 && cursors === 1 ? { first: options[0], options } : null;
  }

  // Choices or a menu wait at the bottom of these rows (the pane's last 20).
  function picking(rows) {
    const bottom = rows.slice(-20);
    return findChoices(bottom).choices.length > 0 || !!findMenu(bottom);
  }

  // The line that asks, above the first option: the nearest one ending in "?", else the nearest one with a
  // "?" that ends a sentence in it (cut after that "?", so not a URL's), else the nearest approval line.
  function question(rows, firstChoice = rows.length) {
    const above = rows.slice(0, Math.max(0, firstChoice)).map(row => squash(unbox(row).replace(LEAD, '')));
    const asked = above.map(text => squash(text.replace(YES_NO, '')));
    for (let i = asked.length - 1; i >= 0; i--) {
      if (asked[i].endsWith('?') && /\p{L}/u.test(asked[i])) return cut(asked[i], 200);
    }
    for (let i = asked.length - 1; i >= 0; i--) {
      const part = asked[i].match(/^.*?\p{L}.*?\?(?=\s)/u);
      if (part) return cut(part[0], 200);
    }
    for (let i = above.length - 1; i >= 0; i--) {
      if (APPROVAL.test(above[i])) return cut(above[i], 200);
    }
    return null;
  }

  function mostlyPunctuation(text) {
    const visible = text.replace(/\s/g, '');
    return (visible.match(/[\p{L}\p{N}]/gu) || []).length * 2 < visible.length;
  }

  // The last lines the agent wrote above its input box. A prompt line above them is your own earlier message.
  // Checklist rows are left out (they are in plan), and so are the rows in skip (a dialog's options).
  function preview(rows, skip = new Set()) {
    const listed = checklistRows(rows);
    const picked = [];
    for (let i = rows.length - 1; i >= 0 && picked.length < 3; i--) {
      if (skip.has(i) || listed.has(i)) continue;
      const text = squash(unbox(rows[i]));
      if (!text || BOX_LINE.test(rows[i])) continue;
      if (PROMPT.test(rows[i])) {
        if (picked.length) break;
        continue;
      }
      if (HINT.test(text) || OWN_LINE.test(text) || mostlyPunctuation(text)) continue;
      const clean = text.replace(BULLET, '').trim();
      if (clean) picked.unshift(cut(clean, 200));
    }
    return picked;
  }

  // Everything a Session needs from the screen. lines: the pane's text down to the last drawn row;
  // approvalFrom: where the approval check starts (the desktop checks from 12 rows above the cursor).
  // asking: choices or a menu wait at the bottom and the agent isn't working, so the pane needs you.
  function parts(lines, approvalFrom = lines.length - 12) {
    const bottom = lines.slice(-20);
    const found = findChoices(bottom);
    const menu = found.choices.length ? null : findMenu(bottom);
    const working = activity(lines.slice(-15));
    const asking = (found.choices.length > 0 || !!menu) && !working;
    const rows = lines.slice(-40);
    const options = (menu || found).options.map(index => index + rows.length - bottom.length);
    return {
      activity: working,
      asks: approval(lines.slice(Math.max(0, approvalFrom))),
      choices: found.choices,
      menu: !!menu,
      asking,
      question: question(bottom, menu ? menu.first : found.first),
      plan: plan(lines),
      preview: asking ? [] : preview(rows, new Set(options)),
    };
  }

  // One alert per dialog (REMOTE_API.md, Attention). A dialog is its question with its choices; '' when the screen
  // (a parts() result) doesn't ask.
  const dialogSig = screen => screen && screen.asking
    ? JSON.stringify([screen.question || '', (screen.choices || []).map(choice => `${choice.key} ${choice.label}`)])
    : '';
  // A dialog gone this long without input was dismissed; shorter gaps are a redraw. An agent's own "needs you"
  // notification sent before its dialog is drawn stands for the dialog that shows within DIALOG_SOON_MS.
  const DIALOG_GONE_MS = 3000;
  const DIALOG_SOON_MS = 3000;

  // tracker holds one pane's alert state: sig (the dialog alerted last), input (keys went to the pane since),
  // goneAt (when that dialog left the screen) and claimedAt (an alert for a dialog that wasn't drawn yet).
  const remember = (tracker, sig) => Object.assign(tracker, { sig, input: false, goneAt: 0, claimedAt: 0 });

  // Called every second with the pane's screen: true when a dialog appeared that nobody was alerted about. It is
  // recorded, so the alert that follows goes out once. redrawn: the pane was just resized, so a changed signature is
  // the same dialog wrapped again.
  function dialogAppeared(tracker, screen, now, redrawn = false) {
    const sig = dialogSig(screen);
    if (!sig) {
      if (tracker.sig && (tracker.input || now - (tracker.goneAt ||= now) >= DIALOG_GONE_MS)) remember(tracker, '');
      return false;
    }
    tracker.goneAt = 0;
    if (sig === tracker.sig) return false;
    const announced = !!tracker.claimedAt && now - tracker.claimedAt < DIALOG_SOON_MS;
    const rewrapped = redrawn && !!tracker.sig && !tracker.input;
    remember(tracker, sig);
    return !announced && !rewrapped;
  }

  // For a needsYou alert from anywhere else (the agent's own notification, the bell, the quiet timer): true when it
  // is due, false when its dialog was alerted already. Records it.
  function claimDialog(tracker, screen, now) {
    const sig = dialogSig(screen);
    if (sig) {
      if (sig === tracker.sig) return false;
      remember(tracker, sig);
      return true;
    }
    if (tracker.claimedAt && now - tracker.claimedAt < DIALOG_SOON_MS) return false;
    tracker.claimedAt = now;
    return true;
  }

  // ---------- expect: what the phone saw (REMOTE_API.md) ----------

  // The dialog of a pane whose status is needsYou: its question with its choices, as for Attention. Unlike dialogSig it
  // also names a pane that asks only through the agent's own notification.
  const dialogKey = screen => JSON.stringify([screen.question || '', (screen.choices || []).map(choice => `${choice.key} ${choice.label}`)]);

  // The dialog a pane shows and the screen seq at which the PC first saw it. tracker is { key, seq, input, inputSeq } or
  // null; key is dialogKey while the pane needs you, else ''; seq is the pane's screen seq now. Returns the tracker to
  // keep. A dialog is new when its key changes, when it was gone at a look, or when keys went to the pane and the screen
  // changed since: it may have been answered and asked again. Arrow keys inside one dialog renew it too, which can only
  // make a phone answer again, never answer a dialog it didn't see.
  function trackDialog(tracker, key, seq) {
    if (!key) return null;
    if (tracker && tracker.key === key && !(tracker.input && seq > tracker.inputSeq)) return tracker;
    return { key, seq, input: false, inputSeq: 0 };
  }

  // Keys or text went to the pane at screen seq: from now on a changed screen may show a new dialog.
  function dialogInput(tracker, seq) {
    if (tracker && !tracker.input) Object.assign(tracker, { input: true, inputSeq: seq });
  }

  // Whether a phone's expect doesn't fit the pane as it is now (409 dialogChanged). pane: asking (its status would be
  // needsYou), question (the Session's question now), dialogSeq (when this dialog was first seen), seq (its screen seq).
  function expectRefused(expect, pane) {
    const question = typeof expect.question === 'string' ? expect.question : null;
    const now = pane.asking && typeof pane.question === 'string' ? pane.question : null;
    if (question !== now) return true;
    if (pane.asking && !(pane.dialogSeq <= expect.seq)) return true;
    return !(expect.seq <= pane.seq);
  }

  // OSC 9;4 progress (payload "4;state;percent"). null clears it; undefined means it isn't a progress report.
  function progress(data) {
    const match = /^4;(\d+)(?:;(\d*))?$/.exec(String(data));
    if (!match) return undefined;
    switch (Number(match[1])) {
      case 0: return null;
      case 1: return { state: 'percent', percent: Math.min(100, Math.max(0, Number(match[2]) || 0)) };
      case 2: return { state: 'error' };
      case 3: return { state: 'busy' };
      case 4: return { state: 'paused' };
      default: return undefined;
    }
  }

  const KEYS = { enter: '\r', esc: '\x1b', tab: '\t', shiftTab: '\x1b[Z', space: ' ', backspace: '\x7f', ctrlC: '\x03' };
  const ARROWS = { up: 'A', down: 'B', right: 'C', left: 'D' };

  function keySequence(name, appCursor) {
    if (typeof name !== 'string') return null;
    if (Object.hasOwn(KEYS, name)) return KEYS[name];
    if (Object.hasOwn(ARROWS, name)) return (appCursor ? '\x1bO' : '\x1b[') + ARROWS[name];
    return /^[0-9a-z/?!]$/.test(name) ? name : null;
  }

  // ---------- screens ----------

  const ANSI = ['black', 'red', 'green', 'yellow', 'blue', 'magenta', 'cyan', 'white'];
  const LEVELS = [0, 95, 135, 175, 215, 255];

  // The 16 ANSI colors and the default colors of an xterm.js theme.
  function palette(theme) {
    const names = [...ANSI, ...ANSI.map(name => 'bright' + name[0].toUpperCase() + name.slice(1))];
    return { ansi: names.map(name => theme[name]), foreground: theme.foreground, background: theme.background };
  }

  const hex = (r, g, b) => '#' + [r, g, b].map(value => value.toString(16).padStart(2, '0')).join('');
  const rgb = value => '#' + (value & 0xffffff).toString(16).padStart(6, '0');

  function color256(index, ansi) {
    if (index < 16) return ansi[index];
    if (index < 232) {
      const i = index - 16;
      return hex(LEVELS[Math.floor(i / 36)], LEVELS[Math.floor(i / 6) % 6], LEVELS[i % 6]);
    }
    const gray = 8 + 10 * (index - 232);
    return hex(gray, gray, gray);
  }

  // A color as [isRGB, isPalette, value]; undefined is the theme default. bright: bold text in palette colors
  // 0-7 uses 8-15 (xterm.js drawBoldTextInBrightColors).
  function colorOf([isRgb, isPalette, value], ansi, bright) {
    if (isRgb) return rgb(value);
    if (isPalette) return color256(bright && value < 8 ? value + 8 : value, ansi);
    return undefined;
  }

  function cellStyle(cell, colors) {
    const front = [cell.isFgRGB(), cell.isFgPalette(), cell.getFgColor()];
    const back = [cell.isBgRGB(), cell.isBgPalette(), cell.getBgColor()];
    const inverse = cell.isInverse();
    // Like xterm.js 5.5: inverse swaps the colors first, then bold brightens the foreground it ended up with.
    let fg = colorOf(inverse ? back : front, colors.ansi, cell.isBold());
    let bg = colorOf(inverse ? front : back, colors.ansi, false);
    if (inverse) {
      fg ||= colors.background;
      bg ||= colors.foreground;
    }
    const s = (cell.isBold() ? 1 : 0) | (cell.isItalic() ? 2 : 0) | (cell.isUnderline() ? 4 : 0) |
      (cell.isDim() ? 8 : 0) | (cell.isStrikethrough() ? 16 : 0);
    return { fg, bg, s: s || undefined };
  }

  function serializeLine(line, cell, colors) {
    const spans = [];
    if (!line) return spans;
    for (let x = 0; x < line.length; x++) {
      const current = line.getCell(x, cell);
      if (!current) continue;
      const width = current.getWidth();
      if (width === 0) continue;
      const text = current.isInvisible() ? ' '.repeat(width) : current.getChars() || ' ';
      const { fg, bg, s } = cellStyle(current, colors);
      const last = spans[spans.length - 1];
      if (last && last.fg === fg && last.bg === bg && last.s === s) {
        last.t += text;
        continue;
      }
      const span = { t: text };
      if (fg) span.fg = fg;
      if (bg) span.bg = bg;
      if (s) span.s = s;
      spans.push(span);
    }
    // Trailing blanks go, unless they have a background.
    while (spans.length) {
      const last = spans[spans.length - 1];
      if (last.bg) break;
      last.t = last.t.replace(/ +$/, '');
      if (last.t) break;
      spans.pop();
    }
    return spans;
  }

  function cursorOf(term, buffer) {
    let hidden = false;
    try {
      hidden = !!term._core.coreService.isCursorHidden;
    } catch { /* not readable in this xterm.js: show the cursor */ }
    return hidden ? null : { x: buffer.cursorX, y: buffer.baseY + buffer.cursorY };
  }

  // The last `count` lines of the pane as styled spans; the page adds id and seq.
  function serializeScreen(term, count, colors) {
    const buffer = term.buffer.active;
    const total = buffer.length;
    const from = Math.max(0, total - count);
    const cell = buffer.getNullCell ? buffer.getNullCell() : undefined;
    const lines = [];
    for (let y = from; y < total; y++) lines.push(serializeLine(buffer.getLine(y), cell, colors));
    return { cols: term.cols, rows: term.rows, total, from, cursor: cursorOf(term, buffer), lines };
  }

  return {
    APPROVAL, cut, approval, asksOk, activity, plan, choices, findChoices, findMenu, picking, question, preview, parts,
    dialogSig, dialogAppeared, claimDialog, dialogKey, trackDialog, dialogInput, expectRefused, progress, keySequence, palette, color256,
    serializeScreen,
  };
})();

if (typeof module !== 'undefined') module.exports = Remote;
