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
        if (args.Any(arg => arg == "--self-check"))
            return SelfCheck.Run();

        // One window only: starting the app again brings the open window to the front.
        using var single = new Mutex(true, AppSettings.InstanceName, out var first);
        if (!first)
        {
            ShowExistingWindow();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    static void ShowExistingWindow()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var path = Environment.ProcessPath;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.Id == current.Id || process.MainWindowHandle == IntPtr.Zero) continue;
                    if (!string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsIconic(process.MainWindowHandle)) ShowWindow(process.MainWindowHandle, 9);
                    SetForegroundWindow(process.MainWindowHandle);
                    return;
                }
                catch
                {
                    // A process that exits or hides its path is skipped.
                }
            }
        }
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
    public static int Run()
    {
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
        Check("windows-terminal", Locator.FindOnPath("wt") != null, Locator.FindOnPath("wt") ?? "missing, agents open in a console window");

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

        RemoteSelfCheck.Run(Check);

        var editor = Locator.FindUnityEditor(settings.ProjectPath);
        var running = Locator.UnityEditorIsRunning();
        lines.Add("INFO unity — " + (editor ?? "editor path unknown") + (running ? " (running)" : ""));

        var output = Path.Combine(Path.GetTempPath(), "AgentArcade-selfcheck.txt");
        lines.Add(ok ? "SELF_CHECK_OK" : "SELF_CHECK_FAIL");
        File.WriteAllText(output, string.Join(Environment.NewLine, lines));
        try
        {
            AttachConsole(0xFFFFFFFF);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            foreach (var line in lines) Console.WriteLine(line);
        }
        catch
        {
            // The result file remains readable when this window has no console.
        }
        return ok ? 0 : 1;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(uint dwProcessId);
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

        // Only that nothing throws: whether this PC is on a public network right now is up to Windows.
        var warning = RemoteLink.PublicNetworkWarning(RemoteLink.Hosts(47821, false));
        check("remote-network", true, warning ?? "no address of the pairing link is on a public network");

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
