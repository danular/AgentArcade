using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
}

// The side panel's Changes tab: what git sees changed in a project, and one file's diff. Paths are relative to the
// project, also when it is a folder inside a bigger repository, and only the project's own changes are listed.
// Read-only: git runs with GIT_OPTIONAL_LOCKS=0, so it never takes the index lock an agent's own git command may need
// at the same moment. Output is read up to a limit; git is stopped there, so a huge diff isn't read whole.
static class GitChanges
{
    const int MaxFiles = 1000;
    const int MaxStatusChars = 4_000_000;
    const int MaxDiffChars = 400_000;
    const long MaxNewFileBytes = 512 * 1024;

    public sealed record Change(string Path, string? From, string Status);

    // branch: the line git prints after "## " (branch, upstream, ahead/behind).
    public static (string? Branch, List<Change> Files, int Total) Status(string folder)
    {
        var place = Run(folder, MaxStatusChars, "rev-parse", "--show-prefix");
        if (place.Code != 0) throw new InvalidOperationException(NotARepository(place.Error) ? "This project isn't a git repository." : "git failed: " + FirstLine(place.Error));
        // Where the project sits in the repository: "" at its root, else for example "app/". git prints paths from the root.
        var prefix = place.Output.Trim();
        var (code, output, error, cut) = Run(folder, MaxStatusChars, "status", "--porcelain=v1", "-z", "-b", "--untracked-files=all", "--", ".");
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
        return (branch, files, total);
    }

    // The diff of one changed file against the last commit; an untracked file shows as all new lines.
    public static (string Text, bool Cut) Diff(string folder, string path, string? from, bool untracked)
    {
        if (untracked) return NewFile(folder, path);
        var paths = from != null ? new[] { from, path } : new[] { path };
        var head = Run(folder, MaxDiffChars, ["diff", "HEAD", "-M", "--no-ext-diff", "--", .. paths]);
        if (head.Code == 0 || head.Cut) return (head.Output, head.Cut);
        // No commit yet: what is staged, then what isn't.
        var staged = Run(folder, MaxDiffChars, ["diff", "--cached", "--no-ext-diff", "--", .. paths]);
        var unstaged = Run(folder, MaxDiffChars, ["diff", "--no-ext-diff", "--", .. paths]);
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

    // Code is -1 when the output was cut at maxChars (git is stopped then).
    static (int Code, string Output, string Error, bool Cut) Run(string folder, int maxChars, params string[] args)
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
        // Paths are taken literally (no pathspec magic), printed as they are, and without colors or a pager.
        foreach (var arg in new[] { "--no-pager", "--literal-pathspecs", "-c", "core.quotepath=false", "-c", "color.ui=never" }.Concat(args))
            start.ArgumentList.Add(arg);
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
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
