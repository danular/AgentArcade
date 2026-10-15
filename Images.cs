using System.Collections.Concurrent;
using Microsoft.Web.WebView2.Core;

namespace AgentArcade;

// Serves image files to the page. The page only gets a URL for a file this side has checked; it
// can't ask for an arbitrary path.
sealed class ImageServer
{
    public const string Host = "files.arcade.local";
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];
    const long MaxBytes = 60 * 1024 * 1024;

    readonly ConcurrentDictionary<string, string> _paths = new();
    readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public string Url(string path)
    {
        var full = Path.GetFullPath(path);
        var token = _tokens.GetOrAdd(full, _ => Guid.NewGuid().ToString("N"));
        _paths[token] = full;
        return "https://" + Host + "/" + token + Path.GetExtension(full).ToLowerInvariant();
    }

    public string? PathOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase)) return null;
        var token = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        return _paths.TryGetValue(token, out var path) ? path : null;
    }

    public void Attach(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter("https://" + Host + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += async (_, args) =>
        {
            var path = PathOf(args.Request.Uri);
            if (path == null) return;
            // Not `using`: disposing a deferral completes it a second time, which WebView2 refuses.
            var deferral = args.GetDeferral();
            byte[]? bytes = null;
            try
            {
                bytes = await Task.Run(() => Read(path));
            }
            catch
            {
                // Answered with 404 below.
            }
            try
            {
                args.Response = bytes != null
                    ? core.Environment.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK",
                        "Content-Type: " + Mime(path) + "\r\nCache-Control: no-store")
                    : core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
            }
            catch
            {
                // The page dropped the request while the file was read (a pane closed or redrew, or the
                // window is closing), and WebView2 refuses an answer to a request that is gone.
            }
            try
            {
                deferral.Complete();
            }
            catch
            {
                // Same: nothing is waiting for this request any more.
            }
        };
    }

    // An agent may still be writing the file, so it is read without locking it.
    public static byte[] Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxBytes) throw new IOException("Image too large.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "image/png",
    };
}

// Reports image files that appear or change under a folder, once each file has stopped changing.
sealed class ImageWatcher : IDisposable
{
    readonly FileSystemWatcher _watcher;
    readonly Action<string> _found;
    readonly ConcurrentDictionary<string, CancellationTokenSource> _waiting = new(StringComparer.OrdinalIgnoreCase);
    volatile bool _disposed;

    public string Folder { get; }

    public ImageWatcher(string folder, Action<string> found)
    {
        Folder = Path.GetFullPath(folder);
        _found = found;
        _watcher = new FileSystemWatcher(Folder) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
        foreach (var extension in ImageServer.Extensions) _watcher.Filters.Add("*" + extension);
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
        _watcher.Created += (_, e) => Changed(e.FullPath);
        _watcher.Changed += (_, e) => Changed(e.FullPath);
        _watcher.Renamed += (_, e) => Changed(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    public static bool Skipped(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Deeper than the Images tab looks: it wouldn't list the file anyway.
        return parts.Length == 0 || parts[0] == ".." || parts.Length - 1 > ProjectFiles.MaxDepth
            || parts.SkipLast(1).Any(part => part.StartsWith('.') || ProjectFiles.Skip.Contains(part));
    }

    void Changed(string path)
    {
        if (_disposed || !ImageServer.IsImage(path) || Skipped(Folder, path)) return;
        var wait = new CancellationTokenSource();
        _waiting.AddOrUpdate(path, wait, (_, old) => { old.Cancel(); return wait; });
        _ = Settle(path, wait);
    }

    async Task Settle(string path, CancellationTokenSource wait)
    {
        try
        {
            await Task.Delay(700, wait.Token);
            _waiting.TryRemove(new KeyValuePair<string, CancellationTokenSource>(path, wait));
            if (_disposed || !File.Exists(path) || new FileInfo(path).Length == 0) return;
            _found(path);
        }
        catch (OperationCanceledException)
        {
            // A newer change to the same file follows.
        }
        catch
        {
            // The file went away while it settled.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.Dispose();
        foreach (var wait in _waiting.Values) wait.Cancel();
    }
}
