using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AgentArcade;

// Keys and tokens never reach a chat title, the log, a toast or the phone. Each kind keeps its prefix, so you can still
// tell what it was (sk-proj-[hidden]); hiding twice changes nothing. web/app.js (TOKEN_SHAPE) mirrors this list.
static class TextGuard
{
    static readonly (Regex Pattern, string Hidden)[] Tokens =
    [
        (new(@"sk-ant-[A-Za-z0-9_\-]+", RegexOptions.Compiled), "sk-ant-[hidden]"),
        (new(@"(?<![A-Za-z0-9_\-])sk-proj-[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled), "sk-proj-[hidden]"),
        (new(@"(?<![A-Za-z0-9_\-])sk-[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled), "sk-[hidden]"),
        (new(@"AIza[0-9A-Za-z_\-]{30,}", RegexOptions.Compiled), "AIza[hidden]"),
        (new(@"\b(gh[pousr])_[A-Za-z0-9]{20,}", RegexOptions.Compiled), "$1_[hidden]"),
        (new(@"github_pat_[A-Za-z0-9_]{20,}", RegexOptions.Compiled), "github_pat_[hidden]"),
        (new(@"\b(xox[abeprs])-[A-Za-z0-9\-]{10,}", RegexOptions.Compiled), "$1-[hidden]"),
        (new(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled), "AKIA[hidden]"),
        // A JWT: header and payload are base64 JSON, so both start with eyJ.
        (new(@"eyJ[A-Za-z0-9_\-]{5,}\.eyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]*", RegexOptions.Compiled), "eyJ[hidden]"),
    ];

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        foreach (var (pattern, hidden) in Tokens) value = pattern.Replace(value, hidden);
        return value;
    }
}

// What a chat title may hold: TextGuard's rules, and no mouse reports. A pane that tracks the mouse (Codex) gets ESC [<35;12;9M
// for every move, and up to 1.5.1 web/app.js kept <35;12;9M of it in the typed line; a title left with no letters is empty.
static class ChatTitle
{
    static readonly Regex MouseReport = new(@"<\d{1,3};\d{1,5};\d{1,5}[Mm]", RegexOptions.Compiled);

    // What is left of a report cut off at either end of a title: ";8M", "<65;19", "<65;1…".
    static readonly Regex Piece = new(@"(?<=^|\s)<?[\d;]*;[\d;]*[Mm]?…?(?=\s|$)", RegexOptions.Compiled);

    public static string Clean(string? title)
    {
        title = TextGuard.Redact(title);
        if (!MouseReport.IsMatch(title)) return title;
        title = Piece.Replace(MouseReport.Replace(title, " "), " ");
        title = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return title.Any(char.IsLetter) ? title : "";
    }
}

static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string Str(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}

sealed class AppSettings
{
    // The active workspace. Projects lists every workspace shown in the sidebar. A new install starts in the
    // user's Documents folder, which always exists, until they add a project folder of their own.
    public string ProjectPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public List<string> Projects { get; set; } = new();
    // Phone remote: off until the user switches it on in the Phone dialog.
    public bool RemoteEnabled { get; set; }
    public int RemotePort { get; set; } = 47821;
    // When Windows may not sleep on its own: "agents" while agents run, "remote" also while Phone remote is on, "off".
    public string KeepAwake { get; set; } = AgentArcade.KeepAwake.Agents;
    // Projects whose git config includes other files or names another work tree, where the Changes tab runs git
    // because the user chose "Run git here anyway", each with the SHA-256 of the config it was for. Null in older files.
    public List<GitTrust>? GitTrusted { get; set; }
    // The update notice (UpdateCheck class): on by default, at most once a day. UpdateTag and UpdateUrl are the newest
    // release it saw, so the notice shows again after a restart without asking GitHub again.
    public bool UpdateCheck { get; set; } = true;
    public DateTimeOffset? UpdateCheckedAt { get; set; }
    public string? UpdateTag { get; set; }
    public string? UpdateUrl { get; set; }
    // Where New project makes its folder while the active workspace is Documents, the user folder or a OneDrive root
    // (NewProject.Folder): %USERPROFILE%\source\repos, made with the first project. Only a new install gets it; older
    // files have null and keep making new projects next to the current one.
    public string? DefaultProjectsFolder { get; set; }
    // The window as it was closed: its normal (not maximized) bounds in physical pixels, and whether it was maximized.
    // Null until it was closed once; the window then opens at about 85% of the primary work area (WindowPlace).
    public WindowBox? WindowBounds { get; set; }
    public bool? WindowMaximized { get; set; }
    // Settings → General: the mascot blinks, looks around and reacts to clicks. Null (older files) means on.
    public bool? MascotAnimations { get; set; }
    // Settings → General, "New projects go to": a folder the user picked. Null (older files, or Automatic) keeps
    // NewProject.Folder's own rule; a picked folder that is gone counts as null.
    public string? NewProjectsFolder { get; set; }
    // Settings → Agents and accounts, "New sessions start with": per agent name, its permission mode, model and effort
    // (AgentSetup). Null in older files: every agent starts with its own settings.
    public Dictionary<string, AgentSetupChoice>? AgentSetups { get; set; }

    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // AGENT_ARCADE_HOME moves all data to another folder, so a test build can run next to the installed app.
    public static string? Home => Environment.GetEnvironmentVariable("AGENT_ARCADE_HOME") is { Length: > 0 } home ? Path.GetFullPath(home) : null;

    public static string Folder => Home ?? Path.Combine(LocalAppData, "AgentArcade");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    // The single-instance mutex. A separate data folder gets its own, so both copies can run.
    public static string InstanceName => Home == null
        ? @"Local\AgentArcade"
        : @"Local\AgentArcade-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Home.ToLowerInvariant())))[..16].ToLowerInvariant();

    public static AppSettings Load()
    {
        // The second path holds the settings from before the app was renamed.
        var paths = Home == null ? new[] { FilePath, Path.Combine(LocalAppData, "ClaudeUnitySwitch", "settings.json") } : new[] { FilePath };
        var found = false;
        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                found = true;
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                if (loaded != null && !string.IsNullOrWhiteSpace(loaded.ProjectPath)) return loaded.WithActiveListed();
            }
            catch
            {
                // A broken settings file falls back to the default (the Documents folder).
            }
        }
        // No settings file at all: a new install, whose new projects go to %USERPROFILE%\source\repos.
        return new AppSettings { DefaultProjectsFolder = found ? null : NewProject.DefaultFolder }.WithActiveListed();
    }

    AppSettings WithActiveListed()
    {
        Projects = Projects.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!Projects.Contains(ProjectPath, StringComparer.OrdinalIgnoreCase)) Projects.Insert(0, ProjectPath);
        return this;
    }

    // RemotePort as settings.json has it now; null when the file is missing or broken.
    public static int? StoredPort()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))?.RemotePort : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        // The app never changes RemotePort itself, so a port typed into the file while the app runs is kept.
        RemotePort = StoredPort() ?? RemotePort;
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json.Options));
    }
}

// AppSettings.WindowBounds: a rectangle in physical pixels (screen coordinates).
sealed class WindowBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

// One "Run git here anyway" in the Changes tab: the project folder and GitCheck.Config of the config it was for.
sealed class GitTrust
{
    public string Folder { get; set; } = "";
    public string ConfigSha256 { get; set; } = "";
}

// One AI coding agent. Every agent is the vendor's own command-line tool, so signing in
// always happens in that tool's official login flow and this app never sees a token.
sealed class AgentDef
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    // Where to look when Command isn't on PATH or in the vendor installer's own folder (Locator.Known). One "*"
    // folder is allowed; the newest match wins.
    public string AltPath { get; set; } = "";
    public string Args { get; set; } = "";
    public string Login { get; set; } = "";
    public string LoginHint { get; set; } = "";
    public string Install { get; set; } = "";
    // The tool's own setting for "keep my files in this folder". Extra accounts need it.
    public string HomeVar { get; set; } = "";
    // How to ask the tool which account is signed in: claude, codex, gemini, cursor or empty; ollama asks whether Ollama runs.
    public string AccountCheck { get; set; } = "";
    // Chat history. {id} is the chat's id. NewChatArgs gives a new chat that id, so ResumeChatArgs can
    // open exactly that chat later; ResumeArgs opens the tool's own "resume" (its latest chat or a list).
    // Missing (null) takes the built-in value for the same command; "" turns it off.
    public string? NewChatArgs { get; set; }
    public string? ResumeChatArgs { get; set; }
    public string? ResumeArgs { get; set; }
    // Where the tool saves images it generates, inside its home folder (HomeVar). DefaultHome is that
    // folder for the main account. Missing (null) takes the built-in value; "" turns it off.
    public string? ImagesFolder { get; set; }
    public string? DefaultHome { get; set; }
    // Where the tool keeps its chats, inside its home folder, when it can't be given a new chat's id (NewChatArgs): the
    // id is then learned from the name of the chat's file there (SessionFiles, Codex's names). Null takes the built-in
    // value; "" turns it off.
    public string? SessionsFolder { get; set; }
    // Claude Code's own Remote Control, so a chat also shows in the Claude app: added to the new and resumed chats of an
    // account that opted in (AgentAccount.RemoteControl), never to a sign-in. {project} is the project folder's name.
    // Missing (null) takes the built-in value; "" means the agent has none.
    public string? RemoteControlArgs { get; set; }
}

static class AgentStore
{
    public static string FilePath => Path.Combine(AppSettings.Folder, "agents.json");

    public static List<AgentDef> Defaults() =>
    [
        new()
        {
            Name = "Claude Code",
            Command = "claude",
            Login = "auth login",
            LoginHint = "Approve the sign-in in the browser window that opens.",
            Install = "powershell -NoProfile -ExecutionPolicy Bypass -Command \"irm https://claude.ai/install.ps1 | iex\"",
            HomeVar = "CLAUDE_CONFIG_DIR",
            AccountCheck = "claude",
            NewChatArgs = "--session-id {id}",
            ResumeChatArgs = "--resume {id}",
            ResumeArgs = "--resume",
            // `claude --help`: --remote-control [name] takes an optional name, so it goes last, where nothing follows it.
            RemoteControlArgs = "--remote-control-session-name-prefix {project} --remote-control",
        },
        new()
        {
            Name = "Codex",
            Command = "codex",
            AltPath = @"%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe",
            Login = "login",
            LoginHint = "Choose \"Sign in with ChatGPT\" in the browser window that opens.",
            // OpenAI's Windows installer (github.com/openai/codex README); it needs no Node.js.
            Install = "powershell -NoProfile -ExecutionPolicy Bypass -Command \"irm https://chatgpt.com/codex/install.ps1 | iex\"",
            HomeVar = "CODEX_HOME",
            AccountCheck = "codex",
            // Codex has no setting for a new chat's id; SessionsFolder learns it.
            ResumeChatArgs = "resume {id}",
            ResumeArgs = "resume",
            ImagesFolder = "generated_images",
            DefaultHome = @"%USERPROFILE%\.codex",
            SessionsFolder = "sessions",
        },
        new()
        {
            Name = "Gemini CLI",
            Command = "gemini",
            LoginHint = "Gemini CLI needs a Gemini API key or Code Assist Standard/Enterprise. Personal Google accounts: use Antigravity CLI.",
            Install = "npm install -g @google/gemini-cli",
            HomeVar = "GEMINI_CLI_HOME",
            AccountCheck = "gemini",
            ResumeArgs = "--resume",
        },
        // Google's replacement for Gemini CLI with personal Google accounts (antigravity.google/docs/getting-started,
        // install and resume pages). It has no login command or account-folder setting: it signs in when it starts.
        new()
        {
            Name = "Antigravity CLI",
            Command = "agy",
            LoginHint = "Choose \"Google OAuth\" when Antigravity CLI starts and approve the sign-in in the browser. Personal Google accounts work. Type /logout inside it to sign out.",
            Install = "powershell -NoProfile -ExecutionPolicy Bypass -Command \"irm https://antigravity.google/cli/install.ps1 | iex\"",
            ResumeArgs = "--continue",
        },
        new()
        {
            Name = "GitHub Copilot CLI",
            Command = "copilot",
            LoginHint = "Type /login inside Copilot and follow the steps.",
            // GitHub's WinGet package (docs.github.com, Installing GitHub Copilot CLI); WinGet also brings PowerShell 7.
            Install = "winget install GitHub.Copilot",
            ResumeArgs = "--resume",
        },
        new()
        {
            Name = "Cursor",
            Command = "cursor-agent",
            AltPath = @"%LOCALAPPDATA%\cursor-agent\cursor-agent.cmd",
            Login = "login",
            LoginHint = "Approve the sign-in to your Cursor account in the browser window that opens.",
            Install = "powershell -NoProfile -ExecutionPolicy Bypass -Command \"irm 'https://cursor.com/install?win32=true' | iex\"",
            AccountCheck = "cursor",
            ResumeArgs = "--resume",
        },
        // Codex with a model that Ollama runs on this PC (--oss and --local-provider in `codex --help`): no account and no
        // usage limit. It shares Codex's own folder, so Codex's resume list holds the chats of both. Without -m in Args,
        // Codex uses gpt-oss:20b and pulls it into Ollama on the first start. The account check asks whether Ollama runs.
        new()
        {
            Name = "Codex (Ollama)",
            Command = "codex",
            AltPath = @"%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe",
            Args = "--oss --local-provider ollama",
            LoginHint = "No sign-in: Codex uses a model that Ollama runs on this PC. Install Ollama from ollama.com and keep it running. " +
                "Without -m in Args, Codex uses gpt-oss:20b and downloads it (about 14 GB) when Ollama doesn't have it yet.",
            Install = "powershell -NoProfile -ExecutionPolicy Bypass -Command \"irm https://chatgpt.com/codex/install.ps1 | iex\"",
            AccountCheck = "ollama",
            // Its chats are in Codex's own sessions folder, so their ids are learned the same way (SessionFiles), and a
            // Codex chat and a Codex (Ollama) chat that start close together leave each other unmatched.
            ResumeChatArgs = "resume {id}",
            ResumeArgs = "resume",
            DefaultHome = @"%USERPROFILE%\.codex",
            SessionsFolder = "sessions",
        },
    ];

    static string OfferedPath => Path.Combine(AppSettings.Folder, "agents-offered.json");

    // The built-in list before agents-offered.json existed. Every agents.json from then already had these.
    static readonly string[] FirstDefaults = ["Claude Code", "Codex", "Gemini CLI", "GitHub Copilot CLI"];

    public static List<AgentDef> Load()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<List<AgentDef>>(File.ReadAllText(FilePath), Json.Options);
                // Names identify agents and their accounts, so a repeated name keeps only its first entry.
                var usable = loaded?.Where(agent => !string.IsNullOrWhiteSpace(agent.Command) && !string.IsNullOrWhiteSpace(agent.Name))
                    .GroupBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                if (usable is { Count: > 0 }) return WithNewDefaults(usable);
            }
            catch
            {
                // A broken agents.json falls back to the built-in list; the file itself is left alone.
            }
            return Defaults();
        }

        var defaults = Defaults();
        Directory.CreateDirectory(AppSettings.Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(defaults, Json.Options));
        SaveOffered(defaults.Select(agent => agent.Name));
        return defaults;
    }

    // Fills in settings an older agents.json doesn't have yet, and adds a built-in agent from a newer
    // version once. An agent the user deleted after it was offered stays deleted.
    static List<AgentDef> WithNewDefaults(List<AgentDef> agents)
    {
        var defaults = Defaults();
        foreach (var agent in agents)
        {
            // Codex and Codex (Ollama) share a command, so the built-in with the same name as well comes first.
            var builtIn = defaults.FirstOrDefault(item => item.Command.Equals(agent.Command, StringComparison.OrdinalIgnoreCase) &&
                                                          item.Name.Equals(agent.Name, StringComparison.OrdinalIgnoreCase))
                          ?? defaults.FirstOrDefault(item => item.Command.Equals(agent.Command, StringComparison.OrdinalIgnoreCase));
            if (builtIn == null) continue;
            agent.NewChatArgs ??= builtIn.NewChatArgs;
            agent.ResumeChatArgs ??= builtIn.ResumeChatArgs;
            agent.ResumeArgs ??= builtIn.ResumeArgs;
            agent.ImagesFolder ??= builtIn.ImagesFolder;
            agent.DefaultHome ??= builtIn.DefaultHome;
            agent.SessionsFolder ??= builtIn.SessionsFolder;
            agent.RemoteControlArgs ??= builtIn.RemoteControlArgs;
            agent.LoginHint = CurrentHint(agent, builtIn);
            agent.Install = CurrentInstall(agent, builtIn);
        }

        List<string> offered;
        try
        {
            offered = File.Exists(OfferedPath)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(OfferedPath)) ?? new()
                : FirstDefaults.ToList();
        }
        catch
        {
            offered = FirstDefaults.ToList();
        }
        var added = defaults.Where(builtIn =>
            !offered.Contains(builtIn.Name, StringComparer.OrdinalIgnoreCase) &&
            !agents.Any(agent => agent.Name.Equals(builtIn.Name, StringComparison.OrdinalIgnoreCase) || SameTool(agent, builtIn))).ToList();
        if (added.Count == 0 && File.Exists(OfferedPath)) return agents;
        try
        {
            if (added.Count > 0)
            {
                agents.AddRange(added);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(agents, Json.Options));
            }
            SaveOffered(offered.Concat(defaults.Select(agent => agent.Name)));
        }
        catch
        {
            // Offered again next time.
        }
        return agents;
    }

    // An agent the user already has under another name. A built-in with its own Args (Codex (Ollama)) is that only with
    // the same Args too, so having Codex doesn't hide it.
    static bool SameTool(AgentDef agent, AgentDef builtIn)
        => agent.Command.Equals(builtIn.Command, StringComparison.OrdinalIgnoreCase) &&
           (builtIn.Args.Length == 0 || (agent.Args ?? "").Trim().Equals(builtIn.Args, StringComparison.OrdinalIgnoreCase));

    // Built-in sign-in hints that stopped being true. agents.json keeps a copy of every hint, so one that is exactly an
    // old built-in hint shows the current built-in one instead; a hint the user wrote stays.
    public static readonly Dictionary<string, string[]> RetiredHints = new(StringComparer.OrdinalIgnoreCase)
    {
        // Google ended Gemini CLI's sign-in with personal Google accounts on 18 Jun 2026.
        ["gemini"] = ["Choose \"Sign in with Google\" when Gemini starts. To change account later, type /auth inside Gemini."],
    };

    public static string CurrentHint(AgentDef agent, AgentDef builtIn)
        => RetiredHints.TryGetValue(builtIn.Command, out var retired) && retired.Contains(agent.LoginHint, StringComparer.Ordinal)
            ? builtIn.LoginHint
            : agent.LoginHint;

    // Built-in install commands that were replaced by the vendor's own Windows installer. Like the hints: only an
    // Install that is exactly an old built-in one changes; a command the user wrote stays.
    public static readonly Dictionary<string, string[]> RetiredInstalls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["codex"] = ["npm install -g @openai/codex"],
        ["copilot"] = ["npm install -g @github/copilot"],
    };

    public static string CurrentInstall(AgentDef agent, AgentDef builtIn)
        => RetiredInstalls.TryGetValue(builtIn.Command, out var retired) && retired.Contains(agent.Install, StringComparer.Ordinal)
            ? builtIn.Install
            : agent.Install;

    static void SaveOffered(IEnumerable<string> names)
        => File.WriteAllText(OfferedPath, JsonSerializer.Serialize(names.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), Json.Options));
}

// One chat with an agent, kept so it can be found and continued later.
sealed class ChatRecord
{
    public string Id { get; set; } = "";
    public string Agent { get; set; } = "";
    public string Account { get; set; } = "";
    public string Workspace { get; set; } = "";
    public string Title { get; set; } = "";
    // True when the user typed the title; then it is never replaced by one the agent suggests.
    public bool Named { get; set; }
    // The agent's own id for this chat, when this app chose it or learned it from the name of the agent's chat file
    // (SessionFiles), so it can be resumed exactly.
    public string ChatId { get; set; } = "";
    public DateTimeOffset Started { get; set; }
    public DateTimeOffset LastActive { get; set; }
    public DateTimeOffset? Ended { get; set; }

    [JsonIgnore] public string AccountKey => Agent + "|" + Account;
}

// Only titles, folders and times are stored here; the chats themselves stay in each agent's own files.
static class HistoryStore
{
    const int Keep = 500;

    public static string FilePath => Path.Combine(AppSettings.Folder, "history.json");

    public static List<ChatRecord> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var chats = JsonSerializer.Deserialize<List<ChatRecord>>(File.ReadAllText(FilePath), Json.Options)?
                .Where(chat => chat.Id.Length > 0 && chat.Agent.Length > 0)
                .ToList() ?? new();
            // A key that became a title before TextGuard knew its kind is hidden now, and mouse reports come out
            // (ChatTitle). The file is saved only when a title changed, so this happens once.
            var scrubbed = false;
            foreach (var chat in chats)
            {
                var clean = ChatTitle.Clean(chat.Title);
                if (clean == chat.Title) continue;
                chat.Title = clean;
                if (clean.Length == 0) chat.Named = false;
                scrubbed = true;
            }
            if (scrubbed)
            {
                try
                {
                    Save(chats);
                }
                catch
                {
                    // Tried again at the next start; the titles shown are already clean.
                }
            }
            return chats;
        }
        catch
        {
            return new();
        }
    }

    public static void Save(List<ChatRecord> chats)
    {
        if (chats.Count > Keep)
        {
            var keep = chats.OrderByDescending(chat => chat.LastActive).Take(Keep).ToHashSet();
            chats.RemoveAll(chat => !keep.Contains(chat));
        }
        Directory.CreateDirectory(AppSettings.Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(chats, Json.Options));
        File.Move(temp, FilePath, true);
    }
}

static class ChatArgs
{
    static string Join(string first, string second)
        => string.IsNullOrWhiteSpace(first) ? second.Trim() : string.IsNullOrWhiteSpace(second) ? first.Trim() : first.Trim() + " " + second.Trim();

    // The arguments for a new chat. When the agent lets this app pick the chat's id, it is remembered. setup: the options
    // of New sessions start with (AgentSetup.Args); they go before Remote Control, which must stay last.
    public static string New(AgentDef agent, ChatRecord chat, AgentAccount? account = null, string setup = "")
    {
        var extra = agent.NewChatArgs ?? "";
        if (extra.Contains("{id}", StringComparison.Ordinal))
        {
            chat.ChatId = chat.Id;
            extra = extra.Replace("{id}", chat.Id, StringComparison.Ordinal);
        }
        return Join(Join(Join(agent.Args, extra), setup), RemoteControl(agent, account, chat));
    }

    // Null when the agent can't continue a chat. setup as in New: after Codex's resume subcommand, before Remote Control.
    public static string? Resume(AgentDef agent, ChatRecord chat, AgentAccount? account = null, string setup = "") => Kind(agent, chat) switch
    {
        "exact" => Join(Join(Join(agent.Args, agent.ResumeChatArgs!.Replace("{id}", chat.ChatId, StringComparison.Ordinal)), setup), RemoteControl(agent, account, chat)),
        "agent" => Join(Join(Join(agent.Args, agent.ResumeArgs!), setup), RemoteControl(agent, account, chat)),
        _ => null,
    };

    // Claude Code's Remote Control, last on the line, for an account that opted in: the chat also shows in the Claude app.
    static string RemoteControl(AgentDef agent, AgentAccount? account, ChatRecord chat)
        => account?.RemoteControl == true && !string.IsNullOrWhiteSpace(agent.RemoteControlArgs)
            ? agent.RemoteControlArgs.Replace("{project}", SessionPrefix(chat.Workspace), StringComparison.Ordinal)
            : "";

    // The project folder's name, for Remote Control's session names (project-graceful-unicorn until the first message).
    // Only letters, digits, '.', '_' and '-', so it needs no quotes on the command line or in cmd (an npm shim), and it
    // never starts with '-', which would read as an option.
    public static string SessionPrefix(string workspace)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(workspace.Trim()));
        var safe = Regex.Replace(name, @"[^\p{L}\p{N}._-]+", "-").Trim('-', '.');
        if (safe.Length > 40) safe = safe[..40].TrimEnd('-', '.');
        return safe.Length > 0 ? safe : "AgentArcade";
    }

    // exact: this very chat; agent: the agent's own resume, which may pick its latest chat or ask; "": none.
    public static string Kind(AgentDef? agent, ChatRecord chat)
    {
        if (agent == null) return "";
        if (chat.ChatId.Length > 0 && !string.IsNullOrWhiteSpace(agent.ResumeChatArgs)) return "exact";
        return string.IsNullOrWhiteSpace(agent.ResumeArgs) ? "" : "agent";
    }
}

// Codex can't be given a new chat's id, so it is learned afterwards from the name of the file Codex keeps the chat in:
// <home>\sessions\YYYY\MM\DD\rollout-<local time>-<id>.jsonl (.jsonl.zst once Codex compresses it). Only the names are
// listed; no file is opened. Codex makes the file at the chat's first message, but the name carries the time the chat
// began, seconds after its pane started: the id is a version 7 UUID, whose first 48 bits are that time in milliseconds.
static class SessionFiles
{
    // A chat belongs to the pane that started at most this long before it began.
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    // Another start of the same account this close leaves both unmatched: a slow start could swap their chats.
    public static readonly TimeSpan Apart = TimeSpan.FromSeconds(60);
    // The name's own time has whole seconds.
    static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    static readonly Regex Name = new(
        @"^rollout-(\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2})-([0-9a-f]{8}-[0-9a-f]{4}-([0-9a-f])[0-9a-f]{3}-[0-9a-f]{4}-[0-9a-f]{12})\.jsonl(\.zst)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The folder with an account's chat files, or null when the agent has none.
    public static string? FolderFor(AgentDef agent, AgentAccount account)
    {
        if (string.IsNullOrWhiteSpace(agent.SessionsFolder)) return null;
        var home = account.IsMain ? Environment.ExpandEnvironmentVariables(agent.DefaultHome ?? "") : account.Home;
        return home.Length > 0 && Path.IsPathFullyQualified(home) ? Path.GetFullPath(Path.Combine(home, agent.SessionsFolder)) : null;
    }

    // The chat's id and when it began, from a file name; null for any other name. An id that isn't a version 7 UUID
    // (older Codex) takes the time in the name.
    public static (string Id, DateTimeOffset Began)? Parse(string name)
    {
        var match = Name.Match(name);
        if (!match.Success) return null;
        var id = match.Groups[2].Value.ToLowerInvariant();
        try
        {
            if (match.Groups[3].Value == "7") return (id, DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(id[..8] + id[9..13], 16)));
            return DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd'T'HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out var local)
                ? (id, new DateTimeOffset(local))
                : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            // A time past what a date can hold: not a name Codex made.
            return null;
        }
    }

    // The id of the first chat that began within Window after a pane started: chats the agent starts by itself during a
    // turn (helpers, reviews) begin later. Null when there is none, when two began at once, when another start of the
    // same account (others) is within Apart of this one, or when another chat already has that id (taken).
    public static string? Match(DateTimeOffset started, IEnumerable<DateTimeOffset> others, IEnumerable<string> names, ISet<string> taken)
    {
        if (others.Any(other => (other - started).Duration() < Apart)) return null;
        var first = names.Select(Parse).Where(file => file != null).Select(file => file!.Value)
            .Where(file => file.Began >= started - Slack && file.Began <= started + Window)
            .DistinctBy(file => file.Id)
            .OrderBy(file => file.Began)
            .Take(2)
            .ToList();
        if (first.Count == 0 || (first.Count == 2 && first[1].Began == first[0].Began)) return null;
        return taken.Contains(first[0].Id) ? null : first[0].Id;
    }

    // The file names in the date folders around a start (the day before and after too); nothing is opened.
    public static List<string> Names(string folder, DateTimeOffset started)
    {
        var names = new List<string>();
        var day = started.ToLocalTime().Date;
        for (var offset = -1; offset <= 1; offset++)
        {
            var date = day.AddDays(offset);
            var dated = Path.Combine(folder, date.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture),
                date.ToString("MM", System.Globalization.CultureInfo.InvariantCulture), date.ToString("dd", System.Globalization.CultureInfo.InvariantCulture));
            if (Directory.Exists(dated)) names.AddRange(Directory.EnumerateFiles(dated, "rollout-*").Select(Path.GetFileName).OfType<string>());
        }
        return names;
    }
}

// One agent's "New sessions start with" in settings.json. Null is the agent's own setting.
sealed class AgentSetupChoice
{
    public string? Permission { get; set; }
    public string? Model { get; set; }
    public string? Effort { get; set; }

    [JsonIgnore] public bool IsEmpty => Permission == null && Model == null && Effort == null;
}

// What Settings → Agents and accounts → New sessions start with may set, as each agent's own command-line options. Only
// options its own --help lists are offered, checked on 18 Oct 2026 with claude 2.1.302, codex-cli 0.162.1 (also `codex
// resume --help`), gemini 0.63.0, GitHub Copilot CLI 1.0.98 and cursor-agent 2026.10.14; Antigravity CLI wasn't installed
// to check, so it gets none. Codex has no effort option: its -c sets the config key model_reasoning_effort, which its
// config schema has, to one of the levels `codex debug models` lists for every model. Keyed by command, like Locator.Known.
// The command line is plain text that sometimes runs through cmd /c, so a choice is one from these lists and a model name
// only letters, digits and . _ : / - (never a leading -, which would read as another option).
static class AgentSetup
{
    // Risky: the agent then edits files and runs commands without asking.
    public sealed record Permission(string Id, string Label, string Args, bool Risky = false);

    // Model: the option the name follows. Effort: the option with {0} for the level, and the levels; null and none
    // when the agent has no such option.
    public sealed record Options(Permission[] Permissions, string? Model, string? ModelHint, string? Effort, string[] Efforts);

    static readonly Dictionary<string, Options> ByCommand = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = new(
            [
                new("acceptEdits", "Accept edits", "--permission-mode acceptEdits"),
                new("plan", "Plan mode", "--permission-mode plan"),
                new("auto", "Auto mode", "--permission-mode auto"),
                new("bypassPermissions", "Bypass permissions", "--permission-mode bypassPermissions", true),
            ],
            "--model", "An alias such as opus or sonnet, or a full model name.", "--effort {0}", ["low", "medium", "high", "xhigh", "max"]),
        // The four presets of Codex's own /permissions, each as the sandbox and approval policy it sets.
        ["codex"] = new(
            [
                new("ask", "Ask for approval", "--sandbox workspace-write --ask-for-approval on-request"),
                new("approve-for-me", "Approve for me", "--approve-for-me"),
                new("full-access", "Full Access", "--sandbox danger-full-access --ask-for-approval never", true),
                new("read-only", "Read Only", "--sandbox read-only --ask-for-approval on-request"),
            ],
            "--model", null, "-c model_reasoning_effort={0}", ["low", "medium", "high", "xhigh", "max"]),
        ["gemini"] = new(
            [
                new("auto_edit", "Auto-approve edits", "--approval-mode auto_edit"),
                new("plan", "Plan (read-only)", "--approval-mode plan"),
                new("yolo", "YOLO (auto-approve all tools)", "--approval-mode yolo", true),
            ],
            "--model", null, null, []),
        ["copilot"] = new(
            [
                new("plan", "Plan mode", "--mode plan"),
                new("allow-all-tools", "Allow all tools", "--allow-all-tools", true),
                new("allow-all", "Allow all (tools, paths and URLs)", "--allow-all", true),
            ],
            "--model", "A model name, or auto to let Copilot pick.", "--reasoning-effort {0}", ["none", "minimal", "low", "medium", "high", "xhigh", "max"]),
        ["cursor-agent"] = new(
            [
                new("plan", "Plan mode", "--mode plan"),
                new("auto-review", "Auto-review", "--auto-review"),
                new("force", "Run everything", "--force", true),
            ],
            "--model", "For example gpt-5 or sonnet-4-thinking.", null, []),
    };

    // \z, not $: $ also matches before a final line break.
    static readonly Regex ModelName = new(@"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,99}\z", RegexOptions.Compiled);

    // Null when nothing can be set for this agent.
    public static Options? For(AgentDef agent) => ByCommand.GetValueOrDefault(Path.GetFileNameWithoutExtension(agent.Command.Trim()));

    // settings.json keys the choices by agent name.
    public static AgentSetupChoice? Stored(IReadOnlyDictionary<string, AgentSetupChoice>? setups, AgentDef agent)
        => setups?.FirstOrDefault(pair => pair.Key.Equals(agent.Name, StringComparison.OrdinalIgnoreCase)).Value;

    // The parts of a stored choice this agent takes; the rest is left out (settings.json can be edited by hand).
    public static AgentSetupChoice Usable(AgentDef agent, AgentSetupChoice? choice)
    {
        var options = For(agent);
        if (options == null || choice == null) return new();
        return new AgentSetupChoice
        {
            Permission = options.Permissions.Any(item => item.Id == choice.Permission) ? choice.Permission : null,
            Model = options.Model != null && choice.Model != null && ModelName.IsMatch(choice.Model) ? choice.Model : null,
            Effort = options.Effort != null && options.Efforts.Contains(choice.Effort, StringComparer.Ordinal) ? choice.Effort : null,
        };
    }

    // A choice from the page; anything this agent doesn't take is refused with a message.
    public static AgentSetupChoice Check(AgentDef agent, string permission, string model, string effort)
    {
        static string? Set(string value) => string.IsNullOrEmpty(value) ? null : value;
        var asked = new AgentSetupChoice { Permission = Set(permission), Model = Set(model.Trim()), Effort = Set(effort) };
        var used = Usable(agent, asked);
        if (used.Permission != asked.Permission) throw new InvalidOperationException(agent.Name + " has no permission mode called " + asked.Permission + " here.");
        if (used.Model != asked.Model)
            throw new InvalidOperationException(For(agent)?.Model == null
                ? agent.Name + " can't be given a model here."
                : "A model name has only letters, digits and . _ : / -, starts with a letter or digit, and is at most 100 characters.");
        if (used.Effort != asked.Effort) throw new InvalidOperationException(agent.Name + " has no effort level called " + asked.Effort + " here.");
        return used;
    }

    // The options a stored choice adds to the command line: permission mode, model, effort.
    public static string Args(AgentDef agent, AgentSetupChoice? choice)
    {
        var options = For(agent);
        var used = Usable(agent, choice);
        if (options == null) return "";
        var parts = new List<string>();
        if (options.Permissions.FirstOrDefault(item => item.Id == used.Permission) is { } permission) parts.Add(permission.Args);
        if (used.Model != null) parts.Add(options.Model + " " + used.Model);
        if (used.Effort != null) parts.Add(options.Effort!.Replace("{0}", used.Effort, StringComparison.Ordinal));
        return string.Join(' ', parts);
    }

    // A chat's arguments with the agent's setup after them: after Codex's resume subcommand, whose own --help lists these
    // options too. The other agents' resume is an option, which a following option doesn't fill.
    public static string Add(string args, AgentDef agent, IReadOnlyDictionary<string, AgentSetupChoice>? setups)
    {
        var extra = Args(agent, Stored(setups, agent));
        return extra.Length == 0 ? args : string.IsNullOrWhiteSpace(args) ? extra : args.Trim() + " " + extra;
    }

    // state.agents[].setupOptions for the page; null when nothing can be set.
    public static object? PageOptions(AgentDef agent) => For(agent) is not { } options ? null : new
    {
        permissions = options.Permissions.Select(item => new { id = item.Id, label = item.Label, args = item.Args, risky = item.Risky }),
        model = options.Model,
        modelHint = options.ModelHint,
        effort = options.Effort,
        efforts = options.Efforts,
    };

    // state.agents[].setup: the stored choice as it is used, and the options it adds.
    public static object PageSetup(AgentDef agent, AgentSetupChoice? stored)
    {
        var used = Usable(agent, stored);
        return new { permission = used.Permission ?? "", model = used.Model ?? "", effort = used.Effort ?? "", args = Args(agent, used) };
    }
}

sealed class AgentAccount
{
    public string Agent { get; set; } = "";
    public string Name { get; set; } = "";
    // Empty for the main account, which uses the tool's normal folder.
    public string Home { get; set; } = "";
    // True when this account's new and resumed chats also open in the Claude app (AgentDef.RemoteControlArgs). Off is
    // missing (null), which is also what accounts.json from before had.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? RemoteControl { get; set; }

    [JsonIgnore] public bool IsMain => Home.Length == 0;

    [JsonIgnore] public string Key => Agent + "|" + Name;
}

// Extra accounts live in their own folder under AgentArcade\accounts. The tool keeps its login
// there itself, the same way it does in its normal folder; this app only points it at the folder.
static class AccountStore
{
    public static string FilePath => Path.Combine(AppSettings.Folder, "accounts.json");

    public static string AccountsFolder => Path.Combine(AppSettings.Folder, "accounts");

    public static bool ValidName(string name) => Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9._-]{0,30}$");

    public static List<AgentAccount> All(IReadOnlyList<AgentDef> agents)
    {
        var stored = LoadStored();
        var all = new List<AgentAccount>();
        foreach (var agent in agents)
        {
            var main = stored.FirstOrDefault(account => account.IsMain && account.Agent == agent.Name);
            all.Add(new AgentAccount { Agent = agent.Name, Name = "main", RemoteControl = main?.RemoteControl });
            if (agent.HomeVar.Length > 0)
                all.AddRange(stored.Where(account => !account.IsMain && account.Agent == agent.Name));
        }
        return all;
    }

    public static AgentAccount Add(AgentDef agent, string name)
    {
        if (agent.HomeVar.Length == 0) throw new InvalidOperationException(agent.Name + " can only use one account.");
        if (!ValidName(name)) throw new ArgumentException("Use a short name with letters, digits, dot, _ or -.");
        var stored = LoadStored();
        if (name.Equals("main", StringComparison.OrdinalIgnoreCase) ||
            stored.Any(account => !account.IsMain && account.Agent == agent.Name && account.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(agent.Name + " already has an account called " + name + ".");

        var slug = Regex.Replace(Path.GetFileNameWithoutExtension(agent.Command).ToLowerInvariant(), "[^a-z0-9]", "");
        var home = Path.Combine(AccountsFolder, slug + "-" + name);
        Directory.CreateDirectory(home);
        var added = new AgentAccount { Agent = agent.Name, Name = name, Home = home };
        stored.Add(added);
        Save(stored);
        return added;
    }

    // Settings → an account's ⋯ → Also open in the Claude app. The main account is in accounts.json only while it has
    // this on, as a record without Home (older versions skip those); off is stored as nothing.
    public static void SetRemoteControl(AgentAccount account, bool on)
    {
        var stored = LoadStored();
        var record = stored.FirstOrDefault(item => item.IsMain == account.IsMain && item.Agent == account.Agent && item.Name == account.Name);
        if (record == null)
        {
            if (!account.IsMain) throw new InvalidOperationException("That account no longer exists.");
            record = new AgentAccount { Agent = account.Agent, Name = "main" };
            stored.Add(record);
        }
        record.RemoteControl = on ? true : null;
        stored.RemoveAll(item => item.IsMain && item.RemoteControl == null);
        Save(stored);
        account.RemoteControl = record.RemoteControl;
    }

    public static void Remove(AgentAccount account)
    {
        if (account.IsMain) return;
        var stored = LoadStored();
        stored.RemoveAll(item => !item.IsMain && item.Agent == account.Agent && item.Name == account.Name);
        Save(stored);

        // Only a folder this app created is deleted; a tool's normal folder is never touched.
        var full = Path.GetFullPath(account.Home);
        var root = Path.GetFullPath(AccountsFolder) + Path.DirectorySeparatorChar;
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
            Directory.Delete(full, true);
    }

    // The environment that points the tool at this account's folder. For the main account the
    // variable is cleared, so a value inherited from elsewhere can't pick the wrong account.
    public static Dictionary<string, string?> EnvFor(AgentDef agent, AgentAccount account)
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        // When this app itself was started from inside an agent, that agent's session markers would
        // leak into every agent started here. Settings the user saved in Windows are kept.
        foreach (string name in Environment.GetEnvironmentVariables().Keys)
        {
            if (!SessionMarker.IsMatch(name)) continue;
            if (Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) != null) continue;
            if (Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine) != null) continue;
            env[name] = null;
        }
        if (agent.HomeVar.Length > 0) env[agent.HomeVar] = account.IsMain ? null : account.Home;
        return env;
    }

    static readonly Regex SessionMarker = new("^(CLAUDECODE|CLAUDE_CODE_|CODEX_|GEMINI_CLI|COPILOT_)", RegexOptions.IgnoreCase);

    // The extra accounts, and a main account's record while it has Remote Control on (SetRemoteControl).
    static List<AgentAccount> LoadStored()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<AgentAccount>>(File.ReadAllText(FilePath), Json.Options)?
                .Where(account => account.Agent.Length > 0 && (account.Home.Length > 0 || account.Name == "main"))
                .ToList() ?? new();
        }
        catch
        {
            return new();
        }
    }

    static void Save(List<AgentAccount> accounts)
    {
        Directory.CreateDirectory(AppSettings.Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(accounts, Json.Options));
    }
}

sealed record CommandResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

static class Commands
{
    public static async Task<CommandResult> RunAsync(
        string file,
        IReadOnlyList<string> args,
        int timeoutMs,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        var start = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        ApplyEnv(start, env);

        using var process = new Process { StartInfo = start };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        if (!process.Start()) throw new InvalidOperationException("Could not start: " + file);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!process.HasExited)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            await process.WaitForExitAsync();
            return new CommandResult(-1, stdout.ToString(), stderr.ToString(), true);
        }

        return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false);
    }

    public static void ApplyEnv(ProcessStartInfo start, IReadOnlyDictionary<string, string?>? env)
    {
        if (env == null) return;
        foreach (var (name, value) in env)
        {
            if (value == null) start.Environment.Remove(name);
            else start.Environment[name] = value;
        }
    }

    public static void StartVisible(string file, string arguments, string workingDirectory)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        })?.Dispose();
    }
}

static class Terminal
{
    static string LaunchFolder => Path.Combine(AppSettings.Folder, "launch");

    // Each start writes a small .cmd file that picks the account folder, goes to the project and runs
    // the agent. It gets a fresh name every time because cmd keeps reading a batch file while it runs.
    // Only that plain file name goes on the command line, so odd characters in folder names can't break it.
    public static void Start(string title, string projectPath, string exe, string args, IReadOnlyDictionary<string, string?> env)
    {
        Directory.CreateDirectory(LaunchFolder);
        var name = Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-') + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".cmd";
        File.WriteAllText(Path.Combine(LaunchFolder, name), Script(projectPath, exe, args, env), new UTF8Encoding(false));

        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var wt = Locator.FindOnPath("wt");
        if (wt == null)
        {
            Commands.StartVisible(cmd, "/d /k .\\" + name, LaunchFolder);
            return;
        }
        // All agents open as tabs in one Windows Terminal window named "agents". wt splits its
        // arguments on ';', so those are escaped. The script needs the .\ prefix; a bare name is not found there.
        var start = new ProcessStartInfo { FileName = wt, UseShellExecute = false, WorkingDirectory = LaunchFolder };
        foreach (var arg in new[] { "-w", "agents", "new-tab", "--title", title.Replace(";", "\\;"), "--suppressApplicationTitle",
                     "-d", Path.TrimEndingDirectorySeparator(LaunchFolder).Replace(";", "\\;"), cmd, "/d", "/k", ".\\" + name })
            start.ArgumentList.Add(arg);
        Process.Start(start)?.Dispose();
    }

    public static string Script(string projectPath, string exe, string args, IReadOnlyDictionary<string, string?> env)
    {
        // In a batch file % must be doubled. pushd also works for network folders, where cd can't.
        static string Batch(string value) => value.Replace("%", "%%");
        // A Windows Terminal tab doesn't get this app's environment, so the script itself tells cmd not to run a
        // program from the project folder when the agent's npm shim asks for a bare "node".
        var lines = new List<string> { "@echo off", "set \"NoDefaultCurrentDirectoryInExePath=1\"", "chcp 65001 >nul" };
        foreach (var (name, value) in env) lines.Add("set \"" + name + "=" + Batch(value ?? "") + "\"");
        lines.Add("pushd \"" + Batch(projectPath) + "\"");
        // Last line, so no "call" is needed for .cmd agents (call would double any ^ in the path).
        lines.Add("\"" + Batch(exe) + "\"" + (string.IsNullOrWhiteSpace(args) ? "" : " " + args));
        return string.Join("\r\n", lines) + "\r\n";
    }

    public static void CleanUp()
    {
        try
        {
            if (!Directory.Exists(LaunchFolder)) return;
            foreach (var file in Directory.EnumerateFiles(LaunchFolder, "*.cmd"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-1)) File.Delete(file);
            }
        }
        catch
        {
            // Old launch files are harmless; try again next time.
        }
    }
}

static class Locator
{
    // Reads PATH again from the registry, so tools installed while the app is open are found
    // and the terminals it starts can run them.
    public static void RefreshPath()
    {
        var parts = new List<string>();
        foreach (var source in new[]
        {
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH"),
        })
        {
            foreach (var dir in (source ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!parts.Contains(dir, StringComparer.OrdinalIgnoreCase)) parts.Add(dir);
            }
        }
        Environment.SetEnvironmentVariable("PATH", string.Join(';', parts));
    }

    // PATH first, then the vendor installer's folder, then AltPath. So a Codex installed with Install / update wins
    // over the older one inside the Codex desktop app (its AltPath) even before PATH is read again.
    // env: values for %NAME% in the folders, in place of this process's own (the self-check's fake folders).
    public static string? FindAgent(AgentDef agent, IReadOnlyDictionary<string, string>? env = null)
        => FindOnPath(agent.Command) ?? FindKnown(agent.Command, env) ?? FindAlt(Expand(agent.AltPath, env));

    static string Expand(string pattern, IReadOnlyDictionary<string, string>? env)
        => env == null ? pattern : Regex.Replace(pattern, "%([^%]+)%", match => env.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

    // When a file was last written (UTC), or DateTime.MinValue when that can't be read. An update in place keeps the
    // path, so this tells that the tool changed.
    public static DateTime WriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    // Where each vendor's own Windows installer puts its tool, for when that folder isn't on PATH (yet). Keyed by
    // command, so agents.json files from older versions find them too. Claude Code: its docs' uninstall steps; Codex:
    // its install.ps1; GitHub Copilot CLI: WinGet's link folder for portable apps; Antigravity CLI: its docs and install.ps1.
    static readonly Dictionary<string, string[]> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = [@"%USERPROFILE%\.local\bin\claude.exe"],
        ["codex"] = [@"%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe"],
        ["copilot"] = [@"%LOCALAPPDATA%\Microsoft\WinGet\Links\copilot.exe"],
        ["agy"] = [@"%LOCALAPPDATA%\agy\bin\agy.exe"],
    };

    public static string? FindKnown(string command, IReadOnlyDictionary<string, string>? env = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string name;
        try
        {
            if (Path.IsPathRooted(command)) return null;
            name = Path.GetFileNameWithoutExtension(command.Trim());
        }
        catch
        {
            return null;
        }
        if (!Known.TryGetValue(name, out var patterns)) return null;
        foreach (var pattern in patterns)
        {
            if (FindAlt(Expand(pattern, env)) is { } found) return found;
        }
        return null;
    }

    // PowerShell 7 (pwsh.exe): on PATH, or where its installer puts it.
    public static string? FindPwsh()
    {
        if (FindOnPath("pwsh") is { } pwsh && pwsh.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return pwsh;
        var programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        return File.Exists(programFiles) ? programFiles : null;
    }

    public static string? FindOnPath(string name)
    {
        if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            // npm also puts an extensionless shell script next to the .cmd; Windows cannot run that one.
            foreach (var ext in new[] { ".exe", ".cmd", ".bat" })
            {
                string candidate;
                try { candidate = Path.Combine(dir.Trim().Trim('"'), name + ext); }
                catch { continue; }
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    static string? FindAlt(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(pattern);
            var star = expanded.IndexOf(@"\*\", StringComparison.Ordinal);
            if (star < 0) return File.Exists(expanded) ? expanded : null;
            var root = expanded[..star];
            var rest = expanded[(star + 3)..];
            if (!Directory.Exists(root)) return null;
            return Directory.EnumerateDirectories(root)
                .Select(dir => Path.Combine(dir, rest))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static string? FindUnityEditor(string projectPath)
    {
        var versionFile = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionFile)) return null;
        string? version = null;
        foreach (var line in File.ReadAllLines(versionFile))
        {
            const string prefix = "m_EditorVersion:";
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            version = line[prefix.Length..].Trim();
            break;
        }
        if (string.IsNullOrWhiteSpace(version)) return null;

        var hub = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Unity", "Hub", "Editor", version, "Editor", "Unity.exe");
        return File.Exists(hub) ? hub : null;
    }

    public static bool UnityEditorIsRunning()
    {
        foreach (var process in Process.GetProcessesByName("Unity"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path != null && path.Contains(@"\Editor\Unity.exe", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // Some Unity helper processes hide their path. Only the editor path counts.
            }
            finally
            {
                process.Dispose();
            }
        }
        return false;
    }
}

// One thing an Install command needs that this PC doesn't have, and the page that tells how to get it.
sealed record InstallNeed(string Text, string Url);

// What an agent's Install command needs before it can run: npm and Node.js for an npm install, PowerShell 7 for
// GitHub Copilot CLI from npm, WinGet for a WinGet install. A missing one shows a message instead of a failing install.
static class Prerequisites
{
    public const string NodeUrl = "https://nodejs.org";
    public const string PowerShellUrl = "https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows";
    public const string WinGetUrl = "https://learn.microsoft.com/windows/package-manager/winget/";

    // The oldest Node.js each npm package supports: GitHub's Copilot CLI docs (22), the engines field of
    // @google/gemini-cli (20) and of @openai/codex (16).
    static readonly Dictionary<string, int> NodeMajor = new(StringComparer.OrdinalIgnoreCase) { ["copilot"] = 22, ["gemini"] = 20, ["codex"] = 16 };

    static readonly Regex Tool = new(@"^\s*(npm|winget)(?:\.cmd|\.exe)?(?:\s|$)", RegexOptions.IgnoreCase);

    // The tools found on this PC (full paths or null) and the first line `node --version` printed.
    public sealed record Found(string? Npm, string? Node, string? NodeVersion, string? Pwsh, string? WinGet);

    // "npm", "winget" or "" for an install that needs neither.
    public static string Kind(string install) => Tool.Match(install ?? "") is { Success: true } match ? match.Groups[1].Value.ToLowerInvariant() : "";

    // Node.js's major version from "v22.11.0"; null when it can't be read.
    public static int? NodeMajorOf(string? version)
        => Regex.Match(version ?? "", @"^\s*v?(\d+)\.") is { Success: true } match && int.TryParse(match.Groups[1].Value, out var major) ? major : null;

    public static List<InstallNeed> Missing(AgentDef agent, Found found)
    {
        var needs = new List<InstallNeed>();
        var command = Path.GetFileNameWithoutExtension(agent.Command.Trim()).ToLowerInvariant();
        switch (Kind(agent.Install))
        {
            case "npm":
                var min = NodeMajor.GetValueOrDefault(command);
                var newer = min > 0 ? " " + min + " or newer" : "";
                if (found.Npm == null || found.Node == null)
                {
                    needs.Add(new(agent.Name + " installs with npm, which comes with Node.js" + newer + ". Install Node.js (the LTS version) from nodejs.org, then click Install again.", NodeUrl));
                }
                else if (min > 0 && NodeMajorOf(found.NodeVersion) is var major && (major ?? 0) < min)
                {
                    var has = major == null ? "this PC's Node.js didn't say its version" : "this PC has Node.js " + found.NodeVersion!.Trim();
                    needs.Add(new(agent.Name + " needs Node.js" + newer + "; " + has + ". Update Node.js from nodejs.org, then click Install again.", NodeUrl));
                }
                if (command == "copilot" && found.Pwsh == null)
                    needs.Add(new("GitHub Copilot CLI needs PowerShell 7 (pwsh.exe) on Windows. Install it from Microsoft, then click Install again.", PowerShellUrl));
                break;
            case "winget":
                if (found.WinGet == null)
                    needs.Add(new("This install uses WinGet, which comes with Windows 11's App Installer but isn't on this PC (Windows Sandbox has none). " +
                        "Install App Installer, or change the Install command with Edit agent list.", WinGetUrl));
                break;
        }
        return needs;
    }

    // Looks for the tools now, so ones installed while the app is open count. Node.js runs by its full path.
    public static async Task<List<InstallNeed>> CheckAsync(AgentDef agent)
    {
        var kind = Kind(agent.Install);
        if (kind.Length == 0) return new();
        Locator.RefreshPath();
        var node = kind == "npm" ? Locator.FindOnPath("node") : null;
        string? version = null;
        if (node != null)
        {
            try
            {
                var result = await Commands.RunAsync(node, ["--version"], 10000);
                if (!result.TimedOut) version = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            }
            catch
            {
                // A Node.js that doesn't start says no version.
            }
        }
        return Missing(agent, new Found(Locator.FindOnPath("npm"), node, version, Locator.FindPwsh(), kind == "winget" ? Locator.FindOnPath("winget") : null));
    }

    public static string Text(IEnumerable<InstallNeed> needs) => string.Join(" ", needs.Select(need => need.Text + " (" + need.Url + ")"));
}

// Written: when the file at Path was last written as it was checked (Locator.WriteTime).
sealed record ToolStatus(AgentDef Agent, string? Path, string Version, DateTime Written = default)
{
    // Whether exe is the file this status was checked on, unchanged since.
    public bool Is(string exe) => string.Equals(Path, exe, StringComparison.OrdinalIgnoreCase) && Written == Locator.WriteTime(exe);
}

// SignedIn is null when the tool can't tell; Note then says why.
sealed record AccountInfo(bool? SignedIn, string Email = "", string Plan = "", string Note = "")
{
    public string Text => SignedIn switch
    {
        true when Email.Length == 0 => Plan.Length == 0 ? "signed in" : "signed in · " + Plan,
        true => Plan.Length == 0 ? Email : Email + " · " + Plan,
        false => Note.Length == 0 ? "not signed in" : "not signed in · " + Note,
        _ => Note,
    };
}

static class Probe
{
    public static async Task<ToolStatus> ToolAsync(AgentDef agent)
    {
        var path = Locator.FindAgent(agent);
        if (path == null) return new ToolStatus(agent, null, "");
        var written = Locator.WriteTime(path);
        try
        {
            var result = await Commands.RunAsync(path, ["--version"], 20000);
            var first = (result.Stdout + "\n" + result.Stderr)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "";
            return new ToolStatus(agent, path, result.TimedOut ? "no answer" : TextGuard.Redact(first), written);
        }
        catch (Exception ex)
        {
            return new ToolStatus(agent, path, TextGuard.Redact(ex.Message), written);
        }
    }

    // Asks the tool itself which account is signed in. No login or token file is opened.
    public static async Task<AccountInfo> AccountAsync(AgentDef agent, AgentAccount account, string? exe)
    {
        if (exe == null) return new AccountInfo(null);
        var env = AccountStore.EnvFor(agent, account);
        try
        {
            return agent.AccountCheck switch
            {
                "claude" => await ClaudeAsync(exe, env),
                "codex" => await CodexAsync(exe, env),
                "gemini" => Gemini(account),
                "cursor" => await CursorAsync(exe, env),
                "ollama" => await OllamaAsync(),
                _ => new AccountInfo(null, Note: "check inside the agent"),
            };
        }
        catch (Exception ex)
        {
            return new AccountInfo(null, Note: "couldn't check: " + TextGuard.Redact(ex.Message));
        }
    }

    static async Task<AccountInfo> ClaudeAsync(string exe, IReadOnlyDictionary<string, string?> env)
    {
        var result = await Commands.RunAsync(exe, ["auth", "status", "--json"], 20000, env);
        if (result.TimedOut) return new AccountInfo(null, Note: "no answer");
        var start = result.Stdout.IndexOf('{');
        if (start < 0) return new AccountInfo(false);
        using var doc = JsonDocument.Parse(result.Stdout[start..]);
        var root = doc.RootElement;
        if (!root.TryGetProperty("loggedIn", out var loggedIn) || loggedIn.ValueKind != JsonValueKind.True) return new AccountInfo(false);
        return new AccountInfo(true, Json.Str(root, "email"), Json.Str(root, "subscriptionType"));
    }

    // Codex only names the account through its app-server, the same interface the Codex app uses.
    static async Task<AccountInfo> CodexAsync(string exe, IReadOnlyDictionary<string, string?> env)
    {
        var start = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("app-server");
        Commands.ApplyEnv(start, env);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Codex.");
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        using var timeout = new CancellationTokenSource(20000);
        try
        {
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"agent-arcade\",\"title\":\"Agent Arcade\",\"version\":\"1.0.0\"}}}");
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            await process.StandardInput.WriteLineAsync("{\"method\":\"account/read\",\"id\":2,\"params\":{\"refreshToken\":false}}");
            await process.StandardInput.FlushAsync();

            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (!line.TrimStart().StartsWith('{')) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || id.GetInt32() != 2) continue;
                if (!root.TryGetProperty("result", out var result)) return new AccountInfo(null, Note: "couldn't check");
                if (!result.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object) return new AccountInfo(false);
                if (Json.Str(account, "type") == "apiKey") return new AccountInfo(true, Plan: "API key");
                return new AccountInfo(true, Json.Str(account, "email"), Json.Str(account, "planType"));
            }
            return new AccountInfo(null, Note: "couldn't check");
        }
        catch (OperationCanceledException)
        {
            return new AccountInfo(null, Note: "no answer");
        }
        finally
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }
    }

    // Cursor's own status command prints "Logged in as <email>".
    static async Task<AccountInfo> CursorAsync(string exe, IReadOnlyDictionary<string, string?> env)
    {
        var result = await Commands.RunAsync(exe, ["status"], 20000, env);
        if (result.TimedOut) return new AccountInfo(null, Note: "no answer");
        var text = Regex.Replace(result.Stdout + "\n" + result.Stderr, @"\x1b\[[0-9;?]*[ -/]*[@-~]", "");
        if (Regex.IsMatch(text, @"not (logged|signed) in|logged out", RegexOptions.IgnoreCase)) return new AccountInfo(false);
        var email = Regex.Match(text, @"(?:logged|signed) in as\s+(\S+@[^\s,;]+)", RegexOptions.IgnoreCase);
        if (email.Success) return new AccountInfo(true, email.Groups[1].Value.TrimEnd('.', ')'));
        return Regex.IsMatch(text, @"(logged|signed) in", RegexOptions.IgnoreCase)
            ? new AccountInfo(true)
            : new AccountInfo(null, Note: "check inside the agent");
    }

    // Gemini keeps the signed-in address in google_accounts.json, which holds only email addresses.
    static AccountInfo Gemini(AgentAccount account)
    {
        var home = account.IsMain ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : account.Home;
        var file = Path.Combine(home, ".gemini", "google_accounts.json");
        if (!File.Exists(file)) return new AccountInfo(false, Note: "API key or Code Assist: sign in on first start");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var email = Json.Str(doc.RootElement, "active");
        return email.Length == 0 ? new AccountInfo(false, Note: "API key or Code Assist: sign in on first start") : new AccountInfo(true, email);
    }

    // Ollama's default address. Only GET /api/version is ever asked: nothing is downloaded, started or changed.
    public const string OllamaUrl = "http://127.0.0.1:11434";

    // A model in Ollama has no account, so this says whether Ollama answers, and else whether it is installed at all.
    static async Task<AccountInfo> OllamaAsync()
    {
        var version = await OllamaVersionAsync(OllamaUrl);
        if (version != null) return OllamaInfo(version, true);
        // On PATH, or where OllamaSetup.exe puts it before PATH is read again.
        var installed = Locator.FindOnPath("ollama") != null
            || File.Exists(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Ollama\ollama.exe"));
        return OllamaInfo(null, installed);
    }

    // version: what Ollama answered ("" without a usable version), or null when nothing answered.
    public static AccountInfo OllamaInfo(string? version, bool installed) => version switch
    {
        null when !installed => new AccountInfo(null, Note: "Ollama isn't installed: get it from ollama.com"),
        null => new AccountInfo(null, Note: "Ollama isn't running: start it, then Check accounts again"),
        "" => new AccountInfo(true, Plan: "Ollama"),
        _ => new AccountInfo(true, Plan: "Ollama " + version),
    };

    // Ollama's {"version":"0.32.12"}, 3 s at most and without a proxy. null when nothing (or not Ollama) answered.
    public static async Task<string?> OllamaVersionAsync(string baseUrl, int timeoutMs = 3000)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false, UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs),
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs), MaxResponseContentBufferSize = 64 * 1024 };
            using var response = await http.GetAsync(baseUrl + "/api/version");
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("version", out _)) return null;
            var version = Json.Str(doc.RootElement, "version");
            // It reaches the page and the phone, so only a plain version number is kept.
            return Regex.IsMatch(version, @"^[0-9A-Za-z.+-]{1,32}\z") ? version : "";
        }
        catch
        {
            return null;
        }
    }
}

// One set of project instructions for every agent. AGENTS.md (or CLAUDE.md) stays the
// single source; the other agents get a small file that points to it. Nothing is overwritten.
static class SharedInstructions
{
    // A Unity project (it has ProjectSettings/ProjectVersion.txt) gets the Unity rules; any other project a general start.
    static string Starter(string projectPath) => File.Exists(Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt"))
        ? "# Project instructions for AI agents\n\n" +
          "## The game\n" +
          "- What it is:\n" +
          "- Unity version: see ProjectSettings/ProjectVersion.txt\n\n" +
          "## Rules\n" +
          "- Don't open or edit Library/, Temp/ or Logs/, and don't edit scene or prefab files unless asked.\n" +
          "- Make small changes and say which files you changed.\n" +
          "- Before you stop, write progress to HANDOFF.md: what changed, what works, next steps.\n"
        : "# Project instructions for AI agents\n\n" +
          "## The project\n" +
          "- What it is:\n" +
          "- How to build and test it:\n\n" +
          "## Rules\n" +
          "- Make small changes and say which files you changed.\n" +
          "- Before you stop, write progress to HANDOFF.md: what changed, what works, next steps.\n";

    public static List<string> Ensure(string projectPath)
    {
        var done = new List<string>();
        var agents = Path.Combine(projectPath, "AGENTS.md");
        var claude = Path.Combine(projectPath, "CLAUDE.md");
        var gemini = Path.Combine(projectPath, "GEMINI.md");

        if (!File.Exists(agents) && File.Exists(claude))
        {
            // CLAUDE.md is the source. Codex and Copilot cannot import files, so they get a pointer.
            Write(agents, "# Project instructions\n\nThe instructions for this project are in CLAUDE.md. Read that file before you start.\n", done);
            Write(gemini, "# Gemini CLI instructions\n\n@./CLAUDE.md\n", done);
            return done;
        }

        if (!File.Exists(agents)) Write(agents, Starter(projectPath), done);
        Write(claude, "# Claude Code instructions\n\nThe shared instructions for every AI agent are in AGENTS.md:\n\n@AGENTS.md\n", done);
        Write(gemini, "# Gemini CLI instructions\n\nThe shared instructions for every AI agent are in AGENTS.md:\n\n@./AGENTS.md\n", done);
        return done;
    }

    static void Write(string path, string text, List<string> done)
    {
        if (File.Exists(path)) return;
        File.WriteAllText(path, text.Replace("\n", Environment.NewLine), new UTF8Encoding(false));
        done.Add(Path.GetFileName(path));
    }
}
