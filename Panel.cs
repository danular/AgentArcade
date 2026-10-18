using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace AgentArcade;

// The side panel's Preview serves project pages from https://<id>.arcade-project.example/, one host per project, so
// projects never share an origin (their storage, their cache). Every answer says no-store, so an agent's edit to a
// page's CSS or script shows on the next reload. Only folders this side registered are served, only files inside
// them, and no file reached through a link or junction (those can point anywhere).
sealed class PreviewServer
{
    public const string Domain = "arcade-project.example";
    readonly ConcurrentDictionary<string, string> _folders = new(StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".htm"] = "text/html; charset=utf-8", [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8", [".mjs"] = "text/javascript; charset=utf-8", [".json"] = "application/json",
        [".map"] = "application/json", [".txt"] = "text/plain; charset=utf-8", [".md"] = "text/plain; charset=utf-8",
        [".svg"] = "image/svg+xml", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".bmp"] = "image/bmp", [".ico"] = "image/x-icon", [".avif"] = "image/avif",
        [".woff"] = "font/woff", [".woff2"] = "font/woff2", [".ttf"] = "font/ttf", [".otf"] = "font/otf",
        [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".ogg"] = "audio/ogg", [".m4a"] = "audio/mp4", [".flac"] = "audio/flac",
        [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".wasm"] = "application/wasm",
        [".glb"] = "model/gltf-binary", [".gltf"] = "model/gltf+json",
    };

    // The URL of a file in a project folder; the folder is served from now on.
    public string Url(string folder, string relative)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..16].ToLowerInvariant();
        _folders[id] = root;
        return "https://" + id + "." + Domain + "/" + string.Join('/', relative.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
    }

    public void Attach(CoreWebView2 core)
    {
        // The overload with source kinds: Document includes iframes, where the preview runs. The older one misses
        // a cross-origin iframe's own requests, which then go out to DNS and fail.
        core.AddWebResourceRequestedFilter("https://*." + Domain + "/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.Document);
        core.WebResourceRequested += (_, args) =>
        {
            if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("." + Domain, StringComparison.OrdinalIgnoreCase)) return;
            var id = uri.Host[..^(Domain.Length + 1)];
            var file = _folders.TryGetValue(id, out var root) ? FileFor(root, Uri.UnescapeDataString(uri.AbsolutePath)) : null;
            FileStream? stream = null;
            try
            {
                // Not disposed here: WebView2 reads it after this handler returns. An agent may be writing the file.
                if (file != null) stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gone or locked since the check: answered with 404 below.
            }
            try
            {
                args.Response = stream != null
                    ? core.Environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: " + Type(file!) + "\r\nCache-Control: no-store")
                    : core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "Cache-Control: no-store");
            }
            catch
            {
                // The frame went away while the file was opened.
                stream?.Dispose();
            }
        };
    }

    // The file a URL path names inside root (a folder means its index.html), or null.
    public static string? FileFor(string root, string urlPath)
    {
        try
        {
            var relative = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            // A colon would name an alternate data stream or a drive.
            if (relative.Contains(':')) return null;
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!ProjectFiles.IsInside(root, full)) return null;
            if (Directory.Exists(full)) full = Path.Combine(full, "index.html");
            if (!File.Exists(full)) return null;
            for (var part = full; part != null && !string.Equals(part, root, StringComparison.OrdinalIgnoreCase); part = Path.GetDirectoryName(part))
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) return null;
            return full;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    static string Type(string path) => Types.TryGetValue(Path.GetExtension(path), out var type) ? type : "application/octet-stream";
}

// Files in project folders, for the side panel's Images tab and its preview list. Build output, packages, tool
// caches and hidden folders are skipped and links and junctions are not followed. The walk goes folder level by
// folder level, at most MaxDepth folders down and through at most MaxFolders folders, so a big project answers
// quickly and its shallow folders always count.
static class ProjectFiles
{
    public const int MaxDepth = 8;
    const int MaxFolders = 5000;

    public static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", "bin", "obj", "Library", "Temp", "Logs", "packages", "__pycache__", "venv" };

    public static (List<(string Path, DateTime Time)> Newest, int Total) Newest(string folder, Func<string, bool> match, int maxCount)
        => Newest([folder], match, maxCount);

    // The newest matching files with their times, and how many match in all. A file's time is the later of its write
    // and creation times: a file copied or unzipped into the project keeps an old write time, but is new here.
    public static (List<(string Path, DateTime Time)> Newest, int Total) Newest(IEnumerable<string> folders, Func<string, bool> match, int maxCount)
    {
        var found = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(DirectoryInfo Folder, int Depth)>();
        foreach (var folder in folders) queue.Enqueue((new DirectoryInfo(Path.GetFullPath(folder)), 0));
        var visited = 0;
        while (queue.Count > 0 && visited++ < MaxFolders)
        {
            var (folder, depth) = queue.Dequeue();
            FileInfo[] files;
            DirectoryInfo[] children;
            try
            {
                files = folder.GetFiles();
                children = depth < MaxDepth ? folder.GetDirectories() : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that vanished or is locked is left out; the others are still listed.
                continue;
            }
            // The times come with the folder listing, so this reads nothing more from the disk.
            foreach (var file in files)
                if (match(file.FullName)) found[file.FullName] = file.LastWriteTimeUtc > file.CreationTimeUtc ? file.LastWriteTimeUtc : file.CreationTimeUtc;
            foreach (var child in children)
            {
                // Links and junctions can point anywhere, so they are not followed.
                if (child.Name.StartsWith('.') || Skip.Contains(child.Name) || (child.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                queue.Enqueue((child, depth + 1));
            }
        }
        return (found.OrderByDescending(pair => pair.Value).Take(maxCount).Select(pair => (pair.Key, pair.Value)).ToList(), found.Count);
    }

    // Whether path is the folder itself or inside it, after both are made full paths.
    public static bool IsInside(string folder, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var full = Path.GetFullPath(path);
        // A drive root (C:\) keeps its separator.
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // A path on another computer: \\server\share, //server/share, or a device path (\\?\, \\.\, \??\) other than one on
    // a drive letter. Opening one makes Windows sign in there with the user's account, which hands that computer a
    // hash of the password. Only the text is looked at; nothing on disk or the network is touched.
    public static bool IsNetworkPath(string path)
    {
        var text = path.TrimStart();
        var device = Regex.Match(text, @"^(?:[\\/]{2}[?.][\\/]|[\\/]\?\?[\\/])(.*)\z", RegexOptions.Singleline);
        if (device.Success) return !Regex.IsMatch(device.Groups[1].Value, @"^[A-Za-z]:(?:[\\/]|\z)");
        return text.Length >= 2 && text[0] is '\\' or '/' && text[1] is '\\' or '/';
    }
}

// The side panel's Changes tab: what git sees changed in a project, and one file's diff. Paths are relative to the
// project, also when it is a folder inside a bigger repository, and only the project's own changes are listed.
// Read-only: git runs with GIT_OPTIONAL_LOCKS=0, so it never takes the index lock an agent's own git command may need
// at the same moment. Output is read up to a limit; git is stopped there, so a huge diff isn't read whole.
// git never runs a program the repository's own config names, and never reaches a network share it names: before the
// first git call GitConfigCheck reads the config, and git runs with fsmonitor and every filter off, diffs without
// textconv or an external diff, and without looking inside submodules (each has a config of its own).
// The list also gets the lines added and removed per file (git diff --numstat), kept per project until a changed file
// or the list changes, so the tab's 5 s refresh doesn't run git diff over and over.
// trusted: the config hash the user said "Run git here anyway" for, when the config includes files or names a work tree.
static class GitChanges
{
    const int MaxFiles = 1000;
    const int MaxStatusChars = 4_000_000;
    const int MaxDiffChars = 400_000;
    const long MaxNewFileBytes = 512 * 1024;
    // Line counts (Change.Added, Removed) only for a project with at most this many changes, and at most this much of
    // new files read for them per look; the rest are read on the next looks. The tab looks every 5 seconds.
    const int MaxCountFiles = 500;
    const long MaxCountBytes = 8 * 1024 * 1024;
    // git diff runs for the counts again only when a changed file's size or time, or the list, changed, or after this.
    static readonly TimeSpan CountsAge = TimeSpan.FromSeconds(60);

    // Variables that would point git at another repository than the one GitConfigCheck read.
    static readonly string[] RepositoryVariables =
        ["GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE"];

    static int _started;

    // How many git processes this app started; the self-check sees from it that a refused repository started none.
    public static int Started => Volatile.Read(ref _started);

    // Added, Removed: lines, as the file's diff shows them; null for a binary file, a new file too large to show, or a
    // list without counts.
    public sealed record Change(string Path, string? From, string Status, int? Added = null, int? Removed = null);

    // Per project: the counts git diff gave, for which changed files (Key: their paths, sizes and times) and when; and
    // new files' line counts with the size and time they were read at.
    sealed record Counted(string Key, DateTime At, IReadOnlyDictionary<string, (int? Added, int? Removed)> Tracked,
        IReadOnlyDictionary<string, (long Length, DateTime Time, int? Lines)> New);

    static readonly ConcurrentDictionary<string, Counted> _counted = new(StringComparer.OrdinalIgnoreCase);

    // branch: the line git prints after "## " (branch, upstream, ahead/behind). counts: the Changes tab's list, with
    // the lines added and removed per file.
    public static (string? Branch, List<Change> Files, int Total) Status(string folder, string? trusted = null, bool counts = false)
    {
        var check = Checked(folder, trusted);
        var place = Run(folder, check, MaxStatusChars, "rev-parse", "--show-prefix");
        if (place.Code != 0) throw new InvalidOperationException(NotARepository(place.Error) ? "This project isn't a git repository." : "git failed: " + FirstLine(place.Error));
        // Where the project sits in the repository: "" at its root, else for example "app/". git prints paths from the root.
        var prefix = place.Output.Trim();
        var (code, output, error, cut) = Run(folder, check, MaxStatusChars,
            "status", "--porcelain=v1", "-z", "-b", "--untracked-files=all", "--ignore-submodules=dirty", "--", ".");
        if (code != 0 && !cut) throw new InvalidOperationException("git status failed: " + FirstLine(error));
        string? branch = null;
        var files = new List<Change>();
        var total = 0;
        // Every entry ends with \0, so the last part is empty, or cut off when the output was too long.
        var parts = output.Split('\0')[..^1];
        for (var i = 0; i < parts.Length; i++)
        {
            var entry = parts[i];
            if (entry.StartsWith("## ", StringComparison.Ordinal))
            {
                branch = entry[3..];
                continue;
            }
            if (entry.Length < 4) continue;
            var xy = entry[..2];
            // A rename or copy, in the index or in the working tree, is followed by the path it came from.
            var from = (xy[0] is 'R' or 'C' || xy[1] is 'R' or 'C') && i + 1 < parts.Length ? parts[++i] : null;
            if (!entry[3..].StartsWith(prefix, StringComparison.Ordinal)) continue;
            // An untracked folder (another git repository inside the project) ends with a slash.
            var path = entry[(3 + prefix.Length)..].TrimEnd('/');
            if (path.Length == 0) continue;
            var status = Label(xy);
            if (from != null && from.StartsWith(prefix, StringComparison.Ordinal)) from = from[prefix.Length..];
            else if (from != null)
            {
                // Moved in from outside the project: here it is simply new.
                from = null;
                status = "added";
            }
            total++;
            if (files.Count < MaxFiles) files.Add(new Change(path, from, status));
        }
        if (counts && !cut && total <= MaxCountFiles) files = WithCounts(folder, check, branch, files);
        return (branch, files, total);
    }

    // The files with the lines added and removed: git diff --numstat against the last commit, for a new file its lines.
    // Counts are an extra: when they can't be had, the list shows without them.
    static List<Change> WithCounts(string folder, GitCheck check, string? branch, List<Change> files)
    {
        try
        {
            var root = Path.GetFullPath(folder);
            var last = _counted.TryGetValue(root, out var found) ? found : null;
            var key = new StringBuilder(branch).Append('\n');
            foreach (var file in files.Where(file => file.Status != "new"))
            {
                var info = new FileInfo(Path.Combine(root, file.Path));
                var inside = ProjectFiles.IsInside(root, info.FullName);
                key.Append(file.Path).Append('\0').Append(file.From).Append('\0').Append(file.Status).Append('\0')
                    .Append(inside && info.Exists ? info.Length + ":" + info.LastWriteTimeUtc.Ticks : "-").Append('\n');
            }
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())));
            var now = DateTime.UtcNow;
            var reuse = last != null && last.Key == hash && now - last.At < CountsAge;
            var tracked = reuse ? last!.Tracked
                : files.Any(file => file.Status != "new") ? Numstat(folder, check) : new Dictionary<string, (int? Added, int? Removed)>();
            var at = reuse ? last!.At : now;
            var known = new Dictionary<string, (long Length, DateTime Time, int? Lines)>(StringComparer.Ordinal);
            var budget = MaxCountBytes;
            var result = new List<Change>(files.Count);
            foreach (var file in files)
            {
                if (file.Status != "new")
                {
                    result.Add(tracked.TryGetValue(CountKey(file.Path, file.From), out var count) ? file with { Added = count.Added, Removed = count.Removed } : file);
                    continue;
                }
                var lines = NewLines(root, file.Path, last?.New, ref budget, out var seen);
                if (seen != null) known[file.Path] = seen.Value;
                result.Add(lines == null ? file : file with { Added = lines, Removed = 0 });
            }
            // A handful of projects at most; the cache never grows with them.
            if (_counted.Count > 32) _counted.Clear();
            _counted[root] = new Counted(hash, at, tracked, known);
            return result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return files;
        }
    }

    // git diff --numstat for the whole project (paths relative to it, as Status gives them), against the last commit.
    // Without a commit yet: what is staged plus what isn't, as Diff shows it. Empty when git diff fails or takes too
    // long; that answer is kept too, so a slow repository isn't asked again every 5 seconds.
    static Dictionary<string, (int? Added, int? Removed)> Numstat(string folder, GitCheck check)
    {
        string[] options = ["--numstat", "-z", "--relative", "--no-ext-diff", "--no-textconv", "--ignore-submodules=dirty", "--", "."];
        try
        {
            var head = Run(folder, check, MaxStatusChars, ["diff", "HEAD", "-M", .. options]);
            if (head.Code == 0) return ParseNumstat(head.Output);
            if (head.Cut) return new();
            var staged = Run(folder, check, MaxStatusChars, ["diff", "--cached", .. options]);
            var unstaged = Run(folder, check, MaxStatusChars, ["diff", .. options]);
            if (staged.Code != 0 || unstaged.Code != 0) return new();
            var counts = ParseNumstat(staged.Output);
            foreach (var (path, (added, removed)) in ParseNumstat(unstaged.Output))
                counts[path] = counts.TryGetValue(path, out var before) ? (before.Added + added, before.Removed + removed) : (added, removed);
            return counts;
        }
        catch (InvalidOperationException)
        {
            return new();
        }
    }

    // git's --numstat -z output: "added\tremoved\tpath\0", for a rename "added\tremoved\t\0from\0path\0", and "-" for
    // both numbers of a binary file. Keyed by CountKey.
    public static Dictionary<string, (int? Added, int? Removed)> ParseNumstat(string output)
    {
        var counts = new Dictionary<string, (int? Added, int? Removed)>(StringComparer.Ordinal);
        var parts = output.Split('\0');
        for (var i = 0; i < parts.Length; i++)
        {
            var fields = parts[i].Split('\t', 3);
            if (fields.Length < 3) continue;
            string path;
            string? from = null;
            if (fields[2].Length > 0) path = fields[2];
            else if (i + 2 < parts.Length)
            {
                from = parts[++i];
                path = parts[++i];
            }
            else break;
            counts[CountKey(path, from)] = (int.TryParse(fields[0], out var added) ? added : null, int.TryParse(fields[1], out var removed) ? removed : null);
        }
        return counts;
    }

    public static string CountKey(string path, string? from) => path + "\0" + from;

    // A new file's lines, as NewFile shows them: none for a folder, a link, a binary file or one larger than
    // MaxNewFileBytes. A file read before is read again only when its size or time changed; seen is what to remember.
    static int? NewLines(string root, string path, IReadOnlyDictionary<string, (long Length, DateTime Time, int? Lines)>? known, ref long budget,
        out (long Length, DateTime Time, int? Lines)? seen)
    {
        seen = null;
        var info = new FileInfo(Path.Combine(root, path));
        // A link can point anywhere, also to another computer, so it isn't opened.
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > MaxNewFileBytes || !ProjectFiles.IsInside(root, info.FullName)) return null;
        if (known != null && known.TryGetValue(path, out var before) && before.Length == info.Length && before.Time == info.LastWriteTimeUtc)
        {
            seen = before;
            return before.Lines;
        }
        if (info.Length > budget) return null;
        budget -= info.Length;
        try
        {
            // An agent may be writing it.
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxNewFileBytes) return null;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            var lines = CountLines(bytes);
            seen = (info.Length, info.LastWriteTimeUtc, lines);
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The lines NewFile shows: one per \n, and a last line without one; none for binary (a NUL in the first 8000 bytes).
    public static int? CountLines(byte[] bytes)
    {
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).IndexOf((byte)0) >= 0) return null;
        var lines = bytes.AsSpan().Count((byte)'\n');
        return bytes.Length > 0 && bytes[^1] != '\n' ? lines + 1 : lines;
    }

    // The diff of one changed file against the last commit; an untracked file shows as all new lines.
    public static (string Text, bool Cut) Diff(string folder, string path, string? from, bool untracked, string? trusted = null)
    {
        if (untracked) return NewFile(folder, path);
        var check = Checked(folder, trusted);
        var paths = from != null ? new[] { from, path } : new[] { path };
        string[] options = ["--no-ext-diff", "--no-textconv", "--ignore-submodules=dirty"];
        var head = Run(folder, check, MaxDiffChars, ["diff", "HEAD", "-M", .. options, "--", .. paths]);
        if (head.Code == 0 || head.Cut) return (head.Output, head.Cut);
        // No commit yet: what is staged, then what isn't.
        var staged = Run(folder, check, MaxDiffChars, ["diff", "--cached", .. options, "--", .. paths]);
        var unstaged = Run(folder, check, MaxDiffChars, ["diff", .. options, "--", .. paths]);
        var text = staged.Output + unstaged.Output;
        return text.Length > MaxDiffChars ? (text[..MaxDiffChars], true) : (text, staged.Cut || unstaged.Cut);
    }

    static (string Text, bool Cut) NewFile(string folder, string path)
    {
        var full = Path.GetFullPath(Path.Combine(folder, path));
        if (!ProjectFiles.IsInside(folder, full)) return ("", false);
        if (Directory.Exists(full)) return ("A new folder, probably another git repository: git doesn't list its files here.", false);
        if (!File.Exists(full)) return ("", false);
        var info = new FileInfo(full);
        if (info.Length > MaxNewFileBytes) return ("New file, " + (info.Length / 1024) + " KB: too large to show here.", false);
        var bytes = File.ReadAllBytes(full);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).IndexOf((byte)0) >= 0) return ("New binary file.", false);
        var lines = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];
        var text = new StringBuilder("new file\n@@ -0,0 +1," + lines.Length + " @@\n");
        foreach (var line in lines) text.Append('+').Append(line).Append('\n');
        return text.Length > MaxDiffChars ? (text.ToString(0, MaxDiffChars), true) : (text.ToString(), false);
    }

    static string Label(string xy) => xy switch
    {
        "??" => "new",
        "UU" or "AA" or "DD" or "AU" or "UA" or "DU" or "UD" => "conflict",
        _ when xy.Contains('D') => "deleted",
        _ when xy.Contains('R') => "renamed",
        _ when xy.Contains('C') => "copied",
        _ when xy.Contains('A') => "added",
        _ => "modified",
    };

    static bool NotARepository(string error) => error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase);

    static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    // The check before the first git call: refused, waiting for the user's OK, or what git runs with.
    static GitCheck Checked(string folder, string? trusted)
    {
        var check = GitConfigCheck.Inspect(folder);
        if (check.Refusal != null) throw new InvalidOperationException(check.Refusal);
        if (check.NeedsTrust && check.Config != trusted) throw new GitTrustNeeded(check);
        return check;
    }

    // Code is -1 when the output was cut at maxChars (git is stopped then).
    static (int Code, string Output, string Error, bool Cut) Run(string folder, GitCheck check, int maxChars, params string[] args)
    {
        var git = Locator.FindOnPath("git");
        if (git == null || !git.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Git isn't installed, or git.exe isn't on PATH.");
        var start = new ProcessStartInfo(git)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Paths are taken literally (no pathspec magic), printed as they are, and without colors or a pager. No
        // fsmonitor hook runs, and every filter the repository's config defines does nothing.
        foreach (var arg in new[] { "--no-pager", "--literal-pathspecs", "-c", "core.quotepath=false", "-c", "color.ui=never", "-c", "core.fsmonitor=false" })
            start.ArgumentList.Add(arg);
        foreach (var filter in check.Filters)
        {
            foreach (var setting in new[] { ".clean=", ".smudge=", ".process=", ".required=false" })
            {
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add("filter." + filter + setting);
            }
        }
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        // git finds the repository GitConfigCheck read and no other: nothing in this app's environment points it
        // elsewhere, and it doesn't look above that repository's folder.
        foreach (var name in RepositoryVariables) start.Environment.Remove(name);
        if (check.Ceiling != null) start.Environment["GIT_CEILING_DIRECTORIES"] = check.Ceiling;
        else start.Environment.Remove("GIT_CEILING_DIRECTORIES");
        Interlocked.Increment(ref _started);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git didn't start.");
        var error = process.StandardError.ReadToEndAsync();
        var text = new StringBuilder();
        var cut = false;
        var reading = Task.Run(() =>
        {
            var buffer = new char[16384];
            int count;
            while ((count = process.StandardOutput.Read(buffer, 0, buffer.Length)) > 0)
            {
                var room = maxChars - text.Length;
                text.Append(buffer, 0, Math.Min(count, room));
                if (count >= room)
                {
                    cut = true;
                    return;
                }
            }
        });
        if (!reading.Wait(15_000))
        {
            Stop(process);
            throw new InvalidOperationException("git took longer than 15 seconds.");
        }
        if (cut) Stop(process);
        var ended = process.WaitForExit(5_000);
        return (cut || !ended ? -1 : process.ExitCode, text.ToString(), error.Wait(1_000) ? error.Result : "", cut);
    }

    static void Stop(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch
        {
            // It ended by itself.
        }
    }
}

// What GitConfigCheck found before git runs in a project.
// Refusal: git doesn't run here, and why (no repository, a path on another computer, a config git can't read either).
// Uses: the include.path, includeIf and core.worktree settings that make git read other files or another folder; git
// runs there only after "Run git here anyway" for exactly this Config (a SHA-256 of the git folder and its config).
// Filters: every filter the config and the files it includes define; git runs with each of them off.
// Ceiling: the folder above the repository, so git doesn't look further up than the repository checked here.
sealed record GitCheck(string? Refusal, IReadOnlyList<string> Uses, string Config, IReadOnlyList<string> Filters, string? Ceiling)
{
    public bool NeedsTrust => Uses.Count > 0;

    public static GitCheck Refused(string why) => new(why, [], "", [], null);
}

// git may run in this repository only after the user agrees in the Changes tab.
sealed class GitTrustNeeded(GitCheck check) : InvalidOperationException(
    "This repository's git config makes git read other files or another folder. Run git here only if you trust this project.")
{
    public GitCheck Check { get; } = check;
}

// Finds a project's repository the way git does, from the project folder up, and reads its .git file and config as
// text the way git parses them, without following includes for the decision, before any git process starts. A
// repository's own config can name programs (core.fsmonitor, filters, diff textconv) and paths on another computer
// (include.path, includeIf, core.worktree, a .git file's gitdir), where Windows signs in with the user's account and
// hands that computer a hash of the password.
static class GitConfigCheck
{
    const int MaxFileBytes = 1024 * 1024;
    const int MaxIncludeDepth = 10;
    const int MaxAlternatesDepth = 5;
    const int MaxFilters = 100;
    const string NetworkTail = " The Changes tab doesn't run git here: Windows would sign in to that computer with your account.";

    // One setting. Section and Key are lower case, as git compares them; Subsection keeps its case. Value is null for
    // a key without "=", which git reads as true.
    public sealed record Entry(string Section, string? Subsection, string Key, string? Value);

    sealed class NoGit(string why) : Exception(why);

    public static bool IsHash(string value) => Regex.IsMatch(value, "^[0-9a-f]{64}\\z");

    public static GitCheck Inspect(string folder)
    {
        try
        {
            return Read(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
        }
        catch (NoGit refused)
        {
            return GitCheck.Refused(refused.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return GitCheck.Refused("This repository's git settings name a path the Changes tab can't check, so it doesn't run git here.");
        }
    }

    static GitCheck Read(string project)
    {
        // A project on a network share may use that same server; nothing may reach any other computer.
        var projectHost = Host(project);
        bool Away(string path) => ProjectFiles.IsNetworkPath(path)
            && (projectHost == null || !string.Equals(Host(path), projectHost, StringComparison.OrdinalIgnoreCase));
        // A path from a config, raw and expanded. ~/ may be on a network share when the home folder itself is.
        bool AwayPath(string raw, string? full) => Away(raw) || (full != null && Away(full) && !(IsHome(raw) && Away(Home)));
        static void Refuse(bool away, string what)
        {
            if (away) throw new NoGit(what + NetworkTail);
        }
        byte[]? ReadSmall(string path)
        {
            Refuse(LinksAway(path, Away), "This repository's " + Path.GetFileName(path) + " links to a network path.");
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (info.Length > MaxFileBytes) throw new NoGit("This repository's " + info.Name + " is larger than 1 MB, so the Changes tab doesn't run git here.");
            return File.ReadAllBytes(path);
        }
        List<Entry> ParseFile(byte[] bytes, string path) => Parse(Encoding.UTF8.GetString(bytes))
            ?? throw new NoGit("git can't read this repository's " + Path.GetFileName(path) + " (a line it doesn't understand), so the Changes tab doesn't run git here.");

        // The repository: the first .git going up (a folder, or a file naming one), or a folder that is a git folder
        // itself. A .git folder counts even when git would skip it as broken; git then stops there too (Ceiling).
        string? foundGit = null, foundTop = null;
        for (var dir = project; dir != null && foundGit == null; dir = Path.GetDirectoryName(dir))
        {
            var dotGit = Path.Combine(dir, ".git");
            Refuse(LinksAway(dotGit, Away), "This project's .git links to a network path.");
            if (File.Exists(dotGit))
            {
                // A linked work tree or a submodule: "gitdir: <path>", relative to the folder the file is in.
                var text = Encoding.UTF8.GetString(ReadSmall(dotGit) ?? []);
                if (!text.StartsWith("gitdir: ", StringComparison.Ordinal)) throw new NoGit("This project's .git file isn't one git can read.");
                var target = text["gitdir: ".Length..].TrimEnd('\r', '\n');
                Refuse(Away(target), "This project's .git file points to a network path.");
                foundGit = Path.GetFullPath(Path.Combine(dir, target));
                Refuse(Away(foundGit), "This project's .git file points to a network path.");
                foundTop = dir;
            }
            else if (Directory.Exists(dotGit)) (foundGit, foundTop) = (dotGit, dir);
            else if (File.Exists(Path.Combine(dir, "HEAD")) && (Directory.Exists(Path.Combine(dir, "objects")) || File.Exists(Path.Combine(dir, "commondir"))))
                (foundGit, foundTop) = (dir, dir);
        }
        if (foundGit == null || foundTop == null) throw new NoGit("This project isn't a git repository.");
        string gitDir = foundGit, top = foundTop;
        Refuse(LinksAway(gitDir, Away), "This repository's git folder links to a network path.");

        // A linked work tree keeps most of its repository in the common folder.
        var common = gitDir;
        if (ReadSmall(Path.Combine(gitDir, "commondir")) is { } commonText)
        {
            var target = Encoding.UTF8.GetString(commonText).Trim();
            Refuse(Away(target), "This repository's commondir points to a network path.");
            common = Path.GetFullPath(Path.Combine(gitDir, target));
            Refuse(Away(common) || LinksAway(common, Away), "This repository's commondir points to a network path.");
        }
        foreach (var part in new[] { Path.Combine(common, "objects"), Path.Combine(common, "refs"), Path.Combine(common, "info"), Path.Combine(gitDir, "HEAD"), Path.Combine(gitDir, "index") })
            Refuse(LinksAway(part, Away), "This repository's git folder links to a network path.");

        // Object folders git also reads (objects/info/alternates), relative to the objects folder; each may name more.
        var objectFolders = new Queue<(string Folder, int Depth)>([(Path.Combine(common, "objects"), 0)]);
        while (objectFolders.Count > 0)
        {
            var (objects, depth) = objectFolders.Dequeue();
            if (ReadSmall(Path.Combine(objects, "info", "alternates")) is not { } alternates) continue;
            foreach (var raw in Encoding.UTF8.GetString(alternates).Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] == '"') line = Unquote(line);
                var full = Path.GetFullPath(Path.Combine(objects, line));
                Refuse(Away(line) || Away(full), "This repository reads objects from a network path (objects/info/alternates).");
                if (depth < MaxAlternatesDepth) objectFolders.Enqueue((full, depth + 1));
            }
        }

        var uses = new List<string>();
        var filters = new SortedSet<string>(StringComparer.Ordinal);
        // What the settings of one config file make git read, and the filters they define. Included files are read
        // too, never from another computer, so their filters are switched off as well; only the repository's own config
        // (uses not null) decides whether the user is asked.
        void Settings(List<Entry> entries, string folder, int depth, List<string>? asked)
        {
            foreach (var entry in entries)
            {
                var value = entry.Value;
                switch (entry.Section, entry.Key)
                {
                    case ("filter", _) when entry.Subsection != null:
                        // git -c splits at the first "=", so such a name can't be switched off.
                        if (entry.Subsection.Contains('='))
                            throw new NoGit("This repository's git config defines a filter git can't switch off from its command line, so the Changes tab doesn't run git here.");
                        filters.Add(entry.Subsection);
                        break;
                    case ("include", "path") when entry.Subsection == null:
                    case ("includeif", "path") when entry.Subsection != null:
                        asked?.Add(Shown(entry));
                        if (value == null) break;
                        var included = Expand(value, folder);
                        Refuse(AwayPath(value, included), "This repository's git config (" + Shown(entry with { Value = null }) + ") points to a network path.");
                        if (included != null && depth < MaxIncludeDepth && ReadSmall(included) is { } includedText)
                            Settings(ParseFile(includedText, included), Path.GetDirectoryName(included) ?? folder, depth + 1, null);
                        break;
                    case ("core", "worktree") when entry.Subsection == null:
                        asked?.Add(Shown(entry));
                        if (value != null) Refuse(Away(value) || Away(Path.GetFullPath(Path.Combine(gitDir, value))), "This repository's git config (core.worktree) points to a network path.");
                        break;
                    case ("core", "excludesfile" or "attributesfile") when entry.Subsection == null && value != null:
                        Refuse(AwayPath(value, Expand(value, top)), "This repository's git config (core." + entry.Key + ") points to a network path.");
                        break;
                    // git diff reads it to sort its files.
                    case ("diff", "orderfile") when entry.Subsection == null && value != null:
                        Refuse(AwayPath(value, Expand(value, top)), "This repository's git config (diff.orderFile) points to a network path.");
                        break;
                }
            }
        }

        // The consent is for this git folder and this config, so a change to either asks again.
        var config = new StringBuilder("gitdir\n" + gitDir + "\ncommon\n" + common + "\n");
        foreach (var file in new[] { Path.Combine(common, "config"), Path.Combine(gitDir, "config.worktree") })
        {
            var bytes = ReadSmall(file);
            config.Append(Path.GetFileName(file)).Append('\n').Append(bytes == null ? "-" : Convert.ToHexStringLower(SHA256.HashData(bytes))).Append('\n');
            if (bytes != null) Settings(ParseFile(bytes, file), Path.GetDirectoryName(file)!, 0, uses);
        }
        if (filters.Count > MaxFilters)
            throw new NoGit("This repository's git config defines more than " + MaxFilters + " filters, so the Changes tab doesn't run git here.");
        return new GitCheck(null, uses, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(config.ToString()))), filters.ToList(), Path.GetDirectoryName(top));
    }

    // A path from a config as git reads it: ~/ in the home folder, %(prefix)/ in git's own folder (null: not read
    // here), anything else relative to folder. git puts ~ and the rest together as text, and so does this.
    static string? Expand(string value, string folder)
    {
        if (value.StartsWith("%(prefix)/", StringComparison.Ordinal)) return null;
        if (IsHome(value)) value = Home + value[1..];
        return Path.GetFullPath(Path.Combine(folder, value));
    }

    static bool IsHome(string value) => value == "~" || value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal);

    // git's home folder: HOME when it is set, else the user profile.
    static string Home => Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home ? home : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // The computer a network path is on: the server of \\server\share or \\?\UNC\server\share; null otherwise.
    static string? Host(string path)
    {
        if (!ProjectFiles.IsNetworkPath(path)) return null;
        var parts = path.TrimStart().Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        if (parts[0] is "?" or "." or "??")
            return parts.Length > 2 && parts[1].Equals("UNC", StringComparison.OrdinalIgnoreCase) ? parts[2] : null;
        return parts[0];
    }

    // A symbolic link or junction to a network path. Windows would follow it without asking.
    static bool LinksAway(string path, Func<string, bool> away)
    {
        try
        {
            if (!Path.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
            var target = new FileInfo(path).LinkTarget;
            return target != null && (away(target) || away(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? "", target))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // How a setting shows in the Changes tab: "includeIf.gitdir:~/work/.path = ../work.gitconfig".
    static string Shown(Entry entry)
    {
        var name = (entry.Section == "includeif" ? "includeIf" : entry.Section) + (entry.Subsection == null ? "" : "." + entry.Subsection) + "." + entry.Key;
        var text = string.Concat((entry.Value == null ? name : name + " = " + entry.Value).Select(c => char.IsControl(c) ? ' ' : c));
        return text.Length > 200 ? text[..199] + "…" : text;
    }

    // A C-quoted path, as git writes one in objects/info/alternates: "..." with \\, \", \t, \n and octal escapes.
    static string Unquote(string quoted)
    {
        var text = new StringBuilder();
        for (var i = 1; i < quoted.Length && quoted[i] != '"'; i++)
        {
            if (quoted[i] != '\\' || i + 1 >= quoted.Length)
            {
                text.Append(quoted[i]);
                continue;
            }
            var c = quoted[++i];
            if (c is >= '0' and <= '3' && i + 2 < quoted.Length && quoted[i + 1] is >= '0' and <= '7' && quoted[i + 2] is >= '0' and <= '7')
            {
                text.Append((char)((c - '0') * 64 + (quoted[i + 1] - '0') * 8 + (quoted[i + 2] - '0')));
                i += 2;
            }
            else text.Append(c switch { 'a' => '\a', 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', 'v' => '\v', _ => c });
        }
        return text.ToString();
    }

    // A config file read the way git's config parser reads it (config.c: sections, quoted and legacy subsections,
    // values with quotes, escapes and comments, line continuations). Null where git itself stops with "bad config
    // line": git then runs nothing, and this side doesn't guess what was meant.
    public static List<Entry>? Parse(string text)
    {
        var entries = new List<Entry>();
        var reader = new Reader(text);
        var stem = "";
        var comment = false;
        while (true)
        {
            var c = reader.Next();
            if (c == '\n')
            {
                if (reader.Eof) return entries;
                comment = false;
                continue;
            }
            if (comment || IsSpace(c)) continue;
            if (c is '#' or ';')
            {
                comment = true;
                continue;
            }
            if (c == '[')
            {
                if (SectionName(reader) is not { Length: > 0 } section) return null;
                stem = section + ".";
                continue;
            }
            if (!char.IsAsciiLetter(c)) return null;
            var key = new StringBuilder().Append(char.ToLowerInvariant(c));
            while (true)
            {
                c = reader.Next();
                if (reader.Eof || !IsKeyChar(c)) break;
                key.Append(char.ToLowerInvariant(c));
            }
            while (c is ' ' or '\t') c = reader.Next();
            string? value = null;
            if (c != '\n')
            {
                if (c != '=') return null;
                value = Value(reader);
                if (value == null) return null;
            }
            // git splits the full name at its first and last dot: section, subsection, key.
            var name = stem + key;
            var first = name.IndexOf('.');
            var last = name.LastIndexOf('.');
            entries.Add(first < 0
                ? new Entry("", null, name, value)
                : new Entry(name[..first], first == last ? null : name[(first + 1)..last], name[(last + 1)..], value));
        }
    }

    // [section], [section.legacy] (lower case) or [section "subsection"] (as written).
    static string? SectionName(Reader reader)
    {
        var name = new StringBuilder();
        while (true)
        {
            var c = reader.Next();
            if (reader.Eof) return null;
            if (c == ']') return name.ToString();
            if (IsSpace(c)) return Subsection(reader, name, c);
            if (!IsKeyChar(c) && c != '.') return null;
            name.Append(char.ToLowerInvariant(c));
        }
    }

    static string? Subsection(Reader reader, StringBuilder name, char c)
    {
        do
        {
            if (c == '\n') return null;
            c = reader.Next();
        }
        while (IsSpace(c));
        if (c != '"') return null;
        name.Append('.');
        while (true)
        {
            c = reader.Next();
            if (c == '\n') return null;
            if (c == '"') break;
            if (c == '\\')
            {
                c = reader.Next();
                if (c == '\n') return null;
            }
            name.Append(c);
        }
        return reader.Next() == ']' ? name.ToString() : null;
    }

    static string? Value(Reader reader)
    {
        var value = new StringBuilder();
        var quote = false;
        var comment = false;
        var trim = 0;
        while (true)
        {
            var c = reader.Next();
            if (c == '\n')
            {
                if (quote) return null;
                if (trim > 0) value.Length = trim;
                return value.ToString();
            }
            if (comment) continue;
            if (IsSpace(c) && !quote)
            {
                if (trim == 0) trim = value.Length;
                if (value.Length > 0) value.Append(c);
                continue;
            }
            if (!quote && c is ';' or '#')
            {
                comment = true;
                continue;
            }
            trim = 0;
            if (c == '\\')
            {
                c = reader.Next();
                switch (c)
                {
                    case '\n':
                        continue;
                    case 't':
                        c = '\t';
                        break;
                    case 'b':
                        c = '\b';
                        break;
                    case 'n':
                        c = '\n';
                        break;
                    case '\\' or '"':
                        break;
                    default:
                        return null;
                }
                value.Append(c);
                continue;
            }
            if (c == '"')
            {
                quote = !quote;
                continue;
            }
            value.Append(c);
        }
    }

    static bool IsKeyChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-';

    static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    // git's get_next_char: \r\n reads as \n, and the end of the text as one more \n with Eof set.
    sealed class Reader(string text)
    {
        int _at = text.StartsWith('\uFEFF') ? 1 : 0;

        public bool Eof { get; private set; }

        public char Next()
        {
            if (_at >= text.Length)
            {
                Eof = true;
                return '\n';
            }
            var c = text[_at++];
            if (c == '\r' && _at < text.Length && text[_at] == '\n')
            {
                _at++;
                return '\n';
            }
            return c;
        }
    }
}
