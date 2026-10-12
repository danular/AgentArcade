# Agent Arcade

A Windows app that runs your AI coding agents side by side on one project: Claude Code, Codex (ChatGPT), Gemini CLI, GitHub Copilot CLI and Cursor, each in its own pane inside the app.

- Each agent signs in with its own official login. Agent Arcade never reads or stores passwords or login tokens.
- Extra accounts (for example a second Claude subscription) get their own folder, and the sidebar shows which email each account uses.
- Workspaces are your project folders. Agents always start in the selected one.
- Each pane shows the chat's title: the one the agent gives it, or else your first message. Double-click the title to rename it.
- The sidebar lists each project's running agents. The arrow next to a project hides them, and the arrow next to Agents hides the whole agent list (it then shows how many are running).
- Drag a pane by its header onto another pane to swap their places in the grid.
- History lists every chat per project, including projects you removed from the sidebar. Resume continues a chat: Claude Code reopens exactly that chat; the other agents open their own resume list. Only titles, folders and times are kept (`history.json`); the chats themselves stay in each agent's own files.
- A pane's ⋯ menu pastes a "save progress" prompt (writes `HANDOFF.md`) and a "continue" prompt, so the next agent can pick up where the last one stopped.

## Requirements

- Windows 11 (it includes the WebView2 runtime the window needs)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- The agents themselves; the app can install them for you (Install in the sidebar). Codex, Gemini and Copilot need [Node.js](https://nodejs.org); Cursor installs with its own PowerShell installer.

## Build and install

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

This publishes the app to `%LOCALAPPDATA%\Programs\AgentArcade` and adds a Start menu shortcut. Close Agent Arcade before running it.

To try a change without installing: `dotnet run`. To run the built-in checks: `AgentArcade.exe --self-check`, then read `%TEMP%\AgentArcade-selfcheck.txt`.

## Files

| Path | What it does |
| --- | --- |
| `Program.cs` | Start-up, single-instance check, `--self-check` |
| `MainForm.cs` | The window: hosts the web page and passes messages between it and the agents |
| `Pty.cs` | Runs an agent in a Windows pseudo console (ConPTY) |
| `Backend.cs` | Settings, agent list, accounts, sign-in checks, chat history, Windows Terminal launcher, shared instructions |
| `web/` | The interface (`index.html`, `app.css`, `app.js`, `icon.png`) and xterm.js in `web/vendor` (MIT licence) |
| `app.ico` | The app icon (the exe, window and taskbar) |

Settings, the agent list (`agents.json`), extra accounts, chat history (`history.json`) and a log live in `%LOCALAPPDATA%\AgentArcade`.
