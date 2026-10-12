namespace AgentArcade;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Any(arg => arg == "--self-check"))
            return SelfCheck.Run();

        // One window only: starting the app again brings the open window to the front.
        using var single = new Mutex(true, @"Local\AgentArcade", out var first);
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
        Check("web-page", File.Exists(Path.Combine(web, "index.html")) && File.Exists(Path.Combine(web, "vendor", "xterm.js")), web);

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
