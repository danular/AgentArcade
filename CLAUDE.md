# Agent Arcade

.NET 10 WinForms app whose window is a local web page in WebView2. Agents run in ConPTY sessions (`Pty.cs`) and are drawn by xterm.js 5.5 (`web/vendor`). See README.md for the file map.

## Commands

- Build and check: `dotnet build -c Release -o <temp dir>` then `<temp dir>\AgentArcade.exe --self-check`; results go to `%TEMP%\AgentArcade-selfcheck.txt` (must end with `SELF_CHECK_OK`).
- Install: `install.ps1` (folder publish, `--no-self-contained`). Not single-file: WebView2 and `web/` must sit next to the exe. The app must be closed first.
- `install-and-run.ps1` is the user's one-click update (builds, closes the app with SC_CLOSE so it asks about running agents, installs, starts it). Never run it yourself: it closes the user's agents. `test-run.ps1` starts a test copy instead.
- WebView2 resource filters must use the `AddWebResourceRequestedFilter` overload with `CoreWebView2WebResourceRequestSourceKinds`; the old one misses the side panel's cross-origin iframe.

## Rules

- Never read, copy, store or swap login tokens or credential files, and never switch accounts automatically when usage runs low. Each tool signs in through its own official flow; extra accounts only point the tool at its own folder (`CLAUDE_CONFIG_DIR`, `CODEX_HOME`, `GEMINI_CLI_HOME`).
- Account emails come from the tools themselves: `claude auth status --json`, Codex `app-server` `account/read`, Gemini `.gemini/google_accounts.json` (emails only), Cursor `cursor-agent status`.
- Start Windows programs by full path (`Environment.SystemDirectory`, the Windows folder), never by bare name.
- Message types between `web/app.js` and `MainForm.OnMessage` must match on both sides.
- Chat history (`history.json`) stores only titles, folders, times and the chat id this app chose (Claude `--session-id`); never chat contents.
- A built-in agent added to `AgentStore.Defaults` reaches existing `agents.json` files once, through `agents-offered.json`. New `AgentDef` fields should be nullable so a missing value can take the built-in one.
- The page must not reload while agents run: a reload sends `ready`, which stops every session.
- Phone remote (`Remote.cs`, `web/remote.js`): the contract with the Android app is `docs/phone-remote/REMOTE_API.md`. Change that file first, then both sides together; never rename or drop a field the phone reads.
- Never log tokens, pairing codes or terminal text (also not through exception messages or toasts into `log.txt`). The pairing code appears only in the Phone dialog (QR code, Copy link). `remote.json` keeps only SHA-256 hashes of device tokens.
- The phone gets nothing a person at the PC couldn't see or do inside the panes: no files, no settings, no account tokens, no agents outside the listed workspaces. Validate every phone request in C# before it reaches the page.
- Phone requests are answered by the page (`remoteReply` with the `requestId`); C# times them out after 10 s with 503. Remote actions must not move the PC's view: no workspace switch, no focus, no modal.
- Test a build next to the installed app with `AGENT_ARCADE_HOME=<empty folder>` (own data and single-instance mutex) and `AGENT_ARCADE_REMOTE_LOOPBACK=1` (no firewall prompt). Don't close the installed app to test; it runs the user's agents.
