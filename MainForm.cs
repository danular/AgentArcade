using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AgentArcade;

// The window is a local web page (web/index.html) in WebView2. This class runs the agents in
// pseudo consoles and passes their output to the page, which draws the terminals with xterm.js.
sealed class MainForm : Form, IRemoteHost
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
        // Folders where new images belong to this pane: the project, and the agent's own image folder.
        public List<string> ImageFolders { get; } = new();
        public DateTime LastInput { get; set; } = DateTime.Now;
    }

    // A phone's request that waits for the page's remoteReply. Received runs on the UI thread with the reply's body.
    sealed record PageRequest(TaskCompletionSource<RemoteReply> Done, Action<JsonElement>? Received);

    readonly AppSettings _settings = AppSettings.Load();
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(12, 12, 14) };
    readonly Dictionary<string, Session> _sessions = new();
    readonly List<ChatRecord> _history = HistoryStore.Load();
    readonly System.Windows.Forms.Timer _flush = new() { Interval = 16 };
    readonly ImageServer _images = new();
    readonly Dictionary<string, ImageWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    NotifyIcon? _tray;
    string _notified = "";
    List<AgentDef> _agents = new();
    List<AgentAccount> _accounts = new();
    Dictionary<AgentDef, ToolStatus>? _tools;
    // The newest probe results by agent name. They stay while the tools are checked again, so the phones don't see
    // every agent as not installed for those seconds.
    readonly Dictionary<string, ToolStatus> _lastTools = new();
    Dictionary<string, AccountInfo>? _infos;
    int _refreshId;
    // Phone remote. The page builds each pane's summary and screen; this side passes them on to the phones.
    readonly RemoteServer _remote;
    readonly OrderedDictionary<string, JsonElement> _summaries = new();
    readonly ConcurrentDictionary<string, PageRequest> _pageRequests = new();
    volatile bool _pageReady;
    bool _remoteOn;
    bool _remoteClosed;
    (string Link, string Qr)? _qr;
    (long At, string? Hosts, string? Warning) _networkWarning;
    readonly KeepAwake _awake = new();
    bool _onBattery = KeepAwake.Status().OnBattery;

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
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _remote = new RemoteServer(this, Path.Combine(AppSettings.Folder, "remote.json"));
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
            // A minimized or covered window must keep its timers: the page parses agent output, spots
            // finished agents and types for the phone while nobody looks at it.
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows",
                // Crash dumps stay on this PC (the environment's FailureReportFolderPath) instead of going to Microsoft.
                IsCustomCrashReportingEnabled = true,
            };
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppSettings.Folder, "webview"), options);
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
        // No SmartScreen lookups: the addresses of the pages shown here (the app's own page, previews of local servers
        // and project files) aren't sent to Microsoft. A runtime too old for the setting just keeps its default.
        try
        {
            core.Settings.IsReputationCheckingRequired = false;
        }
        catch (NotImplementedException)
        {
            Log("this WebView2 runtime can't switch off SmartScreen lookups");
        }
        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "web"), CoreWebView2HostResourceAccessKind.DenyCors);
        _images.Attach(core);
        _preview.Attach(core);
        // The page never leaves the app; links open in the normal browser.
        core.NavigationStarting += (_, args) =>
        {
            if (args.Uri.StartsWith("https://" + Host + "/", StringComparison.OrdinalIgnoreCase)) return;
            args.Cancel = true;
            OpenUrl(args.Uri);
        };
        // Only a window you asked for by clicking opens; a previewed page can't flood the browser with popups.
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (args.IsUserInitiated) OpenUrl(args.Uri);
        };
        // The side panel's preview frame may navigate itself, but never to the app's own hosts: there it would share
        // the app page's origin and could reach into it, and through it start or type to agents.
        core.FrameNavigationStarting += (_, args) =>
        {
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
                && (uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase)))
                args.Cancel = true;
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
        // Closing is decided: the window goes now, not after the agents are stopped. A usable window here would look
        // like an answered Cancel to install-and-run.ps1, which waits for the app to close.
        Hide();
        _flush.Stop();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        StopAll();
        _tray?.Dispose();
        _tray = null;
        _pageReady = false;
        FailPageRequests();
        // A phone removal that couldn't be saved yet gets one more try, so the phone stays removed after a restart.
        _remote.Devices.Flush();
        if (_remote.Running && !_remoteClosed && e.CloseReason is not (CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing))
        {
            // The phones hear that Agent Arcade closed (1001) before the window goes.
            _remoteClosed = true;
            e.Cancel = true;
            Hide();
            Run(async () =>
            {
                try
                {
                    await _remote.ApplyAsync(false, 0, false);
                }
                finally
                {
                    Close();
                }
            }, "phone remote");
            return;
        }
        _ = _remote.ApplyAsync(false, 0, false);
        base.OnFormClosing(e);
    }

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Only the app's own page talks to this side; a page previewed in the side panel never does.
        if (!e.Source.StartsWith("https://" + Host + "/", StringComparison.OrdinalIgnoreCase)) return;
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
                    RemotePageReady();
                    Run(RefreshAsync, "refresh");
                    break;
                case "refresh":
                    Run(RefreshAsync, "refresh");
                    break;
                case "start":
                    // A pane the page opens after the window began closing would start an agent nobody can see.
                    if (!_remoteClosed) StartSession(id, Json.Str(m, "account"), Json.Str(m, "mode"), Json.Str(m, "workspace"), Json.Str(m, "chat"), Int(m, "cols"), Int(m, "rows"));
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
                    if (_sessions.TryGetValue(id, out var target))
                    {
                        target.Pty.Write(Json.Str(m, "data"));
                        target.LastInput = DateTime.Now;
                    }
                    break;
                case "dropFiles":
                    // Files dropped on a pane arrive as WebView2 file objects, which know their path on disk.
                    var paths = new List<string>();
                    foreach (var item in e.AdditionalObjects ?? Enumerable.Empty<object>())
                        if (item is CoreWebView2File file && !string.IsNullOrEmpty(file.Path)) paths.Add(file.Path);
                    Post(new { type = "dropped", session = id, paths });
                    break;
                case "saveImage":
                    SaveDroppedImage(id, Json.Str(m, "name"), Json.Str(m, "data"));
                    break;
                case "openImage":
                    OpenImage(id, Json.Str(m, "workspace"), Json.Str(m, "path"));
                    break;
                case "notify":
                    Notify(id, Json.Str(m, "title"), Json.Str(m, "body"), m.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.True);
                    break;
                case "revealImage":
                    RevealImage(Json.Str(m, "url"));
                    break;
                case "listImages":
                    ListImages(Json.Str(m, "workspace"), Int(m, "seq"));
                    break;
                case "listDocs":
                    ListDocs(Json.Str(m, "workspace"));
                    break;
                case "previewFile":
                    PreviewFile(Json.Str(m, "workspace"), Json.Str(m, "path"));
                    break;
                case "gitChanges":
                    GitStatus(Json.Str(m, "workspace"), Int(m, "seq"));
                    break;
                case "gitDiff":
                    GitDiff(Json.Str(m, "workspace"), Json.Str(m, "path"), Json.Str(m, "from"),
                        m.TryGetProperty("untracked", out var untracked) && untracked.ValueKind == JsonValueKind.True);
                    break;
                case "gitTrust":
                    TrustGit(Json.Str(m, "workspace"), Json.Str(m, "config"));
                    break;
                case "consoleStart":
                    // Like a pane: nothing new starts once the window began closing.
                    if (!_remoteClosed) StartConsole(Json.Str(m, "workspace"), Int(m, "cols"), Int(m, "rows"), Int(m, "id"));
                    break;
                case "consoleInput":
                    _console?.Write(Json.Str(m, "data"));
                    break;
                case "consoleResize":
                    if (Int(m, "cols") > 0 && Int(m, "rows") > 0) _console?.Resize(Int(m, "cols"), Int(m, "rows"));
                    break;
                case "consoleStop":
                    StopConsole();
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
                case "newProjectCheck":
                    var checkName = Json.Str(m, "name");
                    var refusal = NewProject.Refusal(NewProject.Folder(_settings.ProjectPath, _settings.Projects), checkName.Trim());
                    Post(new { type = "newProjectProblem", name = checkName, problem = refusal?.Problem });
                    break;
                case "newProject":
                    NewProjectFromPage(Json.Str(m, "name"));
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
                case "remoteSessions":
                    RemoteSessions(m);
                    break;
                case "remoteScreen":
                    if (m.TryGetProperty("screen", out var screen) && screen.ValueKind == JsonValueKind.Object) _remote.Screen(screen);
                    break;
                case "remoteAttention":
                    if (m.TryGetProperty("attention", out var attention) && attention.ValueKind == JsonValueKind.Object)
                        _remote.Send(new { type = "attention", attention = RemoteModel.Redacted(attention) });
                    break;
                case "remoteReply":
                    RemoteReplied(m);
                    break;
                case "remoteDialog":
                    if (_settings.RemoteEnabled && _remote.Pairing.Info().Status != "valid") _remote.NewCode();
                    // Opening the dialog tries again after a start that failed, for example on a port in use, and
                    // after a removal that couldn't be saved.
                    if (_settings.RemoteEnabled && !_remote.Running) ApplyRemote();
                    _remote.Devices.Flush();
                    PostRemoteInfo();
                    break;
                case "remoteEnable":
                    EnableRemote(m.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True);
                    break;
                case "remoteNewCode":
                    if (_settings.RemoteEnabled) _remote.NewCode();
                    PostRemoteInfo();
                    break;
                case "remoteRemove":
                    RemovePhone(Json.Str(m, "device"));
                    break;
                case "keepAwake":
                    var mode = Json.Str(m, "mode");
                    if (mode is KeepAwake.Agents or KeepAwake.Remote or KeepAwake.Off)
                    {
                        _settings.KeepAwake = mode;
                        _settings.Save();
                        UpdateAwake();
                    }
                    PostRemoteInfo();
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
            SessionEnv(env);

            var pty = PtySession.Start(commandLine, directory, env, cols, rows);
            var session = new Session { Pty = pty, AccountKey = accountKey, Mode = mode, Workspace = workspace, Chat = chat };
            _sessions[id] = session;
            UpdateAwake();
            if (chat != null) WatchImages(session, agent, account);
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
        DropUnusedWatchers();
        UpdateAwake();
        Post(new { type = "exit", session = id, code });
        // After signing in or installing, show the new email or version straight away.
        if (session.Mode is "login" or "install") Run(RefreshAsync, "refresh");
    }

    void Stop(string id)
    {
        if (!_sessions.Remove(id, out var session)) return;
        session.Pty.Dispose();
        EndChat(session);
        DropUnusedWatchers();
        UpdateAwake();
    }

    // Also stops the side panel's console: a fresh page has no terminal for it either.
    void StopAll()
    {
        var stopped = _sessions.Values.ToList();
        _sessions.Clear();
        foreach (var session in stopped)
        {
            session.Pty.Dispose();
            EndChat(session);
        }
        DropUnusedWatchers();
        StopConsole();
        UpdateAwake();
    }

    // ---------- notifications ----------

    // An agent is done or needs you. Windows only tells you when you can't see the pane already.
    void Notify(string id, string title, string body, bool paneVisible)
    {
        if (ActiveForm == this && WindowState != FormWindowState.Minimized && paneVisible) return;
        if (_tray == null)
        {
            // The tray icon is how a Windows Forms app shows a notification; it stays until the app closes.
            _tray = new NotifyIcon { Icon = Icon, Text = AppName, Visible = true };
            _tray.BalloonTipClicked += (_, _) => BringBack(_notified);
            _tray.MouseClick += (_, args) =>
            {
                if (args.Button == MouseButtons.Left) BringBack(_notified);
            };
        }
        _notified = id;
        title = TextGuard.Redact(title.Length > 0 ? title : AppName);
        body = TextGuard.Redact(body.Length > 0 ? body : "An agent needs you.");
        // Windows cuts balloon titles at 63 characters and text at 255.
        _tray.ShowBalloonTip(8000, title.Length > 63 ? title[..62] + "…" : title, body.Length > 255 ? body[..254] + "…" : body, ToolTipIcon.None);
        if (ActiveForm != this)
        {
            var flash = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = Handle, Flags = FlashTray | FlashUntilForeground };
            FlashWindowEx(ref flash);
        }
    }

    void BringBack(string id)
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
        if (id.Length > 0) Post(new { type = "focusSession", session = id });
    }

    // ---------- images ----------

    // New images in the project, or in the folder where the agent saves the images it generates, show
    // up on the pane. Several panes can share a folder; the one typed into last most likely made it.
    void WatchImages(Session session, AgentDef agent, AgentAccount account)
    {
        session.ImageFolders.Add(Path.GetFullPath(session.Workspace));
        if (!string.IsNullOrEmpty(agent.ImagesFolder))
        {
            var home = account.IsMain ? Environment.ExpandEnvironmentVariables(agent.DefaultHome ?? "") : account.Home;
            if (home.Length > 0 && Path.IsPathFullyQualified(home) && Directory.Exists(home))
            {
                var folder = Path.GetFullPath(Path.Combine(home, agent.ImagesFolder));
                try
                {
                    Directory.CreateDirectory(folder);
                    session.ImageFolders.Add(folder);
                }
                catch (Exception ex)
                {
                    Log("image folder unavailable: " + ex.Message);
                }
            }
        }
        foreach (var folder in session.ImageFolders)
        {
            if (_watchers.ContainsKey(folder)) continue;
            try
            {
                _watchers[folder] = new ImageWatcher(folder, path => OnUi(() => ImageAppeared(folder, path)));
            }
            catch (Exception ex)
            {
                Log("watching " + folder + " failed: " + ex.Message);
            }
        }
    }

    void DropUnusedWatchers()
    {
        foreach (var folder in _watchers.Keys.ToList())
        {
            if (_sessions.Values.Any(session => session.ImageFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))) continue;
            _watchers[folder].Dispose();
            _watchers.Remove(folder);
        }
    }

    void ImageAppeared(string folder, string path)
    {
        var owner = _sessions.Where(pair => pair.Value.ImageFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Value.LastInput)
            .Select(pair => pair.Key)
            .FirstOrDefault();
        if (owner != null) PostImage(owner, path, false);
    }

    // An image path in a pane was clicked. Relative paths are relative to the project the agent runs in.
    void OpenImage(string id, string workspace, string text)
    {
        var path = text.Trim().Trim('"', '\'', '`');
        if (path.StartsWith('~')) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
        if (_sessions.TryGetValue(id, out var session)) workspace = session.Workspace;
        // A network path (\\server\share) isn't touched: Windows would sign in there with your account. Pane text could
        // name any computer. Checked before and after the path is made whole; the path itself isn't shown or logged.
        const string network = "Agent Arcade doesn't open images from network paths (\\\\server\\share).";
        if (ProjectFiles.IsNetworkPath(path))
        {
            Toast(network, "error");
            return;
        }
        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                if (!Path.IsPathFullyQualified(workspace)) throw new IOException();
                path = Path.Combine(workspace, path.TrimStart('/', '\\'));
            }
            path = Path.GetFullPath(path);
        }
        catch
        {
            Toast("That isn't a file path: " + text, "error");
            return;
        }
        if (ProjectFiles.IsNetworkPath(path))
        {
            Toast(network, "error");
            return;
        }
        if (!ImageServer.IsImage(path) || !File.Exists(path))
        {
            Toast("Can't find " + path, "error");
            return;
        }
        PostImage(id, path, true);
    }

    void PostImage(string id, string path, bool open)
        => Post(new { type = "image", session = id, url = _images.Url(path), path, name = Path.GetFileName(path), open });

    // The side panel's Images tab: the newest images in one of the sidebar's projects, and in the image folders of
    // its agents that lie outside it (Codex's generated_images). Only a listed project and folders this side chose
    // are scanned. seq comes back, so the page keeps only the answer to its newest request.
    void ListImages(string workspace, int seq)
    {
        Run(async () =>
        {
            var folder = ListedProject(workspace);
            var folders = new List<string>();
            if (folder != null)
            {
                folders.Add(folder);
                folders.AddRange(_sessions.Values
                    .Where(session => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(session.Workspace)), folder, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(session => session.ImageFolders)
                    .Where(path => !ProjectFiles.IsInside(folder, path))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            }
            var (found, total) = folders.Count > 0 ? await Task.Run(() => ProjectFiles.Newest(folders, ImageServer.IsImage, 300)) : ([], 0);
            // time: an image written again under the same name gets a fresh thumbnail.
            Post(new
            {
                type = "imageList", workspace, seq, total,
                items = found.Select(file => new { url = _images.Url(file.Path), path = file.Path, name = Path.GetFileName(file.Path), time = file.Time.Ticks }).ToList(),
            });
        }, "list images");
    }

    // One of the sidebar's projects as a full path, if it still exists; null for any other folder the page names.
    string? ListedProject(string workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace)) return null;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        var listed = _settings.Projects.Any(path =>
            string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), full, StringComparison.OrdinalIgnoreCase));
        return listed && Directory.Exists(full) ? full : null;
    }

    // ---------- side panel ----------

    // The Preview tab shows project files from PreviewServer, one host per project. Those are other sites than the
    // app's page, and the page shows them in a sandboxed frame that can't navigate the app or message this side.
    readonly PreviewServer _preview = new();
    // The Console tab: a PowerShell in a project folder. It isn't an agent: it isn't in _sessions, the history or
    // the phone's list, and closing the window doesn't ask about it. Its number comes from the page and goes back
    // with its output, so the page drops what a console it already replaced still sent. Both under _consolePending's lock.
    PtySession? _console;
    int _consoleId;
    readonly StringBuilder _consolePending = new();

    // The web pages in a project, newest first, for the Preview tab's address suggestions.
    void ListDocs(string workspace)
    {
        Run(async () =>
        {
            var folder = ListedProject(workspace) ?? "";
            var (found, _) = folder.Length > 0 ? await Task.Run(() => ProjectFiles.Newest(folder, IsDocument, 200)) : ([], 0);
            Post(new { type = "docList", workspace, files = found.Select(file => Path.GetRelativePath(folder, file.Path).Replace('\\', '/')).ToList() });
        }, "list documents");
    }

    static bool IsDocument(string path) => Path.GetExtension(path).ToLowerInvariant() is ".html" or ".htm" or ".svg";

    // A file (or a folder's index.html) in a project, answered with the URL the Preview tab loads it from.
    void PreviewFile(string workspace, string path)
    {
        var folder = ListedProject(workspace) ?? throw new InvalidOperationException("Pick a project in the sidebar first.");
        var text = path.Trim().Trim('"', '\'');
        var full = Path.GetFullPath(Path.IsPathFullyQualified(text) ? text : Path.Combine(folder, text.TrimStart('/', '\\')));
        if (!ProjectFiles.IsInside(folder, full)) throw new InvalidOperationException("Only files in this project open in the preview.");
        if (Directory.Exists(full)) full = Path.Combine(full, "index.html");
        if (!File.Exists(full)) throw new InvalidOperationException("Can't find " + Path.GetRelativePath(folder, full) + " in this project.");
        var relative = Path.GetRelativePath(folder, full).Replace('\\', '/');
        Post(new { type = "previewUrl", workspace, path = relative, url = _preview.Url(folder, relative) });
    }

    // The Changes tab: what git sees changed in the project. Errors (no repository, no git) are shown in the tab.
    // seq comes back, so the page keeps only the answer to its newest request.
    void GitStatus(string workspace, int seq)
    {
        Run(async () =>
        {
            var folder = ListedProject(workspace);
            if (folder == null)
            {
                Post(new { type = "changes", workspace, seq, error = "Pick a project in the sidebar first." });
                return;
            }
            var trusted = TrustedGitConfig(folder);
            try
            {
                var (branch, files, total) = await Task.Run(() => GitChanges.Status(folder, trusted));
                Post(new { type = "changes", workspace, seq, branch, total, files = files.Select(file => new { path = file.Path, from = file.From, status = file.Status }).ToList() });
            }
            catch (GitTrustNeeded ex)
            {
                // The tab shows what the config reads and a "Run git here anyway" button, which answers with gitTrust.
                Post(new { type = "changes", workspace, seq, error = ex.Message, trust = new { config = ex.Check.Config, uses = ex.Check.Uses.Take(8).ToList(), more = Math.Max(0, ex.Check.Uses.Count - 8) } });
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Post(new { type = "changes", workspace, seq, error = TextGuard.Redact(ex.Message) });
            }
        }, "git status");
    }

    // Errors go to the tab too, not to a toast: the tab asks again every few seconds.
    void GitDiff(string workspace, string path, string from, bool untracked)
    {
        Run(async () =>
        {
            var folder = ListedProject(workspace) ?? throw new InvalidOperationException("Pick a project in the sidebar first.");
            if (!InProject(folder, path) || (from.Length > 0 && !InProject(folder, from))) throw new InvalidOperationException("That file isn't in this project.");
            var trusted = TrustedGitConfig(folder);
            try
            {
                var (text, cut) = await Task.Run(() => GitChanges.Diff(folder, path, from.Length > 0 ? from : null, untracked, trusted));
                Post(new { type = "diff", workspace, path, text, cut });
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Post(new { type = "diff", workspace, path, error = TextGuard.Redact(ex.Message) });
            }
        }, "git diff");
    }

    static bool InProject(string folder, string relative)
        => relative.Length > 0 && !Path.IsPathRooted(relative) && ProjectFiles.IsInside(folder, Path.Combine(folder, relative));

    // "Run git here anyway" in the Changes tab, for a repository whose config makes git read other files (include.path,
    // includeIf) or another folder (core.worktree). Kept per project with the hash of the config the tab showed, so a
    // changed config asks again. The page asks for the changes again right after this message.
    void TrustGit(string workspace, string config)
    {
        var folder = ListedProject(workspace);
        if (folder == null || !GitConfigCheck.IsHash(config)) return;
        var trusted = _settings.GitTrusted ??= new();
        trusted.RemoveAll(item => item.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
        trusted.Add(new GitTrust { Folder = folder, ConfigSha256 = config });
        _settings.Save();
    }

    string? TrustedGitConfig(string folder)
        => _settings.GitTrusted?.FirstOrDefault(item => item.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))?.ConfigSha256;

    void StartConsole(string workspace, int cols, int rows, int id)
    {
        StopConsole();
        try
        {
            var folder = ListedProject(workspace) ?? throw new InvalidOperationException("Pick a project in the sidebar first.");
            var pty = PtySession.Start("\"" + Shell() + "\" -NoLogo", folder, new Dictionary<string, string?> { ["COLORTERM"] = "truecolor" },
                Math.Max(cols, 20), Math.Max(rows, 5));
            lock (_consolePending)
            {
                _console = pty;
                _consoleId = id;
            }
            pty.Output += text =>
            {
                lock (_consolePending)
                {
                    // A console that was replaced may still have output on its way; it doesn't belong to the new one.
                    if (_console == pty) _consolePending.Append(text);
                }
            };
            pty.Exited += code => OnUi(() => ConsoleExited(pty, id, code));
            pty.Begin();
            Post(new { type = "consoleStarted", id, workspace = folder });
            Log("Started the console in " + folder);
        }
        catch (Exception ex)
        {
            Post(new { type = "consoleExit", id, code = -1, error = TextGuard.Redact(ex.Message) });
        }
    }

    // PowerShell 7 when it is installed, else the Windows PowerShell that comes with Windows.
    static string Shell()
        => Locator.FindOnPath("pwsh") is { } pwsh && pwsh.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? pwsh
            : Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    void ConsoleExited(PtySession pty, int id, int code)
    {
        lock (_consolePending)
        {
            if (_console != pty) return;
        }
        FlushConsole();
        lock (_consolePending) _console = null;
        pty.Dispose();
        Post(new { type = "consoleExit", id, code });
    }

    void StopConsole()
    {
        PtySession? pty;
        lock (_consolePending)
        {
            pty = _console;
            _console = null;
            _consolePending.Clear();
        }
        pty?.Dispose();
    }

    void FlushConsole()
    {
        string text;
        int id;
        lock (_consolePending)
        {
            if (_consolePending.Length == 0) return;
            text = _consolePending.ToString();
            id = _consoleId;
            _consolePending.Clear();
        }
        Post(new { type = "consoleOutput", id, data = text });
    }

    void RevealImage(string url)
    {
        var path = _images.PathOf(url);
        if (path == null || !File.Exists(path)) return;
        Commands.StartVisible(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            "/select,\"" + path + "\"", Path.GetDirectoryName(path)!);
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
        // A key typed into a pane or into Rename never lands in history.json.
        title = string.Join(' ', TextGuard.Redact(title).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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
        if (_remote.Phones > 0) _remote.Send(new { type = "history", chats = RemoteChats() });
    }

    void Flush()
    {
        if (_web.CoreWebView2 == null) return;
        foreach (var (id, session) in _sessions) FlushOne(id, session);
        FlushConsole();
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

    // The environment of every agent pane (install, sign-in and chats) on top of the app's own. cmd must not run a
    // program from the project folder when an npm shim asks for a bare "node" (Program.Main sets this for the whole
    // app too; here it holds even if the block is ever built from a copy).
    internal static void SessionEnv(Dictionary<string, string?> env)
    {
        env["COLORTERM"] = "truecolor";
        env["NoDefaultCurrentDirectoryInExePath"] = "1";
    }

    internal static string CommandLine(string exe, string args, string directory)
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
        _lastTools.Clear();
        foreach (var tool in tools.Values) _lastTools[tool.Agent.Name] = tool;
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

    static string Key(AgentAccount account) => account.Key;

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

    // New project, from the page or a phone: an empty folder next to the current workspace, listed in the sidebar.
    (string? Path, RemoteReply Reply) CreateProject(string name)
    {
        var made = NewProject.CreateAndList(NewProject.Folder(_settings.ProjectPath, _settings.Projects), name, _settings.Projects);
        if (made.Path != null) _settings.Save();
        return made;
    }

    // The page's New project: like + with a folder it makes, so the new workspace opens. The dialog stays open until
    // the answer comes back, and shows the problem when there is one.
    void NewProjectFromPage(string name)
    {
        var (path, reply) = CreateProject(name);
        if (path != null)
        {
            _settings.ProjectPath = path;
            _settings.Save();
            PostState();
        }
        Post(new { type = "newProjectResult", problem = reply.Error });
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

    // ---------- phone remote ----------

    static bool RemoteLoopback => Environment.GetEnvironmentVariable("AGENT_ARCADE_REMOTE_LOOPBACK") == "1";

    // Starts or stops the server to match the setting, then shows the result in the Phone dialog.
    void ApplyRemote()
    {
        // A RemotePort typed into settings.json while the app runs counts the next time the server starts.
        if (_settings.RemoteEnabled) _settings.RemotePort = AppSettings.StoredPort() ?? _settings.RemotePort;
        var work = _remote.ApplyAsync(_settings.RemoteEnabled, _settings.RemotePort, RemoteLoopback);
        Run(async () =>
        {
            await work;
            if (_remote.Running != _remoteOn)
            {
                _remoteOn = _remote.Running;
                Log(_remoteOn ? "Phone remote on, port " + _remote.Port : "Phone remote off");
            }
            UpdateAwake();
            PostRemoteInfo();
            PostRemoteStatus();
        }, "phone remote");
    }

    void EnableRemote(bool on)
    {
        _settings.RemoteEnabled = on;
        _settings.Save();
        // Turning it on makes a fresh code; turning it off ends the old one at once.
        if (on) _remote.NewCode();
        else _remote.Pairing.Clear();
        ApplyRemote();
        PostRemoteInfo();
        PostRemoteStatus();
    }

    void RemovePhone(string id)
    {
        var removed = _remote.RemoveDevice(id);
        if (removed != null) Log("Phone removed: " + removed.Name);
        PostRemoteInfo();
        PostRemoteStatus();
    }

    // A fresh page has no panes, and it doesn't know yet what the phones watch.
    void RemotePageReady()
    {
        _pageReady = true;
        FailPageRequests();
        foreach (var id in _summaries.Keys.ToList()) _remote.Closed(id);
        _summaries.Clear();
        _remote.ForgetScreens();
        if (_settings.RemoteEnabled && !_remote.Running) ApplyRemote();
        PostRemoteStatus();
        PostRemoteWatch(_remote.Watches().Select(watch => watch.Id).ToList());
    }

    // Summaries of the panes that changed, and the panes that were closed.
    void RemoteSessions(JsonElement m)
    {
        if (m.TryGetProperty("closed", out var closed) && closed.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in closed.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } id) continue;
                _summaries.Remove(id);
                _remote.Closed(id);
            }
        }
        if (m.TryGetProperty("sessions", out var sessions) && sessions.ValueKind == JsonValueKind.Array)
        {
            foreach (var session in sessions.EnumerateArray())
            {
                var id = Json.Str(session, "id");
                if (id.Length == 0) continue;
                var redacted = RemoteModel.Redacted(session.Clone());
                _summaries[id] = redacted;
                _remote.Send(new { type = "session", session = redacted });
            }
        }
    }

    // Every pane's summary, in pane order, from the page's answer to remoteStatus.
    void ReplaceSummaries(JsonElement body)
    {
        if (!body.TryGetProperty("sessions", out var sessions) || sessions.ValueKind != JsonValueKind.Array) return;
        _summaries.Clear();
        foreach (var session in sessions.EnumerateArray())
        {
            if (Json.Str(session, "id") is { Length: > 0 } id) _summaries[id] = RemoteModel.Redacted(session.Clone());
        }
    }

    void RemoteReplied(JsonElement m)
    {
        if (!_pageRequests.TryRemove(Json.Str(m, "requestId"), out var request)) return;
        var status = Int(m, "status");
        JsonElement? body = m.TryGetProperty("body", out var found) && found.ValueKind == JsonValueKind.Object ? found.Clone() : null;
        var error = Json.Str(m, "error");
        if (status is not (200 or 201 or 400 or 404 or 409 or 413 or 429 or 503)) (status, error) = (500, "Something went wrong on the PC.");
        if (status == 200 && body != null) request.Received?.Invoke(body.Value);
        request.Done.TrySetResult(new RemoteReply(status, body, error.Length > 0 ? error : null));
    }

    // Requests the page can't answer any more (it reloaded, or the window is closing) get 503 at once.
    void FailPageRequests()
    {
        foreach (var id in _pageRequests.Keys)
        {
            if (_pageRequests.TryRemove(id, out var request)) request.Done.TrySetResult(RemoteReply.Busy);
        }
    }

    // Posts a request to the page with a requestId and waits up to 10 s for its remoteReply. Called from any thread.
    // device is the phone that asked, so the page can drop its queued typing on remoteCancel; null for this side's own.
    async Task<RemoteReply> AskPageAsync(string type, string? device, Dictionary<string, object?> fields, Action<JsonElement>? received)
    {
        if (!_pageReady) return RemoteReply.Busy;
        var requestId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var request = new PageRequest(new TaskCompletionSource<RemoteReply>(TaskCreationOptions.RunContinuationsAsynchronously), received);
        _pageRequests[requestId] = request;
        var message = new Dictionary<string, object?> { ["type"] = type, ["requestId"] = requestId };
        if (device != null) message["device"] = device;
        foreach (var (name, value) in fields) message[name] = value;
        OnUi(() =>
        {
            // A phone removed (or Phone remote switched off) after its request passed the token check gets nothing typed:
            // the removal happens before remoteCancel is queued, so a request posted after it is caught here.
            if (device != null && (!_remote.Running || !_remote.Devices.Exists(device)))
            {
                request.Done.TrySetResult(_remote.Running ? RemoteReply.Fail(401, RemoteServer.NotPaired) : RemoteReply.Busy);
                return;
            }
            // remoteStatus also tells the page how many phones are connected. Counted as it goes out, so it can't overtake a newer count.
            if (type == "remoteStatus") message["phones"] = _remote.Phones;
            Post(message);
        });
        try
        {
            return await request.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            return RemoteReply.Busy;
        }
        finally
        {
            _pageRequests.TryRemove(requestId, out _);
        }
    }

    void PostRemoteStatus() => Post(new { type = "remoteStatus", phones = _remote.Phones });

    void PostRemoteWatch(IReadOnlyList<string> refresh)
        => Post(new { type = "remoteWatch", watches = _remote.Watches().Select(watch => new { id = watch.Id, lines = watch.Lines }), refresh });

    // The addresses of the pairing link; none while the server is off.
    List<string> RemoteHosts() => _remote.Running ? RemoteLink.Hosts(_remote.Port, _remote.LoopbackOnly) : [];

    // Whether Windows blocks the phone on one of these addresses' networks. Asked at most every 5 s.
    string? NetworkWarning(List<string> hosts)
    {
        var key = string.Join(',', hosts);
        if (_networkWarning.Hosts != key || Environment.TickCount64 - _networkWarning.At > 5000)
            _networkWarning = (Environment.TickCount64, key, RemoteLink.PublicNetworkWarning(hosts));
        return _networkWarning.Warning;
    }

    // Windows may not sleep on its own while agents run, and with KeepAwake "remote" also while Phone remote is on,
    // so a phone away from home can still start agents.
    void UpdateAwake()
    {
        var mode = AwakeMode();
        var reason = mode == KeepAwake.Off ? null
            : _sessions.Count > 0 ? "Agent Arcade: agents are running"
            : mode == KeepAwake.Remote && _remote.Running ? "Agent Arcade: Phone remote is on"
            : null;
        var before = _awake.Reason;
        if (!_awake.Set(reason)) Log("Couldn't ask Windows to stay awake");
        if (_awake.Reason != before) PowerChanged();
    }

    // The Phone dialog and the phones (State.power) hear when sleeping becomes possible or not, or the plug changes.
    void PowerChanged()
    {
        PostRemoteInfo();
        if (_remote.Phones > 0) _remote.Send(new { type = "state", state = RemoteState() });
    }

    // Windows reports every battery percent too; only plugging in or out counts.
    void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.StatusChange) return;
        OnUi(() =>
        {
            var onBattery = KeepAwake.Status().OnBattery;
            if (onBattery == _onBattery) return;
            _onBattery = onBattery;
            PowerChanged();
        });
    }

    string AwakeMode() => _settings.KeepAwake is KeepAwake.Remote or KeepAwake.Off ? _settings.KeepAwake : KeepAwake.Agents;

    object AwakeInfo()
    {
        var (laptop, onBattery, lidSleeps) = KeepAwake.Status();
        return new { mode = AwakeMode(), on = _awake.Reason != null, agents = _sessions.Count, laptop, onBattery, lidSleeps };
    }

    // Everything the Phone dialog shows.
    void PostRemoteInfo()
    {
        var running = _remote.Running;
        var (code, status, expiresAt, pairedName) = _remote.Pairing.Info();
        var addresses = RemoteHosts();
        var link = running && code != null && _remote.Fingerprint != null ? RemoteLink.Build(RemoteModel.Pc, addresses, code, _remote.Fingerprint) : null;
        if (link != null && _qr?.Link != link) _qr = (link, RemoteLink.Qr(link));
        var connected = _remote.ConnectedDevices();
        Post(new
        {
            type = "remoteInfo",
            enabled = _settings.RemoteEnabled,
            running,
            error = _remote.Error ?? _remote.Devices.SaveError
                ?? (running && addresses.Count == 0 ? "This PC has no network address a phone can reach. Connect it to Wi-Fi or a network cable." : null),
            warning = NetworkWarning(addresses),
            port = _settings.RemotePort,
            link,
            qr = link == null ? null : _qr?.Qr,
            fingerprint = _remote.Fingerprint == null ? null : RemoteCertificate.Short(_remote.Fingerprint),
            expiresAt,
            code = status,
            pairedName,
            addresses,
            devices = _remote.Devices.All().Select(device => new
            {
                id = device.Id,
                name = device.Name,
                paired = device.Paired,
                lastSeen = device.LastSeen,
                connected = connected.Contains(device.Id),
            }),
            phones = _remote.Phones,
            awake = AwakeInfo(),
        });
    }

    object RemoteState() => RemoteModel.State(_settings.ProjectPath, _settings.Projects, _agents, _lastTools, _accounts, _infos, _summaries.Values, RemoteHosts(),
        RemoteModel.Power(AwakeMode(), _awake.Reason != null));

    List<object> RemoteChats() => RemoteModel.Chats(_history, _agents, chat => _sessions.FirstOrDefault(pair => pair.Value.Chat == chat).Key);

    // A phone may only start a known account in a project that is in the sidebar. The page gets the stored spelling.
    (string? Project, string? Problem) CheckRemoteStart(string account, string workspace)
    {
        if (FindAccount(account) == null) return (null, "That agent account isn't on the PC any more.");
        var project = _settings.Projects.FirstOrDefault(path => path.Equals(workspace, StringComparison.OrdinalIgnoreCase));
        return project == null ? (null, "That project isn't in the sidebar on the PC any more.") : (project, null);
    }

    // Every pane's newest summary from the page. When it doesn't answer, the last ones it sent stay.
    Task AskSummariesAsync() => AskPageAsync("remoteStatus", null, new(), ReplaceSummaries);

    async Task<object> IRemoteHost.StateAsync()
    {
        // The page only sends summaries while a phone is connected, so without one they may be old.
        if (_remote.Phones == 0) await AskSummariesAsync();
        return await OnUiAsync(RemoteState);
    }

    Task<object> IRemoteHost.ChatsAsync() => OnUiAsync<object>(RemoteChats);

    async Task<(object State, object Chats)> IRemoteHost.SnapshotAsync()
    {
        await AskSummariesAsync();
        return await OnUiAsync<(object, object)>(() => (RemoteState(), RemoteChats()));
    }

    Task<RemoteReply> IRemoteHost.AskPageAsync(string device, string type, Dictionary<string, object?> fields) => AskPageAsync(type, device, fields, null);

    async Task<RemoteReply> IRemoteHost.StartAsync(string device, string account, string workspace)
    {
        var (project, problem) = await OnUiAsync(() => CheckRemoteStart(account, workspace));
        if (problem != null) return RemoteReply.Fail(404, problem);
        return await AskPageAsync("remoteStart", device, new() { ["account"] = account, ["workspace"] = project }, null);
    }

    // Like + with a folder that is already there, but the folder is new and the PC keeps showing its workspace.
    Task<RemoteReply> IRemoteHost.CreateProjectAsync(string device, string name) => OnUiAsync(() =>
    {
        var (path, reply) = CreateProject(name);
        if (path == null) return reply;
        Log("New project from a phone: " + path);
        PostState();
        return reply;
    });

    // The server already answered the phone's waiting requests; their late replies are dropped.
    void IRemoteHost.Cancel(string? device) => OnUi(() => Post(new { type = "remoteCancel", device }));

    void IRemoteHost.WatchesChanged(IReadOnlyList<string> refresh) => OnUi(() => PostRemoteWatch(refresh));

    void IRemoteHost.RemoteChanged() => OnUi(() =>
    {
        PostRemoteStatus();
        PostRemoteInfo();
    });

    void IRemoteHost.Paired(string name) => OnUi(() =>
    {
        Toast("Paired with " + name + ".");
        PostRemoteInfo();
    });

    void IRemoteHost.Log(string message) => Log(message);

    // ---------- page messages ----------

    void PostState()
    {
        Post(new
        {
            type = "state",
            workspace = _settings.ProjectPath,
            workspaces = RemoteModel.Workspaces(_settings.Projects),
            // Where New project makes its folder, for the dialog's preview; null when there is nowhere.
            projectsFolder = NewProject.Folder(_settings.ProjectPath, _settings.Projects),
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
        if (_remote.Phones > 0) _remote.Send(new { type = "state", state = RemoteState() });
    }

    // An image dragged from a browser has no file on disk, so it is saved to the temp folder first.
    void SaveDroppedImage(string session, string name, string data)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!ImageServer.Extensions.Contains(extension)) extension = ".png";
        var bytes = Convert.FromBase64String(data);
        if (bytes.Length == 0 || bytes.Length > 30 * 1024 * 1024)
        {
            Toast("That image is empty or larger than 30 MB.", "error");
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "AgentArcade", "drops");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "image-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + extension);
        File.WriteAllBytes(path, bytes);
        Post(new { type = "dropped", session, paths = new[] { path } });
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

    // Runs work on the UI thread for a caller on another thread.
    Task<T> OnUiAsync<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnUi(() =>
        {
            try
            {
                done.TrySetResult(work());
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        });
        return done.Task.WaitAsync(TimeSpan.FromSeconds(10));
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

    const uint FlashTray = 2, FlashUntilForeground = 12;

    [StructLayout(LayoutKind.Sequential)]
    struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    static extern bool FlashWindowEx(ref FlashInfo info);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
