# Agent Arcade

.NET 10 WinForms app whose window is a local web page in WebView2. Agents run in ConPTY sessions (`Pty.cs`) and are drawn by xterm.js 5.5 (`web/vendor`). See README.md for the file map.

## Commands

- Build and check: `dotnet build -c Release -o <temp dir>` then `<temp dir>\AgentArcade.exe --self-check`; results go to `%TEMP%\AgentArcade-selfcheck.txt` (must end with `SELF_CHECK_OK`).
- Install: `install.ps1` (folder publish, `--no-self-contained`). Not single-file: WebView2 and `web/` must sit next to the exe. The app must be closed first.

## Rules

- Never read, copy, store or swap login tokens or credential files, and never switch accounts automatically when usage runs low. Each tool signs in through its own official flow; extra accounts only point the tool at its own folder (`CLAUDE_CONFIG_DIR`, `CODEX_HOME`, `GEMINI_CLI_HOME`).
- Account emails come from the tools themselves: `claude auth status --json`, Codex `app-server` `account/read`, Gemini `.gemini/google_accounts.json` (emails only), Cursor `cursor-agent status`.
- Start Windows programs by full path (`Environment.SystemDirectory`, the Windows folder), never by bare name.
- Message types between `web/app.js` and `MainForm.OnMessage` must match on both sides.
- Chat history (`history.json`) stores only titles, folders, times and the chat id this app chose (Claude `--session-id`); never chat contents.
- A built-in agent added to `AgentStore.Defaults` reaches existing `agents.json` files once, through `agents-offered.json`. New `AgentDef` fields should be nullable so a missing value can take the built-in one.
- The page must not reload while agents run: a reload sends `ready`, which stops every session.
