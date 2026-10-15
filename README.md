<img src="web/icon.png" alt="Agent Arcade icon" width="96" align="right">

# Agent Arcade

A Windows app that runs your AI coding agents side by side on one project: Claude Code, Codex (ChatGPT), GitHub Copilot CLI, Cursor and Gemini CLI (API key or Code Assist), each in its own pane inside the app.

Free for personal use. Professional use needs Agent Arcade Pro (EUR 25 per user per month excl. VAT); see [LICENSING.md](LICENSING.md). Pro checkout isn't open yet, and the voice dictation app planned for Pro is still coming.

## Download

1. Download [AgentArcade-win-x64.zip](https://github.com/danular/AgentArcade/releases/latest/download/AgentArcade-win-x64.zip) (the newest version; all versions are on the [releases page](https://github.com/danular/AgentArcade/releases)). It runs on Windows 11 (x64) and needs nothing else installed: the .NET runtime is inside the zip. If Microsoft Edge says the file isn't commonly downloaded, open the download's **⋯** menu, choose **Keep**, then **Show more** → **Keep anyway**.
2. Check the file: each release lists the zip's SHA-256 (so does the [download page](https://danular.github.io/AgentArcade/download.html#verify)). In PowerShell, in the download folder, this prints `True` when the file matches:
   ```powershell
   (Get-FileHash .\AgentArcade-win-x64.zip -Algorithm SHA256).Hash -eq '<SHA-256 from the release>'
   ```
3. Extract the zip to a folder of your choice, for example `%LOCALAPPDATA%\Programs\AgentArcade`. `AgentArcade.exe` is at the top of the zip; start it. Keep the files together: the app needs the `web` folder next to it.
4. The download isn't code-signed yet, so Windows SmartScreen may say it "protected your PC". Click **More info** → **Run anyway**. If Smart App Control is on, Windows blocks unsigned apps and has no per-app exception, so Agent Arcade can't run there until it is code-signed.

To update, close Agent Arcade (its agents stop), extract the new zip over the old folder (the one that holds `AgentArcade.exe`), replacing the files, and start it again. Settings, history and paired phones stay in `%LOCALAPPDATA%\AgentArcade`. To uninstall, delete the app folder and `%LOCALAPPDATA%\AgentArcade`; if you used Phone remote, also remove its certificate (`CN=Agent Arcade Remote` in your Windows user certificate store) and the firewall rule Windows made for `AgentArcade.exe`. The [download page](https://danular.github.io/AgentArcade/download.html#uninstall) has the commands.

Then click **Install** in the sidebar for any agent you don't have yet. Building it yourself is described under [Build and install](#build-and-install).

- Each agent signs in with its own official login. Agent Arcade never reads or stores passwords or login tokens.
- Gemini CLI needs a Gemini API key or a Gemini Code Assist Standard or Enterprise licence: Google stopped serving it for personal Google sign-ins (free, Google AI Pro and Ultra) on 18 Jun 2026.
- Extra accounts (for example a second Claude subscription) get their own folder, and the sidebar shows which email each account uses.
- Workspaces are your project folders. Agents always start in the selected one. **+** adds a folder you already have; **New project** (the folder icon next to it) makes an empty folder next to your current project and opens it.
- Each pane shows the chat's title: the one the agent gives it, or else your first typed message (pasted text and anything that looks like a key don't count). Double-click the title to rename it.
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
- Windows notifies you when an agent is done or asks for your OK, unless you are already looking at its pane: a dialog that asks you something (once per dialog, as soon as it shows), an agent that worked after you pressed Enter and then goes quiet, or one that sends its own notification (OSC 9/777, bell). A focused pane still notifies when nobody touched the keyboard or mouse for 45 seconds or the PC is locked. Click the notification to jump to the pane. The pane's dot turns red until you look at it. Turn notifications off in a pane's ⋯ menu.
- Text size: **Text size** at the bottom of the sidebar, or Ctrl+= / Ctrl+- / Ctrl+0 in a pane. It applies to every pane.
- The version number sits at the bottom right of the sidebar. Once a day Agent Arcade asks GitHub's releases API for the newest version number (no identifier is sent and nothing is downloaded) and shows a notice above it when a newer version is out. Click the version number to switch this off.
- History lists every chat per project, including projects you removed from the sidebar. Resume continues a chat: Claude Code reopens exactly that chat; the other agents open their own resume list. Only titles, folders and times are kept (`history.json`); the chats themselves stay in each agent's own files. A title can be the start of your first message.
- A pane's ⋯ menu pastes a "save progress" prompt (writes `HANDOFF.md`) and a "continue" prompt, so the next agent can pick up where the last one stopped.
- Phone remote: watch and drive the agents from your Android phone with the Agent Arcade Remote app (see below).

## Phone remote

The Android app **Agent Arcade Remote** shows every pane's status and progress, alerts you when an agent needs you, and lets you read a pane's terminal, approve or refuse what it asks, type to it, and start, stop, resume and rename agents. Agent screens and your typing go only between your phone and this PC, over your Wi-Fi or over [Tailscale](https://tailscale.com); there is no Agent Arcade server in between. It isn't on Google Play yet: open [AgentArcadeRemote.apk](https://github.com/danular/AgentArcade/releases/latest/download/AgentArcadeRemote.apk) on your phone (Android 8.0 or newer) and install it. The build plan and the API are in [docs/phone-remote](docs/phone-remote/README.md).

1. Sidebar → **Phone** → switch on **Let my phone watch and drive these agents**. Agent Arcade listens on port 47821 (`RemotePort` in `settings.json`) only while this is on.
2. The first time, Windows asks whether Agent Arcade may use the network. Allow it on **private** networks. On your own home Wi-Fi, set the network to *Private* in Windows (Settings → Network & internet → Wi-Fi → your network); on a *Public* network Windows blocks the phone. On shared Wi-Fi (an office, a café, a hotel) keep it *Public* and use Tailscale (below).
3. On the phone: **Scan QR code**, check that the fingerprint matches the one in the Phone dialog, and tap **Pair**.

Away from home: install Tailscale on the PC and the phone and sign in with the same account. The pairing link lists the PC's Tailscale address after its Wi-Fi address, so the phone finds it either way. Tailscale encrypts the connection on top of Agent Arcade's own encryption. When the phone and PC can't reach each other directly, Tailscale relays that encrypted traffic through its own DERP servers. Tailscale's Personal plan is free for non-commercial use (up to 6 users, unlimited devices). The Phone dialog marks which address is the Tailscale one.

Keeping the PC awake: by default Windows doesn't go to sleep on its own while agents run (the screen still turns off). In the Phone dialog, **Keep this PC awake** can also cover the time Phone remote is on, so the phone can start agents while you're out, or be switched off. Windows still sleeps when you close the lid, press Sleep or the power button, and on battery a few minutes after its sleep time, so keep the PC plugged in; on a laptop, set "When I close the lid" to *Do nothing* for *Plugged in* to run it with the lid closed.

How it stays safe:

- HTTPS only (TLS 1.2 or 1.3). The PC makes its own certificate once (kept in your Windows user certificate store), and the phone accepts only that certificate: it gets the certificate's fingerprint from the QR code.
- A pairing code works once, for 10 minutes; five wrong codes cancel it. Each phone gets its own token. The PC keeps only a hash of it in `remote.json`, and **Remove** in the Phone dialog cuts that phone off at once.
- Only addresses on your local network or Tailscale can connect; the port stays closed to the internet even if the PC has a public address.
- A paired phone is as powerful as your keyboard in the panes: it can read, type, approve, start, stop, resume and rename. Agents run with your Windows permissions, so by typing to an agent a phone can reach your files indirectly, as you could at the PC. It can also make a new, empty project folder next to your current project (**New project** on the phone, `POST /projects`), which then appears at the end of the sidebar. It has no way of its own to read files, change settings, start agents outside your workspaces or see account logins. Treat a paired phone like a key and **Remove** a lost one. Tokens, pairing codes and terminal text are never written to the log.

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

To try a change without installing: `dotnet run` (while the installed app is closed). To run the built-in checks: `AgentArcade.exe --self-check`, then read `%TEMP%\AgentArcade-selfcheck.txt`. `node --test tests/remote.test.js` checks the page's alert rules (one alert per dialog).

To run a test build next to the installed app, set `AGENT_ARCADE_HOME` to an empty folder first. That copy keeps its own settings, agents, history and paired phones there, and doesn't count as the same app. With `AGENT_ARCADE_REMOTE_LOOPBACK=1` its Phone remote listens on 127.0.0.1 only, so Windows doesn't show a firewall prompt for it; give it another `RemotePort` in its `settings.json`.

## Files

| Path | What it does |
| --- | --- |
| `Program.cs` | Start-up, single-instance check, `--self-check` |
| `MainForm.cs` | The window: hosts the web page and passes messages between it and the agents |
| `Pty.cs` | Runs an agent in a Windows pseudo console (ConPTY) |
| `Images.cs` | Serves image files to the page and watches folders for new images |
| `Panel.cs` | The side panel's file lists (Images tab, Preview suggestions) and its Changes tab (git status and diffs, after `GitConfigCheck` reads the repository's config: no program it names runs, no network path it names is reached) |
| `Remote.cs` | Phone remote: the HTTPS and WebSocket server, its certificate, paired phones and pairing codes |
| `Power.cs` | Keeps Windows from sleeping on its own while agents run (a power request), reads the battery and lid settings, and whether someone is at the PC (time since the last input, for alerts) |
| `Update.cs` | The once-a-day update check: the newest release's tag from GitHub's releases API, compared with the running version |
| `Backend.cs` | Settings, agent list, accounts, sign-in checks, chat history, Windows Terminal launcher, shared instructions |
| `web/` | The interface (`index.html`, `app.css`, `app.js`, `icon.png`), `remote.js` (what the phone sees: pane status, approvals, plans and screens read from the terminals) and xterm.js in `web/vendor` (MIT licence) |
| `docs/phone-remote/` | The Android app's build plan and `REMOTE_API.md`, the contract between the PC and the phone |
| `app.ico` | The app icon (the exe, window and taskbar) |
| `website/` | The marketing website: `pages/` (content) → `python website/build.py` (reads the newest release's version, sizes and SHA-256 values from the GitHub releases API) → `site/` (static HTML, no build tools needed to host it). `video/` renders the 30-second film (`promo.html`, `render.mjs`) and the social image |
| `release.ps1` | Builds a GitHub release's two files from the committed code, the flat self-contained `AgentArcade-win-x64.zip` and the signed `AgentArcadeRemote.apk`, and prints their SHA-256 |
| `SECURITY.md` | How to report a vulnerability (privately) |
| `docs/claims-checklist.md` | Every public claim on the website and in this README, with the code or dated source that proves it |

Settings, the agent list (`agents.json`), extra accounts, chat history (`history.json`), paired phones (`remote.json`), a log and the window's WebView2 data (`webview`) live in `%LOCALAPPDATA%\AgentArcade`.

## Security

Report vulnerabilities privately, never in a public issue: see [SECURITY.md](SECURITY.md).

## License

Agent Arcade is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE.md): free for personal and other noncommercial use. Professional use needs an Agent Arcade Pro subscription; [LICENSING.md](LICENSING.md) explains which one applies. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
