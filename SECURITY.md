# Security policy

## Reporting a vulnerability

Report security problems privately through GitHub's private vulnerability reporting:
**[open a private security advisory](https://github.com/danular/AgentArcade/security/advisories/new)**.

Never report a vulnerability in a public issue, discussion or pull request, and keep the details private until a fixed release is out.

A useful report says:

- what an attacker can do, and what they need first (for example: the same Wi-Fi, a paired phone, a project the user cloned)
- the Agent Arcade version and the Windows version
- the steps to reproduce it

Leave out tokens, pairing codes, account emails and private terminal text. If you add the self-check report (`%TEMP%\AgentArcade-selfcheck.txt`), paste only its `FAIL` lines: the rest of it lists account emails and folder paths.

## Supported versions

| Version | Security fixes |
| --- | --- |
| The latest release | Yes |
| Older releases | No; update to the latest release |

Fixes ship in a new release on the [releases page](https://github.com/danular/AgentArcade/releases). To update, close Agent Arcade and extract the new zip over the old folder.

## Scope

In scope: the Windows app in this repository, including Phone remote (`Remote.cs`, `web/remote.js`), the Android app Agent Arcade Remote and its connection to the PC (`docs/phone-remote/REMOTE_API.md`), the release downloads and the website.

Report these to their makers instead: the agents themselves (Claude Code, Codex, GitHub Copilot CLI, Cursor, Gemini CLI), Tailscale, Microsoft WebView2 and .NET.

By design a paired phone is as powerful as your keyboard in the panes: it can type into agents, which run with your Windows permissions. Something a paired phone can do that you could also do by typing in a pane isn't a vulnerability; a way around pairing, the pinned certificate, the address filter or the phone's limits is.
