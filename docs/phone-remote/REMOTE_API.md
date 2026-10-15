# Agent Arcade Remote API, version 1

This is the contract between **Agent Arcade** (the Windows app, "the PC") and **Agent Arcade Remote** (the Android app, "the phone"). Both sides follow it exactly. If one side needs a change, change this file first and update both sides together.

```
Phone (Agent Arcade Remote)
   │  HTTPS + WebSocket, pinned self-signed certificate, device token
   ▼
Agent Arcade on the PC ── Remote.cs (Kestrel server, only while Phone remote is on)
   │  WebView2 messages
   ▼
web/app.js (xterm.js panes: screens, titles, "needs you")  ──  ConPTY  ──  the agents
```

The phone talks to the PC directly over the local network, or over Tailscale when both devices use it. Nothing goes through a cloud service.

## 1. Turning it on and pairing

1. On the PC: sidebar → **Phone** → switch on **Phone remote**. The PC starts listening on port `47821` (`RemotePort` in `settings.json`) and shows a QR code.
2. On the phone: **Scan QR code** (or **Paste pairing link**, or open the link). The QR code holds the pairing link:

```
agentarcade://pair?v=1&name=DESKTOP-ARCADE&hosts=192.168.1.20:47821,100.101.102.103:47821&code=Qm9x2c7Rk1pT0aZ8f3WvEw&fp=3fa2910c7b44e1d0c95a6b8f2e7d3c1a0b9f8e7d6c5b4a39281706f5e4d3c2b1
```

| Field | Meaning |
| --- | --- |
| `v` | Link version, `1` |
| `name` | PC name (URL-encoded), shown on the phone |
| `hosts` | Addresses to try, in order: LAN addresses first (only adapters with a default gateway), then Tailscale (`100.64.0.0/10`). Each is `ipv4:port` |
| `code` | One-time pairing code: 16 random bytes, base64url without padding. Valid 10 minutes, single use, replaced by **New code**. Five wrong codes in a row cancel it |
| `fp` | SHA-256 of the PC's certificate (DER), 64 lowercase hex characters. The phone accepts no other certificate |

3. The phone asks "Pair with DESKTOP-ARCADE?" and shows the short fingerprint (first 8 bytes: `3F:A2:91:0C:7B:44:E1:D0`, the PC dialog shows the same). On OK it calls `POST /api/v1/pair` (below) on the first host that answers, using a 4 s connect timeout per host.
4. The PC answers with a **device token**. The phone keeps it encrypted (Android Keystore) and sends it on every later request.

## 2. Connection rules

- Base URL: `https://<host>`, for example `https://192.168.1.20:47821`. HTTPS only, TLS 1.2 or newer.
- **Certificate pinning:** the phone computes SHA-256 over the server certificate's DER bytes (`chain[0].encoded`) and compares it with `fp` in constant time. Mismatch = refuse the connection and tell the user ("This isn't the PC you paired with"). There is no trust-all fallback, ever. Hostname checks are replaced by this pin, because the certificate is self-signed and the PC is reached by IP address.
- **Authentication:** every request except `hello` and `pair` sends `Authorization: Bearer <token>`. That includes the WebSocket upgrade request.
- JSON everywhere, UTF-8, camelCase names. Unknown fields must be ignored (both sides), and fields may be missing or `null` when they don't apply.
- **Times** are epoch milliseconds (numbers).
- The phone tries the last host that worked first, then the others in link order. State carries `hosts`, the PC's current addresses; the phone stores them, so it learns a changed address or a newly installed Tailscale while it is connected.
- Errors always have this body, and the phone shows the sentence as it is:

```json
{ "error": "That chat is already open." }
```

| Status | Meaning | Phone does |
| --- | --- | --- |
| 200 / 201 | OK / created | |
| 400 | Bad input | Show the error |
| 401 | Token missing or unknown (the phone was removed) | Forget the pairing, open Connect: "This phone isn't paired any more. Pair it again on your PC." |
| 403 | Pairing code wrong or expired | Show the error, offer to scan again |
| 404 | Unknown session or chat, or no projects folder | Show the error, refresh the state |
| 409 | Conflict (already open, still running, ended, folder already there) | Show the error |
| 413 | Body too large | Show the error |
| 429 | Too many requests or pairing attempts | Wait, then retry |
| 503 | Agent Arcade is busy (its window isn't ready, or the page didn't answer within 10 s) | "Agent Arcade is busy, try again" |

## 3. REST endpoints

| Method and path | Body | Answer |
| --- | --- | --- |
| `GET /api/v1/hello` (no token) | | `Hello` |
| `POST /api/v1/pair` (no token) | `{ "code", "device" }` | `{ "token", "deviceId", "pc" }` |
| `POST /api/v1/unpair` | | `{ "ok": true }`, the PC removes this phone |
| `GET /api/v1/state` | | `State` |
| `GET /api/v1/history` | | `{ "chats": [Chat] }`, newest first, at most 300 |
| `GET /api/v1/sessions/{id}/screen?lines=200` | | `Screen` (lines 1 to 2000, default 200) |
| `POST /api/v1/sessions` | `{ "account", "workspace" }` | 201 `{ "id" }` of the new pane |
| `POST /api/v1/projects` | `{ "name" }` | 201 `{ "workspace" }`, the new project's folder |
| `POST /api/v1/chats/{chat}/resume` | | 201 `{ "id" }`; 409 `{ "error", "id" }` when it is already open |
| `POST /api/v1/sessions/{id}/input` | `{ "text", "submit" }` | `{ "ok": true }` |
| `POST /api/v1/sessions/{id}/keys` | `{ "keys": ["down", "enter"] }` | `{ "ok": true }` |
| `POST /api/v1/sessions/{id}/rename` | `{ "title" }` | `{ "ok": true }` |
| `POST /api/v1/sessions/{id}/stop` | | `{ "ok": true }` |
| `POST /api/v1/sessions/{id}/close` | | `{ "ok": true }`; 409 while the agent still runs |

### hello

```json
{ "app": "Agent Arcade", "api": 1, "version": "1.3.0", "pc": "DESKTOP-ARCADE" }
```

### pair

```json
{ "code": "Qm9x2c7Rk1pT0aZ8f3WvEw", "device": "Pixel 9 Pro" }
```

```json
{ "token": "y0o1uW6c8kzq2F4m9Hb3Xr7LdTeSvA5pNjQwZ1gK2cE", "deviceId": "d_7f3a9c", "pc": "DESKTOP-ARCADE" }
```

### input

Types `text` into the pane exactly as a paste at the PC would (bracketed paste when the agent asked for it), and when `submit` is true presses Enter 150 ms later. Text up to 16 KB; it may contain newlines. Typing into an ended pane is a 409.

```json
{ "text": "Yes, and also update the river normals.", "submit": true }
```

### keys

Up to 32 key names, sent 40 ms apart:

| Name | Sends | Name | Sends |
| --- | --- | --- | --- |
| `enter` | `\r` | `up` / `down` | `\x1b[A` / `\x1b[B` (`\x1bOA` / `\x1bOB` in application cursor mode) |
| `esc` | `\x1b` | `right` / `left` | `\x1b[C` / `\x1b[D` (`\x1bOC` / `\x1bOD` in application cursor mode) |
| `tab` | `\t` | `backspace` | `\x7f` |
| `shiftTab` | `\x1b[Z` | `ctrlC` | `\x03` |
| `space` | space | one character `0`–`9`, `a`–`z`, `/`, `?`, `!` | itself |

### start, resume, stop, close, rename

- **Start** opens a new pane for that account in that workspace, the same as clicking the account on the PC. The PC keeps showing the workspace it showed before.
- **Resume** continues a chat from the history, the same as Resume on the PC.
- **Stop** ends the agent like ✕ on the PC, but keeps the pane: it shows "— stopped from your phone —" and the usual Resume / New chat / Close.
- **Close** removes a pane whose agent has ended.
- **Rename** sets the chat title like double-clicking it on the PC. An empty title goes back to the automatic one.

### new project

Makes an empty folder called `name` in `projectsFolder` (see State) and adds it to the end of the sidebar on the PC, like **+** (Add a project folder) does with a folder that is already there. The PC keeps showing the workspace it showed before. The answer is the new folder, and the next `state` lists it at the end of `workspaces`.

```json
{ "name": "RiverRun" }
```

```json
{ "workspace": "C:\\Projects\\RiverRun" }
```

The phone sends only a name, never a path. Both sides remove spaces around it and check it: 1 to 80 characters, none of `\ / : * ? " < > |` and no control characters, not ending in a dot, and not a name Windows keeps for devices (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9` and `COM¹`–`LPT³`, in any case, also with an extension such as `nul.txt`). Answers: 400 for a name that breaks these rules, 404 when the PC has no projects folder (`projectsFolder` is `null`), 409 when something called `name` is already in that folder (`"There's already a folder called RiverRun in C:\Projects."`).

## 4. WebSocket: `wss://<host>/api/v1/live`

Open it with the `Authorization` header. Both sides send WebSocket ping frames every 20 s; if nothing at all arrives for 45 s the phone reconnects. permessage-deflate compression is on when both sides support it (screens compress well).

Reconnect backoff on the phone: 1 s, 2 s, 4 s, 8 s, then every 15 s; immediately when the app comes back to the foreground or the network changes. After every reconnect the phone sends its `watch` again.

Close codes: `1000` replaced by a newer connection from the same phone (only an older socket the phone already gave up gets it; ignore it); `1001` Phone remote was turned off or Agent Arcade closed; `1008` the phone fell too far behind (reconnect, which resyncs everything); `1009` a message from the phone was larger than 4 KB; `1013` too many phones (more than 4); `4401` this phone was removed (handle it like HTTP 401).

### PC → phone

```json
{ "type": "hello", "state": { "...": "State" } }
{ "type": "state", "state": { "...": "State" } }
{ "type": "session", "session": { "...": "Session" } }
{ "type": "closed", "id": "s3" }
{ "type": "screen", "screen": { "...": "Screen" } }
{ "type": "attention", "attention": { "...": "Attention" } }
{ "type": "history", "chats": [ { "...": "Chat" } ] }
```

- `hello` comes first, with the whole state; `history` follows right after.
- `state` whenever workspaces, agents or accounts change (it always includes all sessions too).
- `session` whenever one pane's summary changes, and for new panes. The phone replaces its copy by `id`.
- `closed` when a pane is removed from the PC.
- `screen` only for panes this phone watches: right after `watch`, then whenever the screen changes, at most 4 times per second per pane. Each one replaces the whole window of lines.
- `attention` when an agent needs you, is done, or stopped (see Attention).

### Phone → PC

```json
{ "type": "watch", "id": "s3", "lines": 200 }
{ "type": "unwatch", "id": "s3" }
```

`lines` is how much of the end of the pane the phone wants (1 to 2000, default 200). Send `watch` again with a bigger number for "Load earlier". The phone watches only the chat it shows, and unwatches when it leaves it or goes to the background.

## 5. Objects

### State

```json
{
  "api": 1,
  "pc": "DESKTOP-ARCADE",
  "version": "1.3.0",
  "workspace": "C:\\Projects\\HimalayanExpedition",
  "projectsFolder": "C:\\Projects",
  "hosts": ["192.168.1.20:47821", "100.101.102.103:47821"],
  "power": { "keepAwake": "agents", "awake": true, "onBattery": false, "lidSleeps": true },
  "workspaces": [
    { "path": "C:\\Projects\\HimalayanExpedition", "name": "HimalayanExpedition", "exists": true, "unity": true },
    { "path": "C:\\Projects\\AgentArcade", "name": "AgentArcade", "exists": true, "unity": false }
  ],
  "agents": [
    { "name": "Claude Code", "command": "claude", "installed": true, "version": "2.1.4", "color": "#d97757" },
    { "name": "GitHub Copilot CLI", "command": "copilot", "installed": false, "version": "", "color": "#a371f7" }
  ],
  "accounts": [
    { "key": "Claude Code|main", "agent": "Claude Code", "name": "main", "main": true, "signedIn": true, "email": "you@example.com", "plan": "max" },
    { "key": "Claude Code|second", "agent": "Claude Code", "name": "second", "main": false, "signedIn": true, "email": "second@example.com", "plan": "pro" }
  ],
  "sessions": [ { "...": "Session" } ]
}
```

- `workspace` is the project the PC shows right now.
- `projectsFolder` is where a new project goes (see new project): the folder that holds `workspace`, or when that has none or is missing, the folder of the first listed workspace that has one. `null` when no workspace has one. A PC older than this field leaves it out, and the phone then offers no New project.
- `color` is the agent's brand color by command (`claude #d97757`, `codex #10a37f`, `gemini #4f8cff`, `copilot #a371f7`, `cursor-agent #7c8aa5`) or `null`. When `null` the phone uses `hsl(hash % 360, 55%, 58%)` with `hash = (hash * 31 + charCode) >>> 0` over the agent name, like the desktop.
- `signedIn` is `true`, `false` or `null` (still checking or unknown).
- `hosts` lists the addresses the PC listens on now, in the same order and format as `hosts` in the pairing link. An address in `100.64.0.0/10` is the PC's Tailscale address, which also works away from home; the others are on the PC's local network.
- `power` tells whether the PC may go to sleep, which would stop the agents and cut the phone off. The phone only shows it; the setting is changed on the PC (Phone dialog → Keep this PC awake). A PC older than this field leaves it out, and the phone then shows nothing about sleep.

| Field | Meaning |
| --- | --- |
| `keepAwake` | The PC's setting: `agents` (Windows doesn't sleep on its own while agents run), `remote` (also while Phone remote is on, so the PC stays reachable), `off` (Windows decides) |
| `awake` | `true` while the PC keeps Windows from sleeping on its own right now |
| `onBattery` | `true` when the PC runs on battery: Windows then still sleeps a few minutes after its sleep time |
| `lidSleeps` | `true` on a laptop whose lid, when closed while plugged in, puts it to sleep |

The state goes out again when `awake` or `onBattery` changes.
- While the PC is still checking the agents (just after start or Refresh), `installed` and `version` keep their last known values.

### Session

One pane on the PC. The page builds it from the terminal, so the phone never parses terminal text itself.

```json
{
  "id": "s3",
  "chat": "6f0c2b8e-5d1e-4f7a-9a43-2b1f7c9d0e11",
  "mode": "start",
  "agent": "Claude Code",
  "command": "claude",
  "account": "Claude Code|second",
  "accountName": "second",
  "workspace": "C:\\Projects\\HimalayanExpedition",
  "title": "Fix the foam near the bridge",
  "status": "needsYou",
  "message": "Claude Code needs your OK to continue.",
  "question": "Do you want to make this edit to WaterSurface.shader?",
  "choices": [
    { "key": "1", "label": "Yes" },
    { "key": "2", "label": "Yes, and don't ask again this session" },
    { "key": "3", "label": "No, and tell Claude what to do differently" }
  ],
  "activity": null,
  "plan": {
    "done": 2, "total": 5, "current": "Narrow the foam mask",
    "items": [
      { "text": "Find the foam code", "done": true },
      { "text": "Measure the foam band", "done": true },
      { "text": "Narrow the foam mask", "done": false },
      { "text": "Check the bridge banks", "done": false },
      { "text": "Capture before/after", "done": false }
    ]
  },
  "progress": null,
  "preview": ["The foam mask runs to 0.6 of the water depth, so it spreads far from the banks."],
  "startedAt": 1791890520000,
  "turnStartedAt": 1791893270000,
  "lastOutputAt": 1791893401000,
  "endedAt": null,
  "exitCode": null,
  "error": null,
  "seenOnPc": false,
  "cols": 120,
  "rows": 40
}
```

| Field | Meaning |
| --- | --- |
| `chat` | History id of this chat; `null` for sign-in and install panes |
| `mode` | `start`, `resume`, `login` or `install`. Only `start` and `resume` are chats (with history and quick prompts); sign-in and install panes still show their screen and take input |
| `title` | The chat title: the name you gave it, else the agent's own title, else your first message, else "New chat" |
| `status` | `working`, `needsYou`, `done`, `idle` or `ended`, see the rules below |
| `message` | One sentence for the status: "Claude Code needs your OK to continue.", the agent's own notification text, "Claude Code is done and waiting for you.", "Claude Code stopped." or the error. `null` while working or idle |
| `question` | For `needsYou`: the line that asks (the last line above the choices ending in `?`, or the line that matched the approval check) |
| `choices` | For `needsYou`: the numbered options on screen, or `y`/`n` for a `(y/n)` prompt. Empty otherwise. Sending a choice = `keys: [key]` |
| `activity` | The agent's live status line, for example `✻ Brewing… (1m 12s · ↑ 3.4k tokens · esc to interrupt)` or `• Working (38s • esc to interrupt)`; `null` when none |
| `plan` | The most recent checklist on screen (Claude Code's todo list, Codex's plan), or `null`. `items` has at most 20 entries |
| `progress` | The agent's own progress report (OSC 9;4): `{ "state": "busy" }`, `{ "state": "percent", "percent": 40 }`, `{ "state": "error" }`, `{ "state": "paused" }`, or `null` |
| `preview` | Up to 3 lines the agent last wrote (best effort), plain text, each at most 200 characters |
| `turnStartedAt` | When Enter was last pressed in this pane (the start of the current or last turn) |
| `lastOutputAt` | When the pane last printed anything |
| `seenOnPc` | `true` when someone is looking at this pane on the PC right now (the window has focus and the pane is focused) |
| `cols`, `rows` | The pane's terminal size on the PC |

**Status rules** (the PC decides, in this order):

1. `ended`: the agent's process has exited or was stopped.
2. `needsYou`: the bottom of the screen shows a question waiting for an answer and no activity line is showing: numbered choices or a `(y/n)` prompt at the very bottom (see `choices`), or an arrow-key menu at the very bottom (option lines, one with a cursor `❯ › > ▶ ●`, above a footer like `Enter to confirm · Esc to cancel`; its `choices` stay empty and the phone answers with its key chips). Or the pane turned amber while asking: the agent's own notification asked for permission, or at that moment the bottom 12 rows matched `/do you want|would you like to|allow (this|command|execution)|apply this change|\(y\/n\)|\[y\/n\]/i` (the check the desktop uses for its "needs your OK" notification). Answered dialogs that are still on screen, with output or a new input box below them, don't count.
3. `done`: the pane is amber for any other reason: the agent worked after Enter and then went quiet for 5 s, or it sent its own "waiting for you" notification.
4. `working`: an activity line is showing in the bottom 15 rows, the agent reports `busy` or `percent` through OSC 9;4, or output arrived within the last 5 s after Enter (not counting the repaint after a resize or the echo of typing that wasn't sent yet), or the agent worked long enough after Enter that the "done" check is about to run.
5. `idle`: anything else, for example waiting for its first prompt, or you already looked at it.

Typing into a pane, on the PC or from the phone, clears the amber state, as it does today.

**How the page finds the parts** (best effort; `null` or empty when nothing matches):

- `activity`: the last line in the bottom 15 rows that matches `/esc to interrupt/i`, or `/esc to cancel/i` together with an elapsed time such as `12s` or `1m 3s`, but never a dialog footer (`enter to confirm`, `tab to …`, `↑/↓ to navigate`); box-drawing characters and extra spaces removed, at most 160 characters.
- `plan`: in the last 120 lines, the last run of consecutive checklist lines. Done markers: `☒ ✔ ✓ ■ ◼ [x]`. Open markers: `☐ □ ◻ ○ [ ]`. A leading `⎿`, `└`, `•` or spaces may come before the marker. `current` is the first open item.
- `choices`: in the bottom 20 rows, lines like `❯ 1. Yes` or `  2. Yes, and don't ask again this session`: an optional cursor (`❯ › > ▶`), a digit, `.` or `)`, then the label. They must start at 1 and count up without gaps (at most 9). One option carries the cursor. Labels lose trailing hints in parentheses such as `(esc)` or `(shift+tab)`. The options must be at the very bottom: below the last one only box borders, hint or footer lines and indented descriptions (at most 4 lines). Without numbered choices but with `(y/n)` or `[y/n]` in the last 3 lines: `[{ "key": "y", "label": "Yes" }, { "key": "n", "label": "No" }]`.
- `question`: the last line above the options that ends with `?`, else the last one that contains `?` (cut after it), else the line that matched the approval check.
- `preview`: the last non-empty lines above the agent's input box, skipping box borders, the prompt line, the activity line, checklist lines (they are in `plan`), menu options and hint lines like `? for shortcuts`. Empty while the status is `needsYou`.

### Screen

The end of one pane's terminal, as lines of styled text. The PC resolves colors through the desktop terminal theme (`THEME` in `web/app.js`, the 256-color cube and grays, true color as is), applies inverse video, writes hidden text as spaces, skips the empty trailing cell of wide characters, and trims trailing blanks.

```json
{
  "id": "s3",
  "seq": 418,
  "cols": 120,
  "rows": 40,
  "total": 2133,
  "from": 1933,
  "cursor": { "x": 2, "y": 2121 },
  "lines": [
    [ { "t": "● ", "fg": "#fafafa" }, { "t": "Update", "s": 1 }, { "t": "(Assets/Shaders/WaterSurface.shader)" } ],
    [ { "t": "118 - float foam = smoothstep(0, .6, d);", "fg": "#f87171", "bg": "#3b1d22" } ],
    [],
    [ { "t": "  ⎿  ", "fg": "#71717a" }, { "t": "Read 214 lines", "fg": "#71717a", "s": 8 } ]
  ]
}
```

| Field | Meaning |
| --- | --- |
| `seq` | Goes up every time this pane's screen changes |
| `total` | Lines in the pane's buffer (scrollback plus screen) |
| `from` | Buffer index of `lines[0]`; `lines` runs to the end of the buffer |
| `cursor` | Cursor position (`y` is a buffer index), or `null` when the cursor is hidden |
| `lines` | Each line is a list of spans; an empty list is an empty line |
| `t` | The text of a run with one style |
| `fg`, `bg` | `#rrggbb`, left out for the default colors (the phone uses `#e4e4e7` on `#0f0f12`) |
| `s` | Style bits, left out when 0: `1` bold, `2` italic, `4` underline, `8` dim, `16` strikethrough |

### Chat (history)

```json
{
  "id": "6f0c2b8e-5d1e-4f7a-9a43-2b1f7c9d0e11",
  "agent": "Claude Code",
  "account": "Claude Code|main",
  "accountName": "main",
  "workspace": "C:\\Projects\\HimalayanExpedition",
  "title": "Road mesh cleanup",
  "named": false,
  "startedAt": 1791804300000,
  "endedAt": 1791807060000,
  "open": false,
  "session": null,
  "resume": "exact"
}
```

`session` is the id of the pane that has this chat open, when `open` is true. `resume` is `exact` (Claude Code reopens exactly this chat), `agent` (the agent opens its own resume list) or `none`.

### Attention

```json
{
  "id": "s3",
  "status": "needsYou",
  "title": "Fix the foam near the bridge",
  "message": "Claude Code needs your OK to continue.",
  "question": "Do you want to make this edit to WaterSurface.shader?",
  "seenOnPc": false,
  "at": 1791893401000
}
```

Sent when the desktop would turn the pane amber or notify (`needsYou` or `done`), and when an agent ends (`ended`). It doesn't depend on the PC's Windows notification setting. The phone skips the alert when `seenOnPc` is true, when that chat is open on the phone's screen, or when the user muted that chat or that kind of alert.

## 6. Security

- Phone remote is **off by default**. The PC listens only while it is on, and stops when it is turned off or Agent Arcade closes.
- **Certificate:** made on the PC the first time (ECDSA P-256, `CN=Agent Arcade Remote`, valid 20 years) and kept in the Windows user certificate store. The phone pins its SHA-256 fingerprint from the pairing link.
- **Pairing codes:** 16 random bytes, 10 minutes, single use, five wrong tries cancel the code. At most 10 pairing attempts per minute in total.
- **Device tokens:** 32 random bytes (base64url). The PC stores only their SHA-256 hash in `%LOCALAPPDATA%\AgentArcade\remote.json` (with the phone's name, paired time and last seen) and compares hashes in constant time. **Remove** in the Phone dialog deletes the hash and disconnects that phone at once (close code 4401).
- **What a phone can do** is what a person at the PC can do inside the panes: read the screens, type, answer, start, stop, resume and rename agents. It cannot read files, run anything outside an agent, change settings, or see account tokens. The one thing it can make on the PC's disk is an empty project folder in `projectsFolder`, under a name the PC checked.
- **Limits:** request bodies up to 64 KB, `input` text up to 16 KB, 20 requests per second per phone and at most 4 of its requests waiting for Agent Arcade at once (429 above that), 4 phones connected at once, 8 open connections per address, 4 KB per WebSocket message from the phone.
- **Network:** the PC accepts connections only from local and Tailscale addresses (127.0.0.0/8, 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16, 100.64.0.0/10), so the port stays closed to the internet even when the PC has a public address. TLS 1.2 and 1.3 only, HTTP/1.1 only. More than 20 wrong tokens in a minute from one address lock that address out for a minute.
- **Removing a phone** also cancels typing it asked for that hasn't happened yet; so does turning Phone remote off.
- **Logs:** tokens, pairing codes and screen text are never written to the log, on either side.
- The phone stores the token encrypted with an Android Keystore key, allows no cleartext traffic, and never accepts another certificate.

## 7. Trying it without the phone

From Git Bash on the PC, with Phone remote on. Copy the pairing link with **Copy link** in the Phone dialog and take the `code` from it. `-k` skips the certificate check; only use it for this test.

```bash
curl -k https://127.0.0.1:47821/api/v1/hello
curl -k -X POST https://127.0.0.1:47821/api/v1/pair -H 'Content-Type: application/json' \
  -d '{"code":"<code from the link>","device":"curl test"}'
TOKEN='<token from the answer>'
curl -k https://127.0.0.1:47821/api/v1/state -H "Authorization: Bearer $TOKEN"
curl -k "https://127.0.0.1:47821/api/v1/sessions/s1/screen?lines=40" -H "Authorization: Bearer $TOKEN"
curl -k -X POST https://127.0.0.1:47821/api/v1/sessions/s1/input -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"text":"Say hi in one line.","submit":true}'
curl -k -X POST https://127.0.0.1:47821/api/v1/sessions/s1/keys -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"keys":["esc"]}'
curl -k -X POST https://127.0.0.1:47821/api/v1/projects -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"name":"RiverRun"}'
```

For the WebSocket, any client that can send the `Authorization` header works, for example `websocat -k -H "Authorization: Bearer $TOKEN" wss://127.0.0.1:47821/api/v1/live`, then type `{"type":"watch","id":"s1","lines":40}`.

The phone's Android emulator reaches the PC at the PC's LAN address, or at `10.0.2.2:47821`.
