using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentArcade;

// Settings → MCP servers: the MCP servers each installed agent has, and whether they answer. Every agent account runs its
// CLI's own documented list command in the selected project folder (its own config folder through AccountStore.EnvFor),
// with no window, cmd's current-folder lookup off and 20 s each; a slow or broken one never holds up the others. Only each
// server's name, scope and status are kept. A server's command line, URL, arguments, headers and environment, which the
// CLIs print too, are never returned or logged; the raw output is dropped once parsed.
//
// The list commands, checked on 16 Oct 2026 in each vendor's docs (and their output shape against the installed CLIs):
// - Claude Code: `claude mcp list`, a health status per server (code.claude.com/docs/en/mcp). Lines are
//   "name: <command or url> - ✔ Connected" (! Needs authentication, ✘ Failed to connect, ⏸ Pending approval, ⊘ Disabled
//   for this project), an issue may follow after " — ". It shows no scope; only .mcp.json servers wait for approval.
// - Codex: `codex mcp list --json`, "machine-readable output" (learn.chatgpt.com/docs/developer-commands): an array of
//   { name, enabled, auth_status, transport, ... }. It reads the config without connecting, so a server's status is unknown
//   unless it is off or not signed in.
// - Gemini CLI: `gemini mcp list` (geminicli.com/docs/tools/mcp-server): "✓ name: <command or url> (stdio) - Connected",
//   also Connecting, Disconnected, Disabled and Blocked. It prints to stderr.
// - GitHub Copilot CLI: `copilot mcp list`, "grouped by source", a disabled server marked "(disabled)"
//   (docs.github.com/en/copilot/reference/cli-command-reference): "User servers:" then "  name (http)" lines.
// - Cursor: `cursor-agent mcp list`, names and status (cursor.com/docs/cli/mcp): "name: ready" (requires_authentication,
//   loading, disabled, not loaded (needs approval), Error: ...). Only project servers wait for approval.
// - Antigravity CLI: only /mcp inside the CLI, no command-line list (antigravity.google/docs/cli/reference): "not available".
sealed record McpServerInfo(string Name, string? Scope, string Status);

sealed record McpAgentResult(string Agent, string Command, string Account, bool Ok, string? Note, IReadOnlyList<McpServerInfo> Servers);

// What one list printed, as read by its parser. Note is a fixed sentence of ours, never text from the CLI.
sealed record McpList(IReadOnlyList<McpServerInfo> Servers, string? Note = null);

sealed record McpReport(DateTimeOffset At, IReadOnlyList<McpAgentResult> Agents)
{
    // The page's mcpList message (web/mcp.js reads it).
    public object Message() => new
    {
        type = "mcpList",
        at = At.ToString("o"),
        agents = Agents.Select(agent => new
        {
            agent = agent.Agent,
            command = agent.Command,
            account = agent.Account,
            ok = agent.Ok,
            note = agent.Note,
            servers = agent.Servers.Select(server => new { name = server.Name, scope = server.Scope, status = server.Status }).ToList(),
        }).ToList(),
    };
}

static class Mcp
{
    public const int TimeoutMs = 20_000;

    // The statuses web/mcp.js shows: connected, failed, needs sign-in, off, not checked.
    public const string Connected = "connected", Failed = "failed", NeedsAuth = "needsAuth", Disabled = "disabled", Unknown = "unknown";

    public const string NotAvailable = "not available";
    public const string Unreadable = "couldn't read the list";
    public const string NoAnswer = "no answer within 20 s";
    public const string NotRun = "couldn't run the list";

    // A CLI's list command and its parser. Parse gets stdout and stderr and returns null when it can't read them.
    public sealed record Lister(string[] Args, Func<string, string, McpList?> Parse);

    // By the built-in agent's command (AgentStore.Defaults). Antigravity CLI (agy) has no command-line list.
    public static readonly Dictionary<string, Lister> Listers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = new(["mcp", "list"], (stdout, stderr) => ParseClaude(stdout + "\n" + stderr)),
        ["codex"] = new(["mcp", "list", "--json"], (stdout, _) => ParseCodex(stdout)),
        ["gemini"] = new(["mcp", "list"], (stdout, stderr) => ParseGemini(stdout + "\n" + stderr)),
        ["copilot"] = new(["mcp", "list"], (stdout, stderr) => ParseCopilot(stdout + "\n" + stderr)),
        ["cursor-agent"] = new(["mcp", "list"], (stdout, stderr) => ParseCursor(stdout + "\n" + stderr)),
    };

    static readonly object Gate = new();
    static (string Key, Task<McpReport> Run)? _running;

    // Lists every installed built-in agent's servers, per account, in folder. A second request while the same list still
    // runs gets that run's result, so "Check again" clicked twice starts nothing more.
    public static Task<McpReport> ListAsync(IReadOnlyList<AgentDef> agents, IReadOnlyList<AgentAccount> accounts, string folder)
    {
        var key = folder + "\n" + string.Join("\n", accounts.Select(account => account.Key + "|" + account.Home));
        lock (Gate)
        {
            if (_running is { } running && running.Key == key && !running.Run.IsCompleted) return running.Run;
            var run = Task.Run(() => ListAllAsync(agents, accounts, folder));
            _running = (key, run);
            return run;
        }
    }

    static async Task<McpReport> ListAllAsync(IReadOnlyList<AgentDef> agents, IReadOnlyList<AgentAccount> accounts, string folder)
    {
        var builtIn = AgentStore.Defaults();
        var checks = new List<Task<McpAgentResult>>();
        foreach (var agent in agents)
        {
            if (!builtIn.Any(item => item.Command.Equals(agent.Command, StringComparison.OrdinalIgnoreCase))) continue;
            string? exe;
            try { exe = Locator.FindAgent(agent); }
            catch { exe = null; }
            if (exe == null) continue;
            foreach (var account in accounts.Where(item => item.Agent == agent.Name))
            {
                if (!Listers.TryGetValue(agent.Command, out var lister))
                {
                    checks.Add(Task.FromResult(new McpAgentResult(agent.Name, agent.Command, account.Name, false, NotAvailable, [])));
                    continue;
                }
                if (!Directory.Exists(folder))
                {
                    checks.Add(Task.FromResult(new McpAgentResult(agent.Name, agent.Command, account.Name, false, "the project folder doesn't exist", [])));
                    continue;
                }
                checks.Add(CheckAsync(agent.Name, agent.Command, account.Name, exe, lister, folder, AccountStore.EnvFor(agent, account), TimeoutMs));
            }
        }
        var results = await Task.WhenAll(checks);
        return new McpReport(DateTimeOffset.Now, results);
    }

    // One account's list. Never throws: whatever goes wrong becomes ok false with a fixed note.
    internal static async Task<McpAgentResult> CheckAsync(
        string agent, string command, string account, string exe, Lister lister, string folder,
        IReadOnlyDictionary<string, string?> env, int timeoutMs)
    {
        McpAgentResult Fail(string note) => new(agent, command, account, false, note, []);
        Output output;
        try
        {
            output = await RunAsync(exe, lister.Args, folder, env, timeoutMs);
        }
        catch
        {
            return Fail(NotRun);
        }
        if (output.TimedOut) return Fail(NoAnswer);
        McpList? list;
        try
        {
            list = lister.Parse(output.Stdout, output.Stderr);
        }
        catch
        {
            list = null;
        }
        return list == null ? Fail(Unreadable) : new McpAgentResult(agent, command, account, true, list.Note, list.Servers);
    }

    sealed record Output(string Stdout, string Stderr, bool TimedOut);

    // Output beyond this is read and dropped, so a chatty CLI can't fill memory.
    const int MaxChars = 1 << 20;

    static async Task<Output> RunAsync(string exe, IReadOnlyList<string> args, string folder, IReadOnlyDictionary<string, string?> env, int timeoutMs)
    {
        var start = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = folder,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        Commands.ApplyEnv(start, env);
        // npm's .cmd shims run a bare "node", and cmd looks in its current folder (the project) first; this turns that off.
        start.Environment["NoDefaultCurrentDirectoryInExePath"] = "1";
        start.Environment["NO_COLOR"] = "1";

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();
        if (!process.Start()) throw new InvalidOperationException("not started");
        // A list never needs input; a CLI that asks anyway reads end-of-input instead of waiting.
        try { process.StandardInput.Close(); } catch { /* already gone */ }
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var reading = Task.WhenAll(ReadAsync(process.StandardOutput, stdout), ReadAsync(process.StandardError, stderr));

        using var wait = new CancellationTokenSource();
        var timedOut = await Task.WhenAny(exited.Task, Task.Delay(timeoutMs, wait.Token)) != exited.Task;
        wait.Cancel();
        if (timedOut)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }
        // A server the CLI started can keep the output open after the CLI ended; what was read by then counts.
        await Task.WhenAny(reading, Task.Delay(2000));
        string Text(StringBuilder text) { lock (text) return text.ToString(); }
        return new Output(Text(stdout), Text(stderr), timedOut);
    }

    static async Task ReadAsync(StreamReader reader, StringBuilder text)
    {
        var buffer = new char[8192];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                lock (text)
                {
                    if (text.Length < MaxChars) text.Append(buffer, 0, Math.Min(read, MaxChars - text.Length));
                }
            }
        }
        catch
        {
            // The process was killed or disposed.
        }
    }

    // ---------- parsers: name, scope and status only ----------

    static readonly Regex Ansi = new(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[@-Z\\-_])", RegexOptions.Compiled);
    static readonly Regex Empty = new(@"\bno (?:mcp )?servers\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static string[] Lines(string text) => Ansi.Replace(text, "").Replace("\r", "").Split('\n');

    // A server's name as the CLI printed it. Anything that doesn't look like a name (a URL, control characters, a whole
    // line) is skipped, and a key-shaped name is hidden like everywhere else (TextGuard).
    static void Add(List<McpServerInfo> servers, string name, string? scope, string status)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > 120 || name.Contains("://", StringComparison.Ordinal) || name.Any(char.IsControl)) return;
        servers.Add(new McpServerInfo(TextGuard.Redact(name), scope, status));
    }

    // A name runs to the first ": " (Claude Code's plugin servers are called plugin:<plugin>:<server>).
    const string Name = @"(?<name>[^\s:](?:[^:]|:(?! ))*?)";

    static readonly Regex ClaudeServer = new(
        "^" + Name + @": .*? - (?:\S{1,2} )?(?<status>Connected(?<warn> \S tools fetch failed)?|Needs authentication|Not configured|Failed to connect|Connection error|Pending approval|Disabled for this project|Rejected)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // A policy error is a cross and its own words.
    static readonly Regex ClaudeCross = new("^" + Name + @": .*? - [✘✗×] \S", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static McpList? ParseClaude(string text)
    {
        var servers = new List<McpServerInfo>();
        foreach (var line in Lines(text))
        {
            if (ClaudeServer.Match(line) is { Success: true } match)
            {
                var (status, scope) = match.Groups["status"].Value switch
                {
                    "Connected" => (match.Groups["warn"].Success ? Failed : Connected, (string?)null),
                    "Needs authentication" => (NeedsAuth, null),
                    "Pending approval" => (Unknown, "project"),
                    "Disabled for this project" => (Disabled, null),
                    "Rejected" => (Disabled, "project"),
                    _ => (Failed, null),
                };
                Add(servers, match.Groups["name"].Value, scope, status);
            }
            else if (ClaudeCross.Match(line) is { Success: true } cross)
            {
                Add(servers, cross.Groups["name"].Value, null, Failed);
            }
        }
        if (servers.Count > 0) return new McpList(servers);
        return Empty.IsMatch(Ansi.Replace(text, "")) ? new McpList([]) : null;
    }

    public static McpList? ParseCodex(string stdout)
    {
        // The JSON array is the whole output, or starts on its own line after a warning.
        var text = Ansi.Replace(stdout, "").Replace("\r", "").Trim();
        var from = text.StartsWith('[') ? 0 : text.IndexOf("\n[", StringComparison.Ordinal) is var line and >= 0 ? line + 1 : -1;
        if (from < 0) return Empty.IsMatch(text) ? new McpList([]) : null;
        using var doc = JsonDocument.Parse(text[from..]);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        var servers = new List<McpServerInfo>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            // Only these three fields are read; transport (command, args, env, url, headers) is never touched.
            var off = item.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False;
            var status = off ? Disabled : Json.Str(item, "auth_status") == "not_logged_in" ? NeedsAuth : Unknown;
            Add(servers, Json.Str(item, "name"), null, status);
        }
        return new McpList(servers, servers.Count > 0 ? "Codex lists its servers without connecting to them" : null);
    }

    static readonly Regex GeminiServer = new(
        @"^\s*\S{1,2}\s+" + Name + @"(?: \(from [^)]*\))?: .* - (?<status>Connected|Connecting|Disconnected|Disabled|Blocked)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static McpList? ParseGemini(string text)
    {
        var servers = new List<McpServerInfo>();
        var heading = false;
        var untrusted = false;
        foreach (var line in Lines(text))
        {
            if (line.StartsWith("Configured MCP servers", StringComparison.Ordinal)) heading = true;
            if (line.Contains("this folder is untrusted", StringComparison.OrdinalIgnoreCase)) untrusted = true;
            if (GeminiServer.Match(line) is not { Success: true } match) continue;
            var status = match.Groups["status"].Value switch
            {
                "Connected" => Connected,
                "Disconnected" => Failed,
                "Disabled" or "Blocked" => Disabled,
                _ => Unknown,
            };
            Add(servers, match.Groups["name"].Value, null, status);
        }
        var note = untrusted ? "Gemini CLI keeps its servers off in a folder it doesn't trust" : null;
        if (servers.Count > 0) return new McpList(servers, note);
        return !heading && Empty.IsMatch(Ansi.Replace(text, "")) ? new McpList([], note) : null;
    }

    static readonly Regex CopilotGroup = new(@"^(?<source>\S.*?) servers:\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex CopilotServer = new(@"^\s+(?<name>\S(?:.*?\S)?) \((?<type>[A-Za-z][\w-]*)\)(?<rest>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static McpList? ParseCopilot(string text)
    {
        var servers = new List<McpServerInfo>();
        string? source = null;
        var grouped = false;
        foreach (var line in Lines(text))
        {
            if (CopilotGroup.Match(line) is { Success: true } group)
            {
                grouped = true;
                var name = group.Groups["source"].Value;
                source = Regex.IsMatch(name, @"^user\b", RegexOptions.IgnoreCase) ? "user"
                    : Regex.IsMatch(name, @"^(workspace|project|repo)", RegexOptions.IgnoreCase) ? "project"
                    : Regex.IsMatch(name, @"^local\b", RegexOptions.IgnoreCase) ? "local"
                    : null;
                continue;
            }
            if (!grouped || CopilotServer.Match(line) is not { Success: true } match) continue;
            // Only status words are looked for after the name (not inside a URL); anything else on the line is dropped.
            var rest = Regex.Replace(match.Groups["rest"].Value, @"\S+://\S*", " ");
            var status = Regex.IsMatch(rest, @"\bdisabled\b", RegexOptions.IgnoreCase) ? Disabled
                : Regex.IsMatch(rest, @"needs[- ]?auth|unauthori[sz]ed|not logged in|sign[- ]?in", RegexOptions.IgnoreCase) ? NeedsAuth
                : Regex.IsMatch(rest, @"\b(disconnected|failed|error)\b", RegexOptions.IgnoreCase) ? Failed
                : Regex.IsMatch(rest, @"\bconnected\b", RegexOptions.IgnoreCase) ? Connected
                : Unknown;
            Add(servers, match.Groups["name"].Value, source, status);
        }
        if (servers.Count > 0) return new McpList(servers);
        return Empty.IsMatch(Ansi.Replace(text, "")) ? new McpList([]) : null;
    }

    static readonly Regex CursorServer = new(
        "^" + Name + @": (?<status>ready|requires_authentication|loading|disabled|not loaded \(needs approval\)|Error: .*)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static McpList? ParseCursor(string text)
    {
        var servers = new List<McpServerInfo>();
        foreach (var line in Lines(text))
        {
            if (CursorServer.Match(line) is not { Success: true } match) continue;
            var (status, scope) = match.Groups["status"].Value switch
            {
                "ready" => (Connected, (string?)null),
                "requires_authentication" => (NeedsAuth, null),
                "loading" => (Unknown, null),
                "disabled" => (Disabled, null),
                "not loaded (needs approval)" => (Unknown, "project"),
                _ => (Failed, null),
            };
            Add(servers, match.Groups["name"].Value, scope, status);
        }
        if (servers.Count > 0) return new McpList(servers);
        return Empty.IsMatch(Ansi.Replace(text, "")) ? new McpList([]) : null;
    }
}

// The self-check's mcp-parse and mcp-list cases. All samples are made up here in each CLI's shape, with fake keys and
// tokens in their arguments, URLs, headers and errors; none of those may reach the page's message.
static class McpCheck
{
    public static void Run(Action<string, bool, string> check)
    {
        try
        {
            Parse(check);
        }
        catch (Exception ex)
        {
            check("mcp-parse", false, ex.GetType().Name);
        }
        try
        {
            ListOne(check);
        }
        catch (Exception ex)
        {
            check("mcp-list", false, ex.GetType().Name);
        }
    }

    // Fake values, never real ones. Each must be missing from every result.
    static readonly string FakeKey = "sk-" + new string('Q', 32);
    const string FakeToken = "tok_SeCrEt42xyz";
    const string FakeHeader = "Bearer hdr-SeCrEt-value-9";
    const string FakePassword = "pw-SeCrEt-77";
    static string[] Secrets => [FakeKey, FakeToken, FakeHeader, "hdr-SeCrEt", FakePassword, "SeCrEt", "--api-key", "https://", "npx", "ENOENT", @"C:\Projects"];

    static void Parse(Action<string, bool, string> check)
    {
        var claude = string.Join("\n",
            "Checking MCP server health…",
            "",
            $"github: https://api.githubcopilot.com/mcp/?token={FakeToken} (HTTP) - ✔ Connected",
            $"files: npx -y @modelcontextprotocol/server-filesystem --api-key {FakeKey} C:\\Projects - ✘ Failed to connect — spawn npx ENOENT https://x.example/?token={FakeToken}",
            "figma: https://mcp.figma.com/mcp (HTTP) - ! Needs authentication",
            @"unity: node C:\Projects\unity-mcp\server.js  - ⏸ Pending approval (run `claude` to approve)",
            $"sentry: https://mcp.sentry.dev/sse?access_token={FakeToken} (SSE) - ⊘ Disabled for this project (re-enable via /mcp)",
            "claude.ai Gmail: https://gmail.mcp.claude.com/mcp - ✔ Connected",
            $"plugin:tools:lint: lint-server --password {FakePassword} - ! Connected · tools fetch failed — 401 from https://x.example/?key={FakeToken}",
            "blank:  (HTTP) - - Not configured",
            "");
        var codex = "[\n" + string.Join(",\n",
            $"{{\"name\":\"docs\",\"enabled\":true,\"disabled_reason\":null,\"transport\":{{\"type\":\"stdio\",\"command\":\"npx\",\"args\":[\"-y\",\"docs-mcp\",\"--api-key\",\"{FakeKey}\"],\"env\":{{\"DOCS_TOKEN\":\"{FakeToken}\"}},\"env_vars\":[],\"cwd\":\"C:\\\\Projects\"}},\"startup_timeout_sec\":null,\"tool_timeout_sec\":null,\"auth_status\":\"unsupported\"}}",
            $"{{\"name\":\"linear\",\"enabled\":true,\"disabled_reason\":null,\"transport\":{{\"type\":\"streamable_http\",\"url\":\"https://mcp.linear.app/mcp?token={FakeToken}\",\"bearer_token_env_var\":\"LINEAR_TOKEN\",\"http_headers\":{{\"Authorization\":\"{FakeHeader}\"}},\"env_http_headers\":null,\"http_headers_helper\":null}},\"startup_timeout_sec\":30.0,\"tool_timeout_sec\":null,\"auth_status\":\"not_logged_in\"}}",
            $"{{\"name\":\"old\",\"enabled\":false,\"disabled_reason\":\"off\",\"transport\":{{\"type\":\"stdio\",\"command\":\"node\",\"args\":[\"--password\",\"{FakePassword}\"],\"env\":null,\"env_vars\":[],\"cwd\":null}},\"startup_timeout_sec\":null,\"tool_timeout_sec\":null,\"auth_status\":\"o_auth\"}}") + "\n]\n";
        var gemini = string.Join("\n",
            "Configured MCP servers:",
            "",
            $"✓ stdio-server: npx run-server --api-key {FakeKey} (stdio) - Connected",
            $"✗ remote (from my-ext): https://api.example.com/sse?token={FakeToken} (sse) - Disconnected",
            $"○ off-server: python3 -m srv --password {FakePassword} (stdio) - Disabled",
            "… slow: https://slow.example.com/mcp (http) - Connecting",
            $"⛔ blocked-one: https://blocked.example.com/mcp?key={FakeToken} (http) - Blocked",
            "");
        var geminiUntrusted = "Warning: MCP servers are configured but disabled because this folder is untrusted.\n"
            + "User-level servers are also suppressed in untrusted folders to prevent accidental side-effects.\n\n"
            + "Configured MCP servers:\n\n○ docs: npx docs (stdio) - Disabled\n";
        var copilot = string.Join("\n",
            "User servers:",
            "  docs (local) - connected (sandboxed)",
            $"  notes (stdio) (disabled) --api-key {FakeKey}",
            "  linear (http) - needs-auth",
            $"  wiki (http) https://wiki.example/signin?error=1&token={FakeToken} - connected",
            $"  flaky (stdio) - failed: spawn npx --api-key {FakeKey} ENOENT https://x.example/?token={FakeToken}",
            "",
            "Workspace servers:",
            "  unity (stdio) - disconnected",
            "",
            "Builtin servers:",
            "  github-mcp-server (http)",
            "");
        var cursor = string.Join("\n",
            "docs: ready",
            "linear: requires_authentication",
            "slow: loading",
            "off: disabled",
            "local-tools: not loaded (needs approval)",
            $"broken: Error: Failed to load MCP 'broken': spawn npx --api-key {FakeKey} ENOENT https://x.example/?token={FakeToken}",
            "");

        var samples = new (string Cli, McpList? List, string Expected)[]
        {
            ("claude", Mcp.Listers["claude"].Parse(claude, ""),
                "github=connected, files=failed, figma=needsAuth, unity=unknown/project, sentry=disabled, claude.ai Gmail=connected, plugin:tools:lint=failed, blank=failed"),
            ("codex", Mcp.Listers["codex"].Parse(codex, ""), "docs=unknown, linear=needsAuth, old=disabled"),
            ("gemini", Mcp.Listers["gemini"].Parse("", gemini),
                "stdio-server=connected, remote=failed, off-server=disabled, slow=unknown, blocked-one=disabled"),
            ("gemini-untrusted", Mcp.Listers["gemini"].Parse("", geminiUntrusted), "docs=disabled"),
            ("copilot", Mcp.Listers["copilot"].Parse(copilot, ""),
                "docs=connected/user, notes=disabled/user, linear=needsAuth/user, wiki=connected/user, flaky=failed/user, unity=failed/project, github-mcp-server=unknown"),
            ("cursor", Mcp.Listers["cursor-agent"].Parse(cursor, ""),
                "docs=connected, linear=needsAuth, slow=unknown, off=disabled, local-tools=unknown/project, broken=failed"),
        };
        string Summary(McpList? list) => list == null ? "null"
            : string.Join(", ", list.Servers.Select(server => server.Name + "=" + server.Status + (server.Scope == null ? "" : "/" + server.Scope)));
        var wrong = samples.Where(sample => Summary(sample.List) != sample.Expected).Select(sample => sample.Cli + ": " + Summary(sample.List)).ToList();

        // Empty lists read as empty, anything else as unreadable.
        // CheckAsync reads a parser that throws as unreadable too.
        static bool Unreadable(Func<McpList?> parse)
        {
            try { return parse() == null; }
            catch (JsonException) { return true; }
        }
        var edges = new (string Case, bool Pass)[]
        {
            ("claude empty", Mcp.ParseClaude("No MCP servers configured. Use `claude mcp add` to add a server.\n") is { Servers.Count: 0 }),
            ("codex empty", Mcp.ParseCodex("[]\n") is { Servers.Count: 0 }),
            ("codex after a warning", Mcp.ParseCodex("WARNING: something\n[{\"name\":\"a\",\"enabled\":true}]\n") is { Servers.Count: 1 }),
            ("gemini empty", Mcp.ParseGemini("No MCP servers configured.\n") is { Servers.Count: 0 }),
            ("cursor empty", Mcp.ParseCursor("No MCP servers configured (expected in .cursor/mcp.json or ~/.cursor/mcp.json)\n") is { Servers.Count: 0 }),
            ("claude other", Unreadable(() => Mcp.ParseClaude("error: unknown command 'mcp'\n"))),
            ("gemini other", Unreadable(() => Mcp.ParseGemini("Configured MCP servers:\n\nsomething else\n"))),
            ("cursor error", Unreadable(() => Mcp.ParseCursor("Failed to list MCP servers: boom\n"))),
            ("copilot other", Unreadable(() => Mcp.ParseCopilot("Usage: copilot [options]\n"))),
            ("codex cut off", Unreadable(() => Mcp.ParseCodex("[{\"name\":"))),
            ("codex broken", Unreadable(() => Mcp.ParseCodex("[{\"name\":\"a\",]"))),
            ("codex object", Unreadable(() => Mcp.ParseCodex("{\"servers\":[]}"))),
            ("codex text", Unreadable(() => Mcp.ParseCodex("error: unexpected argument '--json'"))),
        };
        var badEdges = edges.Where(edge => !edge.Pass).Select(edge => edge.Case).ToList();

        // What the page would get: nothing of the fake values, and the Gemini note is our own sentence.
        var report = new McpReport(DateTimeOffset.Now, samples.Select(sample => new McpAgentResult(sample.Cli, sample.Cli, "main", sample.List != null,
            sample.List?.Note, sample.List?.Servers ?? [])).ToList());
        var json = JsonSerializer.Serialize(report.Message());
        var leaked = Secrets.Where(secret => json.Contains(secret, StringComparison.OrdinalIgnoreCase)).ToList();
        var untrustedNote = samples.First(sample => sample.Cli == "gemini-untrusted").List?.Note ?? "";

        check("mcp-parse", wrong.Count == 0 && badEdges.Count == 0 && leaked.Count == 0 && untrustedNote.Length > 0,
            wrong.Count > 0 ? "wrong: " + string.Join(" | ", wrong)
            : leaked.Count > 0 ? leaked.Count + " of the fake values reached the message"
            : badEdges.Count > 0 ? "not read as empty or unreadable: " + string.Join(", ", badEdges)
            : untrustedNote.Length == 0 ? "no note for Gemini CLI in an untrusted folder"
            : "Claude Code, Codex, Gemini CLI, Copilot CLI and Cursor lists read as names, scopes and statuses only; fake keys, tokens, "
                + "URLs, headers and arguments in them reach nothing; empty lists are empty and other output is unreadable");
    }

    // A list command really runs: in the project folder (a planted program there doesn't run), and a slow one stops at its
    // timeout without holding up a fast one beside it.
    static void ListOne(Action<string, bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "AgentArcade-mcp-" + Environment.ProcessId);
        try
        {
            var project = Path.Combine(root, "cloned repo");
            var bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(project);
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(project, "here.txt"), "the project folder");
            // Planted in the project: leaves a file when it runs.
            File.WriteAllText(Path.Combine(project, "aa-planted.cmd"), "@echo planted>\"%~dp0planted.ran\"\r\n");
            var fast = Path.Combine(bin, "fast-agent.cmd");
            File.WriteAllText(fast, string.Join("\r\n",
                "@echo off",
                "call aa-planted 2>nul",
                "if exist here.txt echo docs: ready",
                $"echo broken: Error: spawn npx --api-key {FakeKey} ENOENT",
                "") );
            var slow = Path.Combine(bin, "slow-agent.cmd");
            File.WriteAllText(slow, "@echo off\r\n\"" + Path.Combine(Environment.SystemDirectory, "ping.exe") + "\" -n 30 127.0.0.1 >nul\r\necho late: ready\r\n");

            var lister = Mcp.Listers["cursor-agent"];
            var env = new Dictionary<string, string?>();
            var watch = Stopwatch.StartNew();
            var fastRun = Mcp.CheckAsync("Fast", "fast", "main", fast, lister, project, env, 3000);
            var slowRun = Mcp.CheckAsync("Slow", "slow", "main", slow, lister, project, env, 3000);
            var fastResult = fastRun.GetAwaiter().GetResult();
            var fastMs = watch.ElapsedMilliseconds;
            var slowResult = slowRun.GetAwaiter().GetResult();
            var slowMs = watch.ElapsedMilliseconds;
            var planted = File.Exists(Path.Combine(project, "planted.ran"));
            var json = JsonSerializer.Serialize(new McpReport(DateTimeOffset.Now, [fastResult, slowResult]).Message());

            var passed = fastResult.Ok && fastResult.Servers.Select(server => server.Name + "=" + server.Status).SequenceEqual(["docs=connected", "broken=failed"])
                && !slowResult.Ok && slowResult.Note == Mcp.NoAnswer && slowResult.Servers.Count == 0
                && fastMs < 2500 && slowMs < 10000 && !planted && !json.Contains(FakeKey, StringComparison.Ordinal) && !json.Contains("--api-key", StringComparison.Ordinal);
            check("mcp-list", passed, passed
                ? "a list runs in the project folder without a planted program there running; a slow one stops at its timeout ("
                    + slowMs / 100 / 10.0 + " s for 3 s) and the fast one beside it isn't held up (" + fastMs + " ms)"
                : "fast: " + (fastResult.Ok ? string.Join(", ", fastResult.Servers.Select(server => server.Name + "=" + server.Status)) : fastResult.Note)
                    + " after " + fastMs + " ms; slow: " + (slowResult.Ok ? "answered" : slowResult.Note) + " after " + slowMs + " ms"
                    + (planted ? "; the planted program ran" : ""));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* temp folder */ }
        }
    }
}
