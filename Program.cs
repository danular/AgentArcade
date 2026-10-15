using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentArcade;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // cmd.exe looks for a bare program name in its current folder before PATH, and npm's .cmd shims run a bare
        // "node". Agents run in project folders, which may be cloned from anyone, so a node.cmd planted there would
        // run before the agent. With this set, cmd skips the current folder; every program this app starts inherits it.
        Environment.SetEnvironmentVariable("NoDefaultCurrentDirectoryInExePath", "1");

        if (args.Any(arg => arg == "--self-check"))
            return SelfCheck.Run(args);

        // One window only: starting the app again brings the open window to the front. A copy from another folder
        // can't take over that window, so it says where Agent Arcade runs instead of quietly doing nothing.
        using var single = new Mutex(true, AppSettings.InstanceName, out var first);
        if (!first)
        {
            if (ShowExistingWindow() is { } elsewhere)
            {
                ApplicationConfiguration.Initialize();
                MessageBox.Show(
                    (elsewhere.Length > 0 ? "Agent Arcade is already running from " + elsewhere + "." : "Agent Arcade is already running.") +
                    " Close it first to start this copy.",
                    "Agent Arcade", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    // Brings the open window of this same exe to the front and returns null; also null while that copy is still
    // starting (no window yet). Otherwise returns the folder of the copy that runs from somewhere else ("" when unknown).
    static string? ShowExistingWindow()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var path = Environment.ProcessPath;
        var samePath = false;
        var elsewhere = new List<string>();
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.Id == current.Id) continue;
                    var file = process.MainModule?.FileName;
                    if (!string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Path.GetDirectoryName(file) is { Length: > 0 } folder && !elsewhere.Contains(folder, StringComparer.OrdinalIgnoreCase)) elsewhere.Add(folder);
                        continue;
                    }
                    samePath = true;
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    if (IsIconic(process.MainWindowHandle)) ShowWindow(process.MainWindowHandle, 9);
                    SetForegroundWindow(process.MainWindowHandle);
                    return null;
                }
                catch
                {
                    // A process that exits or hides its path is skipped.
                }
            }
        }
        // Copies with another data folder (AGENT_ARCADE_HOME) can run too, so more than one folder may be listed.
        return samePath ? null : string.Join(" or ", elsewhere);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);
}

static class SelfCheck
{
    // --fixtures <folder> also writes the Phone remote contract fixtures there (RemoteFixtures).
    public static int Run(string[] args)
    {
        var at = Array.IndexOf(args, "--fixtures");
        var fixtures = at < 0 ? null : at + 1 < args.Length ? args[at + 1] : "";
        var lines = new List<string>();
        var ok = true;
        void Check(string name, bool pass, string detail)
        {
            lines.Add((pass ? "OK   " : "FAIL ") + name + " — " + detail);
            if (!pass) ok = false;
        }

        Locator.RefreshPath();
        var settings = AppSettings.Load();
        Check("project", Directory.Exists(settings.ProjectPath), settings.ProjectPath);
        // Optional: without it, Open in terminal uses a console window.
        if (Locator.FindOnPath("wt") is { } wt) Check("windows-terminal", true, wt);
        else lines.Add("INFO windows-terminal — not installed, agents open in a console window");

        var agents = AgentStore.Load();
        Check("agents.json", agents.Count > 0, agents.Count + " agents in " + AgentStore.FilePath);
        var tools = Task.WhenAll(agents.Select(Probe.ToolAsync)).GetAwaiter().GetResult().ToDictionary(tool => tool.Agent);
        foreach (var tool in tools.Values)
            lines.Add("INFO " + tool.Agent.Name + " — " + (tool.Path == null ? "not installed" : tool.Version + " (" + tool.Path + ")"));
        foreach (var account in AccountStore.All(agents))
        {
            var agent = agents.First(item => item.Name == account.Agent);
            var info = Probe.AccountAsync(agent, account, tools[agent].Path).GetAwaiter().GetResult();
            lines.Add("INFO " + agent.Name + " / " + account.Name + " — " + (info.Text.Length == 0 ? "-" : info.Text));
        }

        var web = Path.Combine(AppContext.BaseDirectory, "web");
        Check("web-page", File.Exists(Path.Combine(web, "index.html")) && File.Exists(Path.Combine(web, "vendor", "xterm.js")) && File.Exists(Path.Combine(web, "icon.png")), web);

        using (var awake = new KeepAwake())
        {
            var held = awake.Set("Agent Arcade self-check") && awake.Reason == "Agent Arcade self-check";
            var changed = held && awake.Set("Agent Arcade self-check 2") && awake.Reason == "Agent Arcade self-check 2";
            var released = awake.Set(null) && awake.Reason == null;
            var (laptop, onBattery, lidSleeps) = KeepAwake.Status();
            Check("keep-awake", held && changed && released,
                "a power request is held, replaced and let go; laptop " + laptop + ", on battery " + onBattery + ", lid sleeps " + lidSleeps);
        }

        // Alerts treat a focused pane as unseen after 45 s without keyboard or mouse input, or while the PC is locked.
        Check("presence", Presence.TryIdleMs(out var idle) && idle >= 0 && idle <= Environment.TickCount64 && Presence.Away(true),
            "the time since the last keyboard or mouse input can be read (nothing about the input itself); a locked PC counts as away");

        // The update notice, offline: a fake newer tag against this version, and what an API answer may contain.
        try
        {
            var thisVersion = UpdateCheck.Parse(RemoteModel.Version) ?? new Version(0, 0, 0, 0);
            var fakeNewer = "v" + thisVersion.Major + "." + thisVersion.Minor + "." + (thisVersion.Build + 1);
            var fake = UpdateCheck.FromJson("{\"tag_name\":\"" + fakeNewer + "\",\"html_url\":\"https://github.com/danular/AgentArcade/releases/tag/" + fakeNewer + "\"}");
            var now = DateTimeOffset.Now;
            Check(
                "update-compare",
                UpdateCheck.IsNewer(fakeNewer, RemoteModel.Version) && fake?.Tag == fakeNewer && fake.Url.EndsWith("/releases/tag/" + fakeNewer, StringComparison.Ordinal)
                    && !UpdateCheck.IsNewer("v" + RemoteModel.Version, RemoteModel.Version) && !UpdateCheck.IsNewer("v1.3.2", "1.3.3")
                    && UpdateCheck.IsNewer("v1.10.0", "1.9.9") && !UpdateCheck.IsNewer("v1.3", "1.3.0") && !UpdateCheck.IsNewer("v1.4.0-beta", "1.3.3")
                    && !UpdateCheck.IsNewer("latest", "1.3.3") && UpdateCheck.FromJson("{\"tag_name\":\"<b>9.9</b>\"}") == null
                    && UpdateCheck.PageFor("https://example.com/danular/AgentArcade/releases/tag/v9.9.9") == UpdateCheck.Releases
                    && UpdateCheck.Due(null, now) && !UpdateCheck.Due(now.AddHours(-23), now) && UpdateCheck.Due(now.AddHours(-25), now) && UpdateCheck.Due(now.AddDays(2), now)
                    && UpdateCheck.Api.EndsWith("/releases/latest", StringComparison.Ordinal),
                fakeNewer + " is newer than " + RemoteModel.Version + "; the same, older, pre-release and odd tags aren't; once a day; only the API's JSON, no download");
        }
        catch (Exception ex)
        {
            Check("update-compare", false, ex.Message);
        }

        // Gemini's old "Sign in with Google" hint is replaced in an existing agents.json; a hint the user wrote stays.
        var geminiBuiltIn = AgentStore.Defaults().First(agent => agent.Command == "gemini");
        var oldHint = new AgentDef { Name = "Gemini CLI", Command = "gemini", LoginHint = AgentStore.RetiredHints["gemini"][0] };
        var ownHint = new AgentDef { Name = "Gemini CLI", Command = "gemini", LoginHint = AgentStore.RetiredHints["gemini"][0] + " (mine)" };
        Check("gemini-hint",
            AgentStore.CurrentHint(oldHint, geminiBuiltIn) == geminiBuiltIn.LoginHint && AgentStore.CurrentHint(ownHint, geminiBuiltIn) == ownHint.LoginHint
                && geminiBuiltIn.LoginHint.Contains("Antigravity CLI", StringComparison.Ordinal),
            "the old built-in hint becomes the API key / Code Assist one; an edited hint is kept");

        try
        {
            var ptyText = new System.Text.StringBuilder();
            var code = int.MinValue;
            using var done = new ManualResetEventSlim();
            using var pty = PtySession.Start(
                Path.Combine(Environment.SystemDirectory, "cmd.exe") + " /d /c echo arcade-%AA_TEST%",
                Path.GetTempPath(),
                new Dictionary<string, string?> { ["AA_TEST"] = "ok" },
                100, 30);
            pty.Output += text => { lock (ptyText) ptyText.Append(text); };
            pty.Exited += exit => { code = exit; done.Set(); };
            pty.Begin();
            pty.Resize(120, 40);
            var finished = done.Wait(15000);
            string seen;
            lock (ptyText) seen = ptyText.ToString();
            Check("pseudo-console", finished && code == 0 && seen.Contains("arcade-ok", StringComparison.Ordinal),
                finished ? "exit " + code + ", " + seen.Length + " characters of output" : "no exit within 15 s");
        }
        catch (Exception ex)
        {
            Check("pseudo-console", false, ex.Message);
        }

        var claude = agents.FirstOrDefault(agent => agent.HomeVar == "CLAUDE_CONFIG_DIR");
        if (claude != null)
        {
            var main = Terminal.Script(@"C:\Game", "claude.exe", "", AccountStore.EnvFor(claude, new AgentAccount { Agent = claude.Name, Name = "main" }));
            var extra = Terminal.Script(@"C:\Game", "claude.exe", "", AccountStore.EnvFor(claude, new AgentAccount { Agent = claude.Name, Name = "work", Home = @"C:\Accounts\claude-work" }));
            Check(
                "launch-script",
                main.Contains("set \"CLAUDE_CONFIG_DIR=\"", StringComparison.Ordinal) &&
                extra.Contains("set \"CLAUDE_CONFIG_DIR=C:\\Accounts\\claude-work\"", StringComparison.Ordinal) &&
                extra.Contains("pushd \"C:\\Game\"", StringComparison.Ordinal),
                "main clears the folder setting, extra accounts set it");
        }

        // A node.bat or node.cmd planted in a project must not run in place of Node. npm's .cmd shims run a bare "node"
        // when no node.exe sits next to them, and cmd looks in its current folder (the project) before PATH. The shim
        // starts the way StartSession starts it, and through the Windows Terminal script without this app's environment.
        var shimRoot = Path.Combine(Path.GetTempPath(), "AgentArcade-shim-" + Environment.ProcessId);
        try
        {
            var workspace = Path.Combine(shimRoot, "cloned repo");
            var npm = Path.Combine(shimRoot, "npm");
            var launch = Path.Combine(shimRoot, "launch");
            foreach (var folder in new[] { workspace, npm, launch }) Directory.CreateDirectory(folder);
            var markers = new Dictionary<string, string>();
            foreach (var planted in new[] { "node.bat", "node.cmd" })
            {
                // Each one leaves a file next to itself when it runs.
                File.WriteAllText(Path.Combine(workspace, planted), "@echo planted>\"%~dp0" + planted + ".ran\"\r\n");
                markers[planted] = Path.Combine(workspace, planted + ".ran");
            }
            // npm's cmd-shim, with --version in place of the agent's script.
            var shim = Path.Combine(npm, "fake-agent.cmd");
            File.WriteAllText(shim, string.Join("\r\n",
                "@ECHO off", "GOTO start", ":find_dp0", "SET dp0=%~dp0", "EXIT /b", ":start", "SETLOCAL", "CALL :find_dp0",
                "IF EXIST \"%dp0%\\node.exe\" (", "  SET \"_prog=%dp0%\\node.exe\"", ") ELSE (", "  SET \"_prog=node\"", "  SET PATHEXT=%PATHEXT:;.JS;=;%", ")",
                "endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & \"%_prog%\" --version") + "\r\n");
            var codex = AgentStore.Defaults().First(agent => agent.Command == "codex");
            var account = new AgentAccount { Agent = codex.Name, Name = "main" };

            // Which planted programs ran (they are deleted again for the next run), or that it didn't end in time.
            string Started(string commandLine, string directory, Dictionary<string, string?> env)
            {
                using (var done = new ManualResetEventSlim())
                using (var pty = PtySession.Start(commandLine, directory, env, 100, 30))
                {
                    pty.Exited += _ => done.Set();
                    pty.Begin();
                    if (!done.Wait(15000)) return "no exit within 15 s";
                }
                var ran = markers.Where(item => File.Exists(item.Value)).ToList();
                foreach (var item in ran) File.Delete(item.Value);
                return ran.Count == 0 ? "" : string.Join(" and ", ran.Select(item => item.Key)) + " ran";
            }

            // An agent pane: StartSession's command line, folder and environment.
            var env = AccountStore.EnvFor(codex, account);
            MainForm.SessionEnv(env);
            var pane = Started(MainForm.CommandLine(shim, "", workspace), workspace, env);

            // A Windows Terminal tab: cmd runs the launch script from its folder, without this app's environment.
            var script = Terminal.Script(workspace, shim, "", AccountStore.EnvFor(codex, account));
            File.WriteAllText(Path.Combine(launch, "launch.cmd"), script, new UTF8Encoding(false));
            var unset = new Dictionary<string, string?> { ["NoDefaultCurrentDirectoryInExePath"] = null };
            var tab = Started(Path.Combine(Environment.SystemDirectory, "cmd.exe") + " /d /c .\\launch.cmd", launch, unset);
            var setLine = script.IndexOf("set \"NoDefaultCurrentDirectoryInExePath=1\"", StringComparison.Ordinal);

            // Without the setting the planted program does run, so this check can see the hole.
            var bare = AccountStore.EnvFor(codex, account);
            bare["NoDefaultCurrentDirectoryInExePath"] = null;
            var control = Started(MainForm.CommandLine(shim, "", workspace), workspace, bare);

            var passed = pane.Length == 0 && tab.Length == 0 && control.EndsWith(" ran", StringComparison.Ordinal)
                && setLine >= 0 && setLine < script.IndexOf("pushd ", StringComparison.Ordinal);
            Check(
                "npm-shim-cwd",
                passed,
                passed
                    ? "a node.bat or node.cmd in the project doesn't run in place of Node, in a pane or a Windows Terminal tab (without the setting " + control + ")"
                    : "pane: " + (pane.Length == 0 ? "nothing planted ran" : pane) + "; Windows Terminal: " + (tab.Length == 0 ? "nothing planted ran" : tab)
                        + (setLine < 0 ? " (the script doesn't set NoDefaultCurrentDirectoryInExePath)" : "")
                        + "; without the setting: " + (control.Length == 0 ? "nothing planted ran either" : control));
        }
        catch (Exception ex)
        {
            Check("npm-shim-cwd", false, ex.Message);
        }
        finally
        {
            try { Directory.Delete(shimRoot, true); } catch { /* temp folder */ }
        }

        {
            var defaults = AgentStore.Defaults();
            var claudeDef = defaults.First(agent => agent.Command == "claude");
            var codexDef = defaults.First(agent => agent.Command == "codex");
            var chat = new ChatRecord { Id = "11111111-2222-3333-4444-555555555555", Agent = claudeDef.Name, Account = "main" };
            var started = ChatArgs.New(claudeDef, chat);
            var codexChat = new ChatRecord { Id = "x", Agent = codexDef.Name, Account = "main" };
            ChatArgs.New(codexDef, codexChat);
            Check(
                "chat-args",
                started == "--session-id " + chat.Id && chat.ChatId == chat.Id
                    && ChatArgs.Resume(claudeDef, chat) == "--resume " + chat.Id
                    && codexChat.ChatId.Length == 0 && ChatArgs.Resume(codexDef, codexChat) == "resume"
                    && defaults.Any(agent => agent.Command == "cursor-agent" && agent.Login == "login"),
                "new chats get an id where the agent allows it; resume uses it");
        }

        {
            // Made up here, never real keys: each kind is hidden wherever it sits in a line, and prose stays as it is.
            var keys = new[]
            {
                "sk-ant-api03-" + new string('a', 40), "sk-proj-" + new string('b', 40), "sk-" + new string('c', 40),
                "AIza" + new string('d', 35), "ghp_" + new string('e', 36), "gho_" + new string('f', 36), "github_pat_" + new string('g', 50),
                "xoxb-1234567890-" + new string('h', 24), "AKIA" + new string('J', 16),
                "eyJ" + new string('k', 12) + ".eyJ" + new string('m', 12) + "." + new string('n', 20),
            };
            var hidden = keys.Select(key => (Key: key, Text: TextGuard.Redact("use " + key + " now"))).ToList();
            const string prose = "Fix the foam near the bridge: ask-me about task-runner, desk-reservation-system-version-two, sk-short, AKIA and eyJ at 10:30.";
            var passed = hidden.All(item => item.Text.StartsWith("use ", StringComparison.Ordinal) && item.Text.EndsWith(" now", StringComparison.Ordinal)
                    && item.Text.Contains("[hidden]", StringComparison.Ordinal) && !item.Text.Contains(item.Key[^8..], StringComparison.Ordinal)
                    && TextGuard.Redact(item.Text) == item.Text)
                && TextGuard.Redact(prose) == prose;
            // What the phone gets: a pane summary from the page and a chat title from the history. The ids stay. The page's
            // highest first pane id is still one Remote.cs accepts.
            var key = keys[1];
            var summary = JsonSerializer.SerializeToElement(new
            {
                id = "s799999999", workspace = @"C:\Projects\x", title = "use " + key, message = (string?)null,
                preview = new[] { "line " + key }, plan = new { current = key, items = new[] { new { text = key, done = false } } },
            });
            var redacted = RemoteModel.Redacted(summary);
            var chatTitle = Json.Str(JsonSerializer.SerializeToElement(RemoteModel.Chat(new ChatRecord { Id = "c1", Agent = "a", Title = "use " + key }, null, null)), "title");
            var phone = !redacted.GetRawText().Contains(key[^8..], StringComparison.Ordinal) && Json.Str(redacted, "title") == "use sk-proj-[hidden]"
                && Json.Str(redacted, "id") == "s799999999" && Json.Str(redacted, "workspace") == @"C:\Projects\x"
                && chatTitle == "use sk-proj-[hidden]" && RemoteModel.Redacted(redacted).GetRawText() == redacted.GetRawText()
                && RemoteModel.IsSessionId("s799999999");
            Check("text-guard", passed && phone, passed && phone
                ? keys.Length + " kinds of key (sk-ant, sk-proj, sk, AIza, ghp, gho, github_pat, xoxb, AKIA, JWT) are hidden, hiding twice changes nothing, "
                    + "prose is unchanged; pane summaries and chat titles for the phone are hidden too, their ids kept"
                : string.Join(" | ", hidden.Select(item => item.Text)) + " | " + TextGuard.Redact(prose) + " | phone: " + TextGuard.Redact(redacted.GetRawText()) + " " + chatTitle);

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var masked = Mask("INFO Claude Code / main — jane.doe@example.com · max (" + profile + @"\.local\bin\claude.exe, " + profile.Replace('\\', '/') + "/x)", profile);
            var network = HideNetworks("Windows treats \"Home Wi-Fi 5G\" as a public network. Set it to Private in Settings > Wi-Fi > Home Wi-Fi 5G.", ["Home Wi-Fi 5G"]);
            Check(
                "report-masking",
                masked == @"INFO Claude Code / main — j…@example.com · max (%USERPROFILE%\.local\bin\claude.exe, %USERPROFILE%/x)"
                    && !network.Contains("Home Wi-Fi", StringComparison.Ordinal) && network.Contains("[network]", StringComparison.Ordinal)
                    && Mask("OK   project — " + profile + "Other", profile).Contains(profile + "Other", StringComparison.Ordinal),
                "this report shows emails as their first letter and domain, the user folder as %USERPROFILE% and no network name");
        }

        var scratch = Path.Combine(Path.GetTempPath(), "AgentArcade-selfcheck-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(scratch);
            File.WriteAllText(Path.Combine(scratch, "AGENTS.md"), "keep me");
            var created = SharedInstructions.Ensure(scratch);
            var again = SharedInstructions.Ensure(scratch);
            Check(
                "shared-instructions",
                created.SequenceEqual(["CLAUDE.md", "GEMINI.md"]) && again.Count == 0
                    && File.ReadAllText(Path.Combine(scratch, "AGENTS.md")) == "keep me"
                    && File.ReadAllText(Path.Combine(scratch, "CLAUDE.md")).Contains("@AGENTS.md", StringComparison.Ordinal),
                string.Join(", ", created));
        }
        catch (Exception ex)
        {
            Check("shared-instructions", false, ex.Message);
        }
        finally
        {
            try { Directory.Delete(scratch, true); } catch { /* temp folder */ }
        }

        var images = Path.Combine(Path.GetTempPath(), "AgentArcade-images-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(Path.Combine(images, "node_modules"));
            var seen = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var made = Path.Combine(images, "made.png");
            using (new ImageWatcher(images, seen.Enqueue))
            {
                File.WriteAllBytes(Path.Combine(images, "node_modules", "skip.png"), [1]);
                File.WriteAllBytes(made, [1, 2, 3]);
                var until = DateTime.Now.AddSeconds(5);
                while (seen.IsEmpty && DateTime.Now < until) Thread.Sleep(100);
                Thread.Sleep(1000);
            }
            var server = new ImageServer();
            var url = server.Url(made);
            Check(
                "images",
                seen.Count == 1 && seen.First() == made
                    && server.PathOf(url) == made && server.PathOf("https://" + ImageServer.Host + "/unknown.png") == null
                    && ImageServer.Read(made).Length == 3,
                "new images are found once, build folders are skipped, only served files can be read");

            // The Images tab: newest first (a copied file by its creation time), build and hidden folders skipped,
            // at most ProjectFiles.MaxDepth folders down, and an agent's image folder outside the project included.
            var agentImages = images + "-agent";
            try
            {
                var now = DateTime.UtcNow;
                File.SetCreationTimeUtc(made, now.AddMinutes(-10));
                File.SetLastWriteTimeUtc(made, now.AddMinutes(-10));
                Directory.CreateDirectory(Path.Combine(images, ".cache"));
                File.WriteAllBytes(Path.Combine(images, ".cache", "hidden.png"), [1]);
                var levels = Enumerable.Range(0, ProjectFiles.MaxDepth).Select(i => "d" + i).ToArray();
                var deepest = Path.Combine([images, .. levels]);
                Directory.CreateDirectory(Path.Combine(deepest, "too-deep"));
                File.WriteAllBytes(Path.Combine(deepest, "too-deep", "skip.png"), [1]);
                var newer = Path.Combine(deepest, "newer.png");
                File.WriteAllBytes(newer, [1]);
                File.SetLastWriteTimeUtc(newer, now.AddMinutes(1));
                var copied = Path.Combine(images, "copied.png");
                File.WriteAllBytes(copied, [1]);
                File.SetCreationTimeUtc(copied, now.AddMinutes(-5));
                File.SetLastWriteTimeUtc(copied, now.AddDays(-7));
                File.WriteAllText(Path.Combine(images, "notes.txt"), "not an image");
                Directory.CreateDirectory(agentImages);
                var generated = Path.Combine(agentImages, "generated.png");
                File.WriteAllBytes(generated, [1]);
                File.SetCreationTimeUtc(generated, now.AddMinutes(-2));
                File.SetLastWriteTimeUtc(generated, now.AddMinutes(-2));
                var (first, total) = ProjectFiles.Newest([images, agentImages], ImageServer.IsImage, 1);
                var (all, _) = ProjectFiles.Newest([images, agentImages], ImageServer.IsImage, 10);
                Check(
                    "image-box",
                    total == 4 && first.Select(file => file.Path).SequenceEqual([newer]) && all.Select(file => file.Path).SequenceEqual([newer, generated, copied, made])
                        && ImageWatcher.Skipped(images, Path.Combine(deepest, "too-deep", "skip.png")) && !ImageWatcher.Skipped(images, newer),
                    "the newest images come first (copied ones by when they arrived), the list is cut with the total kept, "
                        + "an agent's image folder counts, and build, hidden and too-deep folders are skipped");
            }
            catch (Exception ex)
            {
                Check("image-box", false, ex.Message);
            }
            finally
            {
                try { Directory.Delete(agentImages, true); } catch { /* temp folder */ }
            }
        }
        catch (Exception ex)
        {
            Check("images", false, ex.Message);
        }
        finally
        {
            try { Directory.Delete(images, true); } catch { /* temp folder */ }
        }

        // The side panel: only files inside the project count, and the Changes tab reads git correctly.
        var repo = Path.Combine(Path.GetTempPath(), "AgentArcade-panel-" + Environment.ProcessId);
        try
        {
            var inside = ProjectFiles.IsInside(repo, Path.Combine(repo, "a", "b.html")) && ProjectFiles.IsInside(repo, repo + Path.DirectorySeparatorChar)
                && !ProjectFiles.IsInside(repo, Path.Combine(repo, "..", "other.html")) && !ProjectFiles.IsInside(repo, repo + "X" + Path.DirectorySeparatorChar + "c.html");
            if (Locator.FindOnPath("git") is not { } git || !git.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Check("side-panel", inside, "only files inside the project open in the preview");
                lines.Add("INFO side-panel-git — git.exe isn't on PATH, so the Changes tab wasn't checked");
            }
            else
            {
                Directory.CreateDirectory(repo);
                void Git(params string[] args)
                {
                    var start = new System.Diagnostics.ProcessStartInfo(git) { WorkingDirectory = repo, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    foreach (var arg in new[] { "-c", "user.name=Self check", "-c", "user.email=self-check@localhost", "-c", "commit.gpgsign=false" }.Concat(args)) start.ArgumentList.Add(arg);
                    using var process = System.Diagnostics.Process.Start(start)!;
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0) throw new InvalidOperationException("git " + args[0] + " failed");
                }
                Git("init", "-q");
                File.WriteAllText(Path.Combine(repo, "a.txt"), "one\n");
                File.WriteAllText(Path.Combine(repo, "b.txt"), "moved\n");
                // A project in a subfolder of the repository: app.
                var app = Path.Combine(repo, "app");
                Directory.CreateDirectory(app);
                File.WriteAllText(Path.Combine(app, "page.html"), "<p>one</p>\n");
                File.WriteAllText(Path.Combine(app, "index.html"), "<p>home</p>\n");
                Git("add", "-A");
                Git("commit", "-q", "-m", "first");
                File.WriteAllText(Path.Combine(repo, "a.txt"), "two\n");
                Git("mv", "b.txt", "c.txt");
                File.WriteAllText(Path.Combine(repo, "my new file.txt"), "hello\n");
                File.WriteAllText(Path.Combine(app, "page.html"), "<p>two</p>\n");
                File.WriteAllText(Path.Combine(app, "new.html"), "<p>new</p>\n");
                var (branch, files, total) = GitChanges.Status(repo);
                var (_, appFiles, appTotal) = GitChanges.Status(app);
                var appChanged = appFiles.FirstOrDefault(file => file.Path == "page.html");
                var appNew = appFiles.FirstOrDefault(file => file.Path == "new.html");
                var appDiff = appChanged == null ? "" : GitChanges.Diff(app, appChanged.Path, null, false).Text;
                var appNewDiff = appNew == null ? "" : GitChanges.Diff(app, appNew.Path, null, true).Text;
                var served = PreviewServer.FileFor(repo, "/app/page.html") == Path.Combine(app, "page.html")
                    && PreviewServer.FileFor(repo, "/app/") == Path.Combine(app, "index.html")
                    && PreviewServer.FileFor(repo, "/app/../../outside.html") == null
                    && PreviewServer.FileFor(repo, "/app/page.html:hidden") == null
                    && PreviewServer.FileFor(repo, "/app/missing.html") == null;
                var modified = files.FirstOrDefault(file => file.Path == "a.txt");
                var renamed = files.FirstOrDefault(file => file.Path == "c.txt");
                var added = files.FirstOrDefault(file => file.Path == "my new file.txt");
                var diff = modified == null ? "" : GitChanges.Diff(repo, modified.Path, null, false).Text;
                var newFile = added == null ? "" : GitChanges.Diff(repo, added.Path, null, true).Text;
                var renameDiff = renamed == null ? "" : GitChanges.Diff(repo, renamed.Path, renamed.From, false).Text;
                Check(
                    "side-panel",
                    inside && served && branch != null && total == 5
                        && appTotal == 2 && appChanged?.Status == "modified" && appNew?.Status == "new"
                        && appDiff.Contains("+<p>two</p>") && appNewDiff.Contains("+<p>new</p>")
                        && modified?.Status == "modified" && renamed?.Status == "renamed" && renamed.From == "b.txt" && added?.Status == "new"
                        && diff.Contains("-one") && diff.Contains("+two") && newFile.Contains("+hello") && renameDiff.Contains("rename from b.txt"),
                    "only files inside the project open in the preview (no streams, nothing above it; a folder means its index.html); "
                        + "Changes lists modified, renamed and new files (spaces too) with their diffs, also for a project inside a bigger repository");
            }
        }
        catch (Exception ex)
        {
            Check("side-panel", false, ex.Message);
        }
        finally
        {
            try
            {
                // git makes its object files read-only.
                foreach (var file in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(repo, true);
            }
            catch { /* temp folder */ }
        }

        GitSafetyCheck.Run(Check, lines.Add);

        RemoteSelfCheck.Run(Check);
        RemoteFixtures.Run(Check, fixtures);

        var editor = Locator.FindUnityEditor(settings.ProjectPath);
        var running = Locator.UnityEditorIsRunning();
        lines.Add("INFO unity — " + (editor ?? "editor path unknown") + (running ? " (running)" : ""));

        var output = Path.Combine(Path.GetTempPath(), "AgentArcade-selfcheck.txt");
        lines.Add(ok ? "SELF_CHECK_OK" : "SELF_CHECK_FAIL");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var report = lines.Select(line => Mask(line, home)).ToList();
        File.WriteAllText(output, string.Join(Environment.NewLine, report));
        try
        {
            AttachConsole(0xFFFFFFFF);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            foreach (var line in report) Console.WriteLine(line);
        }
        catch
        {
            // The result file remains readable when this window has no console.
        }
        return ok ? 0 : 1;
    }

    // People share this report with bug reports, so an email keeps only its first letter and domain (a…@example.com)
    // and the user's own folder becomes %USERPROFILE%.
    static readonly Regex Email = new(@"([A-Za-z0-9])[A-Za-z0-9._%+\-]*@([A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)+)",
        RegexOptions.CultureInvariant);

    public static string Mask(string line, string profile)
    {
        profile = Path.TrimEndingDirectorySeparator(profile);
        if (profile.Length > 3)
        {
            foreach (var form in new[] { profile, profile.Replace('\\', '/') }.Distinct())
                line = Regex.Replace(line, Regex.Escape(form) + @"(?![A-Za-z0-9_\-])", "%USERPROFILE%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return Email.Replace(line, match => match.Groups[1].Value + "…@" + match.Groups[2].Value);
    }

    // A network's name (often the home Wi-Fi's) is left out of the report.
    public static string HideNetworks(string text, IEnumerable<string> names)
    {
        foreach (var name in names.Where(name => name.Length > 0).OrderByDescending(name => name.Length))
            text = text.Replace(name, "[network]", StringComparison.Ordinal);
        return text;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(uint dwProcessId);
}

// The Changes tab against hostile repositories, in a temp folder. Nothing here reaches the network: every path to
// another computer names aa-selfcheck.invalid and is refused before a git process starts.
static class GitSafetyCheck
{
    const string Server = @"\\aa-selfcheck.invalid\share";

    public static void Run(Action<string, bool, string> check, Action<string> info)
    {
        var root = Path.Combine(Path.GetTempPath(), "AgentArcade-git-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(root);
            NetworkPaths(check, root);
            var git = Locator.FindOnPath("git");
            var hasGit = git != null && git.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            Trust(check, root, hasGit);
            if (hasGit) Hostile(check, root, git!);
            else info("INFO git-hostile-config — git.exe isn't on PATH, so fsmonitor, filters and textconv weren't checked");
        }
        catch (Exception ex)
        {
            check("git-hostile-config", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try
            {
                // git makes its object files read-only.
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
            catch { /* temp folder */ }
        }
    }

    // A repository made by hand (a valid one: HEAD, objects, refs) with extra lines in its config.
    static string Repo(string root, string name, string config)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(dir, ".git", "objects"));
        Directory.CreateDirectory(Path.Combine(dir, ".git", "refs", "heads"));
        File.WriteAllText(Path.Combine(dir, ".git", "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(dir, ".git", "config"), "[core]\n\trepositoryformatversion = 0\n\tbare = false\n" + config);
        return dir;
    }

    // A value as git config writes it: quoted, with \ and " escaped.
    static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // What the Changes tab says for a project: "ran", "asked", or the refusal.
    static string Outcome(string folder, string? trusted = null)
    {
        try
        {
            GitChanges.Status(folder, trusted);
            return "ran";
        }
        catch (GitTrustNeeded)
        {
            return "asked";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    // Every way a repository can name another computer is refused before git starts; image paths from pane text too.
    static void NetworkPaths(Action<string, bool, string> check, string root)
    {
        var paths = ProjectFiles.IsNetworkPath(Server + @"\a.png") && ProjectFiles.IsNetworkPath("//aa-selfcheck.invalid/share/a.png")
            && ProjectFiles.IsNetworkPath(@"\/aa-selfcheck.invalid\share") && ProjectFiles.IsNetworkPath(@"\\?\UNC\aa-selfcheck.invalid\share")
            && ProjectFiles.IsNetworkPath(@"\??\UNC\aa-selfcheck.invalid\share") && ProjectFiles.IsNetworkPath(Path.GetFullPath(Server + @"\..\..\a.png"))
            && !ProjectFiles.IsNetworkPath(@"C:\Projects\a.png") && !ProjectFiles.IsNetworkPath(@"\\?\C:\Projects\a.png")
            && !ProjectFiles.IsNetworkPath("/Projects/a.png") && !ProjectFiles.IsNetworkPath("a.png");

        var gitFile = Path.Combine(root, "gitfile");
        Directory.CreateDirectory(gitFile);
        File.WriteAllText(Path.Combine(gitFile, ".git"), "gitdir: //aa-selfcheck.invalid/share/repo.git\n");
        var alternates = Repo(root, "alternates", "");
        Directory.CreateDirectory(Path.Combine(alternates, ".git", "objects", "info"));
        File.WriteAllText(Path.Combine(alternates, ".git", "objects", "info", "alternates"), Server + @"\objects" + "\n");
        // An included file on this PC that includes one on another computer.
        var nested = Repo(root, "nested", "[include]\n\tpath = local.gitconfig\n");
        File.WriteAllText(Path.Combine(nested, ".git", "local.gitconfig"), "[include]\n\tpath = " + Quote(Server + @"\x") + "\n");

        var before = GitChanges.Started;
        var outcomes = new Dictionary<string, string>
        {
            ["includeIf"] = Outcome(Repo(root, "includeif", "[includeIf \"gitdir:C:/\"]\n\tpath = //aa-selfcheck.invalid/share/y\n")),
            ["core.worktree"] = Outcome(Repo(root, "worktree", "[core]\n\tworktree = " + Quote(Server + @"\w") + "\n")),
            ["core.excludesFile"] = Outcome(Repo(root, "excludes", "[core]\n\texcludesFile = " + Quote(Server + @"\ignore") + "\n")),
            [".git file"] = Outcome(gitFile),
            ["alternates"] = Outcome(alternates),
            ["nested include"] = Outcome(nested),
        };
        var refused = outcomes.Where(item => !item.Value.Contains("network path", StringComparison.Ordinal)).Select(item => item.Key + ": " + item.Value).ToList();
        check(
            "git-network-config",
            paths && refused.Count == 0 && GitChanges.Started == before,
            refused.Count > 0 ? "not refused: " + string.Join("; ", refused)
                : GitChanges.Started != before ? "git started anyway"
                : !paths ? "a network path wasn't recognised"
                : "includeIf, core.worktree, core.excludesFile, a .git file, alternates and a nested include on another computer are refused before git starts; "
                    + "image paths like \\\\server\\share and //server/share are recognised");
    }

    // include.path or core.worktree: the tab asks once, per config.
    static void Trust(Action<string, bool, string> check, string root, bool hasGit)
    {
        var repo = Repo(root, "trust", "[include]\n\tpath = extra.gitconfig\n[includeIf \"gitdir:never/\"]\n\tpath = other.gitconfig\n");
        File.WriteAllText(Path.Combine(repo, ".git", "extra.gitconfig"), "[filter \"fromInclude\"]\n\tclean = nothing\n");
        var first = GitConfigCheck.Inspect(repo);
        var asked = Outcome(repo) == "asked" && Outcome(repo, new string('0', 64)) == "asked";
        var ran = !hasGit || Outcome(repo, first.Config) == "ran";
        File.AppendAllText(Path.Combine(repo, ".git", "config"), "[core]\n\tworktree = ..\n");
        var changed = GitConfigCheck.Inspect(repo);
        var again = Outcome(repo, first.Config) == "asked" && changed.Config != first.Config && changed.Uses.Count == 3;

        // The parser reads what git reads: legacy and quoted sections, escapes, comments and continued lines; it stops
        // where git stops.
        var parsed = GitConfigCheck.Parse("\uFEFF[filter.Legacy]\n\tclean = a ; comment\n[filter \"Quo\\\"ted\"] process = \"b # not a comment\"\\\n  more\n"
            + "[Core]\n\tFSMonitor\n[include]\npath = \"x\\\\y\"   \n");
        var entries = parsed?.Select(entry => entry.Section + "|" + entry.Subsection + "|" + entry.Key + "|" + (entry.Value ?? "<true>")).ToList();
        var parser = entries != null && entries.SequenceEqual([
                "filter|legacy|clean|a", "filter|Quo\"ted|process|b # not a comment  more", "core||fsmonitor|<true>", "include||path|x\\y"])
            && GitConfigCheck.Parse("[core]\n\tbad = \\q\n") == null && GitConfigCheck.Parse("[core\n") == null
            && GitConfigCheck.Parse("[core]\n\tkey # comment\n") == null && GitConfigCheck.Parse("[core]\n\tx = \"open\n") == null;
        check(
            "git-trust",
            first.NeedsTrust && first.Uses.Count == 2 && first.Filters.SequenceEqual(["fromInclude"]) && asked && ran && again && parser,
            !parser ? "the config parser read: " + (entries == null ? "nothing" : string.Join(" / ", entries))
                : "include.path and includeIf ask before git runs (filters in included files are switched off too); the stored answer lets git run "
                    + (hasGit ? "" : "(not run: no git) ") + "until the config changes; the config parser reads what git reads");
    }

    // Repository A names a program for fsmonitor, filters and textconv, and .gitattributes applies them to every file.
    static void Hostile(Action<string, bool, string> check, string root, string git)
    {
        var repo = Path.Combine(root, "hostile");
        Directory.CreateDirectory(repo);
        Git(git, repo, true, "init", "-q");
        File.WriteAllText(Path.Combine(repo, "tracked.txt"), "one\n");
        File.WriteAllText(Path.Combine(repo, "second.txt"), "one\n");
        Git(git, repo, true, "add", "-A");
        Git(git, repo, true, "-c", "user.name=Self check", "-c", "user.email=self-check@localhost", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "first");

        // Each program leaves a file when it runs. git runs them through its own sh, which reads paths with /.
        var markers = new[] { "fsmonitor", "clean", "smudge", "process", "clean2", "textconv" }.ToDictionary(name => name, name => Path.Combine(root, name + ".ran"));
        string Planted(string name) => Quote("echo x > '" + markers[name].Replace('\\', '/') + "'");
        File.AppendAllText(Path.Combine(repo, ".git", "config"),
            "[core]\n\tfsmonitor = " + Planted("fsmonitor") + "\n"
            + "[filter \"evil\"]\n\tclean = " + Planted("clean") + "\n\tsmudge = " + Planted("smudge") + "\n\tprocess = " + Planted("process") + "\n"
            + "[filter \"evil2\"]\n\tclean = " + Planted("clean2") + "\n"
            + "[diff \"evil\"]\n\ttextconv = " + Planted("textconv") + "\n");
        File.WriteAllText(Path.Combine(repo, ".gitattributes"), "* filter=evil diff=evil\nsecond.txt filter=evil2\n");
        File.WriteAllText(Path.Combine(repo, "tracked.txt"), "two\n");
        File.WriteAllText(Path.Combine(repo, "second.txt"), "two\n");
        List<string> Ran()
        {
            var ran = markers.Where(item => File.Exists(item.Value)).Select(item => item.Key).ToList();
            foreach (var name in ran) File.Delete(markers[name]);
            return ran;
        }

        var (_, files, _) = GitChanges.Status(repo);
        var modified = files.Where(file => file.Status == "modified").Select(file => file.Path).Order().ToList();
        var diffs = modified.Select(path => GitChanges.Diff(repo, path, null, false).Text).ToList();
        var ranProtected = Ran();
        var listed = modified.SequenceEqual(["second.txt", "tracked.txt"]) && files.Any(file => file.Path == ".gitattributes")
            && diffs.All(diff => diff.Contains("-one") && diff.Contains("+two"));

        // A network include in the same repository is refused before any git process starts.
        File.AppendAllText(Path.Combine(repo, ".git", "config"), "[include]\n\tpath = " + Quote(Server + @"\x") + "\n");
        var before = GitChanges.Started;
        var unc = Outcome(repo);
        var uncRefused = unc.Contains("network path", StringComparison.Ordinal) && GitChanges.Started == before;

        // Without the protection (plain git, the include taken out again) git runs them, so this check can see the hole.
        var config = File.ReadAllText(Path.Combine(repo, ".git", "config"));
        File.WriteAllText(Path.Combine(repo, ".git", "config"), config[..config.LastIndexOf("[include]", StringComparison.Ordinal)]);
        Git(git, repo, false, "status");
        Git(git, repo, false, "diff", "HEAD");
        // second.txt's own filter works, so its diff gets as far as textconv.
        Git(git, repo, false, "diff", "HEAD", "--", "second.txt");
        var ranControl = Ran();

        check(
            "git-hostile-config",
            listed && ranProtected.Count == 0 && uncRefused && ranControl.Count > 0,
            (ranProtected.Count > 0 ? "RAN: " + string.Join(", ", ranProtected) + "; "
                : listed ? "status and diff ran no fsmonitor, filter or textconv program; " : "status or diff went wrong; ")
                + (uncRefused ? "a network include.path is refused before git starts; " : "a network include.path wasn't refused (" + unc + "); ")
                + "plain git ran: " + (ranControl.Count == 0 ? "none of them either" : string.Join(", ", ranControl)));
    }

    static void Git(string git, string folder, bool mustWork, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo(git) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(true); } catch { /* ended */ }
            throw new InvalidOperationException("git " + args[0] + " didn't end within 30 s");
        }
        Task.WaitAll(output, error);
        if (mustWork && process.ExitCode != 0) throw new InvalidOperationException("git " + args[0] + " failed");
    }
}

// Phone remote checks. They use a temp folder and their own throw-away certificate, never remote.json or the
// real "Agent Arcade Remote" certificate, and the test server listens on 127.0.0.1 only, so Windows Firewall stays quiet.
static class RemoteSelfCheck
{
    const string CertPrefix = "Agent Arcade Remote self-check ";
    // Each run has its own certificate, so two self-checks at the same time don't delete each other's.
    static readonly string CertName = CertPrefix + Environment.ProcessId;
    const string NotPaired = "This phone isn't paired any more. Pair it again on your PC.";

    public static void Run(Action<string, bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "AgentArcade-remote-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(folder);
            DeleteLeftovers();
            Pieces(check, folder);
            ServerAsync(check, folder).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            check("remote", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { RemoteCertificate.Delete(CertName); } catch { /* checked below on the next run */ }
            try { Directory.Delete(folder, true); } catch { /* temp folder */ }
        }
    }

    static void Pieces(Action<string, bool, string> check, string folder)
    {
        var file = Path.Combine(folder, "tokens.json");
        var devices = new RemoteDevices(file);
        var (device, token) = devices.Add("Test phone");
        var wrong = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var saved = File.ReadAllText(file);
        check(
            "remote-token",
            RemoteDevices.IsToken(token) && devices.Find(token)?.Id == device.Id && devices.Find(wrong) == null
                && devices.Find(token[..42]) == null && devices.Find(token + "A") == null
                && !saved.Contains(token, StringComparison.Ordinal) && saved.Contains(RemoteDevices.Hash(token), StringComparison.Ordinal)
                && new RemoteDevices(file).Find(token)?.Id == device.Id,
            "only the token's SHA-256 is saved; the right token matches, a wrong one or a wrong length doesn't");

        var reopened = new RemoteDevices(file);
        Thread.Sleep(20);
        reopened.Seen(device.Id);
        check(
            "remote-last-seen",
            new RemoteDevices(file).All().Single().LastSeen > device.LastSeen,
            "the first request after a start saves lastSeen; later ones at most once a minute");

        bool goneAtOnce;
        string? whileLocked;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            goneAtOnce = reopened.Remove(device.Id) != null && !reopened.Exists(device.Id);
            whileLocked = reopened.SaveError;
        }
        reopened.Flush();
        check(
            "remote-remove-save",
            goneAtOnce && whileLocked != null && reopened.SaveError == null && new RemoteDevices(file).All().Count == 0,
            "a removal takes effect at once even when remote.json is locked, and is saved once it can be");

        var now = DateTimeOffset.Now;
        var codes = new PairingCodes(() => now);
        var code = codes.New();
        var once = codes.Check(code, "first") == PairResult.Ok && codes.Check(code, "second") == PairResult.Used
            && codes.Info() is { Status: "used", PairedName: "first", Code: null };
        code = codes.New();
        now += PairingCodes.Lifetime;
        var expired = codes.Check(code, "late") == PairResult.Expired && codes.Info() is { Status: "expired", Code: null };
        code = codes.New();
        var tries = Enumerable.Range(0, 5).Select(_ => codes.Check(new string('A', 22), "guess")).ToList();
        var cancelled = tries.Take(4).All(result => result == PairResult.Wrong) && tries[4] == PairResult.Cancelled
            && codes.Check(code, "right") == PairResult.Cancelled && codes.Info().Status == "cancelled";
        var attempts = Enumerable.Range(0, 11).Select(_ => codes.Attempt()).ToList();
        now += TimeSpan.FromMinutes(1);
        var limited = attempts.Take(10).All(allowed => allowed) && !attempts[10] && codes.Attempt();
        check(
            "remote-pairing",
            code.Length == 22 && once && expired && cancelled && limited,
            "a code works once, expires after 10 minutes, five wrong codes cancel it, 10 attempts a minute");

        var link = RemoteLink.Build("PC Name&x", ["192.168.1.20:47821", "100.101.102.103:47821"], code, new string('a', 64));
        check(
            "remote-link",
            Regex.IsMatch(link, @"^agentarcade://pair\?v=1&name=PC%20Name%26x&hosts=192\.168\.1\.20:47821,100\.101\.102\.103:47821&code=[A-Za-z0-9_-]{22}&fp=[0-9a-f]{64}\z")
                && RemoteLink.Qr(link).StartsWith("data:image/png;base64,iVBORw0KGgo", StringComparison.Ordinal),
            "v=1, encoded PC name, ip:port hosts, 22-character code, 64-hex fingerprint; the QR code is a PNG");

        var names = new[] { "", "a/b", "tab\tthere", "dot.", "CON", "com1.txt", "LPT³", new string('x', 81) }.Select(NewProject.Problem).ToList();
        var projects = Path.Combine(folder, "projects");
        Directory.CreateDirectory(Path.Combine(projects, "First"));
        var (made, madeReply) = NewProject.Create(projects, " RiverRun ");
        var (_, twice) = NewProject.Create(projects, "riverrun");
        var (_, none) = NewProject.Create(null, "RiverRun");
        var (_, sneaky) = NewProject.Create(projects, "..");
        check(
            "new-project",
            names.All(problem => problem != null) && NewProject.Problem("River Run 2") == null && NewProject.Problem("CONSOLE") == null
                && NewProject.Folder(Path.Combine(projects, "First"), []) == projects
                && NewProject.Folder(@"Q:\", [Path.Combine(projects, "First")]) == projects
                && madeReply.Status == 201 && made == Path.Combine(projects, "RiverRun") && Directory.Exists(made)
                && twice.Status == 409 && twice.Error == "There's already a folder called riverrun in " + projects + "."
                && none.Status == 404 && sneaky.Status == 400,
            "names follow the contract's rules; the folder holds the workspace; 201 makes an empty folder, 409 when it is there, 404 without a folder");

        // The page's New project and the phone's share CreateAndList.
        var listed = new List<string> { Path.Combine(projects, "First"), Path.Combine(projects, "Gone") };
        var (second, _) = NewProject.CreateAndList(projects, "Second", listed);
        var (gone, goneReply) = NewProject.CreateAndList(projects, "gone", listed);
        var (refused, refusedReply) = NewProject.CreateAndList(projects, "Second", listed);
        check(
            "new-project-list",
            second == Path.Combine(projects, "Second") && gone == Path.Combine(projects, "Gone") && Directory.Exists(gone)
                && goneReply.Status == 201 && goneReply.Body?.GetProperty("workspace").GetString() == gone
                && refused == null && refusedReply.Status == 409
                && listed.SequenceEqual([Path.Combine(projects, "First"), Path.Combine(projects, "Gone"), Path.Combine(projects, "Second")])
                && NewProject.Refusal(projects, "Second")?.Status == 409 && NewProject.Refusal(projects, "Third") == null
                && NewProject.Refusal(null, "Third")?.Status == 404 && NewProject.Refusal(projects, "a?b")?.Status == 400,
            "a new folder is listed at the end; a listed folder that was missing on disk is made but not listed twice; the check while typing gives the reason");

        var agents = AgentStore.Defaults();
        var claude = agents.First(agent => agent.Command == "claude");
        using var summary = JsonDocument.Parse("{\"id\":\"s1\"}");
        // Probe results are found by agent name, so the ones from before a refresh still count.
        var probed = new Dictionary<string, ToolStatus> { [claude.Name] = new(AgentStore.Defaults().First(agent => agent.Command == "claude"), @"C:\Tools\claude.exe", "2.1.0") };
        var state = JsonSerializer.SerializeToElement(
            RemoteModel.State(folder, [folder], agents, probed, [new AgentAccount { Agent = claude.Name, Name = "main" }], null, [summary.RootElement], ["192.168.1.20:47821"], RemoteModel.Power("agents", true)),
            RemoteModel.Wire);
        var record = new ChatRecord { Id = "c1", Agent = claude.Name, Account = "main", Workspace = folder, ChatId = "c1", Started = DateTimeOffset.Now, LastActive = DateTimeOffset.Now };
        var chat = JsonSerializer.SerializeToElement(RemoteModel.Chats([record], agents, _ => "s1")[0], RemoteModel.Wire);
        check(
            "remote-json",
            Names(state, "api pc version workspace projectsFolder power workspaces agents accounts sessions hosts")
                && Names(state.GetProperty("power"), "keepAwake awake onBattery lidSleeps")
                && Json.Str(state, "projectsFolder") == Path.GetDirectoryName(folder)
                && state.GetProperty("hosts")[0].GetString() == "192.168.1.20:47821"
                && state.GetProperty("agents")[0].GetProperty("installed").GetBoolean() && Json.Str(state.GetProperty("agents")[0], "version") == "2.1.0"
                && Json.Str(chat, "title") == "New chat" && !chat.GetProperty("named").GetBoolean()
                && Names(state.GetProperty("workspaces")[0], "path name exists unity")
                && Names(state.GetProperty("agents")[0], "name command installed version color")
                && Names(state.GetProperty("accounts")[0], "key agent name main signedIn email plan")
                && Names(chat, "id agent account accountName workspace title named startedAt endedAt open session resume")
                && Names(JsonSerializer.SerializeToElement(RemoteModel.Hello()), "app api version pc")
                && Names(JsonSerializer.SerializeToElement(RemoteModel.PairAnswer("t", "d_000000")), "token deviceId pc")
                && state.GetProperty("agents")[0].GetProperty("color").GetString() == "#d97757"
                && state.GetProperty("accounts")[0].GetProperty("key").GetString() == "Claude Code|main"
                && state.GetProperty("accounts")[0].GetProperty("signedIn").ValueKind == JsonValueKind.Null
                && state.GetProperty("sessions")[0].GetProperty("id").GetString() == "s1"
                && chat.GetProperty("resume").GetString() == "exact" && chat.GetProperty("open").GetBoolean() && chat.GetProperty("session").GetString() == "s1"
                && Regex.IsMatch(RemoteModel.Version, @"^\d+\.\d+\.\d+\z"),
            "State, Chat, Hello and the pair answer have exactly the names in REMOTE_API.md; an untitled chat is \"New chat\"; version " + RemoteModel.Version);

        // Only that nothing throws: whether this PC is on a public network right now is up to Windows. The network's name
        // stays out of the report.
        var warning = RemoteLink.PublicNetworkWarning(RemoteLink.Hosts(47821, false));
        check("remote-network", true, warning == null
            ? "no address of the pairing link is on a public network"
            : Regex.Replace(SelfCheck.HideNetworks(warning, PublicNetworkNames()), "\"[^\"]*\"", "\"[network]\""));

        using var screen = JsonDocument.Parse("{\"id\":\"s1\",\"seq\":3,\"total\":100,\"from\":90,\"cursor\":{\"x\":1,\"y\":99},\"lines\":[[],[],[],[],[],[],[],[],[],[{\"t\":\"last\"}]]}");
        using var cut = JsonDocument.Parse(RemoteModel.ScreenMessage(screen.RootElement, 4));
        using var whole = JsonDocument.Parse(RemoteModel.ScreenMessage(screen.RootElement, 200));
        var cutScreen = cut.RootElement.GetProperty("screen");
        check(
            "remote-rules",
            RemoteModel.IsSessionId("s12") && !RemoteModel.IsSessionId("s12\n") && !RemoteModel.IsSessionId("x1") && !RemoteModel.IsSessionId("s1234567890")
                && RemoteModel.IsChatId("6f0c2b8e-5d1e-4f7a-9a43-2b1f7c9d0e11") && !RemoteModel.IsChatId("../x")
                && RemoteModel.IsKey("shiftTab") && RemoteModel.IsKey("7") && RemoteModel.IsKey("?") && !RemoteModel.IsKey("A") && !RemoteModel.IsKey("f1")
                && RemoteModel.DeviceName("  Pixel‮ 9\n\tPro\u0007 ") == "Pixel 9 Pro" && RemoteModel.DeviceName("​") == "Phone"
                && RemoteModel.DeviceName(new string('x', 80)).Length == 60
                && RemoteModel.Lines("99999") == 2000 && RemoteModel.Lines("0") == 1 && RemoteModel.Lines("x") == 200
                && Allowed("127.0.0.1") && Allowed("10.1.2.3") && Allowed("172.20.1.1") && Allowed("192.168.1.5") && Allowed("169.254.3.4")
                && Allowed("100.101.102.103") && Allowed("::ffff:192.168.1.5")
                && !Allowed("8.8.8.8") && !Allowed("172.32.0.1") && !Allowed("100.128.0.1") && !Allowed("2001:db8::1")
                && cut.RootElement.GetProperty("type").GetString() == "screen" && cutScreen.GetProperty("from").GetInt32() == 96
                && cutScreen.GetProperty("lines").GetArrayLength() == 4 && cutScreen.GetProperty("cursor").GetProperty("y").GetInt32() == 99
                && cutScreen.GetProperty("lines")[3][0].GetProperty("t").GetString() == "last"
                && whole.RootElement.GetProperty("screen").GetProperty("from").GetInt32() == 90,
            "ids, keys, phone names, line counts, the local-network address filter and cutting a screen to the lines a phone wants");
    }

    static List<string> PublicNetworkNames()
    {
        try
        {
            return NetworkProfiles.PublicNetworks().Values.ToList();
        }
        catch
        {
            return [];
        }
    }

    static async Task ServerAsync(Action<string, bool, string> check, string folder)
    {
        var host = new StubHost();
        var server = new RemoteServer(host, Path.Combine(folder, "remote.json"), CertName);
        var port = FreePort();
        await StartWithStrayEnvironmentAsync(server, port);
        var urls = server.Urls();
        check("remote-server", server.Running && urls.SequenceEqual(["https://127.0.0.1:" + port]),
            server.Running ? "listens only on " + string.Join(", ", urls) + ", whatever ASPNETCORE_URLS or Kestrel variables say" : server.Error ?? "didn't start");
        if (!server.Running) return;
        var taken = new TcpListener(IPAddress.Loopback, FreePort());
        taken.Start();
        var busy = new RemoteServer(host, Path.Combine(folder, "busy.json"), CertName);
        await busy.ApplyAsync(true, ((IPEndPoint)taken.LocalEndpoint).Port, true);
        taken.Stop();
        check("remote-port-in-use", !busy.Running && busy.Error?.Contains("already in use", StringComparison.Ordinal) == true, busy.Error ?? "started anyway");
        string? keyName = null;
        CngProvider? keyProvider = null;
        try
        {
            var fingerprint = server.Fingerprint!;
            using (var stored = RemoteCertificate.LoadOrCreate(CertName))
            using (var key = stored.GetECDsaPrivateKey())
            {
                using var saved = (key as ECDsaCng)?.Key;
                (keyName, keyProvider) = (saved?.KeyName, saved?.Provider);
                check(
                    "remote-cert",
                    RemoteCertificate.Fingerprint(stored) == fingerprint && Regex.IsMatch(fingerprint, "^[0-9a-f]{64}\\z")
                        && Regex.IsMatch(RemoteCertificate.Short(fingerprint), "^([0-9A-F]{2}:){7}[0-9A-F]{2}\\z")
                        && key?.KeySize == 256 && saved != null && (saved.ExportPolicy & CngExportPolicies.AllowPlaintextExport) == 0
                        && stored.NotAfter > DateTime.Now.AddYears(19) && Count(CertName) == 1,
                    "made once, found again in the user's store with its saved key, which can't be exported; " + RemoteCertificate.Short(fingerprint));
            }

            var root = "https://127.0.0.1:" + port + "/api/v1/";
            check(
                "remote-connection-cap",
                await ConnectionCapAsync(server, port, fingerprint, root),
                "one address can have 8 connections open; the ninth is cut before TLS");

            // Every check below comes from 127.0.0.1, so it keeps its connections under that cap.
            using var http = Pinned(fingerprint, 2);
            using var hello = await http.GetAsync(root + "hello");
            var helloBody = await Body(hello);
            var wrongPin = false;
            try
            {
                using var other = Pinned(new string('0', 64), 1);
                using var refused = await other.GetAsync(root + "hello");
            }
            catch (HttpRequestException)
            {
                wrongPin = true;
            }
            check(
                "remote-https",
                hello.StatusCode == HttpStatusCode.OK && Json.Str(helloBody, "app") == "Agent Arcade" && wrongPin
                    && hello.Headers.CacheControl?.NoStore == true && !hello.Headers.Contains("Server")
                    && hello.Headers.TryGetValues("X-Content-Type-Options", out var sniff) && sniff.Contains("nosniff")
                    && hello.Content.Headers.ContentType?.MediaType == "application/json",
                "hello answers over TLS to a client that pins the fingerprint (AllowedHosts in the environment changes nothing); a wrong pin is refused");

            var code = server.NewCode();
            using var pair = await Send(http, HttpMethod.Post, root + "pair", null, new { code, device = "Self‮ check\nphone" });
            var pairBody = await Body(pair);
            var token = Json.Str(pairBody, "token");
            var deviceId = Json.Str(pairBody, "deviceId");
            using var reuse = await Send(http, HttpMethod.Post, root + "pair", null, new { code, device = "again" });
            var reuseBody = await Body(reuse);
            using var state = await Send(http, HttpMethod.Get, root + "state", token);
            var stateBody = await Body(state);
            using var wrongToken = await Send(http, HttpMethod.Get, root + "state", Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)));
            var wrongBody = await Body(wrongToken);
            using var noToken = await Send(http, HttpMethod.Get, root + "state", null);
            check(
                "remote-pair",
                pair.StatusCode == HttpStatusCode.OK && RemoteDevices.IsToken(token) && server.Devices.All().Single().Name == "Self check phone"
                    && reuse.StatusCode == HttpStatusCode.Forbidden && Json.Str(reuseBody, "error").Contains("already used", StringComparison.Ordinal)
                    && state.StatusCode == HttpStatusCode.OK && stateBody.TryGetProperty("sessions", out _) && stateBody.TryGetProperty("hosts", out _)
                    && wrongToken.StatusCode == HttpStatusCode.Unauthorized && Json.Str(wrongBody, "error") == NotPaired
                    && noToken.StatusCode == HttpStatusCode.Unauthorized
                    && !host.Logged.Any(line => line.Contains(token, StringComparison.Ordinal) || line.Contains(code, StringComparison.Ordinal)),
                "pairing with the code gives a token that works; the code works once; a wrong token gets 401; no token or code in the log");

            using var input = await Send(http, HttpMethod.Post, root + "sessions/s1/input", token, new { text = "hi", submit = true });
            var inputBody = await Body(input);
            using var notJson = await Send(http, HttpMethod.Post, root + "sessions/s1/input", token, raw: "not json");
            using var longText = await Send(http, HttpMethod.Post, root + "sessions/s1/input", token, new { text = new string('x', 16 * 1024 + 1) });
            using var huge = await Send(http, HttpMethod.Post, root + "sessions/s1/input", token, raw: "\"" + new string('x', 70 * 1024) + "\"", expectContinue: true);
            using var badKey = await Send(http, HttpMethod.Post, root + "sessions/s1/keys", token, new { keys = new[] { "f13" } });
            var badKeyBody = await Body(badKey);
            using var badId = await Send(http, HttpMethod.Post, root + "sessions/x1/stop", token);
            using var unknown = await Send(http, HttpMethod.Get, root + "nothing", token);
            var unknownBody = await Body(unknown);
            using var started = await Send(http, HttpMethod.Post, root + "sessions", token, new { account = "Claude Code|main", workspace = folder });
            var startedBody = await Body(started);
            using var badProject = await Send(http, HttpMethod.Post, root + "projects", token, new { name = " nul.txt " });
            var badProjectBody = await Body(badProject);
            using var project = await Send(http, HttpMethod.Post, root + "projects", token, new { name = " RiverRun " });
            var projectBody = await Body(project);
            check(
                "remote-requests",
                input.StatusCode == HttpStatusCode.OK && inputBody.TryGetProperty("ok", out var ok) && ok.GetBoolean() && host.Asked.Contains(("remoteInput", deviceId))
                    && notJson.StatusCode == HttpStatusCode.BadRequest
                    && longText.StatusCode == HttpStatusCode.RequestEntityTooLarge && huge.StatusCode == HttpStatusCode.RequestEntityTooLarge
                    && badKey.StatusCode == HttpStatusCode.BadRequest && Json.Str(badKeyBody, "error") == "Unknown key: f13"
                    && badId.StatusCode == HttpStatusCode.NotFound
                    && unknown.StatusCode == HttpStatusCode.NotFound && Json.Str(unknownBody, "error") == "Unknown request."
                    && started.StatusCode == HttpStatusCode.Created && Json.Str(startedBody, "id") == "s9" && host.Asked.Contains(("remoteStart", deviceId))
                    && badProject.StatusCode == HttpStatusCode.BadRequest && Json.Str(badProjectBody, "error") == "Windows keeps NUL for itself. Choose another name."
                    && project.StatusCode == HttpStatusCode.Created && Json.Str(projectBody, "workspace") == "RiverRun" && host.Projects.SequenceEqual(["RiverRun"]),
                "input reaches the page with the phone's device id; bad JSON 400, too long 413, unknown key 400, bad pane id 404, unknown route 404, start 201, "
                    + "a new project's name is trimmed and checked before it reaches the window");

            await WaitingForPageAsync(check, server, host, http, root, fingerprint);

            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await SettleAsync(server, 2);
            using (var live = await Connect(port, fingerprint, token, cancel.Token))
            using (var newer = new ClientWebSocket())
            {
                var first = await Receive(live, cancel.Token);
                var second = await Receive(live, cancel.Token);
                var extensions = live.HttpResponseHeaders?.TryGetValue("Sec-WebSocket-Extensions", out var values) == true ? string.Join(",", values) : "";
                await live.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"watch\",\"id\":\"s1\",\"lines\":4}"), WebSocketMessageType.Text, true, cancel.Token);
                for (var i = 0; i < 100 && !host.Refreshed.Contains("s1"); i++) await Task.Delay(20);
                server.Screen(ScreenFor("s1"));
                var screen = await Receive(live, cancel.Token);
                check(
                    "remote-live",
                    Json.Str(first, "type") == "hello" && first.GetProperty("state").GetProperty("api").GetInt32() == 1
                        && Json.Str(second, "type") == "history" && second.GetProperty("chats").ValueKind == JsonValueKind.Array
                        && extensions.Contains("permessage-deflate", StringComparison.Ordinal) && extensions.Contains("server_no_context_takeover", StringComparison.Ordinal)
                        && host.Refreshed.Contains("s1") && Json.Str(screen, "type") == "screen"
                        && screen.GetProperty("screen").GetProperty("lines").GetArrayLength() == 4 && screen.GetProperty("screen").GetProperty("from").GetInt32() == 96,
                    "the WebSocket opens with the token, compressed per message; hello, then history; a watched pane's screen arrives cut to its lines");

                await Connect(newer, port, fingerprint, token, cancel.Token);
                await Receive(newer, cancel.Token);
                await Receive(newer, cancel.Token);
                var replaced = await ReceiveWithinAsync(live, 5);
                check(
                    "remote-replace",
                    replaced.TryGetProperty("close", out var replacedCode) && replacedCode.GetInt32() == 1000
                        && Json.Str(replaced, "reason") == "Replaced by a newer connection." && server.Phones == 1,
                    "a phone that connects again replaces its older connection (1000), so it still counts as one phone");

                await newer.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"watch\",\"id\":\"s1\",\"pad\":\"" + new string('x', 5000) + "\"}"), WebSocketMessageType.Text, true, cancel.Token);
                var tooBig = await Receive(newer, cancel.Token);
                for (var i = 0; i < 100 && server.Phones > 0; i++) await Task.Delay(20);
                check("remote-message-size", tooBig.TryGetProperty("close", out var bigCode) && bigCode.GetInt32() == 1009 && server.Phones == 0,
                    "a message from a phone over 4 KB closes its connection with 1009");
            }

            var (tokenC, _) = await PairAsync(server, http, root, "Phone C");
            var (tokenD, _) = await PairAsync(server, http, root, "Phone D");
            var (tokenE, _) = await PairAsync(server, http, root, "Phone E");
            var (tokenF, _) = await PairAsync(server, http, root, "Phone F");
            await SettleAsync(server, 2);
            var phones = new List<ClientWebSocket>();
            try
            {
                foreach (var phoneToken in new[] { token, tokenC, tokenD, tokenE })
                {
                    var phone = new ClientWebSocket();
                    phones.Add(phone);
                    await Connect(phone, port, fingerprint, phoneToken, cancel.Token);
                    await Receive(phone, cancel.Token);
                    await Receive(phone, cancel.Token);
                }
                using var fifth = await Connect(port, fingerprint, tokenF, cancel.Token);
                var tooMany = await Receive(fifth, cancel.Token);

                using var unpair = await Send(http, HttpMethod.Post, root + "unpair", token);
                var removed = await Receive(phones[0], cancel.Token);
                using var afterUnpair = await Send(http, HttpMethod.Get, root + "state", token);

                await SettleAsync(server, 5);
                using var burst = Pinned(fingerprint, 2);
                var statuses = await Task.WhenAll(Enumerable.Range(0, 40).Select(async _ =>
                {
                    using var response = await Send(burst, HttpMethod.Get, root + "state", tokenC);
                    return response.StatusCode;
                }));

                // With forwarded headers on, these would count against 10.9.9.9 instead of this address.
                for (var i = 0; i < 21; i++)
                {
                    using var guess = await Send(http, HttpMethod.Get, root + "state", Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), forwardedFor: "10.9.9.9");
                }
                using var blocked = await Send(http, HttpMethod.Get, root + "state", tokenD);
                var blockedBody = await Body(blocked);

                var closing = phones.Skip(1).Select(phone => Receive(phone, cancel.Token)).ToList();
                await server.ApplyAsync(false, port, true);
                var closed = await Task.WhenAll(closing);
                check(
                    "remote-close",
                    tooMany.TryGetProperty("close", out var tooManyCode) && tooManyCode.GetInt32() == 1013
                        && unpair.StatusCode == HttpStatusCode.OK && removed.TryGetProperty("close", out var removedCode) && removedCode.GetInt32() == 4401
                        && afterUnpair.StatusCode == HttpStatusCode.Unauthorized
                        && statuses.Contains(HttpStatusCode.OK) && statuses.Contains(HttpStatusCode.TooManyRequests)
                        && closed.All(message => message.TryGetProperty("close", out var offCode) && offCode.GetInt32() == 1001)
                        && !server.Running && server.Phones == 0 && host.Cancelled.Contains(null),
                    "a fifth phone gets 1013, an unpaired phone 4401 and 401, 20 requests a second, turning off sends 1001 and tells the page to drop all queued typing");
                check(
                    "remote-lockout",
                    blocked.StatusCode == HttpStatusCode.TooManyRequests && Json.Str(blockedBody, "error").Contains("without a valid pairing", StringComparison.Ordinal),
                    "20 wrong tokens lock out the address they came from; X-Forwarded-For can't move that, even with forwarded headers switched on in the environment");
            }
            finally
            {
                foreach (var phone in phones) phone.Dispose();
            }
        }
        finally
        {
            await server.ApplyAsync(false, port, true);
        }
        await DevicesFileAsync(check, folder, host);
        RemoteCertificate.Delete(CertName);
        check(
            "remote-cleanup",
            Count(CertName) == 0 && keyName != null && keyProvider != null && !CngKey.Exists(keyName, keyProvider),
            "the throw-away certificate and its key are deleted again");
    }

    // Starts the server with settings in the environment that ASP.NET Core would normally pick up. Each of them must
    // change nothing; the checks after this one would fail if one did.
    static async Task StartWithStrayEnvironmentAsync(RemoteServer server, int port)
    {
        var stray = new Dictionary<string, string>
        {
            ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true",
            ["ForwardedHeaders_Enabled"] = "true",
            ["AllowedHosts"] = "example.com",
            ["ASPNETCORE_URLS"] = "http://127.0.0.1:" + FreePort(),
            ["Kestrel__Endpoints__Stray__Url"] = "http://127.0.0.1:" + FreePort(),
        };
        var before = stray.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (name, value) in stray) Environment.SetEnvironmentVariable(name, value);
            await server.ApplyAsync(true, port, true);
        }
        finally
        {
            foreach (var (name, value) in before) Environment.SetEnvironmentVariable(name, value);
        }
    }

    // Eight connections that never start TLS take everything one address may have; the ninth is cut.
    static async Task<bool> ConnectionCapAsync(RemoteServer server, int port, string fingerprint, string root)
    {
        var held = new List<TcpClient>();
        try
        {
            for (var i = 0; i < 8; i++)
            {
                var client = new TcpClient();
                held.Add(client);
                await client.ConnectAsync(IPAddress.Loopback, port);
            }
            for (var i = 0; i < 100 && server.Connections < 8; i++) await Task.Delay(20);
            var full = server.Connections == 8;
            var cut = false;
            try
            {
                using var ninth = Pinned(fingerprint, 1);
                using var refused = await ninth.GetAsync(root + "hello");
            }
            catch (HttpRequestException)
            {
                cut = true;
            }
            foreach (var client in held) client.Dispose();
            held.Clear();
            await SettleAsync(server, 0);
            using var after = Pinned(fingerprint, 1);
            using var hello = await after.GetAsync(root + "hello");
            return full && cut && hello.StatusCode == HttpStatusCode.OK;
        }
        finally
        {
            foreach (var client in held) client.Dispose();
        }
    }

    // A phone may have 4 requests waiting for the page; removing it answers them at once.
    static async Task WaitingForPageAsync(Action<string, bool, string> check, RemoteServer server, StubHost host, HttpClient http, string root, string fingerprint)
    {
        var (token, device) = await PairAsync(server, http, root, "Busy phone");
        await SettleAsync(server, 2);
        var hold = new TaskCompletionSource<RemoteReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Hold = hold;
        try
        {
            using var parallel = Pinned(fingerprint, 5);
            var waiting = Enumerable.Range(0, 4).Select(_ => Send(parallel, HttpMethod.Post, root + "sessions/s1/input", token, new { text = "x", submit = false })).ToList();
            for (var i = 0; i < 150 && host.Held < 4; i++) await Task.Delay(20);
            var asking = Send(parallel, HttpMethod.Post, root + "sessions/s1/input", token, new { text = "x", submit = false });
            await Task.WhenAny(asking, Task.Delay(5000));
            server.RemoveDevice(device);
            var all = Task.WhenAll(waiting);
            var atOnce = await Task.WhenAny(all, Task.Delay(5000)) == all;
            hold.TrySetResult(RemoteReply.Ok(new { ok = true }));
            var answers = await all;
            using var fifth = await asking;
            var fifthBody = await Body(fifth);
            check(
                "remote-in-flight",
                fifth.StatusCode == HttpStatusCode.TooManyRequests && Json.Str(fifthBody, "error") == "Too many requests. Wait a moment."
                    && atOnce && answers.All(answer => answer.StatusCode == HttpStatusCode.Unauthorized) && host.Cancelled.Contains(device),
                "a fifth request waiting for the page gets 429; removing the phone answers its four with 401 at once and tells the page to drop its typing");
            foreach (var answer in answers) answer.Dispose();
        }
        finally
        {
            host.Hold = null;
            hold.TrySetResult(RemoteReply.Ok(new { ok = true }));
        }
    }

    // A remote.json that can't be parsed keeps Phone remote off and is never written over; once it is fixed, it starts.
    static async Task DevicesFileAsync(Action<string, bool, string> check, string folder, IRemoteHost host)
    {
        const string garbage = "{ \"this isn't\": a list of phones";
        var path = Path.Combine(folder, "damaged.json");
        File.WriteAllText(path, garbage);
        var server = new RemoteServer(host, path, CertName);
        var port = FreePort();
        await server.ApplyAsync(true, port, true);
        var refused = !server.Running && server.Error?.Contains("stays off to keep your paired phones", StringComparison.Ordinal) == true;
        var error = server.Error;
        var notSaved = false;
        try
        {
            server.Devices.Add("Phone");
        }
        catch (InvalidOperationException)
        {
            notSaved = true;
        }
        await server.ApplyAsync(false, port, true);
        var unchanged = File.ReadAllText(path) == garbage && server.Devices.All().Count == 0;
        File.WriteAllText(path, "[]");
        await server.ApplyAsync(true, port, true);
        var recovered = server.Running;
        await server.ApplyAsync(false, port, true);
        check(
            "remote-devices-file",
            refused && notSaved && unchanged && recovered,
            refused ? "a damaged remote.json keeps Phone remote off and stays as it is; once fixed, Phone remote starts" : error ?? "started anyway");
    }

    static async Task<(string Token, string Device)> PairAsync(RemoteServer server, HttpClient http, string root, string name)
    {
        using var pair = await Send(http, HttpMethod.Post, root + "pair", null, new { code = server.NewCode(), device = name });
        var body = await Body(pair);
        return (Json.Str(body, "token"), Json.Str(body, "deviceId"));
    }

    // Waits until the server has at most this many connections open, so the per-address cap doesn't cut the next ones.
    static async Task SettleAsync(RemoteServer server, int open)
    {
        for (var i = 0; i < 150 && server.Connections > open; i++) await Task.Delay(20);
    }

    // Certificates of self-checks that crashed before they could clean up.
    static void DeleteLeftovers()
    {
        var names = new HashSet<string>();
        using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
        {
            store.Open(OpenFlags.ReadOnly);
            foreach (var cert in store.Certificates)
            {
                using (cert)
                {
                    if (cert.FriendlyName.StartsWith(CertPrefix, StringComparison.Ordinal)
                        && int.TryParse(cert.FriendlyName[CertPrefix.Length..], out var id) && !IsRunning(id))
                        names.Add(cert.FriendlyName);
                }
            }
        }
        foreach (var name in names) RemoteCertificate.Delete(name);
    }

    static bool IsRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    static JsonElement ScreenFor(string id)
    {
        var lines = string.Join(",", Enumerable.Range(0, 10).Select(i => "[{\"t\":\"line " + i + "\"}]"));
        using var screen = JsonDocument.Parse("{\"id\":\"" + id + "\",\"seq\":1,\"cols\":80,\"rows\":10,\"total\":100,\"from\":90,\"cursor\":null,\"lines\":[" + lines + "]}");
        return screen.RootElement.Clone();
    }

    static bool Names(JsonElement element, string names)
        => element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(names.Split(' ').Order());

    static bool Allowed(string address) => RemoteModel.IsAllowedAddress(new IPEndPoint(IPAddress.Parse(address), 1));

    static int Count(string name)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var count = 0;
        foreach (var cert in store.Certificates)
        {
            using (cert)
                if (cert.FriendlyName == name) count++;
        }
        return count;
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Accepts only the certificate with this SHA-256, the way the phone does.
    static bool Pins(X509Certificate? cert, string fingerprint)
        => cert != null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()), Convert.FromHexString(fingerprint));

    static HttpClient Pinned(string fingerprint, int connections) => new(new SocketsHttpHandler
    {
        SslOptions = { RemoteCertificateValidationCallback = (_, cert, _, _) => Pins(cert, fingerprint) },
        MaxConnectionsPerServer = connections,
    })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    // expectContinue holds a large body back until the server asks for it, so its early 413 can't race the upload.
    static async Task<HttpResponseMessage> Send(
        HttpClient http, HttpMethod method, string url, string? token, object? body = null, string? raw = null, bool expectContinue = false, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (expectContinue) request.Headers.ExpectContinue = true;
        if (forwardedFor != null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null || raw != null) request.Content = new StringContent(raw ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await http.SendAsync(request);
    }

    static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    static async Task<ClientWebSocket> Connect(int port, string fingerprint, string token, CancellationToken cancel)
    {
        var socket = new ClientWebSocket();
        try
        {
            await Connect(socket, port, fingerprint, token, cancel);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    static async Task Connect(ClientWebSocket socket, int port, string fingerprint, string token, CancellationToken cancel)
    {
        socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        socket.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => Pins(cert, fingerprint);
        socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions();
        socket.Options.CollectHttpResponseDetails = true;
        await socket.ConnectAsync(new Uri("wss://127.0.0.1:" + port + "/api/v1/live"), cancel);
    }

    // The next message as JSON, or { "close": code, "reason": text } when the server closed the connection.
    static async Task<JsonElement> Receive(ClientWebSocket socket, CancellationToken cancel)
    {
        var buffer = new byte[256 * 1024];
        var count = 0;
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(count), cancel);
            count += result.Count;
        }
        while (!result.EndOfMessage);
        if (result.MessageType != WebSocketMessageType.Close)
        {
            using var message = JsonDocument.Parse(buffer.AsMemory(0, count));
            return message.RootElement.Clone();
        }
        try
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cancel);
        }
        catch (Exception)
        {
            // The server already went.
        }
        return JsonSerializer.SerializeToElement(new { close = (int?)socket.CloseStatus ?? 0, reason = socket.CloseStatusDescription ?? "" });
    }

    // The next message, or { "timeout": true } when none arrives in time (the socket is then aborted).
    static async Task<JsonElement> ReceiveWithinAsync(ClientWebSocket socket, int seconds)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try
        {
            return await Receive(socket, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return JsonSerializer.SerializeToElement(new { timeout = true });
        }
    }

    // Stands in for the window: answers every page request at once (or when Hold is set, once it ends) and
    // remembers what was asked.
    sealed class StubHost : IRemoteHost
    {
        int _held;

        public ConcurrentBag<(string Type, string Device)> Asked { get; } = new();
        public ConcurrentBag<string> Refreshed { get; } = new();
        public ConcurrentQueue<string> Logged { get; } = new();
        public ConcurrentBag<string?> Cancelled { get; } = new();
        public volatile TaskCompletionSource<RemoteReply>? Hold;
        public int Held => Volatile.Read(ref _held);

        static object State() => RemoteModel.State(Path.GetTempPath(), [Path.GetTempPath()], AgentStore.Defaults(), null, [], null, [], []);

        public Task<object> StateAsync() => Task.FromResult(State());

        public Task<object> ChatsAsync() => Task.FromResult<object>(new List<object>());

        public Task<(object State, object Chats)> SnapshotAsync() => Task.FromResult((State(), (object)new List<object>()));

        public Task<RemoteReply> AskPageAsync(string device, string type, Dictionary<string, object?> fields)
        {
            Asked.Add((type, device));
            if (Hold is not { } hold) return Task.FromResult(RemoteReply.Ok(new { ok = true }));
            Interlocked.Increment(ref _held);
            return hold.Task;
        }

        public ConcurrentQueue<string> Projects { get; } = new();

        public Task<RemoteReply> CreateProjectAsync(string device, string name)
        {
            Projects.Enqueue(name);
            return Task.FromResult(new RemoteReply(201, JsonSerializer.SerializeToElement(new { workspace = name })));
        }

        public Task<RemoteReply> StartAsync(string device, string account, string workspace)
        {
            Asked.Add(("remoteStart", device));
            return Task.FromResult(new RemoteReply(201, JsonSerializer.SerializeToElement(new { id = "s9" })));
        }

        public void Cancel(string? device) => Cancelled.Add(device);

        public void WatchesChanged(IReadOnlyList<string> refresh)
        {
            foreach (var id in refresh) Refreshed.Add(id);
        }

        public void RemoteChanged()
        {
        }

        public void Paired(string name)
        {
        }

        public void Log(string message) => Logged.Enqueue(message);
    }
}

// The Phone remote contract's example files (docs/phone-remote/fixtures, README there): byte for byte what a phone
// gets, made by the server's own serializers and options (RemoteModel) from made-up data. The Android app pins each
// file's SHA-256 and decodes it (ContractFixturesTest), so the PC and the phone can't drift apart unnoticed.
// Nothing in them comes from this PC: no machine name, version, path, account, terminal text or clock.
// Session, Screen and Attention are built by the page (web/app.js); here a made-up object in the page's field order
// stands in for it and goes through the same C# steps as the page's (Redacted, ScreenMessage, Event).
static class RemoteFixtures
{
    const string Pc = "DESKTOP-ARCADE", Version = "1.4.0", Projects = @"C:\Projects";
    const string River = Projects + @"\RiverRun", Garden = Projects + @"\StoneGarden", Maze = Projects + @"\OldMaze";
    const string FoamChat = "6f0c2b8e-5d1e-4f7a-9a43-2b1f7c9d0e11", StoneChat = "3b9d2f4a-8c1e-4e6b-a0d7-5f2c9e8b1a64";
    const string FoamQuestion = "Do you want to make this edit to WaterSurface.shader?";

    // The sentences REMOTE_API.md's error table gives for these codes.
    const string DialogChanged = "The question on your PC changed, so nothing was sent. Check it and answer again.";
    const string NotApproved = "Nobody allowed this phone on your PC in time. Press New code on your PC, scan again and click Allow.";

    public static void Run(Action<string, bool, string> check, string? folder)
    {
        try
        {
            var files = Build();
            var again = Build();
            var same = files.Keys.SequenceEqual(again.Keys) && files.All(file => file.Value.AsSpan().SequenceEqual(again[file.Key]));
            var objects = files.Values.All(bytes =>
            {
                using var json = JsonDocument.Parse(bytes);
                return json.RootElement.ValueKind == JsonValueKind.Object;
            });
            var local = LocalNames().Where(name => files.Values.Any(bytes => Encoding.UTF8.GetString(bytes).Contains(name, StringComparison.OrdinalIgnoreCase))).ToList();
            var built = same && objects && local.Count == 0;
            var detail = files.Count + " files, the same bytes each time, " + (local.Count == 0 ? "nothing from this PC in them" : "this PC's name or folder in them");
            if (folder == null)
            {
                check("remote-fixtures", built, detail + "; not written (--fixtures <folder> writes them)");
                return;
            }
            if (folder.Length == 0)
            {
                check("remote-fixtures", false, "--fixtures needs a folder");
                return;
            }
            var target = Path.GetFullPath(folder);
            if (built)
            {
                Directory.CreateDirectory(target);
                foreach (var (name, bytes) in files) File.WriteAllBytes(Path.Combine(target, name), bytes);
            }
            check("remote-fixtures", built, detail + (built ? "; written to " + target : "; not written"));
        }
        catch (Exception ex)
        {
            check("remote-fixtures", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    // File name -> its bytes, in name order.
    public static SortedDictionary<string, byte[]> Build()
    {
        var agents = AgentStore.Defaults().Where(agent => agent.Command is "claude" or "codex" or "copilot").ToList();
        AgentDef Agent(string command) => agents.First(agent => agent.Command == command);
        var tools = new Dictionary<string, ToolStatus>
        {
            [Agent("claude").Name] = new(Agent("claude"), "claude.exe", "2.1.4"),
            [Agent("codex").Name] = new(Agent("codex"), "codex.exe", "0.46.0"),
        };
        var accounts = new List<AgentAccount>
        {
            new() { Agent = Agent("claude").Name, Name = "main" },
            new() { Agent = Agent("claude").Name, Name = "second", Home = "second" },
            new() { Agent = Agent("codex").Name, Name = "main" },
        };
        var infos = new Dictionary<string, AccountInfo>
        {
            ["Claude Code|main"] = new(true, "you@example.com", "max"),
            ["Claude Code|second"] = new(true, "second@example.com", "pro"),
            // Still being checked: signedIn null.
            ["Codex|main"] = new(null),
        };
        var workspaces = RemoteModel.Workspaces([River, Garden, Maze], path => path != Maze, path => path == River);
        var state = RemoteModel.State(Pc, Version, River, Projects, workspaces, agents, tools, accounts, infos,
            [RemoteModel.Redacted(Page(Asking())), RemoteModel.Redacted(Page(Ended()))],
            ["192.168.1.20:47821", "100.101.102.103:47821"], RemoteModel.Power("agents", true, false, true));

        var history = new List<ChatRecord>
        {
            Record(FoamChat, Agent("claude").Name, "second", River, "Calm the foam near the bridge", false, FoamChat, 1791890520000, 1791893401000, null),
            Record(StoneChat, Agent("codex").Name, "main", Garden, "Sort the stone sizes", true, "", 1791804300000, 1791807060000, 1791807060000),
            Record("a1d4c7e0-2b5f-4c8a-9e3d-6f1b0a7c2d95", Agent("copilot").Name, "main", Maze, "", false, "", 1791700000000, 1791700900000, 1791700900000),
        };
        var chats = RemoteModel.Chats(history, agents, chat => chat.Id == FoamChat ? "s3" : null);

        return new(StringComparer.Ordinal)
        {
            // GET /api/v1/hello, /state and /history: the bodies WriteAsync sends.
            ["hello.json"] = RemoteModel.ReplyJson(RemoteReply.Ok(RemoteModel.Hello(Pc, Version))),
            ["state.json"] = RemoteModel.ReplyJson(RemoteReply.Ok(state)),
            ["history.json"] = RemoteModel.ReplyJson(RemoteReply.Ok(new { chats = (object)chats })),
            // WebSocket events, as RemoteServer sends them.
            ["session.json"] = RemoteModel.Event(new { type = "session", session = RemoteModel.Redacted(Page(Working())) }),
            ["screen.json"] = RemoteModel.ScreenMessage(Page(Screen()), 6),
            ["attention.json"] = RemoteModel.Event(new { type = "attention", attention = RemoteModel.Redacted(Page(Attention())) }),
            // Error bodies; the status is in the file name.
            ["error-409-dialogChanged.json"] = RemoteModel.ReplyJson(RemoteReply.Fail(409, DialogChanged)),
            ["error-403-notApproved.json"] = RemoteModel.ReplyJson(RemoteReply.Fail(403, NotApproved)),
        };
    }

    // What the page posts arrives as JSON text and is parsed, as MainForm does with the page's messages.
    static JsonElement Page(object value)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return json.RootElement.Clone();
    }

    static ChatRecord Record(string id, string agent, string account, string workspace, string title, bool named, string chatId, long started, long lastActive, long? ended) => new()
    {
        Id = id,
        Agent = agent,
        Account = account,
        Workspace = workspace,
        Title = title,
        Named = named,
        ChatId = chatId,
        Started = DateTimeOffset.FromUnixTimeMilliseconds(started),
        LastActive = DateTimeOffset.FromUnixTimeMilliseconds(lastActive),
        Ended = ended is { } at ? DateTimeOffset.FromUnixTimeMilliseconds(at) : null,
    };

    // Sessions in the field order of the page's remoteSummary.
    static object Asking() => new
    {
        id = "s3",
        chat = FoamChat,
        mode = "start",
        agent = "Claude Code",
        command = "claude",
        account = "Claude Code|second",
        accountName = "second",
        workspace = River,
        title = "Calm the foam near the bridge",
        status = "needsYou",
        message = "Claude Code needs your OK to continue.",
        question = FoamQuestion,
        choices = new[]
        {
            new { key = "1", label = "Yes" },
            new { key = "2", label = "Yes, and don't ask again this session" },
            new { key = "3", label = "No, and tell Claude what to do differently" },
        },
        activity = (string?)null,
        plan = new
        {
            done = 2,
            total = 4,
            current = "Narrow the foam mask",
            items = new[]
            {
                new { text = "Find the foam code", done = true },
                new { text = "Measure the foam band", done = true },
                new { text = "Narrow the foam mask", done = false },
                new { text = "Check the bridge banks", done = false },
            },
        },
        progress = (object?)null,
        preview = Array.Empty<string>(),
        startedAt = 1791890520000,
        turnStartedAt = 1791893270000,
        lastOutputAt = 1791893401000,
        endedAt = (long?)null,
        exitCode = (long?)null,
        error = (string?)null,
        seenOnPc = false,
        cols = 120,
        rows = 40,
    };

    static object Working() => new
    {
        id = "s4",
        chat = "9e7a1c3b-4d2f-4a6e-8b0c-1f5d3e9a7c20",
        mode = "start",
        agent = "Claude Code",
        command = "claude",
        account = "Claude Code|main",
        accountName = "main",
        workspace = Garden,
        title = "Add a moss shader",
        status = "working",
        message = (string?)null,
        question = (string?)null,
        choices = Array.Empty<object>(),
        activity = "✻ Brewing… (1m 12s · ↑ 3.4k tokens · esc to interrupt)",
        plan = (object?)null,
        progress = new { state = "percent", percent = 40 },
        preview = new[] { "The moss needs a second UV set.", "Reading the stone textures." },
        startedAt = 1791892000000,
        turnStartedAt = 1791893300000,
        lastOutputAt = 1791893402000,
        endedAt = (long?)null,
        exitCode = (long?)null,
        error = (string?)null,
        seenOnPc = true,
        cols = 120,
        rows = 40,
    };

    // 0xC000013A, the exit code after Ctrl+C, is above a 32-bit int.
    static object Ended() => new
    {
        id = "s5",
        chat = StoneChat,
        mode = "resume",
        agent = "Codex",
        command = "codex",
        account = "Codex|main",
        accountName = "main",
        workspace = Garden,
        title = "Sort the stone sizes",
        status = "ended",
        message = "Codex stopped.",
        question = (string?)null,
        choices = Array.Empty<object>(),
        activity = (string?)null,
        plan = (object?)null,
        progress = (object?)null,
        preview = new[] { "Sorted 48 stones by size." },
        startedAt = 1791804300000,
        turnStartedAt = 1791806900000,
        lastOutputAt = 1791807050000,
        endedAt = 1791807060000,
        exitCode = 3221225786L,
        error = (string?)null,
        seenOnPc = false,
        cols = 100,
        rows = 30,
    };

    // The page's screenOf: id and seq, then cols, rows, total, from, cursor and lines. Eight lines, cut to the last
    // six as for a phone that watches 6 lines, so from goes up by 2.
    static object Screen() => new
    {
        id = "s3",
        seq = 418,
        cols = 120,
        rows = 40,
        total = 2133,
        from = 2125,
        cursor = new { x = 3, y = 2131 },
        lines = new object[][]
        {
            [new { t = "● ", fg = "#fafafa" }, new { t = "Update", s = 1 }, new { t = "(Assets/Shaders/WaterSurface.shader)" }],
            [new { t = "  ⎿  ", fg = "#71717a" }, new { t = "Updated 1 line", fg = "#71717a", s = 8 }],
            [new { t = "118 - float foam = smoothstep(0, .6, d);", fg = "#f87171", bg = "#3b1d22" }],
            [new { t = "118 + float foam = smoothstep(0, .25, d);", fg = "#4ade80", bg = "#16301f" }],
            [],
            [new { t = " " + FoamQuestion, s = 1 }],
            [new { t = " ❯ 1. Yes", fg = "#d97757" }],
            [new { t = "   2. Yes, and don't ask again this session" }],
        },
    };

    static object Attention() => new
    {
        id = "s3",
        status = "needsYou",
        title = "Calm the foam near the bridge",
        message = "Claude Code needs your OK to continue.",
        question = FoamQuestion,
        seenOnPc = false,
        at = 1791893401000,
    };

    // This PC's name, user and folder, which must not get into the files.
    static IEnumerable<string> LocalNames()
    {
        var profile = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return new[] { Environment.MachineName, Environment.UserName, profile, profile.Replace('\\', '/'), JsonEncodedText.Encode(profile).ToString() }
            .Where(name => name.Length >= 4 && !Pc.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Distinct();
    }
}
