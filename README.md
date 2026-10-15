# Agent Arcade

A Windows app that runs your AI coding agents side by side on one project: Claude Code, Codex (ChatGPT), Gemini CLI, GitHub Copilot CLI and Cursor, each in its own pane inside the app.

- Each agent signs in with its own official login. Agent Arcade never reads or stores passwords or login tokens.
- Extra accounts (for example a second Claude subscription) get their own folder, and the sidebar shows which email each account uses.
- Workspaces are your project folders. Agents always start in the selected one.
- Each pane shows the chat's title: the one the agent gives it, or else your first message. Double-click the title to rename it.
- The sidebar lists each project's running agents. The arrow next to a project hides them, and the arrow next to Agents hides the whole agent list (it then shows how many are running). The button left of the project name hides the whole sidebar, so the panes get its width; click it again to bring the sidebar back.
- Drag a pane by its header onto another pane to swap their places in the grid.
- Drop files or images on a pane to paste their paths into the agent's prompt. Claude Code and Codex attach pasted image paths; Gemini gets them as `@path`. Images dragged from a browser are saved to `%TEMP%\AgentArcade\drops` first.
- Click an image path an agent prints to view the image in the pane (arrow keys switch images, Esc closes).
- The side panel on the right (the button at the right end of the top bar shows and hides it; drag its left edge to resize; in a narrow window it floats over the panes) has four tabs:
  - **Preview** shows a page you are working on: a local address such as `localhost:5173`, or a page in the project such as `index.html` (the address box suggests the project's pages). Clicking a localhost link in a pane or the console opens it here; other links open in your browser. Previewed pages run sandboxed and can't reach or reload the app.
  - **Images** lists the images in the project and in the agents' own image folders (Codex's `generated_images`), the 300 newest of them, skipping build, package and hidden folders. Drag one onto a pane to paste its path into that agent's prompt. **Images** in the top bar opens this tab.
  - **Changes** shows what git sees changed in the project, with the diff of the file you pick. It looks again every 5 seconds while it shows, without taking git's index lock.
  - **Console** is a PowerShell (PowerShell 7 if installed) in the project folder. It isn't an agent: it doesn't count when you close the app, and the phone never sees it.
- Each pane's dot shows its status: orange while the agent works, red when it needs you, green when it is idle, grey when it has ended.
- Windows notifies you when an agent is done or asks for your OK, unless you are already looking at its pane: an agent that worked after you pressed Enter and then goes quiet, or one that sends its own notification (OSC 9/777, bell). Click the notification to jump to the pane. The pane's dot turns red until you look at it. Turn notifications off in a pane's ⋯ menu.
- Text size: **Text size** at the bottom of the sidebar, or Ctrl+= / Ctrl+- / Ctrl+0 in a pane. It applies to every pane.
- History lists every chat per project, including projects you removed from the sidebar. Resume continues a chat: Claude Code reopens exactly that chat; the other agents open their own resume list. Only titles, folders and times are kept (`history.json`); the chats themselves stay in each agent's own files.
- A pane's ⋯ menu pastes a "save progress" prompt (writes `HANDOFF.md`) and a "continue" prompt, so the next agent can pick up where the last one stopped.
- Phone remote: watch and drive the agents from your Android phone with the Agent Arcade Remote app (see below).

## Phone remote

The Android app **Agent Arcade Remote** shows every pane's status and progress, alerts you when an agent needs you, and lets you read a pane's terminal, approve or refuse what it asks, type to it, and start, stop, resume and rename agents. The phone talks to this PC directly, over your Wi-Fi or over [Tailscale](https://tailscale.com); nothing goes through a cloud service. The build plan and the API are in [docs/phone-remote](docs/phone-remote/README.md).

1. Sidebar → **Phone** → switch on **Let my phone watch and drive these agents**. Agent Arcade listens on port 47821 (`RemotePort` in `settings.json`) only while this is on.
2. The first time, Windows asks whether Agent Arcade may use the network. Allow it on **private** networks. Your Wi-Fi must be set to *Private* in Windows (Settings → Network & internet → Wi-Fi → your network); on a *Public* network Windows blocks the phone.
3. On the phone: **Scan QR code**, check that the fingerprint matches the one in the Phone dialog, and tap **Pair**.

Away from home: install Tailscale on the PC and the phone and sign in with the same account. The pairing link lists the PC's Tailscale address after its Wi-Fi address, so the phone finds it either way. Tailscale encrypts the connection on top of Agent Arcade's own encryption. Tailscale's Personal plan is free for non-commercial use (up to 6 users, unlimited devices). The Phone dialog marks which address is the Tailscale one.

Keeping the PC awake: by default Windows doesn't go to sleep on its own while agents run (the screen still turns off). In the Phone dialog, **Keep this PC awake** can also cover the time Phone remote is on, so the phone can start agents while you're out, or be switched off. Windows still sleeps when you close the lid, press Sleep or the power button, and on battery a few minutes after its sleep time, so keep the PC plugged in; on a laptop, set "When I close the lid" to *Do nothing* for *Plugged in* to run it with the lid closed.

How it stays safe:

- HTTPS only (TLS 1.2 or 1.3). The PC makes its own certificate once (kept in your Windows user certificate store), and the phone accepts only that certificate: it gets the certificate's fingerprint from the QR code.
- A pairing code works once, for 10 minutes; five wrong codes cancel it. Each phone gets its own token. The PC keeps only a hash of it in `remote.json`, and **Remove** in the Phone dialog cuts that phone off at once.
- Only addresses on your local network or Tailscale can connect; the port stays closed to the internet even if the PC has a public address.
- A paired phone can do what you can do inside the panes (read, type, approve, start, stop, resume, rename). It can also make a new, empty project folder next to your current project (**New project** on the phone), which then appears at the end of the sidebar. It can't read files, change settings, start agents outside your workspaces or see account logins. Tokens, pairing codes and terminal text are never written to the log.

## Requirements

- Windows 11 (it includes the WebView2 runtime the window needs)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build. It includes the .NET and ASP.NET Core runtimes the app needs; on a PC without the SDK, install the .NET 10 Desktop Runtime and the ASP.NET Core 10 Runtime.
- The agents themselves; the app can install them for you (Install in the sidebar). Codex, Gemini and Copilot need [Node.js](https://nodejs.org); Cursor installs with its own PowerShell installer.

## Build and install

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

This publishes the app to `%LOCALAPPDATA%\Programs\AgentArcade` and adds a Start menu shortcut. Close Agent Arcade before running it.

`install-and-run.ps1` does it all in one go: it builds first (a build error leaves the open app alone), then closes the open app the way its close button does (with agents running, the app asks you first, and Cancel stops the script), installs, and starts the new version. Run it from a terminal outside Agent Arcade or with the Claude app's Run button, not from a pane inside Agent Arcade. `test-run.ps1` builds a test copy into `%TEMP%\AgentArcade-test-build` and starts it next to the installed app, with its own data in `%TEMP%\AgentArcade-test-home` (see below); it installs nothing.

To try a change without installing: `dotnet run` (while the installed app is closed). To run the built-in checks: `AgentArcade.exe --self-check`, then read `%TEMP%\AgentArcade-selfcheck.txt`.

To run a test build next to the installed app, set `AGENT_ARCADE_HOME` to an empty folder first. That copy keeps its own settings, agents, history and paired phones there, and doesn't count as the same app. With `AGENT_ARCADE_REMOTE_LOOPBACK=1` its Phone remote listens on 127.0.0.1 only, so Windows doesn't show a firewall prompt for it; give it another `RemotePort` in its `settings.json`.

## Files

| Path | What it does |
| --- | --- |
| `Program.cs` | Start-up, single-instance check, `--self-check` |
| `MainForm.cs` | The window: hosts the web page and passes messages between it and the agents |
| `Pty.cs` | Runs an agent in a Windows pseudo console (ConPTY) |
| `Images.cs` | Serves image files to the page and watches folders for new images |
| `Panel.cs` | The side panel's file lists (Images tab, Preview suggestions) and its Changes tab (git status and diffs) |
| `Remote.cs` | Phone remote: the HTTPS and WebSocket server, its certificate, paired phones and pairing codes |
| `Power.cs` | Keeps Windows from sleeping on its own while agents run (a power request), and reads the battery and lid settings |
| `Backend.cs` | Settings, agent list, accounts, sign-in checks, chat history, Windows Terminal launcher, shared instructions |
| `web/` | The interface (`index.html`, `app.css`, `app.js`, `icon.png`), `remote.js` (what the phone sees: pane status, approvals, plans and screens read from the terminals) and xterm.js in `web/vendor` (MIT licence) |
| `docs/phone-remote/` | The Android app's build plan and `REMOTE_API.md`, the contract between the PC and the phone |
| `app.ico` | The app icon (the exe, window and taskbar) |

Settings, the agent list (`agents.json`), extra accounts, chat history (`history.json`), paired phones (`remote.json`) and a log live in `%LOCALAPPDATA%\AgentArcade`.
