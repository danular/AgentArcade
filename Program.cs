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

        AgentCatalogueCheck.Run(Check, lines.Add);

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

            // Titles that 1.5.1 and older made from mouse reports, whole, cut off by the 60-character limit, and mixed with words.
            string[] mouse = [";12;9M<35;11;8M<35;9;7M<35;7;6M<35;4;5M<35;2;5M<35;23;1…", "<35;1;1M", "fix the bridge<35;12;9M<35;11;8M foam"];
            var titles = mouse.Select(ChatTitle.Clean).ToList();
            const string numbers = "Fix the foam at 10:30; 2;3 items <b>";
            var clean = titles.SequenceEqual(["", "", "fix the bridge foam"]) && ChatTitle.Clean(numbers) == numbers
                && ChatTitle.Clean("use " + key) == "use sk-proj-[hidden]";
            Check("chat-title", clean, clean
                ? "mouse reports come out of chat titles, a title of only mouse reports becomes empty, other text stays, keys are hidden"
                : string.Join(" | ", titles) + " | " + ChatTitle.Clean(numbers));
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

        McpCheck.Run(Check);

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

// The built-in agents on a clean PC: Antigravity CLI offered once, the vendors' own installers, finding a tool whose
// installer didn't put it on PATH, what an Install needs first, and the golden-screen status tests (tests/status).
// Everything runs in temp folders with fake tools; nothing is installed and no agent starts.
static class AgentCatalogueCheck
{
    public static void Run(Action<string, bool, string> check, Action<string> info)
    {
        var temp = Path.Combine(Path.GetTempPath(), "AgentArcade-catalogue-" + Environment.ProcessId);
        try
        {
            Catalogue(check, Path.Combine(temp, "home"));
            Known(check, temp);
            Needs(check, info);
        }
        catch (Exception ex)
        {
            check("agent-catalogue", false, ex.Message);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { /* temp folder */ }
        }
        StatusTests(check, info);
    }

    // Antigravity CLI is a built-in with the commands from Google's docs, and reaches an older agents.json once; old
    // built-in npm installs of Codex and Copilot become the official installers, edited ones stay.
    static void Catalogue(Action<string, bool, string> check, string home)
    {
        var defaults = AgentStore.Defaults();
        var agy = defaults.FirstOrDefault(agent => agent.Command == "agy");
        var codex = defaults.First(agent => agent.Command == "codex");
        var copilot = defaults.First(agent => agent.Command == "copilot");
        var builtIn = agy != null && agy.Name == "Antigravity CLI" && agy.ResumeArgs == "--continue" && agy.Login.Length == 0
            && agy.Install == "powershell -NoProfile -ExecutionPolicy Bypass -Command \"irm https://antigravity.google/cli/install.ps1 | iex\""
            && agy.HomeVar.Length == 0 && agy.AccountCheck.Length == 0 && agy.LoginHint.Contains("Google OAuth", StringComparison.Ordinal)
            && codex.Install.Contains("https://chatgpt.com/codex/install.ps1", StringComparison.Ordinal) && copilot.Install == "winget install GitHub.Copilot";
        var migrated = AgentStore.RetiredInstalls.All(item => item.Value.All(old =>
        {
            var current = defaults.First(agent => agent.Command == item.Key);
            var edited = old + " --foreground-scripts";
            return AgentStore.CurrentInstall(new AgentDef { Command = item.Key, Install = old }, current) == current.Install
                && AgentStore.CurrentInstall(new AgentDef { Command = item.Key, Install = edited }, current) == edited;
        }));

        // An agents.json from 1.4.0 (the five agents it offered), in its own data folder.
        var before = Environment.GetEnvironmentVariable("AGENT_ARCADE_HOME");
        bool offeredOnce;
        try
        {
            Directory.CreateDirectory(home);
            Environment.SetEnvironmentVariable("AGENT_ARCADE_HOME", home);
            var old = AgentStore.Defaults().Where(agent => agent.Command != "agy").ToList();
            foreach (var agent in old) agent.Install = AgentStore.RetiredInstalls.TryGetValue(agent.Command, out var retired) ? retired[0] : agent.Install;
            File.WriteAllText(AgentStore.FilePath, JsonSerializer.Serialize(old, Json.Options));
            File.WriteAllText(Path.Combine(home, "agents-offered.json"), JsonSerializer.Serialize(old.Select(agent => agent.Name), Json.Options));
            var first = AgentStore.Load();
            var again = AgentStore.Load();
            // Deleted by the user after it was offered: it stays deleted.
            File.WriteAllText(AgentStore.FilePath, JsonSerializer.Serialize(again.Where(agent => agent.Command != "agy"), Json.Options));
            var deleted = AgentStore.Load();
            offeredOnce = first.Count(agent => agent.Command == "agy") == 1 && again.Count(agent => agent.Command == "agy") == 1
                && deleted.All(agent => agent.Command != "agy") && first.Count == old.Count + 1
                && first.First(agent => agent.Command == "codex").Install == codex.Install && first.First(agent => agent.Command == "copilot").Install == copilot.Install;
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENT_ARCADE_HOME", before);
        }
        check("agent-catalogue", builtIn && migrated && offeredOnce,
            "Antigravity CLI (agy) is built in and reaches an older agents.json once; Codex and Copilot use their official installers, "
                + "an Install that is exactly an old built-in npm command follows them, an edited one stays");
    }

    // Claude Code, Codex, Copilot and Antigravity CLI are found where their installers put them even when that folder
    // isn't on PATH, also for an agents.json entry without AltPath. A fake user folder stands in for the real one.
    static void Known(Action<string, bool, string> check, string temp)
    {
        var env = new Dictionary<string, string> { ["USERPROFILE"] = Path.Combine(temp, "profile"), ["LOCALAPPDATA"] = Path.Combine(temp, "local") };
        string Fake(params string[] parts)
        {
            var file = Path.Combine([temp, .. parts]);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, []);
            return file;
        }
        var claude = Fake("profile", ".local", "bin", "claude.exe");
        var agy = Fake("local", "agy", "bin", "agy.exe");
        var copilot = Fake("local", "Microsoft", "WinGet", "Links", "copilot.exe");
        var found = Locator.FindKnown("claude", env) == claude && Locator.FindKnown("Claude.exe", env) == claude
            && Locator.FindKnown("agy", env) == agy && Locator.FindKnown("copilot", env) == copilot;
        var notThere = Locator.FindKnown("codex", env) == null && Locator.FindKnown("gemini", env) == null
            && Locator.FindKnown(claude, env) == null && Locator.FindKnown("", env) == null;

        // claude isn't on PATH (an empty one): the entry from an old agents.json still finds it.
        var path = Environment.GetEnvironmentVariable("PATH");
        string? viaAgent;
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.Combine(temp, "empty"));
            viaAgent = Locator.FindAgent(new AgentDef { Name = "Claude Code", Command = "claude" }, env);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
        }
        check("locator-known", found && notThere && viaAgent == claude,
            @"off PATH, claude is found in %USERPROFILE%\.local\bin, agy in %LOCALAPPDATA%\agy\bin and copilot in WinGet's links, keyed by command");
    }

    // What an Install needs first, with fake tool paths: npm and Node.js (22+ for Copilot, 20+ for Gemini), PowerShell 7
    // for Copilot from npm, WinGet for a WinGet install. Then, as INFO, what this PC has.
    static void Needs(Action<string, bool, string> check, Action<string> info)
    {
        var defaults = AgentStore.Defaults();
        var gemini = defaults.First(agent => agent.Command == "gemini");
        var copilot = defaults.First(agent => agent.Command == "copilot");
        var claude = defaults.First(agent => agent.Command == "claude");
        var copilotNpm = new AgentDef { Name = copilot.Name, Command = "copilot", Install = "npm install -g @github/copilot" };
        var custom = new AgentDef { Name = "My agent", Command = "my-agent", Install = "npm install -g my-agent" };
        var all = new Prerequisites.Found(@"C:\fake\npm.cmd", @"C:\fake\node.exe", "v22.11.0", @"C:\fake\pwsh.exe", @"C:\fake\winget.exe");
        string Urls(AgentDef agent, Prerequisites.Found found) => string.Join(" ", Prerequisites.Missing(agent, found).Select(need => need.Url));
        const string node = Prerequisites.NodeUrl, pwsh = Prerequisites.PowerShellUrl, winget = Prerequisites.WinGetUrl;
        var passed = Urls(gemini, all) == "" && Urls(copilotNpm, all) == "" && Urls(copilot, all) == ""
            && Urls(gemini, all with { Node = null }) == node && Urls(gemini, all with { Npm = null }) == node
            && Urls(gemini, all with { NodeVersion = "v18.19.0" }) == node && Urls(gemini, all with { NodeVersion = "v20.0.0" }) == ""
            && Urls(copilotNpm, all with { NodeVersion = "v20.10.0" }) == node && Urls(copilotNpm, all with { NodeVersion = "no answer" }) == node
            && Urls(copilotNpm, all with { Pwsh = null }) == pwsh && Urls(copilotNpm, all with { Node = null, Pwsh = null }) == node + " " + pwsh
            && Urls(copilot, all with { Node = null, Npm = null, Pwsh = null }) == "" && Urls(copilot, all with { WinGet = null }) == winget
            && Urls(claude, new Prerequisites.Found(null, null, null, null, null)) == ""
            && Urls(custom, all with { NodeVersion = "v10.0.0" }) == "" && Urls(custom, all with { Node = null }) == node
            && Prerequisites.NodeMajorOf("v24.18.0") == 24 && Prerequisites.NodeMajorOf("garbage") == null
            && Prerequisites.Kind("npm.cmd install -g x") == "npm" && Prerequisites.Kind("winget install X") == "winget" && Prerequisites.Kind("npmx install") == "";
        check("install-prerequisites", passed,
            "an npm install needs npm and Node.js (22+ for Copilot, 20+ for Gemini), Copilot from npm also PowerShell 7, a WinGet install WinGet; "
                + "a missing one gives its nodejs.org, PowerShell or WinGet link instead of running");

        var npmTool = Locator.FindOnPath("npm");
        var nodeTool = Locator.FindOnPath("node");
        info("INFO install-prerequisites — npm " + (npmTool == null ? "not found" : "found") + ", node " + (nodeTool == null ? "not found" : "found")
            + ", PowerShell 7 " + (Locator.FindPwsh() == null ? "not found" : "found") + ", WinGet " + (Locator.FindOnPath("winget") == null ? "not found" : "found"));
    }

    // tests/status through node.exe when it is on PATH (Node.js 20 or newer); INFO when it isn't. Only test names and
    // counts are reported; the fixtures are synthetic.
    static void StatusTests(Action<string, bool, string> check, Action<string> info)
    {
        var tests = Path.Combine(AppContext.BaseDirectory, "tests", "status", "status.test.mjs");
        if (Locator.FindOnPath("node") is not { } node || !node.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            info("INFO status-fixtures — node.exe isn't on PATH, so the golden-screen status tests didn't run");
            return;
        }
        if (!File.Exists(tests))
        {
            check("status-fixtures", false, "tests/status isn't next to the app");
            return;
        }
        try
        {
            var version = Commands.RunAsync(node, ["--version"], 10000).GetAwaiter().GetResult().Stdout.Trim();
            if ((Prerequisites.NodeMajorOf(version) ?? 0) < 20)
            {
                info("INFO status-fixtures — Node.js " + version + " is older than 20, so the golden-screen status tests didn't run");
                return;
            }
            var result = Commands.RunAsync(node, ["--test", "--test-reporter=tap", tests], 120000).GetAwaiter().GetResult();
            int Count(string name) => Regex.Match(result.Stdout, @"^# " + name + @" (\d+)\s*$", RegexOptions.Multiline) is { Success: true } match ? int.Parse(match.Groups[1].Value) : -1;
            var (pass, fail, todo) = (Count("pass"), Count("fail"), Count("todo"));
            var failed = Regex.Matches(result.Stdout, @"^\s*not ok \d+ - (.+?)\s*$", RegexOptions.Multiline)
                .Select(match => match.Groups[1].Value).Where(name => !name.Contains("# TODO", StringComparison.Ordinal)).Take(5).ToList();
            check("status-fixtures", !result.TimedOut && result.ExitCode == 0 && pass > 0 && fail == 0,
                result.TimedOut ? "no answer from node --test within 120 s"
                    : pass + " passed, " + fail + " failed, " + todo + " todo with Node.js " + version + " (tests/status: Claude Code, Codex, Copilot, Cursor and Antigravity CLI screens)"
                        + (failed.Count > 0 ? "; failing: " + string.Join("; ", failed) : ""));
        }
        catch (Exception ex)
        {
            check("status-fixtures", false, ex.Message);
        }
    }
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

        // A new install (DefaultProjectsFolder) makes new projects in source\repos while its workspace is Documents or the
        // user folder, and makes that folder with the first one; a real project still gets them next to it. Settings
        // from before keep the old rule.
        var repos = Path.Combine(folder, "home", "source", "repos");
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var noDrive = Enumerable.Range('D', 23).Select(letter => (char)letter + @":\").FirstOrDefault(drive => !Directory.Exists(drive)) ?? @"Q:\";
        var refusedEarly = NewProject.Refusal(repos, "First");
        var (fresh, freshReply) = NewProject.Create(NewProject.Folder(documents, [documents], repos), "First");
        var older = JsonSerializer.Deserialize<AppSettings>("{\"ProjectPath\":\"C:\\\\Projects\\\\Game\"}");
        var freshState = JsonSerializer.SerializeToElement(RemoteModel.State(RemoteModel.NewRun(), documents, [documents], [], null, [], null, [], [], null, repos), RemoteModel.Wire);
        check(
            "new-project-default",
            NewProject.Folder(documents, [documents], repos) == repos && NewProject.Folder(profile, [], repos) == repos
                && NewProject.Folder(Path.Combine(projects, "First"), [documents], repos) == projects
                && NewProject.Folder(documents, [], null) == Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(documents))
                && refusedEarly == null && freshReply.Status == 201 && fresh == Path.Combine(repos, "First") && Directory.Exists(fresh)
                && NewProject.Refusal(Path.Combine(noDrive, "source", "repos"), "First")?.Status == 404
                && Json.Str(freshState, "projectsFolder") == repos
                && older is { DefaultProjectsFolder: null, WindowBounds: null, WindowMaximized: null }
                && (File.Exists(AppSettings.FilePath) || AppSettings.Load().DefaultProjectsFolder == NewProject.DefaultFolder),
            "Documents and the user folder put new projects in source\\repos, made with the first one; a project keeps them next to it; older settings keep the old rule");

        // A folder picked in Settings (NewProjectsFolder) comes first while it is there; a missing, relative or network
        // one keeps the rule above (a network path is only read as text). Older settings files have neither that nor
        // MascotAnimations, which then means on.
        var picked = Path.Combine(folder, "home", "picked");
        Directory.CreateDirectory(picked);
        var pickedState = JsonSerializer.SerializeToElement(RemoteModel.State(RemoteModel.NewRun(), documents, [documents], [], null, [], null, [], [], null, repos, picked), RemoteModel.Wire);
        check(
            "new-project-chosen",
            NewProject.Folder(documents, [documents], repos, picked) == picked
                && NewProject.Folder(Path.Combine(projects, "First"), [], null, picked + Path.DirectorySeparatorChar) == picked
                && NewProject.Folder(Path.Combine(projects, "First"), [], null, Path.Combine(folder, "home", "gone")) == projects
                && NewProject.Chosen(@"\\server\share\projects") == null && NewProject.Chosen("relative") == null && NewProject.Chosen(null) == null
                && Json.Str(pickedState, "projectsFolder") == picked
                && older is { NewProjectsFolder: null, MascotAnimations: null },
            "a folder picked for new projects wins while it is there; a missing, relative or network one falls back to the rule; older settings have neither field");

        // The window: the first start takes about 85% of the primary work area; a saved place stays on a screen that is
        // there (moved and shrunk into the one it overlaps most), or centred on the primary one when its screen is gone.
        var primaryArea = new Rectangle(0, 0, 2000, 1000);
        var areas = new[] { primaryArea, new Rectangle(2000, 0, 1600, 900) };
        var smallest = new Size(960, 620);
        check(
            "window-place",
            WindowPlace.Restore(null, areas, primaryArea, smallest) == new Rectangle(150, 75, 1700, 850)
                && WindowPlace.Restore(new Rectangle(100, 50, 1200, 800), areas, primaryArea, smallest) == new Rectangle(100, 50, 1200, 800)
                && WindowPlace.Restore(new Rectangle(-300, 100, 1200, 800), areas, primaryArea, smallest) == new Rectangle(0, 100, 1200, 800)
                && WindowPlace.Restore(new Rectangle(2500, 300, 1200, 800), areas, primaryArea, smallest) == new Rectangle(2400, 100, 1200, 800)
                && WindowPlace.Restore(new Rectangle(1700, -40, 3000, 1400), areas, primaryArea, smallest) == new Rectangle(2000, 0, 1600, 900)
                && WindowPlace.Restore(new Rectangle(-6000, 200, 1200, 800), areas, primaryArea, smallest) == new Rectangle(400, 100, 1200, 800)
                && WindowPlace.Restore(new Rectangle(10, 10, 300, 200), areas, primaryArea, smallest) == new Rectangle(10, 10, 960, 620)
                && WindowPlace.Restore(null, areas, new Rectangle(0, 0, 1000, 600), smallest) == new Rectangle(20, 0, 960, 600),
            "85% of the primary work area at first; kept, moved onto or shrunk into a screen that is there; centred when its screen is gone");

        var agents = AgentStore.Defaults();
        var claude = agents.First(agent => agent.Command == "claude");
        var stateRun = RemoteModel.NewRun();
        using var summary = JsonDocument.Parse("{\"id\":\"s1\"}");
        // Probe results are found by agent name, so the ones from before a refresh still count.
        var probed = new Dictionary<string, ToolStatus> { [claude.Name] = new(AgentStore.Defaults().First(agent => agent.Command == "claude"), @"C:\Tools\claude.exe", "2.1.0") };
        var state = JsonSerializer.SerializeToElement(
            RemoteModel.State(stateRun, folder, [folder], agents, probed, [new AgentAccount { Agent = claude.Name, Name = "main" }], null, [summary.RootElement], ["192.168.1.20:47821"], RemoteModel.Power("agents", true)),
            RemoteModel.Wire);
        var record = new ChatRecord { Id = "c1", Agent = claude.Name, Account = "main", Workspace = folder, ChatId = "c1", Started = DateTimeOffset.Now, LastActive = DateTimeOffset.Now };
        var chat = JsonSerializer.SerializeToElement(RemoteModel.Chats([record], agents, _ => "s1")[0], RemoteModel.Wire);
        check(
            "remote-json",
            Names(state, "api pc version run workspace projectsFolder power workspaces agents accounts sessions hosts")
                && Json.Str(state, "run") == stateRun
                && Names(state.GetProperty("power"), "keepAwake awake onBattery lidSleeps")
                && Json.Str(state, "projectsFolder") == Path.GetDirectoryName(folder)
                && state.GetProperty("hosts")[0].GetString() == "192.168.1.20:47821"
                && state.GetProperty("agents")[0].GetProperty("installed").GetBoolean() && Json.Str(state.GetProperty("agents")[0], "version") == "2.1.0"
                && Json.Str(chat, "title") == "New chat" && !chat.GetProperty("named").GetBoolean()
                && Names(state.GetProperty("workspaces")[0], "path name exists unity")
                && Names(state.GetProperty("agents")[0], "name command installed version color")
                && Names(state.GetProperty("accounts")[0], "key agent name main signedIn email plan")
                && Names(chat, "id agent account accountName workspace title named startedAt endedAt open session resume")
                && Names(JsonSerializer.SerializeToElement(RemoteModel.Hello()), "app api version pc minClientApi features")
                && Names(JsonSerializer.SerializeToElement(RemoteModel.PairAnswer("t", "d_000000")), "token deviceId pc")
                && state.GetProperty("agents")[0].GetProperty("color").GetString() == "#d97757"
                && state.GetProperty("accounts")[0].GetProperty("key").GetString() == "Claude Code|main"
                && state.GetProperty("accounts")[0].GetProperty("signedIn").ValueKind == JsonValueKind.Null
                && state.GetProperty("sessions")[0].GetProperty("id").GetString() == "s1"
                && chat.GetProperty("resume").GetString() == "exact" && chat.GetProperty("open").GetBoolean() && chat.GetProperty("session").GetString() == "s1"
                && Regex.IsMatch(RemoteModel.Version, @"^\d+\.\d+\.\d+\z"),
            "State, Chat, Hello and the pair answer have exactly the names in REMOTE_API.md; an untitled chat is \"New chat\"; version " + RemoteModel.Version);

        // run: 16 hex characters, new each time; a Session or Attention gets the State's run as its last field, in place of
        // any the page put in, and its text is still hidden.
        var runs = Enumerable.Range(0, 50).Select(_ => RemoteModel.NewRun()).ToList();
        using var fromThePage = JsonDocument.Parse("{\"id\":\"s7\",\"run\":\"page\",\"title\":\"use sk-proj-" + new string('b', 40) + "\",\"seenOnPc\":false}");
        var stamped = RemoteModel.Summary(fromThePage.RootElement, stateRun);
        var plain = RemoteModel.Summary(summary.RootElement, stateRun);
        check(
            "remote-run",
            runs.All(run => Regex.IsMatch(run, "^[0-9a-f]{16}\\z")) && runs.Distinct().Count() == runs.Count
                && stamped.EnumerateObject().Select(property => property.Name).SequenceEqual(["id", "title", "seenOnPc", "run"])
                && Json.Str(stamped, "run") == stateRun && Json.Str(stamped, "title") == "use sk-proj-[hidden]" && Json.Str(stamped, "id") == "s7"
                && plain.EnumerateObject().Select(property => property.Name).SequenceEqual(["id", "run"]),
            "a run is 16 lowercase hex characters and new each time; Sessions and Attention carry the State's run, never the page's, with their text still hidden");

        // Only that nothing throws: whether this PC is on a public network right now is up to Windows. The network's name
        // stays out of the report.
        var warning = RemoteLink.PublicNetworkWarning(RemoteLink.Hosts(47821, false));
        check("remote-network", true, warning == null
            ? "no address of the pairing link is on a public network, or Windows lets this copy take connections there"
            : Regex.Replace(SelfCheck.HideNetworks(warning, PublicNetworkNames()), "\"[^\"]*\"", "\"[network]\""));

        // A rule's ports, and (reading only) whether Windows' rules let this copy take connections on public networks.
        var covers = Firewall.Covers("*", 47821) && Firewall.Covers(null, 47821) && Firewall.Covers("80, 47821", 47821)
            && Firewall.Covers("47000-48000", 47821) && !Firewall.Covers("80,443", 47821) && !Firewall.Covers("47822-48000", 47821);
        check("remote-firewall", covers, covers
            ? "rule ports read right; Windows " + (Firewall.AllowsOnPublic(Environment.ProcessPath, 47821) ? "lets" : "doesn't let") + " this copy take connections on public networks"
            : "a rule's ports read wrong");

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

        // What the page answers goes to the phone only with a code the contract's table gives for its status.
        using var typed = JsonDocument.Parse("{\"typed\":true}");
        var fromPage = new[]
        {
            RemoteReply.FromPage(409, typed.RootElement, "Enter wasn't pressed.", "dialogOpen"),
            RemoteReply.FromPage(409, null, "Changed.", "badRequest"),
            RemoteReply.FromPage(409, null, "Changed.", null),
            RemoteReply.FromPage(404, null, "", "sessionNotFound"),
            RemoteReply.FromPage(201, null, "ignored", "ignored"),
        };
        using var withCode = JsonDocument.Parse(RemoteModel.ReplyJson(fromPage[0]));
        using var chatOpen = JsonDocument.Parse(RemoteModel.ReplyJson(new RemoteReply(409, JsonSerializer.SerializeToElement(new { id = "s3", code = "x", error = "x" }), "That chat is already open.", "chatOpen")));
        check(
            "remote-error-codes",
            fromPage[0] is { Status: 409, Code: "dialogOpen" } && Names(withCode.RootElement, "typed error code") && Json.Str(withCode.RootElement, "code") == "dialogOpen"
                && fromPage.Skip(1).Take(3).All(reply => reply is { Status: 500, Code: "internal", Error: "Something went wrong on the PC." })
                && fromPage[4] is { Status: 201, Code: null, Error: null }
                && Names(chatOpen.RootElement, "id error code") && Json.Str(chatOpen.RootElement, "code") == "chatOpen" && Json.Str(chatOpen.RootElement, "error") == "That chat is already open."
                && RemoteModel.IsCode(403, "notApproved") && RemoteModel.IsCode(503, "busy") && !RemoteModel.IsCode(404, "busy") && !RemoteModel.IsCode(400, "nope")
                && RemoteReply.Busy.Code == "busy" && RemoteReply.Internal is { Status: 500, Code: "internal" },
            "every error body carries a code from REMOTE_API.md's table, next to error and the body's own fields; a page reply with a code that doesn't fit its status becomes 500 internal");

        // Copy link: read back through the COM interface Windows' clipboard history reads, without touching the clipboard.
        const string secretLink = "agentarcade://pair?v=1&code=self-check";
        var secret = (System.Runtime.InteropServices.ComTypes.IDataObject)MainForm.SecretData(secretLink);
        var text = ClipboardBytes(secret, DataFormats.UnicodeText);
        var flags = new[] { "ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" }
            .Select(format => ClipboardBytes(secret, format)).ToList();
        check(
            "pair-link-clipboard",
            text != null && Encoding.Unicode.GetString(text).TrimEnd('\0') == secretLink
                && flags.All(bytes => bytes is { Length: >= 4 } && BitConverter.ToUInt32(bytes, 0) == 0),
            "Copy link puts the link as text with ExcludeClipboardContentFromMonitorProcessing, and CanIncludeInClipboardHistory and CanUploadToCloudClipboard "
                + "set to 0, so Windows keeps it out of clipboard history and the cloud clipboard");
    }

    // A format's bytes as a clipboard reader gets them (TYMED_HGLOBAL), or null when the data has no such format.
    static byte[]? ClipboardBytes(System.Runtime.InteropServices.ComTypes.IDataObject data, string format)
    {
        var request = new System.Runtime.InteropServices.ComTypes.FORMATETC
        {
            cfFormat = unchecked((short)DataFormats.GetFormat(format).Id),
            dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
        };
        try
        {
            data.GetData(ref request, out var medium);
            try
            {
                if (medium.tymed != System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero) return null;
                var size = checked((int)GlobalSize(medium.unionmember));
                var pointer = GlobalLock(medium.unionmember);
                try
                {
                    var bytes = new byte[size];
                    System.Runtime.InteropServices.Marshal.Copy(pointer, bytes, 0, size);
                    return bytes;
                }
                finally
                {
                    GlobalUnlock(medium.unionmember);
                }
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr GlobalLock(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern UIntPtr GlobalSize(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);

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
            using var pair = await Send(http, HttpMethod.Post, root + "pair", null, new { code, device = "Self‮ check\nphone" }, client: [" AgentArcadeRemote/1.1.0 (1010099) "]);
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
                    && reuse.StatusCode == HttpStatusCode.Forbidden && Json.Str(reuseBody, "error").Contains("already used", StringComparison.Ordinal) && Json.Str(reuseBody, "code") == "codeUsed"
                    && state.StatusCode == HttpStatusCode.OK && stateBody.TryGetProperty("sessions", out _) && stateBody.TryGetProperty("hosts", out _)
                    && wrongToken.StatusCode == HttpStatusCode.Unauthorized && Json.Str(wrongBody, "error") == NotPaired && Json.Str(wrongBody, "code") == "notPaired"
                    && noToken.StatusCode == HttpStatusCode.Unauthorized
                    && host.PairAsked.TryPeek(out var allowed) && allowed.Name == "Self check phone" && host.Logged.Contains("Phone pairing allowed: Self check phone")
                    && !host.Logged.Any(line => line.Contains(token, StringComparison.Ordinal) || line.Contains(code, StringComparison.Ordinal)),
                "pairing with the code and Allow on the PC gives a token that works; the code works once; a wrong token gets 401; no token or code in the log");

            // X-Arcade-Client: kept with the phone from pairing on; a newer valid value replaces it, a bad or doubled one changes nothing.
            var pairedWith = server.Devices.All().Single().Client;
            using var newClient = await Send(http, HttpMethod.Get, root + "state", token, client: ["AgentArcadeRemote/1.1.1-dev (1010101)"]);
            var updated = server.Devices.All().Single().Client;
            using var bad = await Send(http, HttpMethod.Get, root + "state", token, client: ["Agent Arcade Remote/1.2"]);
            using var doubled = await Send(http, HttpMethod.Get, root + "state", token, client: ["curl/8.10.1", "curl/8.10.2"]);
            using var helloWith = await Send(http, HttpMethod.Get, root + "hello", null, client: ["curl/8.10.1"]);
            check(
                "remote-client-header",
                pairedWith == "AgentArcadeRemote/1.1.0 (1010099)" && updated == "AgentArcadeRemote/1.1.1-dev (1010101)"
                    && newClient.StatusCode == HttpStatusCode.OK && bad.StatusCode == HttpStatusCode.OK && doubled.StatusCode == HttpStatusCode.OK && helloWith.StatusCode == HttpStatusCode.OK
                    && server.Devices.All().Single().Client == updated
                    && RemoteModel.Client("AgentArcadeRemote/1.1.0-beta.2 (1010002)") != null && RemoteModel.Client("AgentArcadeRemote/1.1.0-beta.1-dev (1010001)") != null
                    && RemoteModel.Client("curl/8.10.1") == "curl/8.10.1" && RemoteModel.Client("  curl/8.10.1\t") == "curl/8.10.1"
                    && RemoteModel.Client(new Microsoft.Extensions.Primitives.StringValues(["curl/8.10.1", "curl/8.10.1"])) == null
                    && RemoteModel.Client(Microsoft.Extensions.Primitives.StringValues.Empty) == null
                    && new[] { "", "/1", "1a/1", "a/", "a/1 ()", "a/1 (12345678901)", "a/1\n", "a/1  (1)", "ä/1", "a/1,b/2", "a/1;x", "x/" + new string('1', 25), new string('a', 33) + "/1" }
                        .All(value => RemoteModel.Client(value) == null)
                    && RemoteModel.Client("a/" + new string('1', 24) + " (1234567890)") != null
                    && RemoteModel.Client(new string('a', 32) + "/" + new string('1', 24) + " (1234567890)") == null
                    && !host.Logged.Any(line => line.Contains("AgentArcadeRemote", StringComparison.Ordinal) || line.Contains("curl", StringComparison.Ordinal)),
                "the phone's app version is kept from pairing on and the newest valid one shown; a bad or doubled header is ignored and never fails a request; it is never logged");

            using var input = await Send(http, HttpMethod.Post, root + "sessions/s1/input", token, new { text = "hi", submit = true });
            var inputBody = await Body(input);
            using var notJson = await Send(http, HttpMethod.Post, root + "sessions/s1/input", token, raw: "not json");
            var notJsonBody = await Body(notJson);
            using var longText = await Send(http, HttpMethod.Post, root + "sessions/s1/input", token, new { text = new string('x', 16 * 1024 + 1) });
            var longTextBody = await Body(longText);
            using var huge = await Send(http, HttpMethod.Post, root + "sessions/s1/input", token, raw: "\"" + new string('x', 70 * 1024) + "\"", expectContinue: true);
            using var badKey = await Send(http, HttpMethod.Post, root + "sessions/s1/keys", token, new { keys = new[] { "f13" } });
            var badKeyBody = await Body(badKey);
            using var badId = await Send(http, HttpMethod.Post, root + "sessions/x1/stop", token);
            var badIdBody = await Body(badId);
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
                    && !inputBody.TryGetProperty("code", out _)
                    && notJson.StatusCode == HttpStatusCode.BadRequest && Json.Str(notJsonBody, "code") == "badRequest"
                    && longText.StatusCode == HttpStatusCode.RequestEntityTooLarge && Json.Str(longTextBody, "code") == "tooLarge" && huge.StatusCode == HttpStatusCode.RequestEntityTooLarge
                    && badKey.StatusCode == HttpStatusCode.BadRequest && Json.Str(badKeyBody, "error") == "Unknown key: f13" && Json.Str(badKeyBody, "code") == "unknownKey"
                    && badId.StatusCode == HttpStatusCode.NotFound && Json.Str(badIdBody, "code") == "sessionNotFound"
                    && unknown.StatusCode == HttpStatusCode.NotFound && Json.Str(unknownBody, "error") == "Unknown request." && Json.Str(unknownBody, "code") == "unknownRequest"
                    && started.StatusCode == HttpStatusCode.Created && Json.Str(startedBody, "id") == "s9" && host.Asked.Contains(("remoteStart", deviceId))
                    && badProject.StatusCode == HttpStatusCode.BadRequest && Json.Str(badProjectBody, "error") == "Windows keeps NUL for itself. Choose another name."
                    && Json.Str(badProjectBody, "code") == "badName"
                    && project.StatusCode == HttpStatusCode.Created && Json.Str(projectBody, "workspace") == "RiverRun" && host.Projects.SequenceEqual(["RiverRun"]),
                "input reaches the page with the phone's device id; bad JSON 400, too long 413, unknown key 400, bad pane id 404, unknown route 404, start 201, "
                    + "a new project's name is trimmed and checked before it reaches the window");

            await StaleDialogAsync(check, host, http, root, token);
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
                    Json.Str(first, "type") == "hello" && first.GetProperty("state").GetProperty("api").GetInt32() == 1 && Json.Str(first.GetProperty("state"), "run") == host.Run
                        && Json.Str(second, "type") == "history" && second.GetProperty("chats").ValueKind == JsonValueKind.Array
                        && extensions.Contains("permessage-deflate", StringComparison.Ordinal) && extensions.Contains("server_no_context_takeover", StringComparison.Ordinal)
                        && host.Refreshed.Contains("s1") && Json.Str(screen, "type") == "screen"
                        && screen.GetProperty("screen").GetProperty("lines").GetArrayLength() == 4 && screen.GetProperty("screen").GetProperty("from").GetInt32() == 96,
                    "the WebSocket opens with the token, compressed per message; hello, then history; a watched pane's screen arrives cut to its lines");

                // A new run (the page started again) ends every watch: a pane of the new run with an old id reaches
                // nobody, and the phone's next watch is the only one.
                server.ForgetWatches();
                var forgotten = server.Watches().Count == 0;
                await live.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"watch\",\"id\":\"s2\",\"lines\":4}"), WebSocketMessageType.Text, true, cancel.Token);
                for (var i = 0; i < 100 && !host.Refreshed.Contains("s2"); i++) await Task.Delay(20);
                server.Screen(ScreenFor("s1"));
                server.Screen(ScreenFor("s2"));
                var next = await Receive(live, cancel.Token);
                check(
                    "remote-run-watches",
                    forgotten && Json.Str(next, "type") == "screen" && Json.Str(next.GetProperty("screen"), "id") == "s2"
                        && server.Watches().Select(watch => watch.Id).SequenceEqual(["s2"]),
                    "a new run ends the phones' watches, so a pane of the new run that gets a watched pane's old id sends its screen to nobody");

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
                    blocked.StatusCode == HttpStatusCode.TooManyRequests && Json.Str(blockedBody, "error").Contains("without a valid pairing", StringComparison.Ordinal) && Json.Str(blockedBody, "code") == "lockedOut",
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
        await PairApprovalAsync(check, folder);
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

    // expect on keys and input (REMOTE_API.md): what C# checks before anything reaches the page. A malformed expect is
    // 400 and another run 409 runChanged, and then the page is never asked, so nothing is typed. A valid one goes to the
    // page with the request, and the page's 409 dialogChanged reaches the phone with its code. The page's own comparison
    // (question, the dialog's first seq, seq) is Remote.expectRefused, which tests/remote.test.js covers.
    static async Task StaleDialogAsync(Action<string, bool, string> check, StubHost host, HttpClient http, string root, string token)
    {
        async Task<(HttpStatusCode Status, JsonElement Body, Dictionary<string, object?>? Asked)> Post(string path, string raw)
        {
            // Under the 20 requests a second a phone may send.
            await Task.Delay(60);
            var before = host.Requests.Count;
            using var response = await Send(http, HttpMethod.Post, root + "sessions/s1/" + path, token, raw: raw);
            var body = await Body(response);
            return (response.StatusCode, body, host.Requests.Count > before ? host.Requests.Last().Fields : null);
        }
        Task<(HttpStatusCode Status, JsonElement Body, Dictionary<string, object?>? Asked)> Keys(string expect) => Post("keys", "{\"keys\":[\"1\"],\"expect\":" + expect + "}");

        var malformed = new List<string>
        {
            "\"x\"", "[]", "5", "{}", "{\"question\":null}", "{\"seq\":418.0}", "{\"seq\":4.18e2}", "{\"seq\":\"418\"}", "{\"seq\":-1}", "{\"seq\":9007199254740992}",
            "{\"seq\":1,\"question\":5}", "{\"seq\":1,\"question\":\"" + new string('x', 1001) + "\"}", "{\"seq\":1,\"run\":\"\"}", "{\"seq\":1,\"run\":\"" + new string('a', 65) + "\"}",
            "{\"seq\":1,\"run\":7}",
        };
        var refused = new List<string>();
        foreach (var expect in malformed)
        {
            var (status, body, asked) = await Keys(expect);
            if (status != HttpStatusCode.BadRequest || Json.Str(body, "code") != "badRequest" || asked != null) refused.Add(expect.Length > 40 ? expect[..40] + "…" : expect);
        }
        var badInput = await Post("input", "{\"text\":\"x\",\"submit\":true,\"expect\":{\"seq\":\"1\"}}");

        const string otherRun = "0000000000000000";
        var staleKeys = await Keys("{\"question\":\"Do you want to proceed?\",\"seq\":418,\"run\":\"" + otherRun + "\"}");
        var staleInput = await Post("input", "{\"text\":\"yes\",\"submit\":true,\"expect\":{\"question\":null,\"seq\":3,\"run\":\"" + otherRun + "\"}}");

        var longest = new string('q', 1000) + "?";
        var valid = await Keys("{\"question\":\"Do you want to proceed?\",\"seq\":418,\"run\":\"" + host.Run + "\",\"other\":[1]}");
        var edge = await Keys("{\"question\":\"" + longest[1..] + "\",\"seq\":9007199254740991}");
        var none = await Keys("null");
        var input = await Post("input", "{\"text\":\"later\",\"submit\":false,\"expect\":{\"seq\":0,\"run\":null}}");

        // The page saw another dialog: its refusal reaches the phone as it is, with the code.
        host.Answers.Enqueue(RemoteReply.FromPage(409, null, RemoteServer.DialogChanged, "dialogChanged"));
        var changed = await Keys("{\"question\":\"Do you want to proceed?\",\"seq\":417,\"run\":\"" + host.Run + "\"}");
        host.Answers.Enqueue(RemoteReply.FromPage(409, JsonSerializer.SerializeToElement(new { typed = true }), RemoteServer.DialogOpenTyped, "dialogOpen"));
        var typedInput = await Post("input", "{\"text\":\"go on\",\"submit\":true}");

        check(
            "stale-dialog-409",
            refused.Count == 0 && badInput.Status == HttpStatusCode.BadRequest && badInput.Asked == null
                && staleKeys is { Status: HttpStatusCode.Conflict, Asked: null } && Json.Str(staleKeys.Body, "code") == "runChanged"
                && staleInput is { Status: HttpStatusCode.Conflict, Asked: null } && Json.Str(staleInput.Body, "code") == "runChanged"
                && valid.Status == HttpStatusCode.OK && valid.Asked?["expect"] is RemoteExpect { Question: "Do you want to proceed?", Seq: 418 } sent && sent.Run == host.Run
                && edge.Status == HttpStatusCode.OK && edge.Asked?["expect"] is RemoteExpect { Seq: 9007199254740991, Run: null } far && far.Question?.Length == 1000
                && none.Status == HttpStatusCode.OK && none.Asked != null && !none.Asked.ContainsKey("expect")
                && input.Status == HttpStatusCode.OK && input.Asked?["expect"] is RemoteExpect { Question: null, Seq: 0, Run: null }
                && changed.Status == HttpStatusCode.Conflict && Json.Str(changed.Body, "code") == "dialogChanged" && Json.Str(changed.Body, "error") == RemoteServer.DialogChanged
                && typedInput.Status == HttpStatusCode.Conflict && Json.Str(typedInput.Body, "code") == "dialogOpen" && typedInput.Body.TryGetProperty("typed", out var typed) && typed.GetBoolean(),
            refused.Count == 0
                ? "keys and input with an expect from another run get 409 runChanged and nothing reaches the page; " + malformed.Count + " malformed expects get 400; "
                    + "a valid one goes to the page, whose dialogChanged and dialogOpen (typed) reach the phone with their codes (the page's comparison: tests/remote.test.js)"
                : "not refused with 400: " + string.Join(" | ", refused));
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
                fifth.StatusCode == HttpStatusCode.TooManyRequests && Json.Str(fifthBody, "error") == "Too many requests. Wait a moment." && Json.Str(fifthBody, "code") == "tooManyRequests"
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

    // REMOTE_API.md, pair: the right code alone adds no phone; the PC must click Allow. Deny, silence, New code, the
    // phone leaving and Phone remote going off all add nothing. Its own server, so these attempts don't count against
    // the other checks' 10 a minute.
    static async Task PairApprovalAsync(Action<string, bool, string> check, string folder)
    {
        var host = new StubHost { PairMode = 0 };
        var server = new RemoteServer(host, Path.Combine(folder, "approval.json"), CertName);
        var port = FreePort();
        await server.ApplyAsync(true, port, true);
        if (!server.Running)
        {
            check("pair-needs-approval", false, server.Error ?? "didn't start");
            return;
        }
        try
        {
            var root = "https://127.0.0.1:" + port + "/api/v1/";
            var fingerprint = server.Fingerprint!;
            using var http = Pinned(fingerprint, 4);
            var codes = new List<string>();
            async Task<(HttpStatusCode Status, string Code, string Error, double Seconds)> Pair(string pairCode, string device, HttpClient? via = null)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                using var response = await Send(via ?? http, HttpMethod.Post, root + "pair", null, new { code = pairCode, device }, client: ["AgentArcadeRemote/1.1.0 (1010099)"]);
                var body = await Body(response);
                return (response.StatusCode, Json.Str(body, "code"), Json.Str(body, "error"), watch.Elapsed.TotalSeconds);
            }
            async Task<bool> Until(Func<bool> done)
            {
                for (var i = 0; i < 250 && !done(); i++) await Task.Delay(20);
                return done();
            }
            string NewCode()
            {
                var code = server.NewCode();
                codes.Add(code);
                return code;
            }

            // Deny: 403 denied, nothing added, and the code is used up.
            var code = NewCode();
            var denied = await Pair(code, "Denied‮ phone");
            var again = await Pair(code, "Denied phone");
            host.PairAsked.TryPeek(out var asked);

            // Nobody answers within PairWait: 403 notApproved, and the question goes away.
            host.PairMode = -1;
            server.PairWait = TimeSpan.FromSeconds(1.5);
            var silent = await Pair(NewCode(), "Silent phone");
            var silentGone = host.PairAsked.Last().Cancel.IsCancellationRequested;

            // New code while a phone waits: it gets 403 notApproved at once.
            server.PairWait = TimeSpan.FromSeconds(30);
            var waiting = Pair(NewCode(), "Waiting phone");
            await Until(() => host.PairAsked.Count == 3);
            server.NewCode();
            var replaced = await waiting;

            // The phone gives up: the question goes at once and no phone is added.
            code = NewCode();
            using (var impatient = Pinned(fingerprint, 1))
            {
                impatient.Timeout = TimeSpan.FromSeconds(1);
                try
                {
                    await Pair(code, "Impatient phone", impatient);
                }
                catch (TaskCanceledException)
                {
                    // It gave up.
                }
            }
            var left = await Until(() => host.PairAsked.Count == 4 && host.PairAsked.Last().Cancel.IsCancellationRequested);

            // Phone remote off while a phone waits: 403 notApproved before the server goes.
            var stopping = Pair(NewCode(), "Stopping phone");
            await Until(() => host.PairAsked.Count == 5);
            await server.ApplyAsync(false, port, true);
            var stopped = await stopping;

            var notApproved = new[] { silent, replaced, stopped }.All(answer => answer.Status == HttpStatusCode.Forbidden && answer.Code == "notApproved"
                && answer.Error == RemoteServer.NotApproved);
            check(
                "pair-needs-approval",
                denied.Status == HttpStatusCode.Forbidden && denied.Code == "denied" && denied.Error == RemoteServer.Denied
                    && again.Status == HttpStatusCode.Forbidden && again.Code == "codeUsed"
                    && asked.Name == "Denied phone" && asked.Client == "AgentArcadeRemote/1.1.0 (1010099)"
                    && notApproved && silent.Seconds >= 1.3 && silentGone && replaced.Seconds < 10 && left
                    && host.PairAsked.Count == 5 && host.PairAsked.All(question => question.Cancel.IsCancellationRequested)
                    && server.Devices.All().Count == 0
                    && host.Logged.Contains("Phone pairing denied: Denied phone")
                    && !host.Logged.Any(line => codes.Any(used => line.Contains(used, StringComparison.Ordinal)) || line.Contains("AgentArcadeRemote", StringComparison.Ordinal)),
                "the right code only asks the PC: Deny gets 403 denied and uses the code up; no answer in time, New code and Phone remote off get 403 notApproved; "
                    + "a phone that gives up takes its question away; none of them is added, and no code or app version is logged");
        }
        finally
        {
            await server.ApplyAsync(false, port, true);
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
    // client: X-Arcade-Client values; more than one goes as a list.
    static async Task<HttpResponseMessage> Send(
        HttpClient http, HttpMethod method, string url, string? token, object? body = null, string? raw = null, bool expectContinue = false, string? forwardedFor = null,
        string[]? client = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (expectContinue) request.Headers.ExpectContinue = true;
        if (forwardedFor != null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        foreach (var value in client ?? []) request.Headers.TryAddWithoutValidation("X-Arcade-Client", value);
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

        object State() => RemoteModel.State(Run, Path.GetTempPath(), [Path.GetTempPath()], AgentStore.Defaults(), null, [], null, [], []);

        public Task<object> StateAsync() => Task.FromResult(State());

        public Task<object> ChatsAsync() => Task.FromResult<object>(new List<object>());

        public Task<(object State, object Chats)> SnapshotAsync() => Task.FromResult((State(), (object)new List<object>()));

        // Every request that reached the page, with its fields, and the answers to give before the usual ok.
        public ConcurrentQueue<(string Type, Dictionary<string, object?> Fields)> Requests { get; } = new();
        public ConcurrentQueue<RemoteReply> Answers { get; } = new();

        public Task<RemoteReply> AskPageAsync(string device, string type, Dictionary<string, object?> fields)
        {
            Asked.Add((type, device));
            Requests.Enqueue((type, fields));
            if (Answers.TryDequeue(out var answer)) return Task.FromResult(answer);
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

        public string Run { get; } = RemoteModel.NewRun();

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

        // How the Phone dialog answers a pairing: 1 Allow, 0 Deny, -1 never. Each question, as the server asked it.
        public volatile int PairMode = 1;
        public ConcurrentQueue<(string Name, string? Client, CancellationToken Cancel)> PairAsked { get; } = new();

        public Task<bool> AskPairAsync(string name, string? client, CancellationToken cancel)
        {
            PairAsked.Enqueue((name, client, cancel));
            return PairMode switch
            {
                1 => Task.FromResult(true),
                0 => Task.FromResult(false),
                _ => new TaskCompletionSource<bool>().Task,
            };
        }
    }
}

// The Phone remote contract's example files (docs/phone-remote/fixtures, README there): byte for byte what a phone
// gets, made by the server's own serializers and options (RemoteModel) from made-up data. The Android app pins each
// file's SHA-256 and decodes it (ContractFixturesTest), so the PC and the phone can't drift apart unnoticed.
// Nothing in them comes from this PC: no machine name, version, path, account, terminal text or clock.
// Session, Screen, Attention and the page's errors are built by the page (web/app.js); here a made-up object in the
// page's field order stands in for it and goes through the same C# steps as the page's (Summary, ScreenMessage, Event,
// RemoteReply.FromPage).
static class RemoteFixtures
{
    const string Pc = "DESKTOP-ARCADE", Version = "1.4.0", Projects = @"C:\Projects";
    const string River = Projects + @"\RiverRun", Garden = Projects + @"\StoneGarden", Maze = Projects + @"\OldMaze";
    const string FoamChat = "6f0c2b8e-5d1e-4f7a-9a43-2b1f7c9d0e11", StoneChat = "3b9d2f4a-8c1e-4e6b-a0d7-5f2c9e8b1a64";
    const string FoamQuestion = "Do you want to make this edit to WaterSurface.shader?";
    // A fixed run, the contract's example, so the files are the same each time.
    const string FixedRun = "9c4e1a7f03b2d865";

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
            var contract = HasContract11(files);
            var built = same && objects && local.Count == 0 && contract;
            var detail = files.Count + " files, the same bytes each time, " + (local.Count == 0 ? "nothing from this PC in them" : "this PC's name or folder in them")
                + (contract ? ", with the 1.1 fields" : ", WITHOUT the 1.1 fields (features, minClientApi, run, code)");
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

    // Contract 1.1 in the files: hello lists every feature with minClientApi 1, State, every Session and Attention carry the
    // fixed run, and every error body has a code (dialogOpen-typed also typed: true).
    static bool HasContract11(SortedDictionary<string, byte[]> files)
    {
        JsonElement Read(string name)
        {
            using var json = JsonDocument.Parse(files[name]);
            return json.RootElement.Clone();
        }
        var hello = Read("hello.json");
        var state = Read("state.json");
        return hello.GetProperty("minClientApi").GetInt32() == 1
            && hello.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()).Order()
                .SequenceEqual(new[] { "clientHeader", "errorCodes", "expect", "pairApproval", "run" })
            && Json.Str(state, "run") == FixedRun && state.GetProperty("sessions").EnumerateArray().All(session => Json.Str(session, "run") == FixedRun)
            && Json.Str(Read("session.json").GetProperty("session"), "run") == FixedRun
            && Json.Str(Read("attention.json").GetProperty("attention"), "run") == FixedRun
            // error-<status>-<code>[-variant].json: the code is in the name and fits the status.
            && files.Keys.Where(name => name.StartsWith("error-", StringComparison.Ordinal)).All(name =>
            {
                var parts = name[..^".json".Length].Split('-');
                var body = Read(name);
                return parts.Length >= 3 && int.TryParse(parts[1], out var status) && Json.Str(body, "code") == parts[2]
                    && RemoteModel.IsCode(status, parts[2]) && Json.Str(body, "error").Length > 0;
            })
            && Read("error-409-dialogOpen-typed.json").GetProperty("typed").GetBoolean();
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
        var state = RemoteModel.State(Pc, Version, FixedRun, River, Projects, workspaces, agents, tools, accounts, infos,
            [RemoteModel.Summary(Page(Asking()), FixedRun), RemoteModel.Summary(Page(Ended()), FixedRun)],
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
            ["session.json"] = RemoteModel.Event(new { type = "session", session = RemoteModel.Summary(Page(Working()), FixedRun) }),
            ["screen.json"] = RemoteModel.ScreenMessage(Page(Screen()), 6),
            ["attention.json"] = RemoteModel.Event(new { type = "attention", attention = RemoteModel.Summary(Page(Attention()), FixedRun) }),
            // Error bodies; the status is in the file name. The page's refusals come through FromPage, as MainForm takes
            // them from its remoteReply; the server's are its own replies.
            ["error-409-dialogChanged.json"] = RemoteModel.ReplyJson(RemoteReply.FromPage(409, null, RemoteServer.DialogChanged, "dialogChanged")),
            ["error-409-dialogOpen.json"] = RemoteModel.ReplyJson(RemoteReply.FromPage(409, null, RemoteServer.DialogOpen, "dialogOpen")),
            ["error-409-dialogOpen-typed.json"] = RemoteModel.ReplyJson(RemoteReply.FromPage(409, Page(new { typed = true }), RemoteServer.DialogOpenTyped, "dialogOpen")),
            ["error-409-runChanged.json"] = RemoteModel.ReplyJson(RemoteServer.RunChanged),
            ["error-403-denied.json"] = RemoteModel.ReplyJson(RemoteServer.PairDenied),
            ["error-403-notApproved.json"] = RemoteModel.ReplyJson(RemoteServer.PairNotApproved),
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
