# Contract fixtures

Golden files of what the phone gets from the PC, byte for byte, made by Agent Arcade's own serializers (`RemoteModel` in `Remote.cs`, with the same `Wire` options the server uses) from made-up data. The Android app keeps a copy in `app/src/test/resources/contract/`, and its `ContractFixturesTest` checks each file's SHA-256 and decodes it with `ArcadeJson`. So when the PC changes what it sends, the phone's tests notice. The contract itself is [REMOTE_API.md](../REMOTE_API.md).

Don't edit these files by hand: make them again.

## Making them

```powershell
dotnet build -c Release -o $env:TEMP\aa-fixtures
$env:AGENT_ARCADE_HOME = "$env:TEMP\aa-fixtures-home"   # an empty folder: the self-check keeps away from your own data
Start-Process $env:TEMP\aa-fixtures\AgentArcade.exe -ArgumentList '--self-check','--fixtures','<repo>\docs\phone-remote\fixtures' -Wait
```

`%TEMP%\AgentArcade-selfcheck.txt` must end with `SELF_CHECK_OK`; its `remote-fixtures` line says where the files went. The self-check builds them twice and fails when the bytes differ, when the files contain this PC's name, user name or user folder, or when a 1.1 field is missing (see below). Without `--fixtures` it still builds and checks them, but writes nothing.

Then, in the Android repo: copy the `.json` files to `app/src/test/resources/contract/`, put the new SHA-256 values in the one map at the top of `ContractFixturesTest.kt` (`Get-FileHash -Algorithm SHA256`, in lowercase), and run `gradlew testDebugUnitTest`.

## The files

| File | What it is | Made by |
| --- | --- | --- |
| `hello.json` | Body of `GET /api/v1/hello`, with `minClientApi` and all five `features` | `RemoteModel.Hello` → `ReplyJson` |
| `state.json` | Body of `GET /api/v1/state`: `run`, three workspaces (one missing), three agents (one not installed), three accounts (one still being checked), two sessions (one asking, one ended with exit code 0xC000013A) | `RemoteModel.State` → `ReplyJson` |
| `history.json` | Body of `GET /api/v1/history`: three chats (open and exact, ended and named, untitled) | `RemoteModel.Chats` → `ReplyJson` |
| `session.json` | WebSocket `session` event: a working pane with an activity line, progress and preview | `RemoteModel.Summary` → `RemoteModel.Event` |
| `screen.json` | WebSocket `screen` event: eight lines cut to the last six, as for a phone that watches 6 lines (`from` goes up by 2) | `RemoteModel.ScreenMessage` |
| `attention.json` | WebSocket `attention` event: a needsYou alert | `RemoteModel.Summary` → `RemoteModel.Event` |
| `error-409-dialogChanged.json` | Error body of 409 `dialogChanged` (the page's) | `RemoteReply.FromPage` → `ReplyJson` |
| `error-409-dialogOpen.json` | Error body of 409 `dialogOpen`, nothing typed (the page's) | `RemoteReply.FromPage` → `ReplyJson` |
| `error-409-dialogOpen-typed.json` | Error body of 409 `dialogOpen` with `"typed": true`: the text was typed, Enter wasn't pressed (the page's) | `RemoteReply.FromPage` → `ReplyJson` |
| `error-409-runChanged.json` | Error body of 409 `runChanged` | `RemoteServer.RunChanged` → `ReplyJson` |
| `error-403-denied.json` | Error body of 403 `denied` | `RemoteServer.PairDenied` → `ReplyJson` |
| `error-403-notApproved.json` | Error body of 403 `notApproved` | `RemoteServer.PairNotApproved` → `ReplyJson` |

The files are compact JSON as sent, without a line break at the end. The PC's encoder escapes a few ASCII characters, such as `+` and `'` (`\u002B`, `\u0027`), and writes other text, such as `❯` or `…`, as plain UTF-8; the phone must read both.

Session, Screen, Attention and the `dialogChanged` and `dialogOpen` errors are built by the page (`web/app.js`) on the PC. For these files a made-up object in the page's field order stands in for the page's message, and then goes through the same C# steps as the page's: `Summary` adds the run as the last field, `FromPage` takes an error only with a code its status allows.

## What's made up

Everything: the PC name (`DESKTOP-ARCADE`), the version (`1.4.0`, fixed, so the files don't change with each release), the run (`9c4e1a7f03b2d865`, the contract's example; a real PC makes a random one each time its page starts), paths under `C:\Projects`, accounts at `example.com`, chat ids, times and the screen text. Nothing is read from the PC that makes them, and no real terminal text is in them.

## Contract 1.1 in them

The self-check fails unless the files have every 1.1 field the PC sends: `minClientApi` 1 and the five `features` in hello; the same `run` in State, in each of its sessions, in Session and in Attention; and a `code` in every error file that matches the file's name and status (`typed` in `error-409-dialogOpen-typed.json`).
