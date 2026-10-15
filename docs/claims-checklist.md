# Public claims checklist

Every claim the website (`website/pages`) and the README make about Agent Arcade, and what proves it. Check the rows before each release, and change a row when its claim changes. Code lines are from the 1.3.3 working tree on 15 Oct 2026; if a line has moved, search for the name in brackets. Outside sources were read on 15 Oct 2026.

The website build can't drift from the release: `website/build.py` reads the version, date, sizes and SHA-256 values from the GitHub releases API, and `.github/workflows/pages.yml` rebuilds the site when a release is published. The same workflow fails when a retired claim (last section) comes back.

## Download and install

| Claim | Where | Proof |
| --- | --- | --- |
| Version, release date, zip size and SHA-256, APK size and SHA-256 are the newest release's | download.html:8, 12, 29–30, 62, 70–71; phone.html:14; index.html:17 | website/build.py `latest_release()` (the `digest` and `size` of each asset from https://api.github.com/repos/danular/AgentArcade/releases/latest) |
| A download button next to a hash serves exactly that file | download.html:11, 33, 61 | website/build.py `download_facts()`: `releases/download/<tag>/<asset>` |
| The README link always serves the newest version | README.md:11, 46 | release.ps1:25–26 (one asset name per platform); `releases/latest/download/<name>` |
| One zip with everything inside, including the .NET runtime | download.html:10; README.md:11 | release.ps1:41 (`--self-contained true`) |
| 1.3.3 ships the .NET 10.0.13 runtime | changelog.html:22 | release.ps1:46–53 (refuses a runtime older than `latest-runtime`); https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json (10.0.13, released 13 Oct 2026) |
| `AgentArcade.exe` is at the top of the zip | download.html:23; docs.html:19; README.md:16; changelog.html:23 | release.ps1:57–61 (flat zip) |
| A release has exactly two files | changelog.html:23 | release.ps1:22–26 |
| Updating keeps settings, history and paired phones | download.html:41–46; docs.html:21–26; README.md:19 | Backend.cs:306 (`history.json` in `AppSettings.Folder`); MainForm.cs:77 (`remote.json` in `AppSettings.Folder`), i.e. `%LOCALAPPDATA%\AgentArcade`, outside the app folder |
| The hash one-liner prints `True` for a matching file | download.html:31–32; README.md:14 | `Get-FileHash` gives upper-case hex and `-eq` compares strings without case: https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_comparison_operators |
| Not code-signed; SmartScreen **More info → Run anyway** | download.html:35–36; index.html:250; README.md:17 | release.ps1 has no signing step |
| Smart App Control blocks unsigned apps, with no per-app exception | download.html:38–39; security.html:51; index.html:250; README.md:17 | https://support.microsoft.com/topic/what-is-smart-app-control-285ea03d-fa88-4d56-882e-6698afdb7003 ("no way to bypass Smart App Control protection for individual apps") |
| Uninstall: the Phone remote certificate is `CN=Agent Arcade Remote` in the user store | download.html:50–51; README.md:19 | Remote.cs:71 (`RemoteCertificate.Name`), 75 (`StoreName.My, StoreLocation.CurrentUser`), 150 (`"CN=" + name`) |
| Uninstall: Windows made firewall rules for `AgentArcade.exe` | download.html:52–53; README.md:19 | The Windows firewall prompt makes them; on the owner's PC on 15 Oct 2026 `Get-NetFirewallApplicationFilter` showed two inbound rules named `agentarcade.exe` |
| Uninstall: the WebView2 data is in `%LOCALAPPDATA%\AgentArcade` | download.html:49; README.md:103 | MainForm.cs:103 (`AppSettings.Folder`, `"webview"`) |
| APK v1.0, Android 8.0 and up | download.html:62; phone.html:14; README.md:46 | website/build.py `SITE['apk_version']` (typed; the API has no field for it); minimum SDK 26: docs/phone-remote/README.md:23 |
| APK signing certificate SHA-256 | download.html:72–73 | website/build.py `SITE['apk_cert']` (typed by the owner); check with `apksigner verify --print-certs AgentArcadeRemote.apk` |

## Agents

| Claim | Where | Proof |
| --- | --- | --- |
| Five built-in agents: Claude Code, Codex, GitHub Copilot CLI, Cursor, Gemini CLI | index.html:3, 12, 19, 32; README.md:5 | Backend.cs:137 (`AgentStore.Defaults()`) |
| Gemini CLI needs a Gemini API key or Gemini Code Assist Standard/Enterprise; personal Google sign-in stopped on 18 Jun 2026 | index.html:3, 12, 19; docs.html:34; download.html:57; README.md:5, 24 | Google, 19 May 2026: https://developers.googleblog.com/an-important-update-transitioning-gemini-cli-to-antigravity-cli/ |
| Codex, GitHub Copilot CLI and Gemini CLI need Node.js; Cursor has its own installer | download.html:57; docs.html:31; README.md:67 | Backend.cs `Install` of each default (npm for Codex, Copilot and Gemini; Backend.cs:191 for Cursor) |
| Account emails come from the tools themselves; for Gemini CLI from `google_accounts.json` | security.html:19; README.md:25 | Backend.cs:749 (`Probe.AccountAsync`), 848–851 (`Gemini`) |
| Extra accounts get their own folder | security.html:20; docs.html:50; README.md:25 | Backend.cs:146, 160, 172 (`CLAUDE_CONFIG_DIR`, `CODEX_HOME`, `GEMINI_CLI_HOME`) |
| Agents aren't sandboxed and run with your Windows permissions | security.html:46; index.html:248; legal.html:20 | Pty.cs:80 (`CreateProcessW` as the signed-in user, no restricted token) |
| From 1.3.3 a `node.bat`/`node.cmd` planted in a cloned project doesn't run in place of Node.js | security.html:48; changelog.html:20 | Program.cs:23 (whole app); MainForm.cs:960 (`SessionEnv`, every agent pane); Backend.cs:581 (Windows Terminal script); self-check Program.cs:204–221 |

## Privacy: what leaves your PC

| Claim | Where | Proof |
| --- | --- | --- |
| No telemetry, no analytics, no account with us | index.html:181, 190, 246; security.html:10, 18; legal.html:23; pricing.html:31 | The app's code has no outbound address except the vendor installers you start: `grep -n "https://" *.cs` lists Backend.cs:145 and 191 (installers) and local hosts only |
| Chat history keeps titles, agent, account, folder and times, never the chat | index.html:192, 246; security.html:21; legal.html:23; README.md:40 | Backend.cs:283–299 (`ChatRecord`) |
| A title can be the start of your first message (up to 120 characters) | index.html:192, 246; security.html:21; legal.html:23; README.md:40 | web/app.js:462–472 (`trackPrompt`); web/app.js:449 (`agentTitle`, the agent's terminal title) |
| From 1.3.3 the window makes no SmartScreen lookups; before, it did by default | index.html:194; security.html:23; legal.html:23; changelog.html:21 | MainForm.cs:124 (`IsReputationCheckingRequired = false`); default `true`: https://learn.microsoft.com/dotnet/api/microsoft.web.webview2.core.corewebview2settings.isreputationcheckingrequired (updated 12 Oct 2026) |
| From 1.3.3 WebView2 crash dumps stay on the PC; before, crash reports went to Microsoft as Windows diagnostic-data consent allowed | index.html:194; security.html:23; legal.html:23; changelog.html:21 | MainForm.cs:101 (`IsCustomCrashReportingEnabled = true`); https://learn.microsoft.com/dotnet/api/microsoft.web.webview2.core.corewebview2environmentoptions.iscustomcrashreportingenabled (default `false` respects OS consent; updated 12 Oct 2026) |
| The self-check report lists account emails and paths, so share only `FAIL` lines | docs.html:73; security.html:55; SECURITY.md | Program.cs:96 and 101 (`INFO` lines with tool paths and account text) |

## Phone remote

| Claim | Where | Proof |
| --- | --- | --- |
| Off by default; port 47821 | security.html:34; phone.html:56; README.md:48 | Backend.cs:37–38 (`RemoteEnabled`, `RemotePort = 47821`) |
| Only local-network and Tailscale addresses can connect | security.html:35; phone.html:57; README.md:60 | Remote.cs:830–843 (`IsAllowedAddress`) |
| HTTPS only, TLS 1.2 or 1.3, with the PC's own pinned certificate | security.html:36; phone.html:58; README.md:58 | Remote.cs:1497 (`SslProtocols.Tls12 | SslProtocols.Tls13`); Remote.cs:150 (self-signed); REMOTE_API.md §6 (the phone pins the fingerprint) |
| The certificate's key can't be exported | security.html:36 | Remote.cs:159–160 (`PersistKeySet | UserKeySet`, no `Exportable`) |
| Pairing codes: single use, 10 minutes, five wrong tries cancel | security.html:37; phone.html:59; README.md:59 | Remote.cs:396–400 (`Lifetime`, `MaxWrong = 5`) |
| The PC keeps only SHA-256 hashes of phone tokens | security.html:38; phone.html:60; README.md:59 | Remote.cs:171 (`TokenHash`), 217 (`Hash`) |
| A paired phone is as powerful as your keyboard in the panes and can reach files through the agents | security.html:39; phone.html:61; index.html:116, 247; README.md:61; REMOTE_API.md:382 | Remote.cs:1608–1630 (the routes: state, history, start, projects, resume, screen, input, keys, rename, stop, close); agents run as you (Pty.cs:80) |
| The phone can make an empty project folder (`POST /projects`), named on the phone and checked on the PC | security.html:39; phone.html:61; README.md:61; REMOTE_API.md:128–138 | Remote.cs:606 (`NewProject`), 1614 |
| Agent screens and your typing go only between your phone and your PC; 0 Agent Arcade servers | index.html:33, 180, 193; phone.html:3, 12, 54; security.html:22; README.md:46 | The phone talks to the PC's own server: Remote.cs:1491 (`options.Listen`); the pairing link holds only the PC's addresses: Remote.cs:557 (`Build`) |
| Over Tailscale, traffic may be relayed through Tailscale's DERP servers | index.html:181, 247; security.html:22, 40; phone.html:65; docs.html:64; legal.html:23; README.md:52 | https://tailscale.com/kb/1232/derp-servers |
| Tailscale's Personal plan is free for non-commercial use (6 users, unlimited devices) | phone.html:65; docs.html:64; README.md:52 | https://tailscale.com/pricing |
| Home Wi-Fi: set it to Private; shared Wi-Fi: keep Public and use Tailscale | docs.html:59, 71; phone.html:24; download.html:80; README.md:49 | Windows blocks the phone on a Public network: Remote.cs:528 (`PublicNetworkWarning`) |
| Nothing done on the phone moves the PC's view | index.html:115; phone.html:45 | web/app.js:582 (`options.remote`: no maximize reset, no focus, no dialogs); CLAUDE.md rule |
| Tokens, pairing codes and terminal text are never logged | security.html:41; README.md:61 | Remote.cs:1529 (errors log only what went wrong); CLAUDE.md rule; REMOTE_API.md §6 |
| Report vulnerabilities privately | security.html:58; SECURITY.md; README.md:107 | Private vulnerability reporting is on (`gh api repos/danular/AgentArcade/private-vulnerability-reporting` → `enabled: true`, 15 Oct 2026) |

## Pricing, Pro and Voice

| Claim | Where | Proof |
| --- | --- | --- |
| Free for personal use; Pro for work is EUR 25 per user per month, excl. VAT | index.html:17, 228; pricing.html:37; voice.html:88; README.md:7 | LICENSING.md:12; LICENSE.md:1–7 |
| Pro checkout isn't open yet | index.html:231, 244; pricing.html:3, 44; voice.html:11, 88; legal.html:25, 29; README.md:7 | No checkout, payment or licence-key code in this repository; the owner's decision |
| Voice is coming (not available yet); Prompt Coach is planned | index.html:157–161; voice.html:3–13, 51–66; pricing.html:41–44; security.html:25 | Voice isn't in this repository or on the releases page; the owner's decision |
| Agent Arcade never checks a licence | pricing.html:75 | No licence check in the code (`grep -ri licen *.cs web` finds only notices) |
| EUR 30.25 per month / EUR 302.50 per year incl. 21% Dutch VAT; EUR 250 per year excl. VAT | pricing.html:76–77 | 25 × 1.21 = 30.25; 250 × 1.21 = 302.50; the Dutch standard rate is 21%: https://www.government.nl/topics/vat/vat-rates-and-exemptions |
| Source-available under the PolyForm Noncommercial License 1.0.0 | legal.html:19; README.md:111 | LICENSE.md:5–7 |

## Retired claims

`.github/workflows/pages.yml` fails when one of the phrases in brackets appears in `website/site` or the README.

| Retired claim | Why | Now |
| --- | --- | --- |
| "0 relay servers" ("relay servers", "no relay") | Tailscale can relay through its DERP servers | "0 Agent Arcade servers", with the DERP note |
| "Your phone talks to your PC. Nothing else." ("Nothing else.") | Tailscale can relay; the claim was absolute | "Agent screens and your typing go only between your phone and your PC" |
| "It can't read files" / "No files, no settings, no logins" ("can't read files") | A phone can type into agents, which run with your Windows permissions | "As powerful as your keyboard in the panes" |
| "Priority email support" ("Priority email support") | There is no support email | Removed |
| The product called "open source" ("open source") | It is source-available (PolyForm Noncommercial) | "source-available"; open-source work by users is written "open-source" |
| Pro "includes Voice and Prompt Coach" | Voice isn't released and checkout isn't open | "Voice is coming", Prompt Coach "planned", Pro FAQ in the future tense |
| Gemini CLI listed as just working | Google ended personal sign-in on 18 Jun 2026 | "Gemini CLI (API key or Code Assist)", not first in lists |
| "Your Wi-Fi must be set to Private" | Unsafe advice on shared Wi-Fi | Private only on your own home Wi-Fi; otherwise Public plus Tailscale |
| Film caption "Phone to PC. No relay server." | Tailscale can relay | Caption track and `website/video/promo.html` say "No Agent Arcade server"; the MP4 still shows the old line until it is rendered again |
