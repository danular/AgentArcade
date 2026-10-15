using System.Runtime.InteropServices;

namespace AgentArcade;

// Keeps Windows from going to sleep on its own while agents run (and, when chosen, while Phone remote is on), so they
// keep working and the phone can still reach the PC. It uses a power request, which powercfg /requests lists with its
// reason. The screen still turns off. Windows ends the request anyway when the lid closes, the power button or Sleep
// is pressed, and on battery a few minutes after the sleep time (Modern Standby).
sealed class KeepAwake : IDisposable
{
    public const string Agents = "agents", Remote = "remote", Off = "off";

    const int PowerRequestSystemRequired = 1;
    const uint ContextVersion = 0, ContextSimpleString = 1;

    IntPtr _request;
    string? _reason;

    // The reason Windows shows now; null while Windows may sleep.
    public string? Reason => _reason;

    // Holds a request with this reason, or lets go of it for null. A new reason makes a new request, because Windows
    // keeps the one it was created with.
    public bool Set(string? reason)
    {
        if (reason == _reason) return true;
        Release();
        if (reason == null) return true;
        var context = new ReasonContext { Version = ContextVersion, Flags = ContextSimpleString, Reason = reason };
        var request = PowerCreateRequest(ref context);
        if (request == IntPtr.Zero || request == new IntPtr(-1)) return false;
        if (!PowerSetRequest(request, PowerRequestSystemRequired))
        {
            CloseHandle(request);
            return false;
        }
        _request = request;
        _reason = reason;
        return true;
    }

    void Release()
    {
        if (_request != IntPtr.Zero)
        {
            PowerClearRequest(_request, PowerRequestSystemRequired);
            CloseHandle(_request);
        }
        _request = IntPtr.Zero;
        _reason = null;
    }

    public void Dispose() => Release();

    // What the Phone dialog tells about sleep: on battery (Windows sleeps anyway after a while), and whether closing
    // the lid of a laptop puts it to sleep while plugged in.
    public static (bool Laptop, bool OnBattery, bool LidSleeps) Status()
    {
        var laptop = false;
        var onBattery = false;
        if (GetSystemPowerStatus(out var power))
        {
            // 128: no system battery, 255: unknown.
            laptop = power.BatteryFlag is not 128 and not 255;
            onBattery = power.ACLineStatus == 0;
        }
        return (laptop, onBattery, laptop && LidActionPluggedIn() is not null and not 0);
    }

    // The plugged-in lid action of the active power plan: 0 do nothing, 1 sleep, 2 hibernate, 3 shut down.
    static uint? LidActionPluggedIn()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var scheme) != 0) return null;
        try
        {
            var plan = Marshal.PtrToStructure<Guid>(scheme);
            var buttons = new Guid("4f971e89-eebd-4455-a8de-9e59040e7347");
            var lid = new Guid("5ca83367-6e45-459f-a27b-476b1d01c936");
            return PowerReadACValueIndex(IntPtr.Zero, ref plan, ref buttons, ref lid, out var value) == 0 ? value : null;
        }
        finally
        {
            LocalFree(scheme);
        }
    }

    // REASON_CONTEXT with a simple string. The padding covers the larger "detailed" member of its union.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string Reason;
        IntPtr _pad1, _pad2, _pad3;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool PowerSetRequest(IntPtr request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool PowerClearRequest(IntPtr request, int type);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("powrprof.dll")]
    static extern uint PowerGetActiveScheme(IntPtr rootKey, out IntPtr scheme);

    [DllImport("powrprof.dll")]
    static extern uint PowerReadACValueIndex(IntPtr rootKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
}
