using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace AgentArcade;

// Phone remote: the HTTPS and WebSocket server the phone app "Agent Arcade Remote" talks to.
// The contract is docs/phone-remote/REMOTE_API.md. Tokens, pairing codes and terminal text never go into the log.

// The page's answer to a phone's request: the HTTP status, a JSON object and a sentence the phone shows.
sealed record RemoteReply(int Status, JsonElement? Body = null, string? Error = null)
{
    public static RemoteReply Ok(object body) => new(200, JsonSerializer.SerializeToElement(body, RemoteModel.Wire));

    public static RemoteReply Fail(int status, string error) => new(status, null, error);

    public static RemoteReply Busy => Fail(503, "Agent Arcade is busy, try again.");
}

sealed record RemoteWatch(string Id, int Lines);

// What the server needs from the window. It is called from the thread pool; the window does the work on its own thread.
interface IRemoteHost
{
    Task<object> StateAsync();
    Task<object> ChatsAsync();
    // The state and chats a new phone connection starts with, after the page sent its newest summaries.
    Task<(object State, object Chats)> SnapshotAsync();
    // Posts a request from this phone (its device id) to the page and waits for its remoteReply: 503 when the page
    // isn't ready or takes over 10 s.
    Task<RemoteReply> AskPageAsync(string device, string type, Dictionary<string, object?> fields);
    // Checks the account and the project, then asks the page to open the pane.
    Task<RemoteReply> StartAsync(string device, string account, string workspace);
    // Makes the empty project folder name (already checked) in the projects folder and adds it to the sidebar.
    Task<RemoteReply> CreateProjectAsync(string device, string name);
    // The page drops the typing it still has queued for this phone, or for every phone when device is null.
    void Cancel(string? device);
    // The panes the phones watch changed; the page sends a screen for each pane in refresh at once.
    void WatchesChanged(IReadOnlyList<string> refresh);
    // A phone connected, left, paired or was removed, or the pairing code changed.
    void RemoteChanged();
    void Paired(string name);
    void Log(string message);
}

// The PC's own certificate. It is self-signed; the phone trusts it by the fingerprint in the pairing link.
static class RemoteCertificate
{
    public const string Name = "Agent Arcade Remote";

    public static X509Certificate2 LoadOrCreate(string name = Name)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var found = Find(store, name);
        if (found != null) return found;
        var created = Create(name);
        store.Add(created);
        return created;
    }

    // Removes the certificate and its private key (the self-check uses this for its own throw-away certificate).
    public static void Delete(string name)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        foreach (var cert in store.Certificates)
        {
            using (cert)
            {
                if (cert.FriendlyName != name) continue;
                try
                {
                    using var key = cert.GetECDsaPrivateKey();
                    (key as ECDsaCng)?.Key.Delete();
                }
                catch (CryptographicException)
                {
                    // The key is already gone.
                }
                store.Remove(cert);
            }
        }
    }

    // SHA-256 of the certificate, as the pairing link carries it: 64 lowercase hex characters.
    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexStringLower(SHA256.HashData(cert.RawData));

    // The first 8 bytes, as the Phone dialog and the phone show it: 3F:A2:91:0C:7B:44:E1:D0.
    public static string Short(string fingerprint)
        => string.Join(':', Enumerable.Range(0, 8).Select(i => fingerprint.Substring(i * 2, 2).ToUpperInvariant()));

    static X509Certificate2? Find(X509Store store, string name)
    {
        X509Certificate2? best = null;
        foreach (var cert in store.Certificates)
        {
            if (Usable(cert, name) && (best == null || cert.NotBefore > best.NotBefore))
            {
                best?.Dispose();
                best = cert;
            }
            else
            {
                cert.Dispose();
            }
        }
        return best;
    }

    static bool Usable(X509Certificate2 cert, string name)
    {
        if (cert.FriendlyName != name || cert.Subject != "CN=" + name || !cert.HasPrivateKey || cert.NotAfter < DateTime.Now.AddDays(30)) return false;
        try
        {
            using var key = cert.GetECDsaPrivateKey();
            return key != null;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    static X509Certificate2 Create(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var now = DateTimeOffset.Now;
        using var made = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(20));
        // SChannel can only use a key that is saved, so the certificate goes through a PFX into the user's key store.
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var cert = X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx, password), password,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
        cert.FriendlyName = name;
        return cert;
    }
}

sealed class RemoteDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    // Base64url of the SHA-256 of the phone's token. The token itself is never kept.
    public string TokenHash { get; set; } = "";
    public long Paired { get; set; }
    public long LastSeen { get; set; }
}

// The paired phones, in remote.json.
sealed class RemoteDevices
{
    static readonly JsonSerializerOptions FileOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    readonly object _lock = new();
    readonly string _path;
    readonly List<RemoteDevice> _devices = new();
    // When a save was last tried (Environment.TickCount64), so lastSeen is written at most once a minute.
    long _savedAt;
    bool _dirty;
    bool _loaded;
    string? _loadError;
    string? _saveError;

    public RemoteDevices(string path)
    {
        _path = path;
        Load();
    }

    // Why remote.json couldn't be read, as a sentence for the Phone dialog; null once it was read.
    public string? LoadError
    {
        get
        {
            lock (_lock) return _loadError;
        }
    }

    // Why a removed phone isn't out of remote.json yet; null once it is.
    public string? SaveError
    {
        get
        {
            lock (_lock) return _saveError;
        }
    }

    public static bool IsToken(string token) => token.Length == 43 && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string Hash(string token) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    public List<RemoteDevice> All()
    {
        lock (_lock) return _devices.Select(Copy).ToList();
    }

    public bool Exists(string id)
    {
        lock (_lock) return _devices.Any(device => device.Id == id);
    }

    // A new phone and its token. The token goes to the phone once; only its hash stays here.
    public (RemoteDevice Device, string Token) Add(string name)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        lock (_lock)
        {
            string id;
            do id = "d_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));
            while (_devices.Any(device => device.Id == id));
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var added = new RemoteDevice { Id = id, Name = name, TokenHash = Hash(token), Paired = now, LastSeen = now };
            _devices.Add(added);
            try
            {
                Save();
            }
            catch
            {
                _devices.Remove(added);
                throw;
            }
            return (Copy(added), token);
        }
    }

    // The phone that holds this token. Every stored hash is compared in constant time, without stopping at a match.
    public RemoteDevice? Find(string token)
    {
        if (!IsToken(token)) return null;
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(token));
        lock (_lock)
        {
            RemoteDevice? found = null;
            foreach (var device in _devices)
            {
                if (CryptographicOperations.FixedTimeEquals(hash, Decode(device.TokenHash)) && found == null) found = device;
            }
            return found == null ? null : Copy(found);
        }
    }

    // The phone is gone from memory at once, even when remote.json can't be saved right now. The removal is then
    // saved at the next chance (the next save, or when the server stops) and SaveError says so until it is.
    public RemoteDevice? Remove(string id)
    {
        lock (_lock)
        {
            var device = _devices.FirstOrDefault(item => item.Id == id);
            if (device == null) return null;
            _devices.Remove(device);
            _dirty = true;
            if (!TrySave())
                _saveError = "Agent Arcade couldn't save remote.json, so a removed phone could come back after a restart. Close any program that has the file open; Agent Arcade keeps trying.";
            return device;
        }
    }

    // Every request counts as seen; the file is written at most once a minute for it.
    public void Seen(string id)
    {
        lock (_lock)
        {
            var device = _devices.FirstOrDefault(item => item.Id == id);
            if (device == null) return;
            device.LastSeen = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            _dirty = true;
            if (Environment.TickCount64 - _savedAt > 60_000) TrySave();
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (_dirty) TrySave();
        }
    }

    // Reads remote.json when it couldn't be read before, trying twice. A file that exists but can't be read is
    // never saved over, so the paired phones come back once it can be read. False while it can't.
    public bool Load()
    {
        lock (_lock)
        {
            if (_loaded) return true;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    _devices.Clear();
                    if (File.Exists(_path)) _devices.AddRange(Read(_path));
                    _loaded = true;
                    _loadError = null;
                    return true;
                }
                catch (Exception) when (attempt == 1)
                {
                    // Antivirus or a backup program may hold the file for a moment.
                    Thread.Sleep(250);
                }
                catch (JsonException)
                {
                    _devices.Clear();
                    _loadError = "remote.json is damaged, so Phone remote stays off to keep your paired phones. Fix or delete it in "
                        + Path.GetDirectoryName(_path) + " (after deleting it you pair your phones again), then switch Phone remote on again.";
                    return false;
                }
                catch (Exception)
                {
                    _devices.Clear();
                    _loadError = "remote.json couldn't be read, so Phone remote stays off to keep your paired phones. Close any program that has it open and switch Phone remote on again.";
                    return false;
                }
            }
        }
    }

    static List<RemoteDevice> Read(string path)
        => (JsonSerializer.Deserialize<List<RemoteDevice?>>(File.ReadAllText(path), FileOptions) ?? throw new JsonException("remote.json has no list of phones."))
            .OfType<RemoteDevice>()
            .Where(device => device is { Id.Length: > 0, TokenHash.Length: > 0 })
            .ToList();

    bool TrySave()
    {
        try
        {
            Save();
            return true;
        }
        catch
        {
            // Saved again at the next chance.
            return false;
        }
    }

    void Save()
    {
        if (!_loaded) throw new InvalidOperationException(_loadError);
        _savedAt = Environment.TickCount64;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_devices, FileOptions));
        File.Move(temp, _path, true);
        _dirty = false;
        _saveError = null;
    }

    static byte[] Decode(string hash)
    {
        try
        {
            return Base64Url.DecodeFromChars(hash);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    static RemoteDevice Copy(RemoteDevice device)
        => new() { Id = device.Id, Name = device.Name, TokenHash = device.TokenHash, Paired = device.Paired, LastSeen = device.LastSeen };
}

enum PairResult { Ok, None, Wrong, Expired, Used, Cancelled }

// The one-time code in the QR code: 10 minutes, single use, and five wrong codes in a row cancel it.
sealed class PairingCodes
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    const int MaxWrong = 5, MaxAttempts = 10;

    readonly object _lock = new();
    readonly Func<DateTimeOffset> _now;
    readonly Queue<DateTimeOffset> _attempts = new();
    string? _code;
    DateTimeOffset _expires;
    string _status = "";
    int _wrong;
    string? _pairedName;

    public PairingCodes(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.Now);

    public string New()
    {
        lock (_lock)
        {
            _code = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
            _expires = _now() + Lifetime;
            _status = "valid";
            _wrong = 0;
            _pairedName = null;
            return _code;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _code = null;
            _status = "";
            _pairedName = null;
        }
    }

    // For the Phone dialog: the code while it still works, its state (valid, used, expired, cancelled) and when it expires.
    public (string? Code, string? Status, long? ExpiresAt, string? PairedName) Info()
    {
        lock (_lock)
        {
            if (_code == null) return (null, null, null, null);
            var status = _status == "valid" && _now() >= _expires ? "expired" : _status;
            return (status == "valid" ? _code : null, status, _expires.ToUnixTimeMilliseconds(), _pairedName);
        }
    }

    // At most 10 pairing attempts a minute in total. Every attempt counts, before its code is looked at.
    public bool Attempt()
    {
        lock (_lock)
        {
            var now = _now();
            while (_attempts.Count > 0 && now - _attempts.Peek() >= TimeSpan.FromMinutes(1)) _attempts.Dequeue();
            if (_attempts.Count >= MaxAttempts) return false;
            _attempts.Enqueue(now);
            return true;
        }
    }

    public PairResult Check(string code, string deviceName)
    {
        lock (_lock)
        {
            if (_code == null) return PairResult.None;
            var expired = _now() >= _expires;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(_code)))
            {
                if (_status != "valid" || expired) return PairResult.Wrong;
                if (++_wrong < MaxWrong) return PairResult.Wrong;
                _status = "cancelled";
                return PairResult.Cancelled;
            }
            if (_status == "used") return PairResult.Used;
            if (_status == "cancelled") return PairResult.Cancelled;
            if (expired) return PairResult.Expired;
            _status = "used";
            _pairedName = deviceName;
            return PairResult.Ok;
        }
    }
}

// The pairing link in the QR code, and the addresses it lists.
static class RemoteLink
{
    // LAN addresses first (only adapters with a default gateway, so no Hyper-V, WSL or Docker switches), then Tailscale.
    public static List<string> Hosts(int port, bool loopbackOnly)
    {
        if (loopbackOnly) return ["127.0.0.1:" + port];
        var lan = new List<string>();
        var tailscale = new List<string>();
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
        foreach (var adapter in adapters)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            IPInterfaceProperties properties;
            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }
            var gateway = properties.GatewayAddresses.Any(item => item.Address.AddressFamily == AddressFamily.InterNetwork && !item.Address.Equals(IPAddress.Any));
            foreach (var unicast in properties.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) continue;
                var bytes = address.GetAddressBytes();
                if (gateway && !(bytes[0] == 169 && bytes[1] == 254)) lan.Add(address + ":" + port);
                if (IsTailscale(address)) tailscale.Add(address + ":" + port);
            }
        }
        return lan.Concat(tailscale).Distinct().ToList();
    }

    // A sentence for the Phone dialog when a LAN address in hosts is on a network Windows treats as public: Windows
    // then blocks the phone there. Null when there is none, or when Windows can't tell.
    public static string? PublicNetworkWarning(IEnumerable<string> hosts)
    {
        try
        {
            var lan = hosts.Select(host => IPAddress.TryParse(host.Split(':')[0], out var address) ? address : null)
                .OfType<IPAddress>()
                .Where(address => !IsTailscale(address))
                .ToHashSet();
            if (lan.Count == 0) return null;
            var networks = NetworkProfiles.PublicNetworks();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!Guid.TryParse(adapter.Id, out var id) || !networks.TryGetValue(id, out var name)) continue;
                if (!adapter.GetIPProperties().UnicastAddresses.Any(unicast => lan.Contains(unicast.Address))) continue;
                var page = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi > " + name : "Ethernet";
                return "Windows treats \"" + name + "\" as a public network, so it blocks your phone there. Set it to Private in Settings > Network & internet > "
                    + page + ", or use Tailscale.";
            }
        }
        catch (Exception)
        {
            // No warning when Windows can't tell.
        }
        return null;
    }

    static bool IsTailscale(IPAddress address)
        => address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes() is [100, >= 64 and <= 127, _, _];

    public static string Build(string pcName, IEnumerable<string> hosts, string code, string fingerprint)
        => "agentarcade://pair?v=1&name=" + Uri.EscapeDataString(pcName) + "&hosts=" + string.Join(',', hosts) + "&code=" + code + "&fp=" + fingerprint;

    public static string Qr(string link)
        => "data:image/png;base64," + Convert.ToBase64String(PngByteQRCodeHelper.GetQRCode(link, QRCodeGenerator.ECCLevel.M, 8, true));
}

// The networks Windows treats as public, from the Network List Manager.
static class NetworkProfiles
{
    static readonly Guid ListManager = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
    const int Connected = 1, Public = 0;

    // Adapter id -> the name of the public network it is connected to.
    public static Dictionary<Guid, string> PublicNetworks()
    {
        var found = new Dictionary<Guid, string>();
        dynamic manager = Activator.CreateInstance(Type.GetTypeFromCLSID(ListManager, true)!)!;
        foreach (dynamic network in manager.GetNetworks(Connected))
        {
            if ((int)network.GetCategory() != Public) continue;
            string name = network.GetName();
            foreach (object connection in network.GetNetworkConnections())
                found[((INetworkConnection)connection).GetAdapterId()] = name;
        }
        return found;
    }

    // The interface up to GetAdapterId, which has to be called directly: a GUID can't come back through IDispatch.
    [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    interface INetworkConnection
    {
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object GetNetwork();

        bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }

        bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }

        int GetConnectivity();

        Guid GetConnectionId();

        Guid GetAdapterId();
    }
}

// New project from the phone (REMOTE_API.md, new project): an empty folder in the projects folder. The phone sends
// only a name; the path is made here, and only as a direct child of that folder.
static partial class NewProject
{
    public const int MaxName = 80;

    // Names Windows keeps for devices, alone or with an extension: CON, nul.txt, com1, LPT³.
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Reserved();

    // Why name (already trimmed) can't be a project folder, or null when it can. The same sentences as the phone.
    public static string? Problem(string name)
    {
        if (name.Length == 0) return "Give the project a name.";
        if (name.Length > MaxName) return "Use at most " + MaxName + " characters.";
        if (name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0) return "A folder name can't contain \\ / : * ? \" < > |.";
        if (name.Any(char.IsControl)) return "A folder name can't contain tabs or other control characters.";
        if (name.EndsWith('.')) return "A folder name can't end with a dot.";
        if (Reserved().IsMatch(name)) return "Windows keeps " + name.Split('.')[0].ToUpperInvariant() + " for itself. Choose another name.";
        return null;
    }

    // The folder that holds workspace, or when that has none or is missing, the one of the first listed workspace
    // that has one; null when none has.
    public static string? Folder(string workspace, IEnumerable<string> projects)
    {
        foreach (var path in projects.Prepend(workspace))
        {
            try
            {
                if (path.Length == 0) continue;
                var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
                if (parent != null && Directory.Exists(parent)) return parent;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
            {
                // A path Windows can't read doesn't count.
            }
        }
        return null;
    }

    // Why name (already trimmed) can't be made in folder right now, with the status the phone gets; null when it can.
    // The page's New project dialog asks this while you type.
    public static (int Status, string Problem)? Refusal(string? folder, string name)
    {
        if (Problem(name) is { } problem) return (400, problem);
        if (folder == null || !Directory.Exists(folder)) return (404, "Agent Arcade on your PC has no projects folder. Add a project there first.");
        var path = Path.Combine(folder, name);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase))
            return (400, "That name can't be a folder.");
        if (Directory.Exists(path) || File.Exists(path)) return (409, "There's already a folder called " + name + " in " + folder + ".");
        return null;
    }

    // Makes the empty folder name in folder: 201 { workspace }, or 400, 404, 409 with the sentence the phone shows.
    public static (string? Path, RemoteReply Reply) Create(string? folder, string name)
    {
        name = name.Trim();
        if (Refusal(folder, name) is { } refusal) return (null, RemoteReply.Fail(refusal.Status, refusal.Problem));
        var path = Path.Combine(folder!, name);
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, RemoteReply.Fail(409, "Windows didn't let Agent Arcade make " + name + " in " + folder + "."));
        }
        return (path, Made(path));
    }

    // Create, then lists the folder at the end of projects. A listed folder that was missing on disk stays where it is,
    // so it never shows twice; the path is then the listed one.
    public static (string? Path, RemoteReply Reply) CreateAndList(string? folder, string name, List<string> projects)
    {
        var (path, reply) = Create(folder, name);
        if (path == null) return (null, reply);
        var listed = projects.FirstOrDefault(item => string.Equals(Path.TrimEndingDirectorySeparator(item), path, StringComparison.OrdinalIgnoreCase));
        if (listed != null) return (listed, Made(listed));
        projects.Add(path);
        return (path, reply);
    }

    static RemoteReply Made(string path) => new(201, JsonSerializer.SerializeToElement(new { workspace = path }, RemoteModel.Wire));
}

// The JSON the phone gets, and the checks on what it sends.
static class RemoteModel
{
    public const int Api = 1;

    // Non-ASCII text such as box drawing stays as it is instead of becoming \uXXXX escapes.
    public static readonly JsonSerializerOptions Wire = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
    static readonly JsonWriterOptions WireWriter = new() { Encoder = Wire.Encoder };

    static readonly Dictionary<string, string> Colors = new()
    {
        ["claude"] = "#d97757", ["codex"] = "#10a37f", ["gemini"] = "#4f8cff", ["copilot"] = "#a371f7", ["cursor-agent"] = "#7c8aa5",
    };

    static readonly HashSet<string> NamedKeys = ["enter", "esc", "tab", "shiftTab", "space", "up", "down", "left", "right", "backspace", "ctrlC"];
    static readonly Regex SessionId = new(@"^s[0-9]{1,9}\z", RegexOptions.CultureInvariant);
    static readonly Regex ChatId = new(@"^[A-Za-z0-9-]{1,100}\z", RegexOptions.CultureInvariant);

    public static string Pc => Environment.MachineName;

    // The app's version without the commit hash the build adds: "1.3.0".
    public static string Version { get; } =
        (typeof(RemoteModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0];

    public static string? Color(string command) => Colors.GetValueOrDefault(command.ToLowerInvariant());

    public static object Hello() => new { app = "Agent Arcade", api = Api, version = Version, pc = Pc };

    public static object PairAnswer(string token, string deviceId) => new { token, deviceId, pc = Pc };

    public static List<object> Workspaces(IEnumerable<string> projects) => projects.Select(path => (object)new
    {
        path,
        name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } folder ? folder : path,
        exists = Directory.Exists(path),
        unity = File.Exists(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt")),
    }).ToList();

    // Whether the PC may go to sleep (State.power). keepAwake is the setting, awake whether the request is held now.
    public static object Power(string keepAwake, bool awake)
    {
        var (_, onBattery, lidSleeps) = KeepAwake.Status();
        return new { keepAwake, awake, onBattery, lidSleeps };
    }

    // tools holds the probe results by agent name; while the PC checks again, the previous ones. hosts are the
    // addresses of the pairing link.
    public static object State(
        string workspace,
        IEnumerable<string> projects,
        IEnumerable<AgentDef> agents,
        IReadOnlyDictionary<string, ToolStatus>? tools,
        IEnumerable<AgentAccount> accounts,
        IReadOnlyDictionary<string, AccountInfo>? infos,
        IEnumerable<JsonElement> sessions,
        IEnumerable<string> hosts,
        object? power = null) => new
    {
        api = Api,
        pc = Pc,
        version = Version,
        workspace,
        projectsFolder = NewProject.Folder(workspace, projects),
        power,
        workspaces = Workspaces(projects),
        agents = agents.Select(agent =>
        {
            ToolStatus? tool = null;
            tools?.TryGetValue(agent.Name, out tool);
            return new { name = agent.Name, command = agent.Command, installed = tool?.Path != null, version = tool?.Version ?? "", color = Color(agent.Command) };
        }).ToList(),
        accounts = accounts.Select(account =>
        {
            AccountInfo? info = null;
            infos?.TryGetValue(account.Key, out info);
            return new { key = account.Key, agent = account.Agent, name = account.Name, main = account.IsMain, signedIn = info?.SignedIn, email = info?.Email ?? "", plan = info?.Plan ?? "" };
        }).ToList(),
        sessions = sessions.ToList(),
        hosts = hosts.ToList(),
    };

    public static object Chat(ChatRecord chat, AgentDef? agent, string? session) => new
    {
        id = chat.Id,
        agent = chat.Agent,
        account = chat.AccountKey,
        accountName = chat.Account,
        workspace = chat.Workspace,
        // The same fallback as Session.title. named stays false.
        title = chat.Title.Length > 0 ? TextGuard.Redact(chat.Title) : "New chat",
        named = chat.Named,
        startedAt = chat.Started.ToUnixTimeMilliseconds(),
        endedAt = chat.Ended?.ToUnixTimeMilliseconds(),
        open = session != null,
        session,
        resume = ChatArgs.Kind(agent, chat) is { Length: > 0 } kind ? kind : "none",
    };

    // Newest first, at most 300. sessionOf gives the pane that has a chat open, or null.
    public static List<object> Chats(IEnumerable<ChatRecord> history, IReadOnlyList<AgentDef> agents, Func<ChatRecord, string?> sessionOf)
        => history.OrderByDescending(chat => chat.LastActive).Take(300)
            .Select(chat => Chat(chat, agents.FirstOrDefault(agent => agent.Name == chat.Agent), sessionOf(chat)))
            .ToList();

    // The fields the phone matches by; they are never changed.
    static readonly HashSet<string> MatchFields = ["id", "chat", "account", "workspace"];

    // A Session or Attention as the page built it, with TextGuard applied to its text (title, message, question,
    // preview, plan and the rest), so a key typed or pasted into a pane doesn't reach the phone in a summary.
    public static JsonElement Redacted(JsonElement item)
    {
        var raw = item.GetRawText();
        if (TextGuard.Redact(raw) == raw) return item;
        var node = JsonNode.Parse(raw);
        RedactStrings(node);
        return JsonSerializer.SerializeToElement(node, Wire);
    }

    static void RedactStrings(JsonNode? node)
    {
        if (node is JsonObject item)
        {
            foreach (var (name, child) in item.ToList())
            {
                if (MatchFields.Contains(name)) continue;
                if (child is JsonValue value && value.TryGetValue<string>(out var text)) item[name] = TextGuard.Redact(text);
                else RedactStrings(child);
            }
        }
        else if (node is JsonArray list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is JsonValue value && value.TryGetValue<string>(out var text)) list[i] = TextGuard.Redact(text);
                else RedactStrings(list[i]);
            }
        }
    }

    public static bool IsSessionId(string id) => SessionId.IsMatch(id);

    public static bool IsChatId(string id) => ChatId.IsMatch(id);

    public static bool IsKey(string name)
        => NamedKeys.Contains(name) || (name.Length == 1 && name[0] is >= '0' and <= '9' or >= 'a' and <= 'z' or '/' or '?' or '!');

    // How many lines of a pane: 1 to 2000, 200 when not given.
    public static int Lines(string? text)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var lines) ? (int)Math.Clamp(lines, 1, 2000) : 200;

    public static int Lines(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var lines) ? (int)Math.Clamp(lines, 1, 2000) : 200;

    // The phone's own name for itself. It is shown on the PC, so control and formatting characters (such as
    // right-to-left overrides) go, spaces are collapsed and it is cut to 60 characters.
    public static string DeviceName(string name)
    {
        var kept = new StringBuilder();
        foreach (var rune in name.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune)) kept.Append(' ');
            else if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.PrivateUse)) kept.Append(rune.ToString());
        }
        var clean = Cut(string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)), 60).TrimEnd();
        return clean.Length == 0 ? "Phone" : clean;
    }

    // Cuts text to at most max characters without splitting an emoji or other surrogate pair.
    public static string Cut(string text, int max)
        => text.Length <= max ? text : text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)];

    // An IPv4 address that arrived as IPv6 (::ffff:a.b.c.d) as plain IPv4.
    public static IPAddress Plain(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    // Only the local network and Tailscale may connect: 127/8, 10/8, 172.16/12, 192.168/16, 169.254/16 and 100.64/10.
    // This keeps the port closed to the internet even when the PC has a public address or a forwarded port.
    public static bool IsAllowedAddress(EndPoint? endPoint)
    {
        if (endPoint is not IPEndPoint { Address: var found }) return false;
        var address = Plain(found);
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] is 127 or 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    // The HTTP body of a reply: its JSON object, plus "error" when it has an error sentence.
    public static byte[] ReplyJson(RemoteReply reply)
    {
        var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WireWriter))
        {
            writer.WriteStartObject();
            if (reply.Body is { ValueKind: JsonValueKind.Object } body)
            {
                foreach (var property in body.EnumerateObject())
                    if (!property.NameEquals("error")) property.WriteTo(writer);
            }
            if (reply.Error != null) writer.WriteString("error", reply.Error);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    // A screen event cut to the last lines a phone asked for: lines drop from the front and from goes up by as many.
    public static byte[] ScreenMessage(JsonElement screen, int lines)
    {
        var count = screen.TryGetProperty("lines", out var all) && all.ValueKind == JsonValueKind.Array ? all.GetArrayLength() : 0;
        var drop = Math.Max(0, count - lines);
        var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WireWriter))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "screen");
            writer.WritePropertyName("screen");
            writer.WriteStartObject();
            foreach (var property in screen.EnumerateObject())
            {
                if (drop > 0 && property.NameEquals("lines"))
                {
                    writer.WriteStartArray("lines");
                    foreach (var line in property.Value.EnumerateArray().Skip(drop)) line.WriteTo(writer);
                    writer.WriteEndArray();
                }
                else if (drop > 0 && property.NameEquals("from") && property.Value.TryGetInt64(out var from))
                {
                    writer.WriteNumber("from", from + drop);
                }
                else
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}

// 20 requests a second per phone, in bursts of up to 20.
sealed class RequestLimiter
{
    const double Rate = 20;
    readonly object _lock = new();
    readonly Dictionary<string, (double Tokens, long At)> _buckets = new();

    public bool Allow(string key)
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;
            var (tokens, at) = _buckets.TryGetValue(key, out var bucket) ? bucket : (Rate, now);
            tokens = Math.Min(Rate, tokens + (now - at) * Rate / 1000);
            var allowed = tokens >= 1;
            _buckets[key] = (allowed ? tokens - 1 : tokens, now);
            return allowed;
        }
    }
}

// More than 20 wrong or missing tokens in a minute from one address shut that address out for a minute.
sealed class AuthFailures
{
    const int Limit = 20;
    const long Minute = 60_000;
    readonly object _lock = new();
    readonly Dictionary<IPAddress, (long Since, int Count, long Until)> _seen = new();

    public bool Blocked(IPAddress address)
    {
        lock (_lock) return _seen.TryGetValue(address, out var entry) && entry.Until > Environment.TickCount64;
    }

    public void Failed(IPAddress address)
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;
            if (_seen.Count > 1000)
            {
                foreach (var old in _seen.Where(pair => pair.Value.Until < now && now - pair.Value.Since > Minute).Select(pair => pair.Key).ToList())
                    _seen.Remove(old);
            }
            var (since, count, until) = _seen.TryGetValue(address, out var entry) && now - entry.Since < Minute ? entry : (now, 0, 0L);
            count++;
            if (count > Limit) until = now + Minute;
            _seen[address] = (since, count, until);
        }
    }
}

// One phone's live connection. Events wait in order; of the screens, only the newest per pane waits.
sealed class PhoneSocket
{
    const int MaxWaiting = 500;

    readonly object _lock = new();
    readonly List<(string? Screen, byte[] Bytes)> _waiting = new();
    readonly SemaphoreSlim _signal = new(0);
    readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _events;
    bool _started;
    (WebSocketCloseStatus Status, string Text)? _close;

    public PhoneSocket(WebSocket socket, string deviceId)
    {
        Socket = socket;
        DeviceId = deviceId;
    }

    public WebSocket Socket { get; }
    public string DeviceId { get; }
    // Pane id -> how many lines this phone wants. Guarded by the server's lock.
    public Dictionary<string, int> Watches { get; } = new();
    public Task Finished => _finished.Task;

    public void Done() => _finished.TrySetResult();

    public void Send(byte[] bytes)
    {
        lock (_lock)
        {
            if (_close != null) return;
            if (_events >= MaxWaiting)
            {
                // Too far behind: the phone reconnects and starts again from hello.
                _close = (WebSocketCloseStatus.PolicyViolation, "Too many messages were waiting. Reconnect.");
            }
            else
            {
                _waiting.Add((null, bytes));
                _events++;
            }
        }
        _signal.Release();
    }

    public void SendScreen(string id, byte[] bytes)
    {
        lock (_lock)
        {
            if (_close != null) return;
            var index = _waiting.FindIndex(item => item.Screen == id);
            if (index >= 0) _waiting[index] = (id, bytes);
            else _waiting.Add((id, bytes));
        }
        _signal.Release();
    }

    // hello and history go out first, before anything that waited for them.
    public void Begin(byte[] hello, byte[] history)
    {
        lock (_lock)
        {
            _waiting.InsertRange(0, [(null, hello), (null, history)]);
            _events += 2;
            _started = true;
        }
        _signal.Release();
    }

    public void Close(WebSocketCloseStatus status, string text)
    {
        lock (_lock) _close ??= (status, text);
        _signal.Release();
    }

    public async Task SendLoopAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync();
                while (TryTake(out var bytes, out var close))
                {
                    if (close is { } closing)
                    {
                        await CloseOutputAsync(closing.Status, closing.Text);
                        return;
                    }
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
                }
            }
        }
        catch (Exception)
        {
            // A phone that can't take messages any more is cut off.
            Abort();
        }
    }

    bool TryTake(out byte[] bytes, out (WebSocketCloseStatus Status, string Text)? close)
    {
        lock (_lock)
        {
            bytes = [];
            close = _close;
            if (close != null) return true;
            if (!_started || _waiting.Count == 0) return false;
            var (screen, next) = _waiting[0];
            _waiting.RemoveAt(0);
            if (screen == null) _events--;
            bytes = next;
            return true;
        }
    }

    async Task CloseOutputAsync(WebSocketCloseStatus status, string text)
    {
        if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Socket.CloseOutputAsync(status, text, timeout.Token);
        }
        _ = AbortUnlessClosedAsync();
    }

    // A phone that doesn't answer the close is cut off.
    async Task AbortUnlessClosedAsync()
    {
        await Task.Delay(5000);
        if (Socket.State != WebSocketState.Closed) Abort();
    }

    void Abort()
    {
        try
        {
            Socket.Abort();
        }
        catch (Exception)
        {
            // Already gone.
        }
    }
}

// The server the phones talk to. It runs only while Phone remote is on.
sealed class RemoteServer
{
    const int MaxPhones = 4, MaxBody = 64 * 1024, MaxText = 16 * 1024, MaxMessage = 4 * 1024, MaxWatches = 8;
    // Requests one phone may have waiting for the page, and connections one address may have open.
    const int MaxPageRequests = 4, MaxConnections = 8;
    public const string NotPaired = "This phone isn't paired any more. Pair it again on your PC.";

    static readonly RemoteReply Unknown = RemoteReply.Fail(404, "Unknown request.");
    static readonly RemoteReply Invalid = RemoteReply.Fail(400, "That request isn't valid.");
    static readonly RemoteReply PaneGone = RemoteReply.Fail(404, "That agent isn't open on the PC any more.");
    static readonly RemoteReply TooMany = RemoteReply.Fail(429, "Too many requests. Wait a moment.");

    // The requests one phone has waiting for the page, and the signal that answers them all at once.
    sealed class PageRequests
    {
        public int Count;
        public readonly TaskCompletionSource Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    readonly IRemoteHost _host;
    readonly string _certificateName;
    readonly object _lock = new();
    readonly List<PhoneSocket> _sockets = new();
    // The last screen of each watched pane, so a phone that starts watching sees it at once.
    readonly Dictionary<string, JsonElement> _screens = new();
    // Device id -> its requests waiting for the page. Guarded by _lock.
    readonly Dictionary<string, PageRequests> _pageRequests = new();
    // Address -> its open connections. It is its own lock.
    readonly Dictionary<IPAddress, int> _connections = new();
    readonly RequestLimiter _limiter = new();
    readonly AuthFailures _failures = new();
    readonly SemaphoreSlim _gate = new(1, 1);
    WebApplication? _app;
    X509Certificate2? _certificate;
    (bool On, int Port, bool Loopback) _wanted;
    int _codeNumber;
    volatile bool _running;

    public RemoteServer(IRemoteHost host, string devicesPath, string certificateName = RemoteCertificate.Name)
    {
        _host = host;
        _certificateName = certificateName;
        Devices = new RemoteDevices(devicesPath);
    }

    public RemoteDevices Devices { get; }
    public PairingCodes Pairing { get; } = new();
    public bool Running => _running;
    public int Port { get; private set; }
    public bool LoopbackOnly { get; private set; }
    // Why the server couldn't start, as a sentence for the Phone dialog.
    public string? Error { get; private set; }
    public string? Fingerprint { get; private set; }

    // The addresses the server listens on; the self-check makes sure there is only the one it was given.
    public List<string> Urls() => _app?.Urls.ToList() ?? [];

    public int Phones
    {
        get
        {
            lock (_lock) return _sockets.Count;
        }
    }

    // Open connections from every address; the self-check waits on it.
    public int Connections
    {
        get
        {
            lock (_connections) return _connections.Values.Sum();
        }
    }

    // Starts or stops the server to match the last call. Calls may overlap; the last one wins.
    public Task ApplyAsync(bool on, int port, bool loopbackOnly)
    {
        lock (_lock) _wanted = (on, port, loopbackOnly);
        return Task.Run(ApplyWantedAsync);
    }

    public string NewCode()
    {
        var code = Pairing.New();
        _ = TellWhenExpiredAsync(Interlocked.Increment(ref _codeNumber));
        return code;
    }

    // Removes a paired phone: its token stops working, its connections close with 4401, its requests that wait for
    // the page get 401 and the page drops the typing it still has queued for it, all at once.
    public RemoteDevice? RemoveDevice(string id)
    {
        var removed = Devices.Remove(id);
        List<PhoneSocket> sockets;
        lock (_lock) sockets = _sockets.Where(socket => socket.DeviceId == id).ToList();
        foreach (var socket in sockets) socket.Close((WebSocketCloseStatus)4401, "This phone was removed on the PC.");
        EndPageRequests(id);
        if (removed != null && Devices.SaveError != null) _host.Log("Phone removal isn't saved in remote.json yet.");
        return removed;
    }

    public HashSet<string> ConnectedDevices()
    {
        lock (_lock) return _sockets.Select(socket => socket.DeviceId).ToHashSet();
    }

    // Each watched pane with the most lines any phone wants.
    public List<RemoteWatch> Watches()
    {
        lock (_lock)
        {
            return _sockets.SelectMany(socket => socket.Watches)
                .GroupBy(watch => watch.Key)
                .Select(group => new RemoteWatch(group.Key, group.Max(watch => watch.Value)))
                .ToList();
        }
    }

    // Sends an event to every connected phone.
    public void Send(object message)
    {
        List<PhoneSocket> sockets;
        lock (_lock)
        {
            if (_sockets.Count == 0) return;
            sockets = _sockets.ToList();
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, RemoteModel.Wire);
        foreach (var socket in sockets) socket.Send(bytes);
    }

    public void Closed(string id)
    {
        lock (_lock) _screens.Remove(id);
        Send(new { type = "closed", id });
    }

    // A screen from the page goes to the phones that watch its pane, cut to the lines each one asked for.
    public void Screen(JsonElement screen)
    {
        var id = Json.Str(screen, "id");
        if (!RemoteModel.IsSessionId(id)) return;
        List<(PhoneSocket Socket, int Lines)> watchers;
        lock (_lock)
        {
            watchers = _sockets.Where(socket => socket.Watches.ContainsKey(id)).Select(socket => (socket, socket.Watches[id])).ToList();
            if (watchers.Count == 0) return;
            _screens[id] = screen.Clone();
        }
        var made = new Dictionary<int, byte[]>();
        foreach (var (socket, lines) in watchers)
        {
            if (!made.TryGetValue(lines, out var bytes)) made[lines] = bytes = RemoteModel.ScreenMessage(screen, lines);
            socket.SendScreen(id, bytes);
        }
    }

    public void ForgetScreens()
    {
        lock (_lock) _screens.Clear();
    }

    async Task TellWhenExpiredAsync(int number)
    {
        await Task.Delay(PairingCodes.Lifetime + TimeSpan.FromSeconds(1));
        if (number == Volatile.Read(ref _codeNumber)) _host.RemoteChanged();
    }

    // ---------- requests that wait for the page ----------

    Task<RemoteReply> AskPageAsync(string device, string type, Dictionary<string, object?> fields)
        => WaitForPageAsync(device, () => _host.AskPageAsync(device, type, fields));

    // A phone may have at most 4 requests waiting for the page, so it can't queue up minutes of typing. They end at
    // once with 503 when the phone is removed (RouteAsync then answers 401) or the server stops.
    async Task<RemoteReply> WaitForPageAsync(string device, Func<Task<RemoteReply>> ask)
    {
        PageRequests waiting;
        lock (_lock)
        {
            // Checked under the lock EndPageRequests takes, so a request is either ended by a removal or sees it.
            if (!Running) return RemoteReply.Busy;
            if (!Devices.Exists(device)) return RemoteReply.Fail(401, NotPaired);
            if (!_pageRequests.TryGetValue(device, out var found)) _pageRequests[device] = found = new();
            if (found.Count >= MaxPageRequests) return TooMany;
            waiting = found;
            waiting.Count++;
        }
        try
        {
            var asked = ask();
            return await Task.WhenAny(asked, waiting.Ended.Task) == asked ? await asked : RemoteReply.Busy;
        }
        finally
        {
            lock (_lock)
            {
                if (--waiting.Count == 0 && _pageRequests.GetValueOrDefault(device) == waiting) _pageRequests.Remove(device);
            }
        }
    }

    // Ends the waiting requests of one phone, or of every phone when device is null, and tells the page to drop
    // their queued typing.
    void EndPageRequests(string? device)
    {
        var ended = new List<PageRequests>();
        lock (_lock)
        {
            foreach (var id in device == null ? _pageRequests.Keys.ToList() : [device])
                if (_pageRequests.Remove(id, out var waiting)) ended.Add(waiting);
        }
        foreach (var waiting in ended) waiting.Ended.TrySetResult();
        _host.Cancel(device);
    }

    // ---------- connections ----------

    // Runs before TLS, so addresses outside the local network don't even get a handshake, and one address can't
    // take every connection the server allows.
    async Task FilterAsync(ConnectionContext connection, ConnectionDelegate next)
    {
        var address = connection.RemoteEndPoint is IPEndPoint endPoint && RemoteModel.IsAllowedAddress(endPoint) ? RemoteModel.Plain(endPoint.Address) : null;
        if (address == null || !OpenConnection(address))
        {
            connection.Abort();
            return;
        }
        try
        {
            await next(connection);
        }
        finally
        {
            CloseConnection(address);
        }
    }

    bool OpenConnection(IPAddress address)
    {
        lock (_connections)
        {
            var open = _connections.GetValueOrDefault(address);
            if (open >= MaxConnections) return false;
            _connections[address] = open + 1;
            return true;
        }
    }

    void CloseConnection(IPAddress address)
    {
        lock (_connections)
        {
            if (--_connections[address] == 0) _connections.Remove(address);
        }
    }

    // ---------- start and stop ----------

    async Task ApplyWantedAsync()
    {
        await _gate.WaitAsync();
        try
        {
            (bool On, int Port, bool Loopback) wanted;
            lock (_lock) wanted = _wanted;
            if (_app != null && (!wanted.On || wanted.Port != Port || wanted.Loopback != LoopbackOnly)) await StopServerAsync();
            if (!wanted.On)
            {
                Error = null;
                Devices.Flush();
            }
            else if (_app == null)
            {
                await StartServerAsync(wanted.Port, wanted.Loopback);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task StartServerAsync(int port, bool loopbackOnly)
    {
        Error = null;
        if (port is < 1024 or > 65535)
        {
            Error = "RemotePort in settings.json must be a number from 1024 to 65535. Fix it, then switch Phone remote off and on.";
            return;
        }
        // Without the paired phones, each of them would get 401 and forget its pairing.
        if (!Devices.Load())
        {
            Error = Devices.LoadError;
            _host.Log("Phone remote stays off: remote.json couldn't be read.");
            return;
        }
        WebApplication? app = null;
        try
        {
            _certificate ??= RemoteCertificate.LoadOrCreate(_certificateName);
            Fingerprint = RemoteCertificate.Fingerprint(_certificate);
            app = Build(port, loopbackOnly, _certificate);
            await app.StartAsync();
            _app = app;
            Port = port;
            LoopbackOnly = loopbackOnly;
            _running = true;
        }
        catch (Exception ex)
        {
            if (app != null) await app.DisposeAsync();
            Error = StartError(ex, port);
            _host.Log("Phone remote couldn't start: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    async Task StopServerAsync()
    {
        var app = _app!;
        _app = null;
        _running = false;
        EndPageRequests(null);
        List<PhoneSocket> sockets;
        lock (_lock) sockets = _sockets.ToList();
        foreach (var socket in sockets) socket.Close(WebSocketCloseStatus.EndpointUnavailable, "Phone remote is off.");
        await Task.WhenAny(Task.WhenAll(sockets.Select(socket => socket.Finished)), Task.Delay(2000));
        try
        {
            using var timeout = new CancellationTokenSource(3000);
            await app.StopAsync(timeout.Token);
        }
        catch (Exception)
        {
            // Connections that didn't finish in time are cut.
        }
        try
        {
            await app.DisposeAsync();
        }
        catch (Exception ex)
        {
            _host.Log("Phone remote didn't stop cleanly: " + ex.GetType().Name + ": " + ex.Message);
        }
        ForgetScreens();
        Devices.Flush();
    }

    static string StartError(Exception ex, int port)
    {
        for (var inner = ex; inner != null; inner = inner.InnerException)
        {
            if (inner is AddressInUseException || inner is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
                return "Port " + port + " is already in use by another program. Close that program, or set another RemotePort in settings.json, then switch Phone remote off and on.";
            if (inner is SocketException { SocketErrorCode: SocketError.AccessDenied })
                return "Windows doesn't let Agent Arcade use port " + port + ". Set another RemotePort in settings.json, then switch Phone remote off and on.";
        }
        return "Phone remote couldn't start: " + ex.Message;
    }

    WebApplication Build(int port, bool loopbackOnly, X509Certificate2 certificate)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Production",
        });
        // No environment variable or appsettings.json may change the server: forwarded headers would let a client pick
        // the address it is counted under, and allowed hosts or URLs could shut the phones out. The empty source is
        // where UseSetting below writes.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        // Nothing may log URLs, headers or bodies.
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.PreferHostingUrlsKey, "false");
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Only the endpoint below: none from settings files or environment variables.
            options.Configure(new ConfigurationBuilder().Build(), false);
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = MaxBody;
            options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            options.Limits.MaxRequestHeaderCount = 40;
            options.Limits.MaxRequestLineSize = 4 * 1024;
            // Far above the per-address cap in FilterAsync, which is the limit that counts.
            options.Limits.MaxConcurrentConnections = 1024;
            options.Limits.MaxConcurrentUpgradedConnections = 8;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            options.Listen(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
                listen.Use(next => connection => FilterAsync(connection, next));
                listen.UseHttps(certificate, https =>
                {
                    https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                    https.HandshakeTimeout = TimeSpan.FromSeconds(10);
                    https.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                });
            });
        });
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), KeepAliveTimeout = TimeSpan.FromSeconds(45) });
        app.Run(HandleAsync);
        return app;
    }

    // ---------- requests ----------

    async Task HandleAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        try
        {
            await RouteAsync(context);
        }
        catch (Exception) when (context.RequestAborted.IsCancellationRequested)
        {
            // The phone went away.
        }
        catch (TimeoutException)
        {
            if (!context.Response.HasStarted) await WriteAsync(context, RemoteReply.Busy);
        }
        catch (Exception ex)
        {
            // Only what went wrong, never the request itself.
            _host.Log("Phone request failed: " + ex.GetType().Name + ": " + ex.Message);
            if (!context.Response.HasStarted) await WriteAsync(context, RemoteReply.Fail(500, "Something went wrong on the PC."));
        }
    }

    async Task RouteAsync(HttpContext context)
    {
        // While the server starts or stops, nothing gets through to the page.
        if (!Running)
        {
            await WriteAsync(context, RemoteReply.Busy);
            return;
        }
        var request = context.Request;
        var path = request.Path.Value ?? "";
        var get = HttpMethods.IsGet(request.Method);
        var post = HttpMethods.IsPost(request.Method);
        if (get && path == "/api/v1/hello")
        {
            await WriteAsync(context, RemoteReply.Ok(RemoteModel.Hello()));
            return;
        }
        if (post && path == "/api/v1/pair")
        {
            await WriteAsync(context, await PairAsync(context));
            return;
        }

        var address = context.Connection.RemoteIpAddress is { } remote ? RemoteModel.Plain(remote) : IPAddress.None;
        if (_failures.Blocked(address))
        {
            await WriteAsync(context, RemoteReply.Fail(429, "Too many requests without a valid pairing. Wait a minute and try again."));
            return;
        }
        var header = request.Headers.Authorization.ToString();
        var device = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? Devices.Find(header[7..].Trim()) : null;
        if (device == null)
        {
            _failures.Failed(address);
            await WriteAsync(context, RemoteReply.Fail(401, NotPaired));
            return;
        }
        if (!_limiter.Allow(device.Id))
        {
            await WriteAsync(context, TooMany);
            return;
        }
        Devices.Seen(device.Id);

        if (get && path == "/api/v1/live")
        {
            await LiveAsync(context, device);
            return;
        }
        if (post && path == "/api/v1/unpair")
        {
            if (RemoveDevice(device.Id) != null)
            {
                _host.Log("Phone removed: " + device.Name);
                _host.RemoteChanged();
            }
            await WriteAsync(context, RemoteReply.Ok(new { ok = true }));
            return;
        }
        var reply = await AnswerAsync(context, device.Id, get, post, path);
        // A phone that was removed while its request waited gets no answer.
        if (!Devices.Exists(device.Id)) reply = RemoteReply.Fail(401, NotPaired);
        await WriteAsync(context, reply);
    }

    async Task<RemoteReply> AnswerAsync(HttpContext context, string device, bool get, bool post, string path)
    {
        const string prefix = "/api/v1/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return Unknown;
        var route = path[prefix.Length..].Split('/');
        if (route is ["sessions", var pane, "screen" or "input" or "keys" or "rename" or "stop" or "close"] && !RemoteModel.IsSessionId(pane)) return PaneGone;
        switch (route)
        {
            case ["state"] when get:
                return RemoteReply.Ok(await _host.StateAsync());
            case ["history"] when get:
                return RemoteReply.Ok(new { chats = await _host.ChatsAsync() });
            case ["sessions"] when post:
                return await StartAsync(context, device);
            case ["projects"] when post:
                return await ProjectAsync(context, device);
            case ["chats", var chat, "resume"] when post:
                return RemoteModel.IsChatId(chat)
                    ? await AskPageAsync(device, "remoteResume", new() { ["chat"] = chat })
                    : RemoteReply.Fail(404, "That chat is no longer in the history.");
            case ["sessions", var id, "screen"] when get:
                return await AskPageAsync(device, "remoteGetScreen", new() { ["id"] = id, ["lines"] = RemoteModel.Lines(context.Request.Query["lines"].ToString()) });
            case ["sessions", var id, "input"] when post:
                return await InputAsync(context, device, id);
            case ["sessions", var id, "keys"] when post:
                return await KeysAsync(context, device, id);
            case ["sessions", var id, "rename"] when post:
                return await RenameAsync(context, device, id);
            case ["sessions", var id, "stop"] when post:
                return await AskPageAsync(device, "remoteStop", new() { ["id"] = id });
            case ["sessions", var id, "close"] when post:
                return await AskPageAsync(device, "remoteClose", new() { ["id"] = id });
            default:
                return Unknown;
        }
    }

    async Task<RemoteReply> PairAsync(HttpContext context)
    {
        if (!Pairing.Attempt()) return RemoteReply.Fail(429, "Too many pairing attempts. Wait a minute and try again.");
        var (body, problem) = await ReadBodyAsync(context);
        if (body == null) return problem!;
        string code, device;
        using (body)
        {
            if (!TryString(body.RootElement, "code", out code) || !TryString(body.RootElement, "device", out device)) return Invalid;
        }
        var name = RemoteModel.DeviceName(device);
        var result = Pairing.Check(code, name);
        if (result == PairResult.Cancelled) _host.RemoteChanged();
        switch (result)
        {
            case PairResult.None:
                return RemoteReply.Fail(403, "Phone remote has no pairing code right now. Open the Phone dialog on your PC.");
            case PairResult.Wrong:
                return RemoteReply.Fail(403, "That pairing code is wrong. Scan the QR code on your PC again.");
            case PairResult.Expired:
                return RemoteReply.Fail(403, "That pairing code has expired. Press New code on your PC and scan again.");
            case PairResult.Used:
                return RemoteReply.Fail(403, "That pairing code was already used. Press New code on your PC and scan again.");
            case PairResult.Cancelled:
                return RemoteReply.Fail(403, "Too many wrong codes. Press New code on your PC and scan again.");
        }
        var (added, token) = Devices.Add(name);
        _host.Log("Phone paired: " + name);
        _host.Paired(name);
        return RemoteReply.Ok(RemoteModel.PairAnswer(token, added.Id));
    }

    async Task<RemoteReply> StartAsync(HttpContext context, string device)
    {
        var (body, problem) = await ReadBodyAsync(context);
        if (body == null) return problem!;
        string account, workspace;
        using (body)
        {
            if (!TryString(body.RootElement, "account", out account) || !TryString(body.RootElement, "workspace", out workspace)) return Invalid;
        }
        if (account.Length == 0 || workspace.Length == 0) return RemoteReply.Fail(400, "Choose an agent account and a project.");
        return await WaitForPageAsync(device, () => _host.StartAsync(device, account, workspace));
    }

    async Task<RemoteReply> ProjectAsync(HttpContext context, string device)
    {
        var (body, problem) = await ReadBodyAsync(context);
        if (body == null) return problem!;
        string name;
        using (body)
        {
            if (!TryString(body.RootElement, "name", out name)) return Invalid;
        }
        name = name.Trim();
        if (NewProject.Problem(name) is { } bad) return RemoteReply.Fail(400, bad);
        return await _host.CreateProjectAsync(device, name);
    }

    async Task<RemoteReply> InputAsync(HttpContext context, string device, string id)
    {
        var (body, problem) = await ReadBodyAsync(context);
        if (body == null) return problem!;
        string text;
        bool submit;
        using (body)
        {
            if (!TryString(body.RootElement, "text", out text) || !TryBool(body.RootElement, "submit", out submit)) return Invalid;
        }
        if (Encoding.UTF8.GetByteCount(text) > MaxText) return RemoteReply.Fail(413, "That message is too long. Send at most 16 KB.");
        return await AskPageAsync(device, "remoteInput", new() { ["id"] = id, ["text"] = text, ["submit"] = submit });
    }

    async Task<RemoteReply> KeysAsync(HttpContext context, string device, string id)
    {
        var (body, problem) = await ReadBodyAsync(context);
        if (body == null) return problem!;
        var keys = new List<string>();
        using (body)
        {
            if (!body.RootElement.TryGetProperty("keys", out var list) || list.ValueKind != JsonValueKind.Array) return Invalid;
            if (list.GetArrayLength() is < 1 or > 32) return RemoteReply.Fail(400, "Send from 1 to 32 keys.");
            foreach (var item in list.EnumerateArray())
            {
                var name = item.ValueKind == JsonValueKind.String && TryText(item, out var text) ? text : "";
                if (!RemoteModel.IsKey(name)) return RemoteReply.Fail(400, "Unknown key: " + RemoteModel.Cut(name.Length > 0 ? name : item.ValueKind.ToString(), 40));
                keys.Add(name);
            }
        }
        return await AskPageAsync(device, "remoteKeys", new() { ["id"] = id, ["keys"] = keys });
    }

    async Task<RemoteReply> RenameAsync(HttpContext context, string device, string id)
    {
        var (body, problem) = await ReadBodyAsync(context);
        if (body == null) return problem!;
        string title;
        using (body)
        {
            if (!TryString(body.RootElement, "title", out title)) return Invalid;
        }
        return await AskPageAsync(device, "remoteRename", new() { ["id"] = id, ["title"] = RemoteModel.Cut(title.Trim(), 120) });
    }

    // Reads the body with its own limit, so a body that is too large still gets a JSON answer.
    static async Task<(JsonDocument? Body, RemoteReply? Problem)> ReadBodyAsync(HttpContext context)
    {
        var tooLarge = RemoteReply.Fail(413, "That request is too large.");
        if (context.Request.ContentLength > MaxBody) return (null, tooLarge);
        var buffer = new byte[MaxBody + 1];
        var count = 0;
        try
        {
            int read;
            while (count < buffer.Length && (read = await context.Request.Body.ReadAsync(buffer.AsMemory(count), context.RequestAborted)) > 0)
                count += read;
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, tooLarge);
        }
        if (count > MaxBody) return (null, tooLarge);
        try
        {
            var body = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 16 });
            if (body.RootElement.ValueKind == JsonValueKind.Object) return (body, null);
            body.Dispose();
        }
        catch (JsonException)
        {
            // Answered below.
        }
        return (null, RemoteReply.Fail(400, "That request isn't valid JSON."));
    }

    // A string field. Missing or null counts as empty; any other kind of value is invalid.
    static bool TryString(JsonElement body, string name, out string value)
    {
        value = "";
        if (!body.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        return element.ValueKind == JsonValueKind.String && TryText(element, out value);
    }

    // A string with a broken surrogate pair can't be read; it counts as invalid.
    static bool TryText(JsonElement element, out string value)
    {
        try
        {
            value = element.GetString() ?? "";
            return true;
        }
        catch (InvalidOperationException)
        {
            value = "";
            return false;
        }
    }

    static bool TryBool(JsonElement body, string name, out bool value)
    {
        value = false;
        if (!body.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = element.GetBoolean();
        return true;
    }

    static async Task WriteAsync(HttpContext context, RemoteReply reply)
    {
        var bytes = RemoteModel.ReplyJson(reply);
        context.Response.StatusCode = reply.Status is >= 200 and <= 599 ? reply.Status : 500;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes);
    }

    // ---------- live connection ----------

    async Task LiveAsync(HttpContext context, RemoteDevice device)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await WriteAsync(context, RemoteReply.Fail(400, "This address only takes a WebSocket connection."));
            return;
        }
        // No context takeover: each message is compressed on its own, so one screen can't help guess another.
        using var socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext { DangerousEnableCompression = true, DisableServerContextTakeover = true });
        var phone = new PhoneSocket(socket, device.Id);
        // A phone that connects again replaces its own older connections: after a network change they may be dead
        // without having said so, and they must not count toward the four phones.
        List<PhoneSocket> replaced;
        bool added;
        lock (_lock)
        {
            replaced = _sockets.Where(other => other.DeviceId == device.Id).ToList();
            added = Running && _sockets.Count - replaced.Count < MaxPhones;
            if (added)
            {
                _sockets.RemoveAll(replaced.Contains);
                _sockets.Add(phone);
            }
        }
        var sending = phone.SendLoopAsync();
        if (!added)
        {
            phone.Close(Running ? (WebSocketCloseStatus)1013 : WebSocketCloseStatus.EndpointUnavailable,
                Running ? "Too many phones are connected. Close Agent Arcade Remote on another phone first." : "Phone remote is off.");
            await Task.WhenAny(sending, Task.Delay(2000));
            return;
        }
        foreach (var old in replaced) old.Close(WebSocketCloseStatus.NormalClosure, "Replaced by a newer connection.");
        // A phone removed while this connection opened is closed like the others it had.
        if (!Devices.Exists(device.Id)) phone.Close((WebSocketCloseStatus)4401, "This phone was removed on the PC.");
        _host.RemoteChanged();
        _ = HelloAsync(phone);
        try
        {
            await ReceiveAsync(phone, context.RequestAborted);
        }
        catch (Exception)
        {
            // The connection dropped.
        }
        finally
        {
            bool watched;
            lock (_lock)
            {
                _sockets.Remove(phone);
                watched = phone.Watches.Count > 0;
                if (watched) ForgetUnwatchedScreens();
            }
            phone.Close(WebSocketCloseStatus.NormalClosure, "");
            await Task.WhenAny(sending, Task.Delay(5000));
            Devices.Seen(phone.DeviceId);
            if (watched) _host.WatchesChanged([]);
            _host.RemoteChanged();
            phone.Done();
        }
    }

    async Task HelloAsync(PhoneSocket phone)
    {
        try
        {
            var (state, chats) = await _host.SnapshotAsync();
            phone.Begin(
                JsonSerializer.SerializeToUtf8Bytes(new { type = "hello", state }, RemoteModel.Wire),
                JsonSerializer.SerializeToUtf8Bytes(new { type = "history", chats }, RemoteModel.Wire));
        }
        catch (Exception ex)
        {
            _host.Log("Phone hello failed: " + ex.GetType().Name + ": " + ex.Message);
            phone.Close(WebSocketCloseStatus.InternalServerError, "Agent Arcade is busy, try again.");
        }
    }

    async Task ReceiveAsync(PhoneSocket phone, CancellationToken cancel)
    {
        var buffer = new byte[MaxMessage];
        while (true)
        {
            var count = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                if (count == buffer.Length)
                {
                    phone.Close(WebSocketCloseStatus.MessageTooBig, "Messages to the PC can be at most 4 KB.");
                    return;
                }
                result = await phone.Socket.ReceiveAsync(buffer.AsMemory(count), cancel);
                if (result.MessageType == WebSocketMessageType.Close) return;
                count += result.Count;
            }
            while (!result.EndOfMessage);
            if (result.MessageType == WebSocketMessageType.Text) Received(phone, buffer.AsMemory(0, count));
        }
    }

    // The phone only sends watch and unwatch; anything else is ignored.
    void Received(PhoneSocket phone, ReadOnlyMemory<byte> data)
    {
        if (!_limiter.Allow(phone.DeviceId)) return;
        try
        {
            using var message = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 8 });
            var root = message.RootElement;
            var type = Json.Str(root, "type");
            var id = Json.Str(root, "id");
            if (!RemoteModel.IsSessionId(id)) return;
            if (type == "watch") Watch(phone, id, root.TryGetProperty("lines", out var lines) ? RemoteModel.Lines(lines) : 200);
            else if (type == "unwatch") Unwatch(phone, id);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not JSON, or a broken string.
        }
    }

    void Watch(PhoneSocket phone, string id, int lines)
    {
        byte[]? cached = null;
        lock (_lock)
        {
            if (!phone.Watches.ContainsKey(id) && phone.Watches.Count >= MaxWatches) return;
            phone.Watches[id] = lines;
            if (_screens.TryGetValue(id, out var screen)) cached = RemoteModel.ScreenMessage(screen, lines);
        }
        if (cached != null) phone.SendScreen(id, cached);
        _host.WatchesChanged([id]);
    }

    void Unwatch(PhoneSocket phone, string id)
    {
        lock (_lock)
        {
            if (!phone.Watches.Remove(id)) return;
            ForgetUnwatchedScreens();
        }
        _host.WatchesChanged([]);
    }

    // Call with the lock held.
    void ForgetUnwatchedScreens()
    {
        foreach (var id in _screens.Keys.Where(id => !_sockets.Any(socket => socket.Watches.ContainsKey(id))).ToList())
            _screens.Remove(id);
    }
}
