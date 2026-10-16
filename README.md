<img src="web/icon.png" alt="Agent Arcade icon" width="96" align="right">

# Agent Arcade

**Every coding agent's own CLI, side by side on Windows, and one phone app that talks only to your PC.**

[Website](https://danular.github.io/AgentArcade/) · [Download](https://danular.github.io/AgentArcade/download.html) · [Pricing](https://danular.github.io/AgentArcade/pricing.html)

<!-- MEDIA SLOT, for Daan: the 20-second muted clip goes here, above the screenshots. Record it in a test copy with demo
     accounts (no live pairing QR code, no real emails, paths or terminal text) and keep it as docs/media/hero.mp4. GitHub
     plays a video inline only when it is uploaded through the README editor, so drag the file into this README on
     github.com and keep the user-attachments link it inserts on its own line here. -->

<p>
  <img src="docs/media/panes.png" alt="Agent Arcade with four demo agents in a grid: two working (orange dots), one asking for permission (red), one idle (green)" width="49%">
  <img src="docs/media/side-panel.png" alt="One agent pane asking for permission, next to the side panel's Changes tab with a git diff" width="49%">
</p>

<sub>Screenshots from a test copy, with stand-in demo agents and a made-up project.</sub>

A Windows app that runs your AI coding agents side by side on one project: Claude Code, Codex (ChatGPT), GitHub Copilot CLI, Cursor, Antigravity CLI and Gemini CLI (API key or Code Assist), each in its own pane inside the app.

Free for personal use. Professional use needs Agent Arcade Pro (EUR 25 per user per month excl. VAT); see [LICENSING.md](LICENSING.md). Pro checkout isn't open yet, and the voice dictation app planned for Pro is still coming.

## Known gaps

- Windows 11 on x64 only. Windows 10 and Windows on ARM aren't tested, and there is no Mac or Linux version.
- The phone app is Android only (8.0 or newer). There is no iPhone app.
- No worktree isolation: agents in one project share its folder. Give them separate tasks, or open separate folders as separate workspaces.
- Agents don't survive an app restart. Closing Agent Arcade stops them; History can resume their chats afterwards.
- Status dots, alerts and phone approvals come from reading each agent's screen (approval prompts, "esc to interrupt" lines) and the notifications it sends. When a vendor changes its interface, a dot or an alert can be wrong until Agent Arcade catches up.
- Not code-signed yet: SmartScreen warns on first start, and Smart App Control blocks it, until the code-signing certificate arrives.

## How it differs (checked 16 Oct 2026)

Agent Arcade runs each vendor's own CLI, unmodified, in a Windows terminal pane (ConPTY), and its Android app connects only to your PC, over your Wi-Fi or Tailscale. It reads the terminal and never signs in to anything itself, so it works the same with a subscription, an API key, Amazon Bedrock or Google Cloud, and with Codex, GitHub Copilot CLI, Cursor and Gemini CLI as well as Claude Code. Each line links the product's own page as read on 16 Oct 2026, and says where it is ahead.

- **[Happy](https://github.com/slopus/happy)** has phone apps for iOS and Android, a web client and a desktop app for macOS, Windows and Linux, synced through its end-to-end encrypted relay server: ahead on iPhone, the browser and reaching you away from home without a VPN. Its original CLI wraps Claude Code and Codex, and its desktop app runs Happy's own agent runtime on your Claude, Codex and Grok subscriptions; Agent Arcade runs each vendor's CLI as it is, Copilot, Cursor and Gemini CLI included, with no Agent Arcade server in between.
- **[Paseo](https://paseo.sh/)** has native iOS and Android apps, a desktop app for macOS, Windows and Linux, a web app, a git worktree per agent, voice, and an optional end-to-end encrypted relay, a direct connection on your network or your own tunnel such as Tailscale: ahead on iPhone, Mac and Linux, worktree isolation and setup away from home. It shows agents in its own interface, through its own integrations or ACP; Agent Arcade shows each CLI's own terminal screen, on the PC and on the phone.
- **[Coucou](https://github.com/Louis-CFM/coucou)** lives in the Mac's notch (or the top of the screen on Windows and Linux) and watches agents that run in your own terminals, through their hooks, with an iPhone app in TestFlight beta on the Lock Screen and Dynamic Island, and an Android app by a contributor that talks to Coucou on Windows or Linux over your Wi-Fi: ahead on the Mac, the iPhone and fitting the terminal you already use. It watches agents and answers their approvals and questions; Agent Arcade runs the agents in its own panes, so the phone also reads the whole terminal, types to an agent, and starts, stops and resumes agents.
- **[Warp](https://docs.warp.dev/agents/cli-agents/overview)** is a full terminal for macOS, Windows and Linux (x64 and ARM64) that recognises Claude Code, Codex, GitHub Copilot CLI, Cursor CLI, Gemini CLI and more, with a rich input editor and code review comments, and its [Remote Control](https://docs.warp.dev/agents/cli-agents/remote-control/) opens a session in any browser, an iPhone's included: ahead as a terminal and on platforms. That Remote Control uploads the session to Warp's cloud; Agent Arcade's phone app gets agent screens straight from your PC.
- **[Claude Code Remote Control](https://code.claude.com/docs/en/remote-control)** is Anthropic's own, built into Claude Code, with the Claude app for iOS and Android, claude.ai/code in any browser, and a git worktree per session in server mode: ahead for Claude subscribers, with nothing extra to install. It needs a Pro, Max, Team or Enterprise plan (not an API key, Amazon Bedrock, Google Cloud or Microsoft Foundry), goes through Anthropic's servers, which store the session transcript, and covers Claude Code only; Agent Arcade also works for API-key, Bedrock and Google Cloud users, and with Codex, Copilot, Cursor and Gemini CLI.

## Download

1. Download [AgentArcade-win-x64.zip](https://github.com/danular/AgentArcade/releases/latest/download/AgentArcade-win-x64.zip) (the newest version; all versions are on the [releases page](https://github.com/danular/AgentArcade/releases)). It runs on Windows 11 (x64) and needs nothing else installed: the .NET runtime is inside the zip. If Microsoft Edge says the file isn't commonly downloaded, open the download's **⋯** menu, choose **Keep**, then **Show more** → **Keep anyway**.
2. Check the file: each release lists the zip's SHA-256 (so does the [download page](https://danular.github.io/AgentArcade/download.html#verify)). In PowerShell, in the download folder, this prints `True` when the file matches:
   ```powershell
   (Get-FileHash .\AgentArcade-win-x64.zip -Algorithm SHA256).Hash -eq '<SHA-256 from the release>'
   ```
3. Extract the zip to a folder of your choice, for example `%LOCALAPPDATA%\Programs\AgentArcade`. `AgentArcade.exe` is at the top of the zip; start it. Keep the files together: the app needs the `web` folder next to it.
4. The download isn't code-signed yet, so Windows SmartScreen may say it "protected your PC". Click **More info** → **Run anyway**. If Smart App Control is on, Windows blocks unsigned apps and has no per-app exception, so Agent Arcade can't run there until it is code-signed.

To update, close Agent Arcade (its agents stop), extract the new zip over the old folder (the one that holds `AgentArcade.exe`), replacing the files, and start it again. Settings, history and paired phones stay in `%LOCALAPPDATA%\AgentArcade`. To uninstall, delete the app folder and `%LOCALAPPDATA%\AgentArcade`; if you used Phone remote, also remove its certificate (`CN=Agent Arcade Remote` in your Windows user certificate store) and the firewall rule Windows made for `AgentArcade.exe`. The [download page](https://danular.github.io/AgentArcade/download.html#uninstall) has the commands.

Then open **Settings** (the gear at the bottom of the sidebar) → **Agents and accounts** and click **Install** for any agent you don't have yet. Building it yourself is described under [Build and install](#build-and-install).

- Each agent signs in with its own official login. Agent Arcade never reads or stores passwords or login tokens.
- Gemini CLI needs a Gemini API key or a Gemini Code Assist Standard or Enterprise licence: Google stopped serving it for personal Google sign-ins (free, Google AI Pro and Ultra) on 18 Jun 2026. With a personal Google account, use Antigravity CLI (`agy`), Google's replacement: it signs in with Google when it starts.
- Extra accounts (for example a second Claude subscription) get their own folder, and Settings → Agents and accounts shows which email each account uses.
- Projects (workspaces) are your project folders. Agents always start in the selected one. The **+** next to Projects in the sidebar adds a folder you already have, or makes a **New project**: an empty folder next to your current project (or in the folder picked under Settings → General → New projects go to), which then opens. On a new install, while the selected workspace is still your Documents or user folder, new projects go to `%USERPROFILE%\source\repos`, which is made with the first one.
- Each pane shows the chat's title: the one the agent gives it, or else your first typed message (pasted text and anything that looks like a key don't count). Double-click the title to rename it.
- Shift+Enter in a pane starts a new line in your message instead of sending it.
- **Home** (the start screen) shows every project as a card: its git branch and changed files, the agents running there with their status, a 10-day activity line from the chat history, its last two chats and one-click starts for your first three agents. Anything that needs you is listed on top; **Open** shows that pane.
- The sidebar has **New agent** (Ctrl+N, the same menu as in the top bar), **Home**, your **Projects** with a status dot per running agent (click a dot to show that pane) and **Recent**: your chats across projects, newest first; click one to continue it, the magnifier searches them. Its footer says how many agents need you or are working, and has the phone button and the gear for **Settings** (Ctrl+,).
- The top bar shows the project, its git branch and changed files (click it for the Changes tab), **New agent**, and a **⋯** menu with Open folder, Images, Shared instructions, Open in Windows Terminal, Open in Unity (for a Unity project) and the project's chat history. The button left of the project name hides the whole sidebar, so the panes get its width; click it again to bring the sidebar back.
- **Settings** has General (text size, notifications, keep this PC awake, where new projects go, update check, mascot animations), Agents and accounts (versions, accounts and emails, add account, install, sign in, check again, edit agent list), Phone, Shared instructions, MCP servers and About.
- Drag a pane by its header onto another pane to swap their places in the grid.
- Drop files or images on a pane to paste their paths into the agent's prompt. Claude Code and Codex attach pasted image paths; Gemini gets them as `@path`. Images dragged from a browser are saved to `%TEMP%\AgentArcade\drops` first.
- Click an image path an agent prints to view the image in the pane (arrow keys switch images, Esc closes).
- The side panel on the right (the button at the right end of the top bar shows and hides it; drag its left edge to resize; when it would leave a column of panes less than 360 px, it floats over the panes instead) has four tabs:
  - **Preview** shows a page you are working on: a local address such as `localhost:5173`, or a page in the project such as `index.html` (the address box suggests the project's pages). Clicking a localhost link in a pane or the console opens it here; other links open in your browser. Previewed pages run sandboxed and can't reach or reload the app.
  - **Images** lists the images in the project and in the agents' own image folders (Codex's `generated_images`), the 300 newest of them, skipping build, package and hidden folders. Drag one onto a pane to paste its path into that agent's prompt. **Images** in the top bar's ⋯ menu opens this tab.
  - **Changes** shows what git sees changed in the project, with the diff of the file you pick. It looks again every 5 seconds while it shows, without taking git's index lock.
  - **Console** is a PowerShell (PowerShell 7 if installed) in the project folder. It isn't an agent: it doesn't count when you close the app, and the phone never sees it.
- Each pane's dot shows its status by shape as well as colour (point at it to read it): a pulsing orange dot while the agent works, a red diamond when it needs you, a green ring when it finished while you weren't looking, a green dot when it is idle, grey when it has ended.
- Windows notifies you when an agent is done or asks for your OK, unless you are already looking at its pane: a dialog that asks you something (once per dialog, as soon as it shows), an agent that worked after you pressed Enter and then goes quiet, or one that sends its own notification (OSC 9/777, bell). A focused pane still notifies when nobody touched the keyboard or mouse for 45 seconds or the PC is locked. Click the notification to jump to the pane. The pane's dot stays a red diamond or a green ring until you look at it. Turn notifications off in Settings → General or in a pane's ⋯ menu.
- Text size: Settings → General (S, M, L, XL or an exact size), or Ctrl+= / Ctrl+- / Ctrl+0 in a pane. It applies to every pane.
- The window opens where you closed it, at the same size and maximized if it was (on the main screen if its screen is gone). The first time it takes about 85% of the main screen.
- The version number is in Settings → About. Once a day Agent Arcade asks GitHub's releases API for the newest version number (no identifier is sent and nothing is downloaded) and shows a notice at the bottom of the sidebar when a newer version is out. Switch this off in Settings → General → Check for updates.
- Recent in the sidebar and History (in a project's ⋯ menu) list every chat, including projects you removed from the sidebar. Resume continues a chat: Claude Code reopens exactly that chat; the other agents open their own resume list. Only titles, folders and times are kept (`history.json`); the chats themselves stay in each agent's own files. A title can be the start of your first message.
- A pane's ⋯ menu pastes a "save progress" prompt (writes `HANDOFF.md`) and a "continue" prompt, so the next agent can pick up where the last one stopped.
- Phone remote: watch and drive the agents from your Android phone with the Agent Arcade Remote app (see below).

## Phone remote

The Android app **Agent Arcade Remote** shows every pane's status and progress, alerts you when an agent needs you, and lets you read a pane's terminal, approve or refuse what it asks, type to it, and start, stop, resume and rename agents. Agent screens and your typing go only between your phone and this PC, over your Wi-Fi or over [Tailscale](https://tailscale.com); there is no Agent Arcade server in between. It isn't on Google Play yet: open [AgentArcadeRemote.apk](https://github.com/danular/AgentArcade/releases/latest/download/AgentArcadeRemote.apk) on your phone (Android 8.0 or newer) and install it. Coming from version 1.0? Uninstall it first: 1.1 has a new app id (for Google Play), so it can't install over 1.0, and you pair again. The build plan and the API are in [docs/phone-remote](docs/phone-remote/README.md).

1. The phone button at the bottom of the sidebar (Settings → **Phone**) → switch on **Let my phone watch and drive these agents**. Agent Arcade listens on port 47821 (`RemotePort` in `settings.json`) only while this is on.
2. The first time, Windows asks whether Agent Arcade may use the network. Allow it on **private** networks. Set the network to *Private* in Windows only if it is your own home network (Settings → Network & internet → Wi-Fi → your network); on a *Public* network Windows blocks the phone. On shared or public Wi-Fi (an office, a café, a hotel) keep it *Public* and use Tailscale (below).
3. Click **Pair a phone**. On the phone: **Scan QR code**, check that the fingerprint matches the one in the Phone tab, and tap **Pair**.
4. The Phone tab names the phone and asks whether it may pair: click **Allow** within a minute. Only then is it paired. With Settings closed, the phone button shows a red mark and a notice says a phone is waiting.

Away from home: install Tailscale on the PC and the phone and sign in with the same account. The pairing link lists the PC's Tailscale address after its Wi-Fi address, so the phone finds it either way. Tailscale encrypts the connection on top of Agent Arcade's own encryption. When the phone and PC can't reach each other directly, Tailscale relays that encrypted traffic through its own DERP servers. Tailscale's Personal plan is free for non-commercial use (up to 6 users, unlimited devices). The Phone tab marks which address is the Tailscale one.

Keeping the PC awake: by default Windows doesn't go to sleep on its own while agents run (the screen still turns off). In Settings → General, **Keep this PC awake** can also cover the time Phone remote is on, so the phone can start agents while you're out, or be switched off. Windows still sleeps when you close the lid, press Sleep or the power button, and on battery a few minutes after its sleep time, so keep the PC plugged in; on a laptop, set "When I close the lid" to *Do nothing* for *Plugged in* to run it with the lid closed.

How it stays safe:

- HTTPS only (TLS 1.2 or 1.3). The PC makes its own certificate once (kept in your Windows user certificate store), and the phone accepts only that certificate: it gets the certificate's fingerprint from the QR code.
- A pairing code exists only after **Pair a phone**, until you leave the Phone tab or close Settings; it works once, for 10 minutes, and five wrong codes cancel it. The code alone pairs nothing: the PC asks, and only **Allow** adds the phone. **Copy link** keeps the link out of Windows clipboard history. Each phone gets its own token. The PC keeps only a hash of it in `remote.json`, and **Remove** in the Phone tab cuts that phone off at once.
- Only addresses on your local network or Tailscale can connect; the port stays closed to the internet even if the PC has a public address.
- A paired phone is as powerful as your keyboard in the panes: it can read, type, approve, start, stop, resume and rename. Agents run with your Windows permissions, so by typing to an agent a phone can reach your files indirectly, as you could at the PC. It can also make a new, empty project folder next to your current project (**New project** on the phone, `POST /projects`), which then appears at the end of the sidebar. It has no way of its own to read files, change settings, start agents outside your workspaces or see account logins. Treat a paired phone like a key and **Remove** a lost one. Tokens, pairing codes and terminal text are never written to the log.

## Requirements

- Windows 11 (it includes the WebView2 runtime the window needs)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build. It includes the .NET and ASP.NET Core runtimes the app needs; on a PC without the SDK, install the .NET 10 Desktop Runtime and the ASP.NET Core 10 Runtime.
- The agents themselves; the app can install them for you (Install in Settings → Agents and accounts). Claude Code, Codex, Cursor and Antigravity CLI install with their vendor's PowerShell installer, GitHub Copilot CLI with WinGet (it brings PowerShell 7 along), and Gemini CLI with npm, which needs [Node.js](https://nodejs.org) 20 or newer. Install checks first and, when something is missing, says what and links to it instead of running.

## Build and install

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

This publishes the app to `%LOCALAPPDATA%\Programs\AgentArcade` and adds a Start menu shortcut. Close Agent Arcade before running it.

`install-and-run.ps1` does it all in one go: it builds first (a build error leaves the open app alone), then closes the open app the way its close button does (with agents running, the app asks you first, and Cancel stops the script), installs, and starts the new version. Run it from a terminal outside Agent Arcade or with the Claude app's Run button, not from a pane inside Agent Arcade. `test-run.ps1` builds a test copy into `%TEMP%\AgentArcade-test-build` and starts it next to the installed app, with its own data in `%TEMP%\AgentArcade-test-home` (see below); it installs nothing.

To try a change without installing: `dotnet run` (while the installed app is closed). To run the built-in checks: `AgentArcade.exe --self-check`, then read `%TEMP%\AgentArcade-selfcheck.txt`; with `--fixtures docs\phone-remote\fixtures` it also writes the Phone remote contract's example files there ([their README](docs/phone-remote/fixtures/README.md)). `node --test tests/remote.test.js` checks the page's alert rules (one alert per dialog), `node --test tests/home/home.test.js` Home's and Recent's helpers (activity per day, Today / Yesterday / Earlier, short times), and `node --test tests/status/status.test.mjs` the golden screens below (the self-check runs them too when `node.exe` is on PATH).

To run a test build next to the installed app, set `AGENT_ARCADE_HOME` to an empty folder first. That copy keeps its own settings, agents, history and paired phones there, and doesn't count as the same app. With `AGENT_ARCADE_REMOTE_LOOPBACK=1` its Phone remote listens on 127.0.0.1 only, so Windows doesn't show a firewall prompt for it; give it another `RemotePort` in its `settings.json`.

## Tested with

The status dots, needs-you alerts and phone approvals read each agent's screen (`web/remote.js`). `tests/status/fixtures` holds a synthetic screen per agent and state (approval, menu, task list, working, idle): hand-written to mirror each CLI's layout, never copied from a real session. `tests/status/status.test.mjs` checks what Agent Arcade reads from each one; CI runs it on every push and pull request. When a vendor changes its screen, update the fixture and the test shows what broke.

| Agent | CLI version | Checked | Notes |
| --- | --- | --- | --- |
| Claude Code | 2.1.301 | 16 Oct 2026 | In a Windows console without Unicode its task list marks √ done, ■ in progress, □ open |
| Codex | codex-cli 0.162.1 | 16 Oct 2026 | Pickers are numbered, so they read as choices |
| GitHub Copilot CLI | 1.0.98 | 16 Oct 2026 | Approval options as in GitHub's docs. A working line with "Esc to cancel" but no timer isn't an activity line, so the dot goes orange from the output itself |
| Cursor | 2026.09.28-64d2043 | 16 Oct 2026 | Layout best effort. Its approval (`→ Run (once) (y)`) and menus aren't read as dialogs yet: the pane turns amber through the quiet timer instead (a todo in the test) |
| Antigravity CLI | not installed here | 16 Oct 2026 | Unverified: the approval fixture uses only what Google's docs show (the option "Yes, and run in sandbox" or "… without sandbox restrictions", which the approval check matches); the trust dialog follows Google's codelab |
| Gemini CLI | 0.63.0 | 16 Oct 2026 | No fixtures yet |

The versions are what each tool's own `--version` printed on the PC where the fixtures were written; no agent session was run for them.

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
| `Mcp.cs` | Settings → MCP servers: runs each installed agent's own MCP list command (`claude mcp list`, `codex mcp list --json`, `gemini mcp list`, `copilot mcp list`, `cursor-agent mcp list`) per account in the selected project, 20 s each, and keeps only each server's name, scope and status; `web/mcp.js` draws the table |
| `Backend.cs` | Settings, agent list, accounts, sign-in checks, chat history, Windows Terminal launcher, shared instructions |
| `web/` | The interface (`index.html`, `app.css`, `app.js`, `icon.png`), `home.js` (Home and Recent: activity per day, grouping and times from the chat history), `mascot.js` (the robot in the sidebar and About: blinks, wears the agents' status on its antenna, still with reduced motion; `mascot.svg` is the same drawing, static), `mcp.js` (the MCP servers table in Settings), `remote.js` (what the phone sees: pane status, approvals, plans and screens read from the terminals) and xterm.js in `web/vendor` (MIT licence) |
| `tests/` | `remote.test.js` (the page's alert rules), `home/` (Home's and Recent's helpers), `mascot.test.mjs` (the mascot's states, easter eggs and reduced motion), `mcp.test.js` (the MCP servers table) and `status/` (golden screens: synthetic agent screens and what the status parsing must read from them); run with `node --test` |
| `docs/phone-remote/` | The Android app's build plan and `REMOTE_API.md`, the contract between the PC and the phone |
| `app.ico` | The app icon (the exe, window and taskbar) |
| `website/` | The marketing website: `pages/` (content) → `python website/build.py` (reads the newest release's version, sizes and SHA-256 values from the GitHub releases API) → `site/` (static HTML, no build tools needed to host it). `video/` renders the 30-second film (`promo.html`, `render.mjs`) and the social image |
| `release.ps1` | Builds a GitHub release's two files from the committed code, the flat self-contained `AgentArcade-win-x64.zip` and the signed `AgentArcadeRemote.apk`, and prints their SHA-256 |
| `SECURITY.md` | How to report a vulnerability (privately) |
| `docs/claims-checklist.md` | Every public claim on the website and in this README, with the code or dated source that proves it |
| `docs/media/` | The README's screenshots (from a test copy with demo agents) and `social-preview.png`, the 1280x640 image for the repository's social preview (rendered from `website/video/og.html`) |

Settings, the agent list (`agents.json`), extra accounts, chat history (`history.json`), paired phones (`remote.json`), a log and the window's WebView2 data (`webview`) live in `%LOCALAPPDATA%\AgentArcade`.

## Security

Report vulnerabilities privately, never in a public issue: see [SECURITY.md](SECURITY.md).

## License

Agent Arcade is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE.md): free for personal and other noncommercial use. Professional use needs an Agent Arcade Pro subscription; [LICENSING.md](LICENSING.md) explains which one applies. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
