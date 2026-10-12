using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AgentArcade;

static class TextGuard
{
    static readonly Regex Token = new(@"sk-ant-[A-Za-z0-9_\-]+", RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return Token.Replace(value, "sk-ant-[hidden]");
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
    // The active workspace. Projects lists every workspace shown in the sidebar.
    public string ProjectPath { get; set; } = @"C:\Users\daans\HimalayanExpedition";
    public List<string> Projects { get; set; } = new();

    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string Folder => Path.Combine(LocalAppData, "AgentArcade");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        // The second path holds the settings from before the app was renamed.
        foreach (var path in new[] { FilePath, Path.Combine(LocalAppData, "ClaudeUnitySwitch", "settings.json") })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                if (loaded != null && !string.IsNullOrWhiteSpace(loaded.ProjectPath)) return loaded.WithActiveListed();
            }
            catch
            {
                // A broken settings file falls back to the Unity project default.
            }
        }
        return new AppSettings().WithActiveListed();
    }

    AppSettings WithActiveListed()
    {
        Projects = Projects.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!Projects.Contains(ProjectPath, StringComparer.OrdinalIgnoreCase)) Projects.Insert(0, ProjectPath);
        return this;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json.Options));
    }
}

// One AI coding agent. Every agent is the vendor's own command-line tool, so signing in
// always happens in that tool's official login flow and this app never sees a token.
sealed class AgentDef
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    // Where to look when Command isn't on PATH. One "*" folder is allowed; the newest match wins.
    public string AltPath { get; set; } = "";
    public string Args { get; set; } = "";
    public string Login { get; set; } = "";
    public string LoginHint { get; set; } = "";
    public string Install { get; set; } = "";
    // The tool's own setting for "keep my files in this folder". Extra accounts need it.
    public string HomeVar { get; set; } = "";
    // How to ask the tool which account is signed in: claude, codex, gemini, cursor or empty.
    public string AccountCheck { get; set; } = "";
    // Chat history. {id} is the chat's id. NewChatArgs gives a new chat that id, so ResumeChatArgs can
    // open exactly that chat later; ResumeArgs opens the tool's own "resume" (its latest chat or a list).
    // Missing (null) takes the built-in value for the same command; "" turns it off.
    public string? NewChatArgs { get; set; }
    public string? ResumeChatArgs { get; set; }
    public string? ResumeArgs { get; set; }
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
        },
        new()
        {
            Name = "Codex",
            Command = "codex",
            AltPath = @"%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe",
            Login = "login",
            LoginHint = "Choose \"Sign in with ChatGPT\" in the browser window that opens.",
            Install = "npm install -g @openai/codex",
            HomeVar = "CODEX_HOME",
            AccountCheck = "codex",
            ResumeArgs = "resume",
        },
        new()
        {
            Name = "Gemini CLI",
            Command = "gemini",
            LoginHint = "Choose \"Sign in with Google\" when Gemini starts. To change account later, type /auth inside Gemini.",
            Install = "npm install -g @google/gemini-cli",
            HomeVar = "GEMINI_CLI_HOME",
            AccountCheck = "gemini",
            ResumeArgs = "--resume",
        },
        new()
        {
            Name = "GitHub Copilot CLI",
            Command = "copilot",
            LoginHint = "Type /login inside Copilot and follow the steps.",
            Install = "npm install -g @github/copilot",
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
            var builtIn = defaults.FirstOrDefault(item => item.Command.Equals(agent.Command, StringComparison.OrdinalIgnoreCase));
            if (builtIn == null) continue;
            agent.NewChatArgs ??= builtIn.NewChatArgs;
            agent.ResumeChatArgs ??= builtIn.ResumeChatArgs;
            agent.ResumeArgs ??= builtIn.ResumeArgs;
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
            !agents.Any(agent => agent.Name.Equals(builtIn.Name, StringComparison.OrdinalIgnoreCase) ||
                                 agent.Command.Equals(builtIn.Command, StringComparison.OrdinalIgnoreCase))).ToList();
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
    // The agent's own id for this chat, when this app chose it (so it can be resumed exactly).
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
            return JsonSerializer.Deserialize<List<ChatRecord>>(File.ReadAllText(FilePath), Json.Options)?
                .Where(chat => chat.Id.Length > 0 && chat.Agent.Length > 0)
                .ToList() ?? new();
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

    // The arguments for a new chat. When the agent lets this app pick the chat's id, it is remembered.
    public static string New(AgentDef agent, ChatRecord chat)
    {
        var extra = agent.NewChatArgs ?? "";
        if (!extra.Contains("{id}", StringComparison.Ordinal)) return Join(agent.Args, extra);
        chat.ChatId = chat.Id;
        return Join(agent.Args, extra.Replace("{id}", chat.Id, StringComparison.Ordinal));
    }

    // Null when the agent can't continue a chat.
    public static string? Resume(AgentDef agent, ChatRecord chat) => Kind(agent, chat) switch
    {
        "exact" => Join(agent.Args, agent.ResumeChatArgs!.Replace("{id}", chat.ChatId, StringComparison.Ordinal)),
        "agent" => Join(agent.Args, agent.ResumeArgs!),
        _ => null,
    };

    // exact: this very chat; agent: the agent's own resume, which may pick its latest chat or ask; "": none.
    public static string Kind(AgentDef? agent, ChatRecord chat)
    {
        if (agent == null) return "";
        if (chat.ChatId.Length > 0 && !string.IsNullOrWhiteSpace(agent.ResumeChatArgs)) return "exact";
        return string.IsNullOrWhiteSpace(agent.ResumeArgs) ? "" : "agent";
    }
}

sealed class AgentAccount
{
    public string Agent { get; set; } = "";
    public string Name { get; set; } = "";
    // Empty for the main account, which uses the tool's normal folder.
    public string Home { get; set; } = "";

    [JsonIgnore] public bool IsMain => Home.Length == 0;
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
        var extra = LoadExtra();
        var all = new List<AgentAccount>();
        foreach (var agent in agents)
        {
            all.Add(new AgentAccount { Agent = agent.Name, Name = "main" });
            if (agent.HomeVar.Length > 0)
                all.AddRange(extra.Where(account => account.Agent == agent.Name));
        }
        return all;
    }

    public static AgentAccount Add(AgentDef agent, string name)
    {
        if (agent.HomeVar.Length == 0) throw new InvalidOperationException(agent.Name + " can only use one account.");
        if (!ValidName(name)) throw new ArgumentException("Use a short name with letters, digits, dot, _ or -.");
        var extra = LoadExtra();
        if (name.Equals("main", StringComparison.OrdinalIgnoreCase) ||
            extra.Any(account => account.Agent == agent.Name && account.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(agent.Name + " already has an account called " + name + ".");

        var slug = Regex.Replace(Path.GetFileNameWithoutExtension(agent.Command).ToLowerInvariant(), "[^a-z0-9]", "");
        var home = Path.Combine(AccountsFolder, slug + "-" + name);
        Directory.CreateDirectory(home);
        var added = new AgentAccount { Agent = agent.Name, Name = name, Home = home };
        extra.Add(added);
        Save(extra);
        return added;
    }

    public static void Remove(AgentAccount account)
    {
        if (account.IsMain) return;
        var extra = LoadExtra();
        extra.RemoveAll(item => item.Agent == account.Agent && item.Name == account.Name);
        Save(extra);

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

    static List<AgentAccount> LoadExtra()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<AgentAccount>>(File.ReadAllText(FilePath), Json.Options)?
                .Where(account => account.Home.Length > 0 && account.Agent.Length > 0)
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
        var lines = new List<string> { "@echo off", "chcp 65001 >nul" };
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

    public static string? FindAgent(AgentDef agent) => FindOnPath(agent.Command) ?? FindAlt(agent.AltPath);

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

sealed record ToolStatus(AgentDef Agent, string? Path, string Version);

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
        try
        {
            var result = await Commands.RunAsync(path, ["--version"], 20000);
            var first = (result.Stdout + "\n" + result.Stderr)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "";
            return new ToolStatus(agent, path, result.TimedOut ? "no answer" : TextGuard.Redact(first));
        }
        catch (Exception ex)
        {
            return new ToolStatus(agent, path, TextGuard.Redact(ex.Message));
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
        if (!File.Exists(file)) return new AccountInfo(false, Note: "sign in on first start");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var email = Json.Str(doc.RootElement, "active");
        return email.Length == 0 ? new AccountInfo(false, Note: "sign in on first start") : new AccountInfo(true, email);
    }
}

// One set of project instructions for every agent. AGENTS.md (or CLAUDE.md) stays the
// single source; the other agents get a small file that points to it. Nothing is overwritten.
static class SharedInstructions
{
    const string Starter =
        "# Project instructions for AI agents\n\n" +
        "## The game\n" +
        "- What it is:\n" +
        "- Unity version: see ProjectSettings/ProjectVersion.txt\n\n" +
        "## Rules\n" +
        "- Don't open or edit Library/, Temp/ or Logs/, and don't edit scene or prefab files unless asked.\n" +
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

        if (!File.Exists(agents)) Write(agents, Starter, done);
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
