using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AgentArcade;

// The window is a local web page (web/index.html) in WebView2. This class runs the agents in
// pseudo consoles and passes their output to the page, which draws the terminals with xterm.js.
sealed class MainForm : Form
{
    const string AppName = "Agent Arcade";
    const string Host = "arcade.local";

    sealed class Session
    {
        public required PtySession Pty { get; init; }
        public required string AccountKey { get; init; }
        public required string Mode { get; init; }
        public required string Workspace { get; init; }
        // The history entry of an agent chat; null for sign-in and install panes.
        public ChatRecord? Chat { get; init; }
        public StringBuilder Pending { get; } = new();
    }

    readonly AppSettings _settings = AppSettings.Load();
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(12, 12, 14) };
    readonly Dictionary<string, Session> _sessions = new();
    readonly List<ChatRecord> _history = HistoryStore.Load();
    readonly System.Windows.Forms.Timer _flush = new() { Interval = 16 };
    List<AgentDef> _agents = new();
    List<AgentAccount> _accounts = new();
    Dictionary<AgentDef, ToolStatus>? _tools;
    Dictionary<string, AccountInfo>? _infos;
    int _refreshId;

    public MainForm()
    {
        Text = AppName;
        // The exe carries app.ico (ApplicationIcon); the title bar and taskbar use the same one.
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 620);
        Size = new Size(1500, 900);
        BackColor = Color.FromArgb(12, 12, 14);
        Controls.Add(_web);
        _flush.Tick += (_, _) => Flush();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Dark title bar that matches the page (Windows 11; ignored elsewhere).
        var on = 1;
        DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
        var caption = 0x000E0C0C;
        DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppSettings.Folder, "webview"));
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Agent Arcade needs the Microsoft Edge WebView2 Runtime, which is part of Windows 11.\n\n" +
                "Install it from https://developer.microsoft.com/microsoft-edge/webview2 and start Agent Arcade again.\n\n" + ex.Message,
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("AGENT_ARCADE_DEVTOOLS") == "1";
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "web"), CoreWebView2HostResourceAccessKind.DenyCors);
        // The page never leaves the app; links open in the normal browser.
        core.NavigationStarting += (_, args) =>
        {
            if (args.Uri.StartsWith("https://" + Host + "/", StringComparison.OrdinalIgnoreCase)) return;
            args.Cancel = true;
            OpenUrl(args.Uri);
        };
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            OpenUrl(args.Uri);
        };
        // Keep only editing items in the right-click menu. Its Refresh would reload the page, which
        // stops every running agent.
        core.ContextMenuRequested += (_, args) =>
        {
            try
            {
                // Remove by position: each item read from the list is a new wrapper, so Remove(item) finds nothing.
                var items = args.MenuItems;
                for (var i = items.Count - 1; i >= 0; i--)
                {
                    if (items[i].Name is not ("copy" or "cut" or "paste" or "pasteAndMatchStyle" or "selectAll" or "undo" or "redo" or "emoji"))
                        items.RemoveAt(i);
                }
                if (items.Count == 0) args.Handled = true;
            }
            catch (Exception ex)
            {
                Log("menu filter failed: " + ex);
                args.Handled = true;
            }
        };
        core.WebMessageReceived += OnMessage;
        _flush.Start();
        core.Navigate("https://" + Host + "/index.html");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_sessions.Count > 0 && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = MessageBox.Show(this,
                _sessions.Count + (_sessions.Count == 1 ? " agent is" : " agents are") + " still running. Close Agent Arcade and stop them?",
                AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
            {
                e.Cancel = true;
                return;
            }
        }
        _flush.Stop();
        StopAll();
        base.OnFormClosing(e);
    }

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string type = "";
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var m = doc.RootElement;
            type = Json.Str(m, "type");
            var id = Json.Str(m, "session");
            switch (type)
            {
                case "ready":
                    // A fresh page has no terminals, so nothing from before may keep running unseen.
                    StopAll();
                    Run(RefreshAsync, "refresh");
                    break;
                case "refresh":
                    Run(RefreshAsync, "refresh");
                    break;
                case "start":
                    StartSession(id, Json.Str(m, "account"), Json.Str(m, "mode"), Json.Str(m, "workspace"), Json.Str(m, "chat"), Int(m, "cols"), Int(m, "rows"));
                    break;
                case "renameChat":
                    RenameChat(Json.Str(m, "chat"), Json.Str(m, "title"), m.TryGetProperty("named", out var named) && named.ValueKind == JsonValueKind.True);
                    break;
                case "removeChat":
                    RemoveChat(Json.Str(m, "chat"));
                    break;
                case "restoreWorkspace":
                    RestoreWorkspace(Json.Str(m, "path"));
                    break;
                case "input":
                    if (_sessions.TryGetValue(id, out var target)) target.Pty.Write(Json.Str(m, "data"));
                    break;
                case "resize":
                    if (_sessions.TryGetValue(id, out var sized)) sized.Pty.Resize(Int(m, "cols"), Int(m, "rows"));
                    break;
                case "close":
                    Stop(id);
                    break;
                case "external":
                    OpenExternal(Json.Str(m, "account"));
                    break;
                case "addAccount":
                    var agentName = Json.Str(m, "agent");
                    var accountName = Json.Str(m, "name");
                    Run(() => AddAccountAsync(agentName, accountName), "add account");
                    break;
                case "removeAccount":
                    var removeKey = Json.Str(m, "account");
                    Run(() => RemoveAccountAsync(removeKey), "remove account");
                    break;
                case "addWorkspace":
                    AddWorkspace();
                    break;
                case "selectWorkspace":
                    SelectWorkspace(Json.Str(m, "path"));
                    break;
                case "removeWorkspace":
                    RemoveWorkspace(Json.Str(m, "path"));
                    break;
                case "openFolder":
                    OpenFolder();
                    break;
                case "openUnity":
                    OpenUnity();
                    break;
                case "syncInstructions":
                    SyncInstructions();
                    break;
                case "copy":
                    var text = Json.Str(m, "text");
                    if (text.Length > 0) Clipboard.SetText(text);
                    break;
                case "openUrl":
                    OpenUrl(Json.Str(m, "url"));
                    break;
                case "editAgents":
                    AgentStore.Load();
                    Commands.StartVisible(Path.Combine(Environment.SystemDirectory, "notepad.exe"), "\"" + AgentStore.FilePath + "\"", AppSettings.Folder);
                    Toast("Opened agents.json. Save it, then click refresh. Delete the file to get the default list back.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log(type + " failed: " + ex.Message);
            Toast(TextGuard.Redact(ex.Message), "error");
        }
    }

    // ---------- agent sessions ----------

    void StartSession(string id, string accountKey, string mode, string workspace, string chatId, int cols, int rows)
    {
        if (id.Length == 0 || _sessions.ContainsKey(id)) return;
        try
        {
            ChatRecord? chat = null;
            if (mode == "resume")
            {
                chat = _history.FirstOrDefault(item => item.Id == chatId) ?? throw new InvalidOperationException("That chat is no longer in the history.");
                if (_sessions.Values.Any(item => item.Chat == chat)) throw new InvalidOperationException("That chat is already open.");
                accountKey = chat.AccountKey;
                workspace = chat.Workspace;
            }
            var (agent, account) = FindAccount(accountKey) ?? throw new InvalidOperationException("That account no longer exists. Click refresh.");
            string commandLine;
            string directory;
            Dictionary<string, string?> env;
            if (mode == "install")
            {
                if (string.IsNullOrWhiteSpace(agent.Install)) throw new InvalidOperationException("No install command is set for " + agent.Name + ".");
                commandLine = Cmd() + " /d /s /c \"" + agent.Install + "\"";
                directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                env = new();
            }
            else
            {
                if (!Directory.Exists(workspace)) throw new InvalidOperationException("The project folder doesn't exist: " + workspace);
                var exe = Locator.FindAgent(agent) ?? throw new InvalidOperationException(agent.Name + " isn't installed yet. Use Install / update first.");
                string args;
                if (mode == "login") args = agent.Login.Length > 0 ? agent.Login : agent.Args;
                else if (chat != null) args = ChatArgs.Resume(agent, chat) ?? throw new InvalidOperationException(agent.Name + " can't continue an earlier chat. Start a new one.");
                else
                {
                    var now = DateTimeOffset.Now;
                    chat = new ChatRecord
                    {
                        Id = Guid.NewGuid().ToString(), Agent = agent.Name, Account = account.Name, Workspace = workspace,
                        Started = now, LastActive = now,
                    };
                    args = ChatArgs.New(agent, chat);
                }
                commandLine = CommandLine(exe, args, workspace);
                directory = workspace;
                env = AccountStore.EnvFor(agent, account);
            }
            env["COLORTERM"] = "truecolor";

            var pty = PtySession.Start(commandLine, directory, env, cols, rows);
            var session = new Session { Pty = pty, AccountKey = accountKey, Mode = mode, Workspace = workspace, Chat = chat };
            _sessions[id] = session;
            if (chat != null)
            {
                if (!_history.Contains(chat)) _history.Add(chat);
                chat.LastActive = DateTimeOffset.Now;
                chat.Ended = null;
                SaveHistory();
                Post(new { type = "started", session = id, chat = chat.Id, title = chat.Title, named = chat.Named });
            }
            pty.Output += text =>
            {
                lock (session.Pending) session.Pending.Append(text);
            };
            pty.Exited += code => OnUi(() => SessionExited(id, session, code));
            pty.Begin();
            Log("Started " + mode + " for " + accountKey + " in " + directory);
        }
        catch (Exception ex)
        {
            Post(new { type = "exit", session = id, code = -1, error = TextGuard.Redact(ex.Message) });
        }
    }

    void SessionExited(string id, Session session, int code)
    {
        if (!_sessions.TryGetValue(id, out var current) || current != session) return;
        FlushOne(id, session);
        _sessions.Remove(id);
        session.Pty.Dispose();
        EndChat(session);
        Post(new { type = "exit", session = id, code });
        // After signing in or installing, show the new email or version straight away.
        if (session.Mode is "login" or "install") Run(RefreshAsync, "refresh");
    }

    void Stop(string id)
    {
        if (!_sessions.Remove(id, out var session)) return;
        session.Pty.Dispose();
        EndChat(session);
    }

    void StopAll()
    {
        var stopped = _sessions.Values.ToList();
        _sessions.Clear();
        foreach (var session in stopped)
        {
            session.Pty.Dispose();
            EndChat(session);
        }
    }

    // ---------- chat history ----------

    void EndChat(Session session)
    {
        if (session.Chat == null) return;
        session.Chat.Ended = session.Chat.LastActive = DateTimeOffset.Now;
        SaveHistory();
    }

    void RenameChat(string chatId, string title, bool named)
    {
        var chat = _history.FirstOrDefault(item => item.Id == chatId);
        if (chat == null) return;
        title = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (title.Length > 120) title = title[..120];
        if (chat.Title == title && chat.Named == (named && title.Length > 0)) return;
        chat.Title = title;
        chat.Named = named && title.Length > 0;
        SaveHistory();
    }

    void RemoveChat(string chatId)
    {
        var chat = _history.FirstOrDefault(item => item.Id == chatId);
        if (chat == null) return;
        if (_sessions.Values.Any(item => item.Chat == chat)) throw new InvalidOperationException("Close the pane with this chat first.");
        _history.Remove(chat);
        SaveHistory();
    }

    void SaveHistory()
    {
        try
        {
            HistoryStore.Save(_history);
        }
        catch (Exception ex)
        {
            Log("history save failed: " + ex.Message);
        }
        PostHistory();
    }

    void PostHistory()
    {
        Post(new
        {
            type = "history",
            chats = _history.OrderByDescending(chat => chat.LastActive).Select(chat => new
            {
                id = chat.Id,
                agent = chat.Agent,
                account = chat.AccountKey,
                accountName = chat.Account,
                workspace = chat.Workspace,
                title = chat.Title,
                named = chat.Named,
                started = chat.Started,
                ended = chat.Ended,
                open = _sessions.Values.Any(item => item.Chat == chat),
                resume = ChatArgs.Kind(_agents.FirstOrDefault(agent => agent.Name == chat.Agent), chat),
            }),
        });
    }

    void Flush()
    {
        if (_web.CoreWebView2 == null) return;
        foreach (var (id, session) in _sessions) FlushOne(id, session);
    }

    void FlushOne(string id, Session session)
    {
        string text;
        lock (session.Pending)
        {
            if (session.Pending.Length == 0) return;
            text = session.Pending.ToString();
            session.Pending.Clear();
        }
        Post(new { type = "output", session = id, data = text });
    }

    static string Cmd() => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    static string CommandLine(string exe, string args, string directory)
    {
        var line = "\"" + exe + "\"" + (string.IsNullOrWhiteSpace(args) ? "" : " " + args);
        var extension = Path.GetExtension(exe);
        // npm installs agents as .cmd files, which need cmd.exe to run. pushd, because cmd can't
        // start in a network folder (\\server\share) and would fall back to C:\Windows.
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            return Cmd() + " /d /s /c \"pushd \"" + directory + "\" && " + line + "\"";
        return line;
    }

    void OpenExternal(string accountKey)
    {
        var (agent, account) = FindAccount(accountKey) ?? throw new InvalidOperationException("That account no longer exists.");
        var exe = Locator.FindAgent(agent) ?? throw new InvalidOperationException(agent.Name + " isn't installed yet.");
        if (!Directory.Exists(_settings.ProjectPath)) throw new InvalidOperationException("The project folder doesn't exist.");
        Terminal.Start(agent.Name + " (" + account.Name + ")", _settings.ProjectPath, exe, agent.Args, AccountStore.EnvFor(agent, account));
        Toast(agent.Name + " (" + account.Name + ") opened in Windows Terminal.");
    }

    // ---------- accounts ----------

    async Task RefreshAsync()
    {
        var id = ++_refreshId;
        Locator.RefreshPath();
        _agents = AgentStore.Load();
        _accounts = AccountStore.All(_agents);
        _tools = null;
        _infos = null;
        PostState();
        PostHistory();

        var tools = (await Task.WhenAll(_agents.Select(Probe.ToolAsync))).ToDictionary(tool => tool.Agent);
        if (id != _refreshId) return;
        _tools = tools;
        PostState();

        var accounts = _accounts;
        var infos = await Task.WhenAll(accounts.Select(async account =>
        {
            var agent = _agents.First(item => item.Name == account.Agent);
            return (Key: Key(account), Info: await Probe.AccountAsync(agent, account, tools[agent].Path));
        }));
        if (id != _refreshId) return;
        _infos = infos.ToDictionary(item => item.Key, item => item.Info);
        PostState();
    }

    async Task AddAccountAsync(string agentName, string name)
    {
        var agent = _agents.FirstOrDefault(item => item.Name == agentName) ?? throw new InvalidOperationException("Unknown agent.");
        var added = AccountStore.Add(agent, name.Trim());
        Log("Added account " + Key(added) + " at " + added.Home);
        await RefreshAsync();
        Post(new { type = "accountAdded", account = Key(added) });
    }

    async Task RemoveAccountAsync(string accountKey)
    {
        var (_, account) = FindAccount(accountKey) ?? throw new InvalidOperationException("That account no longer exists.");
        if (account.IsMain) throw new InvalidOperationException("The main account can't be removed.");
        foreach (var id in _sessions.Where(item => item.Value.AccountKey == accountKey).Select(item => item.Key).ToList())
        {
            Stop(id);
            Post(new { type = "exit", session = id, code = -1, error = "The account was removed." });
        }
        // The agent needs a moment to let go of its files after it stops.
        await Task.Delay(800);
        try
        {
            AccountStore.Remove(account);
            Toast(accountKey.Replace("|", " · ") + " removed.");
        }
        catch (Exception ex)
        {
            Toast("Couldn't delete everything in " + account.Home + ": " + ex.Message, "error");
        }
        await RefreshAsync();
    }

    (AgentDef Agent, AgentAccount Account)? FindAccount(string key)
    {
        var account = _accounts.FirstOrDefault(item => Key(item) == key);
        var agent = account == null ? null : _agents.FirstOrDefault(item => item.Name == account.Agent);
        return account == null || agent == null ? null : (agent, account);
    }

    static string Key(AgentAccount account) => account.Agent + "|" + account.Name;

    // ---------- workspaces ----------

    void AddWorkspace()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Pick a project folder",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_settings.ProjectPath) ? Path.GetDirectoryName(_settings.ProjectPath) ?? "" : "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!_settings.Projects.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase)) _settings.Projects.Add(dialog.SelectedPath);
        _settings.ProjectPath = dialog.SelectedPath;
        _settings.Save();
        PostState();
    }

    // Puts a project from the history back in the sidebar and opens it.
    void RestoreWorkspace(string path)
    {
        if (path.Length == 0) return;
        if (!Directory.Exists(path)) throw new InvalidOperationException("The folder no longer exists: " + path);
        if (!_settings.Projects.Contains(path, StringComparer.OrdinalIgnoreCase)) _settings.Projects.Add(path);
        _settings.ProjectPath = _settings.Projects.First(item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        PostState();
    }

    void SelectWorkspace(string path)
    {
        if (!_settings.Projects.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        _settings.ProjectPath = path;
        _settings.Save();
        PostState();
    }

    void RemoveWorkspace(string path)
    {
        if (_settings.Projects.Count <= 1) throw new InvalidOperationException("Keep at least one workspace. Add another one first.");
        foreach (var id in _sessions.Where(item => item.Value.Workspace.Equals(path, StringComparison.OrdinalIgnoreCase)).Select(item => item.Key).ToList())
        {
            Stop(id);
            Post(new { type = "exit", session = id, code = -1, error = "The workspace was removed." });
        }
        _settings.Projects.RemoveAll(item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (_settings.ProjectPath.Equals(path, StringComparison.OrdinalIgnoreCase)) _settings.ProjectPath = _settings.Projects[0];
        _settings.Save();
        PostState();
    }

    void OpenFolder()
    {
        if (!Directory.Exists(_settings.ProjectPath)) throw new InvalidOperationException("The project folder doesn't exist.");
        Commands.StartVisible(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + _settings.ProjectPath + "\"", _settings.ProjectPath);
    }

    void OpenUnity()
    {
        if (Locator.UnityEditorIsRunning())
        {
            Toast("Unity is already running.");
            return;
        }
        var editor = Locator.FindUnityEditor(_settings.ProjectPath)
            ?? throw new InvalidOperationException("The Unity editor for this project wasn't found in Unity Hub. Open it from Unity Hub instead.");
        Process.Start(new ProcessStartInfo
        {
            FileName = editor,
            ArgumentList = { "-projectPath", _settings.ProjectPath },
            UseShellExecute = true,
        })?.Dispose();
        Toast("Opening Unity…");
    }

    void SyncInstructions()
    {
        if (!Directory.Exists(_settings.ProjectPath)) throw new InvalidOperationException("The project folder doesn't exist.");
        var created = SharedInstructions.Ensure(_settings.ProjectPath);
        Toast(created.Count == 0
            ? "Every agent already reads the same instructions. Nothing changed."
            : "Created " + string.Join(", ", created) + ". Every agent now reads the same instructions.");
    }

    // ---------- page messages ----------

    void PostState()
    {
        Post(new
        {
            type = "state",
            workspace = _settings.ProjectPath,
            workspaces = _settings.Projects.Select(path => new
            {
                path,
                name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } folder ? folder : path,
                exists = Directory.Exists(path),
                unity = File.Exists(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt")),
            }),
            agents = _agents.Select(agent =>
            {
                ToolStatus? tool = null;
                _tools?.TryGetValue(agent, out tool);
                return new
                {
                    name = agent.Name,
                    command = agent.Command,
                    checking = _tools == null,
                    installed = tool?.Path != null,
                    version = tool?.Version ?? "",
                    multi = agent.HomeVar.Length > 0,
                    loginHint = agent.LoginHint,
                    install = agent.Install,
                };
            }),
            accounts = _accounts.Select(account =>
            {
                AccountInfo? info = null;
                _infos?.TryGetValue(Key(account), out info);
                return new
                {
                    key = Key(account),
                    agent = account.Agent,
                    name = account.Name,
                    main = account.IsMain,
                    checking = _infos == null,
                    signedIn = info?.SignedIn,
                    email = info?.Email ?? "",
                    plan = info?.Plan ?? "",
                    note = info?.Note ?? "",
                };
            }),
        });
    }

    void Toast(string text, string kind = "info") => Post(new { type = "toast", text, kind });

    void Post(object message)
    {
        try
        {
            _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));
        }
        catch (Exception ex)
        {
            Log("post failed: " + ex.Message);
        }
    }

    // Runs a message handler that awaits something and reports its errors like the others.
    async void Run(Func<Task> work, string what)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log(what + " failed: " + ex.Message);
            Toast(TextGuard.Redact(ex.Message), "error");
        }
    }

    void OnUi(Action action)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
        }
    }

    static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;
        Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true })?.Dispose();
    }

    static int Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(Path.Combine(AppSettings.Folder, "log.txt"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + TextGuard.Redact(message).ReplaceLineEndings(" ") + Environment.NewLine);
        }
        catch
        {
            // Logging is best effort.
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
