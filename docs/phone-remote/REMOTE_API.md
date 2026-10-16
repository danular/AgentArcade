# Agent Arcade Remote API, version 1

This is the contract between **Agent Arcade** (the Windows app, "the PC") and **Agent Arcade Remote** (the Android app, "the phone"). Both sides follow it exactly. If one side needs a change, change this file first and update both sides together.

This is **contract 1.1** (Agent Arcade 1.4.0, Agent Arcade Remote 1.1.0). `api` stays `1`: everything 1.1 adds is optional, so a 1.0 phone keeps working with a 1.1 PC and a 1.1 phone with a 1.0 PC. [Changes in 1.1](#changes-in-11) lists the additions; the sections after it describe the whole contract. The PC's own serializers write example files of what the phone gets to [fixtures](fixtures/README.md).

```
Phone (Agent Arcade Remote)
   │  HTTPS + WebSocket, pinned self-signed certificate, device token
   ▼
Agent Arcade on the PC ── Remote.cs (Kestrel server, only while Phone remote is on)
   │  WebView2 messages
   ▼
web/app.js (xterm.js panes: screens, titles, "needs you")  ──  ConPTY  ──  the agents
```

The phone talks to the PC over the local network, or over Tailscale when both devices use it. There is no Agent Arcade server in between; over Tailscale, Tailscale may relay the encrypted traffic through its own DERP servers when the devices can't reach each other directly.

## Changes in 1.1

All additive. A phone ignores fields it doesn't know (section 2), so a 1.0 phone reads a 1.1 PC; a 1.1 PC tells a phone what it does in `hello.features`, and lists a feature only once it does it.

| Addition | Details | Feature in `hello.features` |
| --- | --- | --- |
| The PC asks before it pairs a phone: `POST /pair` waits up to 60 s for **Allow** or **Deny** on the PC, then 200, or 403 `denied` / `notApproved` | [pair](#pair) | `pairApproval` |
| An optional `expect` (`question`, `seq`, `run`) on `/keys` and `/input`: what the phone saw. 409 `dialogChanged` when the pane shows something else now, 409 `runChanged` when Agent Arcade started again | [expect](#expect-what-the-phone-saw) | `expect` |
| `/input` with `submit: true` while the pane asks: 409 `dialogOpen`. The PC never presses Enter on a dialog for a phone | [input](#input) | `expect` |
| `run`, a random id per start of Agent Arcade's page, in State, Session and Attention. Phones bind chats and alerts to (`id`, `run`) | [run](#run) | `run` |
| `features` and `minClientApi` in `hello` | [hello](#hello) | |
| The optional `X-Arcade-Client` request header, for example `AgentArcadeRemote/1.1.0 (1010099)` | [X-Arcade-Client](#x-arcade-client) | `clientHeader` |
| A `code` in every error body, from a fixed table | [Errors](#errors) | `errorCodes` |

### What a 1.0 phone sees

Agent Arcade Remote 1.0 and 1.1.0-beta.1 follow contract 1.0.

- `features`, `minClientApi`, `run` and `code` are fields it doesn't know, so it ignores them.
- Pairing waits for Allow on the PC, but a 1.0 phone gives up after its 15 s read timeout. It pairs when Allow is clicked within about 15 s; otherwise it shows its own connection error, the PC takes the question away and adds no phone, and the code is used up: **New code** on the PC, then scan again.
- `/input` with `submit: true` while the pane asks gets 409 with a sentence it shows; nothing was typed. (1.1.0-beta.1 already presses Esc first and waits until the pane stops asking.)
- It sends no `expect`, so its keys and input are checked as in 1.0, and no `X-Arcade-Client`, so the PC shows no app version for it.

### What a 1.1 phone does

1. Sends [`X-Arcade-Client`](#x-arcade-client) on every request: `hello`, `pair`, the other REST requests and the WebSocket upgrade.
2. Reads `GET /api/v1/hello` before it pairs and after every new live connection (a PC may have been updated in between). When `minClientApi` is above 1, it neither pairs nor connects and asks the user to update Agent Arcade Remote. No `features` means an empty list.
3. Pairs with a 75 s read timeout and tells the user to click Allow on the PC while it waits; handles 403 `denied` and `notApproved` like the other pairing errors (offer to scan again).
4. When the PC lists `run`: binds every chat it shows, alert and notification to (`id`, `run`) and follows the [run](#run) rules.
5. When the PC lists `expect`: sends `expect` with every `/keys` and `/input` request; never repeats a request that got `dialogChanged`, `runChanged` or `dialogOpen` on its own; presses Esc and waits until the pane stops asking before it sends a message to an asking pane.
6. When the PC lists `errorCodes`: decides by `code`, shows `error`, and handles a code it doesn't know, or none, by the HTTP status.

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
4. (1.1, `pairApproval`) The PC asks in its Phone dialog whether this phone may pair: **Allow** or **Deny**. The phone waits up to 75 s and says "Click Allow on DESKTOP-ARCADE" meanwhile (see [pair](#pair)).
5. On Allow the PC answers with a **device token**. The phone keeps it encrypted (Android Keystore) and sends it on every later request.

## 2. Connection rules

- Base URL: `https://<host>`, for example `https://192.168.1.20:47821`. HTTPS only, TLS 1.2 or newer.
- **Certificate pinning:** the phone computes SHA-256 over the server certificate's DER bytes (`chain[0].encoded`) and compares it with `fp` in constant time. Mismatch = refuse the connection and tell the user ("This isn't the PC you paired with"). There is no trust-all fallback, ever. Hostname checks are replaced by this pin, because the certificate is self-signed and the PC is reached by IP address.
- **Authentication:** every request except `hello` and `pair` sends `Authorization: Bearer <token>`. That includes the WebSocket upgrade request.
- JSON everywhere, UTF-8, camelCase names. Unknown fields must be ignored (both sides), and fields may be missing or `null` when they don't apply.
- **Times** are epoch milliseconds (numbers).
- The phone tries the last host that worked first, then the others in link order. State carries `hosts`, the PC's current addresses; the phone stores them, so it learns a changed address or a newly installed Tailscale while it is connected.
- Errors have the body described in [Errors](#errors) below, and the phone shows the sentence as it is.

| Status | Meaning | Phone does |
| --- | --- | --- |
| 200 / 201 | OK / created | |
| 400 | Bad input | Show the error |
| 401 | Token missing or unknown (the phone was removed) | Forget the pairing, open Connect: "This phone isn't paired any more. Pair it again on your PC." |
| 403 | Pairing code wrong, expired or used, or the PC didn't allow the pairing (1.1) | Show the error, offer to scan again |
| 404 | Unknown session or chat, or no projects folder | Show the error, refresh the state |
| 409 | Conflict (already open, still running, ended, folder already there; 1.1: the pane's question changed, the pane is asking, Agent Arcade started again) | Show the error |
| 413 | Body too large | Show the error |
| 429 | Too many requests or pairing attempts | Wait, then retry |
| 500 | Something unexpected went wrong on the PC | Show the error |
| 503 | Agent Arcade is busy (its window isn't ready, or the page didn't answer within 10 s) | "Agent Arcade is busy, try again" |

### X-Arcade-Client

(1.1, feature `clientHeader`.) A 1.1 phone names itself on every request, including `hello`, `pair` and the WebSocket upgrade, with its versionName and, in parentheses, its versionCode:

```
X-Arcade-Client: AgentArcadeRemote/1.1.0 (1010099)
```

- The PC takes the value with spaces at both ends removed. It accepts it only when it is at most 64 characters and matches `^[A-Za-z][A-Za-z0-9]{0,31}/[0-9A-Za-z.+-]{1,24}( \([0-9]{1,10}\))?$`: so only ASCII letters, digits, `.`, `+`, `-`, one `/`, and an optional space plus a number in parentheses. `AgentArcadeRemote/1.1.0-beta.2 (1010002)`, `AgentArcadeRemote/1.1.0-beta.1-dev (1010001)` and `curl/8.10.1` pass.
- A value that doesn't pass, or a request that carries the header more than once, counts as a request without it. The header never makes a request fail and never changes what the PC does or answers.
- The PC may show the newest accepted value next to the phone in the Phone dialog and in the pairing question, and may keep it with that phone in `remote.json`. It never writes it to the log.
- A 1.0 phone sends no header; the PC then shows no app version for it.

### Errors

Every error answer has a JSON body with `error`, a sentence the phone shows as it is, and (1.1, feature `errorCodes`) `code`, which says which error it is so the phone can act on it without reading the sentence. A few errors add fields: `id` for `chatOpen`, `typed` for `dialogOpen`.

```json
{ "error": "That chat is already open.", "code": "chatOpen", "id": "s3" }
```

- The phone decides by `code` and shows `error`. A code it doesn't know, or none at all (a 1.0 PC), it handles by the HTTP status (table above).
- A code is never renamed or reused for another meaning; new codes may be added. Field order in the body means nothing.
- The few errors the HTTP server makes itself, before Agent Arcade sees the request (a broken HTTP request, headers over 16 KB, a request line over 4 KB), have no JSON body: the phone goes by the status.

| Code | Status | When | Sentence (examples) |
| --- | --- | --- | --- |
| `badRequest` | 400 | The body isn't a JSON object or a field has the wrong type; `expect` isn't valid; no keys or more than 32; empty `account` or `workspace`; renaming a pane that isn't a chat; `/live` without a WebSocket upgrade | "That request isn't valid.", "That request isn't valid JSON.", "Send from 1 to 32 keys.", "Choose an agent account and a project.", "Only chats can be renamed.", "This address only takes a WebSocket connection." |
| `unknownKey` | 400 | A key name that isn't in the [keys](#keys) table | "Unknown key: f13" |
| `badName` | 400 | New project: the name breaks the rules | "A folder name can't end with a dot." |
| `notPaired` | 401 | No token, an unknown token, or the phone was removed (also for its requests that were waiting then) | "This phone isn't paired any more. Pair it again on your PC." |
| `noCode` | 403 | Pair: the PC has no pairing code right now | "Phone remote has no pairing code right now. Open the Phone dialog on your PC." |
| `wrongCode` | 403 | Pair: not the code in the QR code | "That pairing code is wrong. Scan the QR code on your PC again." |
| `codeExpired` | 403 | Pair: the code is older than 10 minutes | "That pairing code has expired. Press New code on your PC and scan again." |
| `codeUsed` | 403 | Pair: the code was used already (1.1: also while another pairing with it waits for Allow) | "That pairing code was already used. Press New code on your PC and scan again." |
| `codeCancelled` | 403 | Pair: five wrong codes cancelled it | "Too many wrong codes. Press New code on your PC and scan again." |
| `denied` | 403 | Pair (1.1): someone clicked Deny on the PC | "Pairing was refused on your PC." |
| `notApproved` | 403 | Pair (1.1): nobody clicked Allow within 60 s, or the wait ended without it (New code, Phone remote off, Agent Arcade closing) | "Nobody allowed this phone on your PC in time. Press New code on your PC, scan again and click Allow." |
| `unknownRequest` | 404 | No such method and path | "Unknown request." |
| `sessionNotFound` | 404 | No pane with that id is open (also for an id that isn't valid) | "That agent isn't open on the PC any more." |
| `chatNotFound` | 404 | Resume: the chat isn't in the history | "That chat is no longer in the history." |
| `accountNotFound` | 404 | Start: the account isn't on the PC any more | "That agent account isn't on the PC any more." |
| `workspaceNotFound` | 404 | Start: the project isn't in the sidebar any more | "That project isn't in the sidebar on the PC any more." |
| `noProjectsFolder` | 404 | New project: `projectsFolder` is `null` | "Agent Arcade on your PC has no projects folder. Add a project there first." |
| `sessionEnded` | 409 | Input, keys or stop for a pane whose agent has ended | "That agent has ended. Resume it or start a new chat." |
| `sessionRunning` | 409 | Close while the agent still runs | "Stop the agent first." |
| `chatOpen` | 409 | Resume: the chat is already open; `id` is its pane | "That chat is already open." |
| `cantResume` | 409 | Resume: the agent, its account or its project can't continue this chat now | "The second account is gone.", "Add RiverRun back to the sidebar on the PC first." |
| `agentNotReady` | 409 | Start or resume: the agent isn't installed, or the PC is still checking it | "GitHub Copilot CLI isn't installed on the PC." |
| `folderMissing` | 409 | Start: the project's folder is missing on the PC | "The RiverRun folder is missing on the PC." |
| `startFailed` | 409 | Start or resume: the agent couldn't start | "Claude Code couldn't start on the PC." |
| `folderExists` | 409 | New project: something with that name is already there | "There's already a folder called RiverRun in C:\Projects." |
| `folderFailed` | 409 | New project: Windows didn't let Agent Arcade make the folder | "Windows didn't let Agent Arcade make RiverRun in C:\Projects." |
| `dialogChanged` | 409 | Keys or input (1.1): `expect` doesn't match what the pane shows now; nothing was typed | "The question on your PC changed, so nothing was sent. Check it and answer again." |
| `dialogOpen` | 409 | Input with `submit: true` (1.1) while the pane asks; nothing was typed, or with `"typed": true` the text was typed but Enter wasn't pressed | "The agent is asking a question on your PC, so nothing was typed. Answer it, or press Esc first.", with `typed`: "The agent started asking a question on your PC, so Enter wasn't pressed." |
| `runChanged` | 409 | Keys or input (1.1): `expect.run` isn't the PC's current run; nothing was typed | "Agent Arcade started again on your PC, so nothing was sent. That agent isn't open any more." |
| `tooLarge` | 413 | A body over 64 KB, or `input` text over 16 KB | "That request is too large.", "That message is too long. Send at most 16 KB." |
| `tooManyRequests` | 429 | More than 20 requests a second, or 4 of this phone's requests already wait for Agent Arcade | "Too many requests. Wait a moment." |
| `tooManyPairAttempts` | 429 | More than 10 pairing attempts in a minute | "Too many pairing attempts. Wait a minute and try again." |
| `lockedOut` | 429 | More than 20 wrong tokens in a minute from this address | "Too many requests without a valid pairing. Wait a minute and try again." |
| `internal` | 500 | Something unexpected went wrong on the PC | "Something went wrong on the PC." |
| `busy` | 503 | The window isn't ready, the page didn't answer within 10 s, or Phone remote is starting or stopping (requests that wait for the page end with it) | "Agent Arcade is busy, try again." |

What the phone does beyond the status table: `notPaired` as 401; `chatOpen` opens pane `id`; `dialogChanged`, `runChanged` and `dialogOpen` show the sentence and the pane as it is now and let the user decide again (never an automatic retry); `typed: true` means the text is on the PC already, so the phone doesn't offer to send it again.

## 3. REST endpoints

| Method and path | Body | Answer |
| --- | --- | --- |
| `GET /api/v1/hello` (no token) | | `Hello` |
| `POST /api/v1/pair` (no token) | `{ "code", "device" }` | `{ "token", "deviceId", "pc" }`; 1.1: only after Allow on the PC |
| `POST /api/v1/unpair` | | `{ "ok": true }`, the PC removes this phone |
| `GET /api/v1/state` | | `State` |
| `GET /api/v1/history` | | `{ "chats": [Chat] }`, newest first, at most 300 |
| `GET /api/v1/sessions/{id}/screen?lines=200` | | `Screen` (lines 1 to 2000, default 200) |
| `POST /api/v1/sessions` | `{ "account", "workspace" }` | 201 `{ "id" }` of the new pane |
| `POST /api/v1/projects` | `{ "name" }` | 201 `{ "workspace" }`, the new project's folder |
| `POST /api/v1/chats/{chat}/resume` | | 201 `{ "id" }`; 409 `{ "error", "id" }` when it is already open |
| `POST /api/v1/sessions/{id}/input` | `{ "text", "submit" }`, 1.1: optional `"expect"` | `{ "ok": true }` |
| `POST /api/v1/sessions/{id}/keys` | `{ "keys": ["down", "enter"] }`, 1.1: optional `"expect"` | `{ "ok": true }` |
| `POST /api/v1/sessions/{id}/rename` | `{ "title" }` | `{ "ok": true }` |
| `POST /api/v1/sessions/{id}/stop` | | `{ "ok": true }` |
| `POST /api/v1/sessions/{id}/close` | | `{ "ok": true }`; 409 while the agent still runs |

### hello

```json
{ "app": "Agent Arcade", "api": 1, "version": "1.4.0", "pc": "DESKTOP-ARCADE", "minClientApi": 1, "features": ["pairApproval", "expect", "run", "errorCodes", "clientHeader"] }
```

| Field | Meaning |
| --- | --- |
| `api` | The API version the PC speaks: `1`. It would only go up for a change that 1.x phones can't follow; there is none |
| `minClientApi` | (1.1) The lowest API version a phone must speak to use this PC: `1`. Every Agent Arcade Remote so far speaks 1. A phone that speaks a lower one doesn't pair or connect and asks the user to update it. Missing (a 1.0 PC) means 1 |
| `features` | (1.1) What this PC does beyond contract 1.0, as a list of names in no particular order. Missing (a 1.0 PC) means none. The phone ignores names it doesn't know, and relies on a behaviour only when its name is listed |

| Feature | The PC |
| --- | --- |
| `pairApproval` | holds `POST /pair` until someone clicks Allow or Deny on the PC ([pair](#pair)) |
| `expect` | checks `expect` on keys and input ([expect](#expect-what-the-phone-saw)), and never presses Enter for `/input` while the pane asks (`dialogOpen`, [input](#input)) |
| `run` | sends `run` in State, Session and Attention, and checks `expect.run` ([run](#run)) |
| `errorCodes` | puts `code` in every error body ([Errors](#errors)) |
| `clientHeader` | reads [`X-Arcade-Client`](#x-arcade-client) |

### pair

```json
{ "code": "Qm9x2c7Rk1pT0aZ8f3WvEw", "device": "Pixel 9 Pro" }
```

```json
{ "token": "y0o1uW6c8kzq2F4m9Hb3Xr7LdTeSvA5pNjQwZ1gK2cE", "deviceId": "d_7f3a9c", "pc": "DESKTOP-ARCADE" }
```

**Allow on the PC** (1.1, feature `pairApproval`). A PC with this feature adds a phone only after a person at the PC clicked **Allow**:

1. The attempt is counted first (more than 10 a minute: 429 `tooManyPairAttempts`), then the body is checked (400 `badRequest`, 413 `tooLarge`), then the code. A code that is missing, wrong, expired, used or cancelled is answered at once with its 403 (`noCode`, `wrongCode`, `codeExpired`, `codeUsed`, `codeCancelled`), and the PC asks nothing.
2. The right code is used up the moment it arrives, whatever happens next. Another request with it gets 403 `codeUsed`, so at most one pairing waits at a time, and every new try needs **New code** on the PC.
3. The PC asks in its Phone dialog, naming the phone (`device` as the PC cleaned it, see Security) and, when it came, the [`X-Arcade-Client`](#x-arcade-client) value: **Allow** or **Deny**. Allow is never preselected, so pressing Enter alone doesn't pair a phone. If the Phone dialog is closed, the PC doesn't open it by itself (a phone never changes the PC's view); it shows a notice, and the question is in the Phone dialog when the user opens it. Closing the dialog answers nothing.
4. The PC holds the request at most 60 s, counted from the moment the right code arrived:

| On the PC | Answer |
| --- | --- |
| **Allow** | 200 `{ "token", "deviceId", "pc" }`; only now is the phone added to `remote.json` |
| **Deny** | 403 `denied` |
| No Allow or Deny within 60 s | 403 `notApproved` |
| **New code**, Phone remote switched off, or Agent Arcade closing while it waits | 403 `notApproved`, when the connection still lets the PC answer |

5. When the phone gives up first (it closed the connection), the PC takes the question away at once and adds no phone. When Allow is clicked just as the phone gives up and the answer with the token can't be written, the PC removes that phone again.
6. The phone reads the answer with a 75 s read timeout (the 4 s connect timeout per host stays) and shows "Click Allow on DESKTOP-ARCADE" while it waits. Once the request reached a host, it doesn't try the next host: the code is used up there. A 1.0 phone gives up after 15 s (see [What a 1.0 phone sees](#what-a-10-phone-sees)).

A PC without `pairApproval` answers at once, as in 1.0.

### input

Types `text` into the pane exactly as a paste at the PC would (bracketed paste when the agent asked for it), and when `submit` is true presses Enter 150 ms later. Text up to 16 KB; it may contain newlines. Typing into an ended pane is a 409.

```json
{ "text": "Yes, and also update the river normals.", "submit": true }
```

1.1, with `expect` (see [expect](#expect-what-the-phone-saw)):

```json
{ "text": "Also update the river normals.", "submit": true, "expect": { "question": null, "seq": 431, "run": "9c4e1a7f03b2d865" } }
```

**No Enter on a dialog** (1.1, feature `expect`; for every phone, with or without `expect`):

- With `submit: true`, when the pane asks at the moment the request's turn comes (its Session status would be `needsYou` now), the PC types nothing and answers 409 `dialogOpen`.
- It checks again right before it presses Enter. When the pane started asking in those 150 ms, it doesn't press Enter and answers 409 `dialogOpen` with `"typed": true`: the text was typed, Enter wasn't pressed.
- With `submit: false` the PC types as before (after the `expect` check, when there is one): a dialog may have a text field of its own.
- To send a message to a pane that asks: send `keys: ["esc"]` (Claude Code reads it as "No"), wait until a Session shows the pane no longer `needsYou`, then send the input. To fill in a dialog's own text field: send the text with `submit: false`, then `keys: ["enter"]`, both with `expect`.

### keys

Up to 32 key names, sent 40 ms apart. 1.1: with an optional `expect` (see [expect](#expect-what-the-phone-saw)):

```json
{ "keys": ["1"], "expect": { "question": "Do you want to make this edit to WaterSurface.shader?", "seq": 418, "run": "9c4e1a7f03b2d865" } }
```

| Name | Sends | Name | Sends |
| --- | --- | --- | --- |
| `enter` | `\r` | `up` / `down` | `\x1b[A` / `\x1b[B` (`\x1bOA` / `\x1bOB` in application cursor mode) |
| `esc` | `\x1b` | `right` / `left` | `\x1b[C` / `\x1b[D` (`\x1bOC` / `\x1bOD` in application cursor mode) |
| `tab` | `\t` | `backspace` | `\x7f` |
| `shiftTab` | `\x1b[Z` | `ctrlC` | `\x03` |
| `space` | space | one character `0`–`9`, `a`–`z`, `/`, `?`, `!` | itself |

### expect: what the phone saw

(1.1, feature `expect`.) A phone sends `expect` with `/keys` and `/input` to say which state of the pane the user acted on, so a tap never answers a question the user didn't see: one that replaced it on the PC, or the same question asked again.

| Field | Type | Meaning |
| --- | --- | --- |
| `question` | string or `null` | The `question` of the newest Session the phone has of this pane, exactly as it came. `null`, or left out, when that was `null` (the pane wasn't asking, or asks without a question line) |
| `seq` | whole number, 0 to 9007199254740991 | The `seq` of the newest Screen the phone has of this pane |
| `run` | string, optional | The `run` of that Session (feature `run`) |

- No `expect`, or `"expect": null`: nothing is checked, as in 1.0. A 1.0 PC ignores `expect`, so a phone may always send it.
- The PC answers 400 `badRequest` when `expect` is there but isn't an object, `question` isn't a string, `null` or missing, or is longer than 1000 characters, `seq` is missing, isn't written as a plain whole number (`418`; not `418.0`, `4.18e2` or `"418"`) or is out of that range, or `run` isn't a string of 1 to 64 characters. Other fields inside `expect` are ignored.

The checks, in this order; the first that fails answers, and then nothing was typed:

1. The body is read and checked as for 1.0 (400 `badRequest`, `unknownKey`, 413 `tooLarge`), `expect` included.
2. `run` is there and isn't the PC's current run: 409 `runChanged`. Checked when the request arrives.
3. The pane must be open and running, as before: 404 `sessionNotFound`, 409 `sessionEnded`.
4. When the request's turn comes (requests for one pane run one at a time, in the order they arrived), right before the first key or character, the PC compares with the pane as it is at that moment:
   - `question` must be the `question` the pane's Session would have now, as the phone would get it (after the PC hid key-like text in it): the same string, compared exactly, or both `null`. Otherwise 409 `dialogChanged`.
   - When the pane asks now (its status would be `needsYou`), the dialog must be one the phone's screen showed: the screen `seq` at which the PC first saw this dialog must be at most `seq`. Otherwise 409 `dialogChanged`. A dialog is its question with its choices, as for Attention: one that left the screen and came back, or was answered and then asked again, is a new dialog with a new first `seq`.
   - `seq` is above the pane's current screen `seq` (the phone's screen isn't of this pane, for example from before Agent Arcade started again): 409 `dialogChanged`.
5. Only then, for `/input` with `submit: true`, the `dialogOpen` checks ([input](#input)): before typing, and again right before Enter.

The PC notes a dialog's first `seq` no later than when it first sends a Session or a Screen that shows it, so a phone whose screen showed the dialog doesn't get a false `dialogChanged`. In one `/keys` request only the first key is checked: the rest are the user's own sequence (`down`, `enter`).

What the phone does:

- It sends `expect` with every `/keys` and `/input` request from a chat, the key chips included. The chat watches the pane, so it has a Screen; without one it doesn't send answers.
- It sends a request with `expect` only after its previous request for that pane was answered and a newer Session or Screen shows the result. Otherwise it compares with a pane that is still changing.
- On 409 `dialogChanged` or `runChanged` nothing was typed: it shows the sentence and the pane as it is now, and the user decides again. It never resends with a newer `expect` by itself: that would answer a question the user hasn't seen.
- Session (with `question`) and Screen (with `seq`) arrive separately, so one may be a moment older than the other. That gives at worst a `dialogChanged`, and the user answers again.

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
- `state` whenever workspaces, agents or accounts change (it always includes all sessions too). 1.1: also when `run` changes while the phone is connected, before any Session or Attention of the new run.
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
  "version": "1.4.0",
  "run": "9c4e1a7f03b2d865",
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
- `run` (1.1, feature `run`) is the current start of Agent Arcade's page: see [run](#run).
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
  "rows": 40,
  "run": "9c4e1a7f03b2d865"
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
| `seenOnPc` | `true` only while someone is looking at this pane on the PC right now: the Agent Arcade window has focus, the pane is focused, there was keyboard or mouse input on the PC in the last 45 s, and the Windows session is unlocked. Otherwise `false`, for example when the user walked away from a focused pane or locked the PC |
| `cols`, `rows` | The pane's terminal size on the PC |
| `run` | (1.1, feature `run`) The run this pane belongs to; always the `run` of the State it came with. See [run](#run) |

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
  "at": 1791893401000,
  "run": "9c4e1a7f03b2d865"
}
```

Sent when the desktop would turn the pane amber or notify (`needsYou` or `done`), and when an agent ends (`ended`). It doesn't depend on the PC's Windows notification setting.

- `needsYou` goes out once per dialog, as soon as the screen starts asking (see the status rules), however soon after Enter that is. A dialog is its `question` together with its `choices`: the same dialog still on screen, or drawn again after a resize, sends nothing new. A dialog that comes back after it was answered or left the screen counts as new. An agent's own notification about a dialog that is about to show counts for that dialog.
- `done` goes out when the agent worked after Enter and then went quiet, or sent its own "waiting for you" notification; at most once per 8 s per pane.
- `seenOnPc` has the same meaning as in Session: it is `false` while nobody touched the PC's keyboard or mouse for 45 s or the Windows session is locked, even when the pane still has focus.
- `run` (1.1, feature `run`): the run of the pane `id`, as in Session.

The phone skips the alert when `seenOnPc` is true, when that chat is open on the phone's screen, or when the user muted that chat or that kind of alert.

### run

(1.1, feature `run`.) Pane ids (`s` and up to 9 digits) are numbered from a new starting point whenever Agent Arcade's page starts: when Agent Arcade starts, and if its page ever loads again (every agent stops then). From 1.3.4 that starting point is random, so ids rarely repeat, but nothing guarantees it. So an id alone doesn't say which pane it is after Agent Arcade started again; `run` does.

- `run` is 16 lowercase hex characters (8 random bytes), for example `9c4e1a7f03b2d865`. The PC makes a new one each time its page starts, and keeps it until the next start. Within one run a pane id is never used for a second pane, so (`id`, `run`) names one pane for good.
- State, every Session and every Attention carry it. A Session always has the `run` of the State it came with.
- When the run changes while a phone is connected, the PC sends `state` with the new run before any Session or Attention of that run. The panes of the old run are gone; a `closed` may or may not come for each of them. Every `watch` ends with them, also when a pane of the new run gets a watched id: the phone sends `watch` again for a pane of the new run it shows, and no `unwatch` for the old ones.
- The phone compares runs only for equality, never parses them, and treats a missing `run` (a 1.0 PC) as one unknown run that never changes.

What the phone does:

- It binds every chat it shows, every alert and notification, and every request about a pane to (`id`, `run`), and counts "one alert per dialog" per (`run`, `id`), so a new `s3` alerts even when an old `s3` already did.
- When `hello` or `state` brings another `run`: the chats it shows of the old run end ("That agent isn't open on the PC any more"), it sends nothing for panes of the old run, and a notification of the old run opens the agents list instead of a chat.
- It ignores a Session or Attention whose `run` isn't the current one (it may come from a REST answer that was on its way while Agent Arcade started again).
- It sends the pane's `run` in [`expect`](#expect-what-the-phone-saw); the PC refuses keys and input for another run with 409 `runChanged`. `stop`, `close` and `rename` carry no `run`: before sending one, the phone checks that the pane belongs to the current run.

## 6. Security

- Phone remote is **off by default**. The PC listens only while it is on, and stops when it is turned off or Agent Arcade closes.
- **Certificate:** made on the PC the first time (ECDSA P-256, `CN=Agent Arcade Remote`, valid 20 years) and kept in the Windows user certificate store. The phone pins its SHA-256 fingerprint from the pairing link.
- **Pairing codes:** 16 random bytes, 10 minutes, single use, five wrong tries cancel the code. At most 10 pairing attempts per minute in total.
- **Pairing needs a click on the PC** (1.1, `pairApproval`): the right code alone adds no phone. The PC names the phone and waits up to 60 s for **Allow**; Deny, silence, New code or switching Phone remote off add nothing, and the code is used up either way. So someone who saw the QR code or the copied link can't pair without the person at the PC noticing.
- **Phone names** (`device`) are shown on the PC, so the PC removes control and formatting characters (such as right-to-left overrides), collapses spaces and cuts them to 60 characters; an empty name becomes "Phone".
- **Device tokens:** 32 random bytes (base64url). The PC stores only their SHA-256 hash in `%LOCALAPPDATA%\AgentArcade\remote.json` (with the phone's name, paired time and last seen) and compares hashes in constant time. **Remove** in the Phone dialog deletes the hash and disconnects that phone at once (close code 4401).
- **What a phone can do** is what a person at the PC can do inside the panes: read the screens, type, answer, start, stop, resume and rename agents. A paired phone is as powerful as the keyboard in those panes: agents run with the user's Windows permissions, so by typing to an agent a phone can reach files indirectly. The API itself gives no file access, runs nothing outside an agent pane, changes no settings and shows no account tokens. The one thing the API itself makes on the PC's disk is an empty project folder in `projectsFolder`, under a name the PC checked (`POST /projects`).
- **Limits:** request bodies up to 64 KB, `input` text up to 16 KB, 20 requests per second per phone and at most 4 of its requests waiting for Agent Arcade at once (429 above that), 4 phones connected at once, 8 open connections per address, 4 KB per WebSocket message from the phone.
- **Network:** the PC accepts connections only from local and Tailscale addresses (127.0.0.0/8, 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16, 100.64.0.0/10), so the port stays closed to the internet even when the PC has a public address. TLS 1.2 and 1.3 only, HTTP/1.1 only. More than 20 wrong tokens in a minute from one address lock that address out for a minute.
- **Removing a phone** also cancels typing it asked for that hasn't happened yet; so does turning Phone remote off.
- **Logs:** tokens, pairing codes and screen text are never written to the log, on either side. Neither is the `X-Arcade-Client` header.
- The phone stores the token encrypted with an Android Keystore key, allows no cleartext traffic, and never accepts another certificate.

## 7. Trying it without the phone

From Git Bash on the PC, with Phone remote on. Copy the pairing link with **Copy link** in the Phone dialog and take the `code` from it. `-k` skips the certificate check; only use it for this test. With `pairApproval` the pair request waits: keep the Phone dialog open and click **Allow** within 60 s.

```bash
curl -k https://127.0.0.1:47821/api/v1/hello
curl -k -X POST https://127.0.0.1:47821/api/v1/pair -H 'Content-Type: application/json' -H 'X-Arcade-Client: curl/8.10.1' \
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
